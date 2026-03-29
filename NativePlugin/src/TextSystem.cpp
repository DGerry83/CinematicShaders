#include "TextSystem.h"
#include <cstring>
#include <cmath>
#include <fstream>
#include <algorithm>

// Logging from main native module
extern void LogToFile(const char* fmt, ...);

// Windows headers define min/max macros that conflict with std::min/max
#undef min
#undef max

// stb_truetype implementation
#define STB_TRUETYPE_IMPLEMENTATION
#include "../include/stb_truetype.h"

namespace CinematicShaders {

// ============================================================================
// 8SSEDT Distance Transform
// ============================================================================

// Helper struct for distance transform
struct Point {
    int x, y;
    int distSq() const { return x * x + y * y; }
};

// 8-connected signed sequential Euclidean distance transform
// Input: binary bitmap (0 = inside glyph, 255 = outside)
// Output: 8-bit SDF where 128 = edge, 0 = inside, 255 = outside
static void ComputeSDF(const uint8_t* input, int w, int h, uint8_t* output, int spread) {
    std::vector<Point> grid(w * h);
    
    // Initialize: inside = (0,0), outside = infinity
    const int INF = 1000000;
    for (int y = 0; y < h; y++) {
        for (int x = 0; x < w; x++) {
            int idx = y * w + x;
            // Binary threshold - assume input is anti-aliased
            // Values < 128 considered "inside" (glyph)
            if (input[idx] < 128) {
                grid[idx] = {0, 0};
            } else {
                grid[idx] = {INF, INF};
            }
        }
    }
    
    // Forward pass
    for (int y = 0; y < h; y++) {
        for (int x = 0; x < w; x++) {
            int idx = y * w + x;
            Point& p = grid[idx];
            
            // Check neighbors above and left
            if (y > 0) {
                Point test = grid[(y - 1) * w + x];
                test.y++;
                if (test.distSq() < p.distSq()) p = test;
            }
            if (x > 0) {
                Point test = grid[y * w + (x - 1)];
                test.x++;
                if (test.distSq() < p.distSq()) p = test;
            }
            if (y > 0 && x > 0) {
                Point test = grid[(y - 1) * w + (x - 1)];
                test.x++; test.y++;
                if (test.distSq() < p.distSq()) p = test;
            }
            if (y > 0 && x < w - 1) {
                Point test = grid[(y - 1) * w + (x + 1)];
                test.x--; test.y++;
                if (test.distSq() < p.distSq()) p = test;
            }
        }
    }
    
    // Backward pass
    for (int y = h - 1; y >= 0; y--) {
        for (int x = w - 1; x >= 0; x--) {
            int idx = y * w + x;
            Point& p = grid[idx];
            
            // Check neighbors below and right
            if (y < h - 1) {
                Point test = grid[(y + 1) * w + x];
                test.y--;
                if (test.distSq() < p.distSq()) p = test;
            }
            if (x < w - 1) {
                Point test = grid[y * w + (x + 1)];
                test.x--;
                if (test.distSq() < p.distSq()) p = test;
            }
            if (y < h - 1 && x < w - 1) {
                Point test = grid[(y + 1) * w + (x + 1)];
                test.x--; test.y--;
                if (test.distSq() < p.distSq()) p = test;
            }
            if (y < h - 1 && x > 0) {
                Point test = grid[(y + 1) * w + (x - 1)];
                test.x++; test.y--;
                if (test.distSq() < p.distSq()) p = test;
            }
        }
    }
    
    // Convert distances to 8-bit SDF
    for (int y = 0; y < h; y++) {
        for (int x = 0; x < w; x++) {
            int idx = y * w + x;
            float dist = std::sqrt(static_cast<float>(grid[idx].distSq()));
            
            // Normalize: 128 = edge, positive = outside, negative = inside
            float normalized = 128.0f + (dist / static_cast<float>(spread)) * 128.0f;
            
            // Clamp to valid range
            if (normalized < 0) normalized = 0;
            if (normalized > 255) normalized = 255;
            
            output[idx] = static_cast<uint8_t>(normalized);
        }
    }
}

// ============================================================================
// TextSystem Implementation
// ============================================================================

TextSystem::TextSystem()
    : m_initialized(false)
    , m_device(nullptr)
    , m_context(nullptr)
    , m_atlasTex(nullptr)
    , m_atlasSRV(nullptr)
    , m_fontInfo(nullptr)
    , m_fontScale(0)
    , m_ascent(0)
    , m_descent(0)
    , m_lineGap(0)
    , m_atlasWidth(0)
    , m_atlasHeight(0)
    , m_atlasX(SDF_PADDING)
    , m_atlasY(SDF_PADDING)
    , m_atlasRowHeight(0)
{
}

TextSystem::~TextSystem() {
    Shutdown();
}

bool TextSystem::Init(ID3D11Device* device, const wchar_t* ttfPath, int atlasSize) {
    if (m_initialized) {
        Shutdown();
    }
    
    if (!device || !ttfPath) {
        return false;
    }
    
    m_device = device;
    device->GetImmediateContext(&m_context);
    
    // Load TTF file
    std::ifstream file(ttfPath, std::ios::binary | std::ios::ate);
    if (!file.is_open()) {
        return false;
    }
    
    std::streamsize size = file.tellg();
    file.seekg(0, std::ios::beg);
    
    m_ttfData.resize(static_cast<size_t>(size));
    if (!file.read(reinterpret_cast<char*>(m_ttfData.data()), size)) {
        return false;
    }
    file.close();
    
    // Initialize font
    m_fontInfo = new stbtt_fontinfo();
    if (!stbtt_InitFont(m_fontInfo, m_ttfData.data(), stbtt_GetFontOffsetForIndex(m_ttfData.data(), 0))) {
        delete m_fontInfo;
        m_fontInfo = nullptr;
        return false;
    }
    
    // Get font metrics (at scale 1.0 = pixel height)
    stbtt_GetFontVMetrics(m_fontInfo, &m_ascent, &m_descent, &m_lineGap);
    
    // Create atlas texture
    m_atlasWidth = atlasSize;
    m_atlasHeight = atlasSize;
    m_atlasPixels.resize(m_atlasWidth * m_atlasHeight);
    std::fill(m_atlasPixels.begin(), m_atlasPixels.end(), static_cast<uint8_t>(128));  // Neutral distance
    
    D3D11_TEXTURE2D_DESC texDesc = {};
    texDesc.Width = m_atlasWidth;
    texDesc.Height = m_atlasHeight;
    texDesc.MipLevels = 1;
    texDesc.ArraySize = 1;
    texDesc.Format = DXGI_FORMAT_R8_UNORM;
    texDesc.SampleDesc.Count = 1;
    texDesc.Usage = D3D11_USAGE_DEFAULT;
    texDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    
    D3D11_SUBRESOURCE_DATA initData = {};
    initData.pSysMem = m_atlasPixels.data();
    initData.SysMemPitch = m_atlasWidth;
    
    HRESULT hr = device->CreateTexture2D(&texDesc, &initData, &m_atlasTex);
    if (FAILED(hr)) {
        Shutdown();
        return false;
    }
    
    // Create SRV
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = DXGI_FORMAT_R8_UNORM;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Texture2D.MipLevels = 1;
    
    hr = device->CreateShaderResourceView(m_atlasTex, &srvDesc, &m_atlasSRV);
    if (FAILED(hr)) {
        Shutdown();
        return false;
    }
    
    m_initialized = true;
    return true;
}

void TextSystem::Shutdown() {
    if (m_atlasSRV) {
        m_atlasSRV->Release();
        m_atlasSRV = nullptr;
    }
    if (m_atlasTex) {
        m_atlasTex->Release();
        m_atlasTex = nullptr;
    }
    if (m_fontInfo) {
        delete m_fontInfo;
        m_fontInfo = nullptr;
    }
    if (m_context) {
        m_context->Release();
        m_context = nullptr;
    }
    
    m_device = nullptr;
    m_ttfData.clear();
    m_atlasPixels.clear();
    m_glyphCache.clear();
    m_instances.clear();
    m_initialized = false;
}

uint8_t* TextSystem::RasterizeGlyph(int codepoint, int& outW, int& outH) {
    int glyphIndex = stbtt_FindGlyphIndex(m_fontInfo, codepoint);
    if (glyphIndex == 0) {
        return nullptr;  // Glyph not found
    }
    
    // Get glyph box
    int x0, y0, x1, y1;
    stbtt_GetGlyphBox(m_fontInfo, glyphIndex, &x0, &y0, &x1, &y1);
    
    // Scale to pixels
    float scale = m_fontScale;
    int w = static_cast<int>(std::ceil((x1 - x0) * scale)) + SDF_PADDING * 2;
    int h = static_cast<int>(std::ceil((y1 - y0) * scale)) + SDF_PADDING * 2;
    
    if (w <= 0 || h <= 0) {
        return nullptr;
    }
    
    // Allocate bitmap
    uint8_t* bitmap = new uint8_t[w * h];
    std::fill(bitmap, bitmap + w * h, static_cast<uint8_t>(0));
    
    // Render glyph
    int xOffset, yOffset;
    stbtt_MakeGlyphBitmap(m_fontInfo, bitmap, w, h, w, scale, scale, glyphIndex);
    
    outW = w;
    outH = h;
    return bitmap;
}

void TextSystem::GenerateSDF(const uint8_t* bitmap, int w, int h, uint8_t* outSDF, int outW, int outH) {
    // Input bitmap is already anti-aliased from stbtt
    // We need to threshold it for the distance transform
    uint8_t* binary = new uint8_t[w * h];
    for (int i = 0; i < w * h; i++) {
        binary[i] = (bitmap[i] > 128) ? 0 : 255;  // 0 = inside, 255 = outside
    }
    
    // Compute SDF
    std::vector<uint8_t> sdf(w * h);
    ComputeSDF(binary, w, h, sdf.data(), SDF_SPREAD);
    
    delete[] binary;
    
    // Downsample to output size
    // Simple box filter for now
    int scaleX = w / outW;
    int scaleY = h / outH;
    
    for (int y = 0; y < outH; y++) {
        for (int x = 0; x < outW; x++) {
            int sum = 0;
            for (int sy = 0; sy < scaleY; sy++) {
                for (int sx = 0; sx < scaleX; sx++) {
                    int srcX = x * scaleX + sx;
                    int srcY = y * scaleY + sy;
                    sum += sdf[srcY * w + srcX];
                }
            }
            outSDF[y * outW + x] = static_cast<uint8_t>(sum / (scaleX * scaleY));
        }
    }
}

void TextSystem::UpdateAtlasRegion(int x, int y, int w, int h, const uint8_t* data) {
    // Update CPU-side atlas
    for (int row = 0; row < h; row++) {
        if (y + row >= m_atlasHeight) break;
        std::memcpy(&m_atlasPixels[(y + row) * m_atlasWidth + x], 
                    &data[row * w], 
                    std::min(w, m_atlasWidth - x));
    }
    
    // Update GPU texture
    D3D11_BOX box = {};
    box.left = x;
    box.top = y;
    box.front = 0;
    box.right = x + w;
    box.bottom = y + h;
    box.back = 1;
    
    m_context->UpdateSubresource(m_atlasTex, 0, &box, data, w, 0);
}

bool TextSystem::PackGlyph(int codepoint) {
    // Check if already packed
    auto it = m_glyphCache.find(codepoint);
    if (it != m_glyphCache.end()) {
        return true;
    }
    
    // Rasterize at high resolution for SDF
    int bmpW, bmpH;
    uint8_t* bitmap = RasterizeGlyph(codepoint, bmpW, bmpH);
    if (!bitmap) {
        return false;
    }
    
    // Generate SDF (same size for now, could downsample)
    int sdfW = bmpW;
    int sdfH = bmpH;
    uint8_t* sdf = new uint8_t[sdfW * sdfH];
    GenerateSDF(bitmap, bmpW, bmpH, sdf, sdfW, sdfH);
    
    delete[] bitmap;
    
    // Find position in atlas
    if (m_atlasX + sdfW > m_atlasWidth) {
        // Move to next row
        m_atlasX = SDF_PADDING;
        m_atlasY += m_atlasRowHeight + SDF_PADDING;
        m_atlasRowHeight = 0;
    }
    
    if (m_atlasY + sdfH > m_atlasHeight) {
        // Atlas full
        delete[] sdf;
        return false;
    }
    
    // Store metric
    GlyphMetric metric;
    
    // Get metrics for layout
    int glyphIndex = stbtt_FindGlyphIndex(m_fontInfo, codepoint);
    int advance, leftBearing;
    stbtt_GetGlyphHMetrics(m_fontInfo, glyphIndex, &advance, &leftBearing);
    
    int x0, y0, x1, y1;
    stbtt_GetGlyphBox(m_fontInfo, glyphIndex, &x0, &y0, &x1, &y1);
    
    metric.advance = advance * m_fontScale;
    metric.leftBearing = leftBearing * m_fontScale;
    metric.topBearing = y1 * m_fontScale;
    metric.u0 = static_cast<float>(m_atlasX) / m_atlasWidth;
    metric.v0 = static_cast<float>(m_atlasY) / m_atlasHeight;
    metric.u1 = static_cast<float>(m_atlasX + sdfW) / m_atlasWidth;
    metric.v1 = static_cast<float>(m_atlasY + sdfH) / m_atlasHeight;
    metric.width = sdfW;
    metric.height = sdfH;
    
    // Upload to atlas
    UpdateAtlasRegion(m_atlasX, m_atlasY, sdfW, sdfH, sdf);
    
    delete[] sdf;
    
    // Update packing state
    m_glyphCache[codepoint] = metric;
    m_atlasX += sdfW + SDF_PADDING;
    m_atlasRowHeight = std::max(m_atlasRowHeight, sdfH);
    
    return true;
}

int TextSystem::LayoutString(const char* text, float fontSize, uint32_t color) {
    if (!m_initialized || !text) {
        return 0;
    }
    
    m_instances.clear();
    
    // Set font scale for this layout
    m_fontScale = stbtt_ScaleForPixelHeight(m_fontInfo, fontSize);
    
    float cursorX = 0.0f;
    float cursorY = 0.0f;
    float lineHeight = (m_ascent - m_descent + m_lineGap) * m_fontScale;
    
    for (const char* p = text; *p; ++p) {
        int codepoint = static_cast<unsigned char>(*p);
        
        // Handle newline
        if (codepoint == '\n') {
            cursorX = 0.0f;
            cursorY += lineHeight;
            continue;
        }
        
        // Pack glyph into atlas
        if (!PackGlyph(codepoint)) {
            continue;  // Skip if can't pack
        }
        
        const GlyphMetric& m = m_glyphCache[codepoint];
        
        // Create instance
        GlyphInstance inst;
        inst.posX = cursorX + m.leftBearing;
        inst.posY = cursorY + (m_ascent * m_fontScale) - m.topBearing;
        inst.sizeX = static_cast<float>(m.width);
        inst.sizeY = static_cast<float>(m.height);
        inst.uvX = m.u0;
        inst.uvY = m.v0;
        inst.uvW = m.u1 - m.u0;
        inst.uvH = m.v1 - m.v0;
        inst.color = color;
        inst.smoothing = 1.0f / (SDF_SPREAD * m_fontScale);
        
        m_instances.push_back(inst);
        
        // Advance cursor
        cursorX += m.advance;
    }
    
    return static_cast<int>(m_instances.size());
}

ID3D11Buffer* TextSystem::GetOrCreateGlyphBuffer() {
    if (m_instances.empty())
        return nullptr;
    
    int requiredCount = static_cast<int>(m_instances.size());
    int instanceSize = sizeof(GlyphInstance);
    int requiredSize = requiredCount * instanceSize;
    
    // Create or resize buffer if needed
    if (!m_glyphBuffer || m_glyphBufferCapacity < requiredCount) {
        if (m_glyphBuffer) {
            m_glyphBuffer->Release();
            m_glyphBuffer = nullptr;
        }
        if (m_glyphBufferSRV) {
            m_glyphBufferSRV->Release();
            m_glyphBufferSRV = nullptr;
        }
        
        // Allocate with some headroom
        m_glyphBufferCapacity = std::max(requiredCount * 2, 256);
        
        D3D11_BUFFER_DESC desc = {};
        desc.ByteWidth = m_glyphBufferCapacity * instanceSize;
        desc.Usage = D3D11_USAGE_DYNAMIC;
        desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        desc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        desc.StructureByteStride = instanceSize;
        desc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
        
        HRESULT hr = m_device->CreateBuffer(&desc, nullptr, &m_glyphBuffer);
        if (FAILED(hr)) {
            m_glyphBufferCapacity = 0;
            return nullptr;
        }
        
        // Create SRV for the buffer
        D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Format = DXGI_FORMAT_UNKNOWN;
        srvDesc.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
        srvDesc.Buffer.ElementWidth = instanceSize;
        srvDesc.Buffer.NumElements = m_glyphBufferCapacity;
        
        hr = m_device->CreateShaderResourceView(m_glyphBuffer, &srvDesc, &m_glyphBufferSRV);
        if (FAILED(hr)) {
            m_glyphBuffer->Release();
            m_glyphBuffer = nullptr;
            m_glyphBufferCapacity = 0;
            return nullptr;
        }
    }
    
    // Update buffer with current glyph data
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(m_context->Map(m_glyphBuffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        memcpy(mapped.pData, m_instances.data(), requiredSize);
        m_context->Unmap(m_glyphBuffer, 0);
    }
    
    return m_glyphBuffer;
}

} // namespace CinematicShaders

// ============================================================================
// C Interface Exports
// ============================================================================

using namespace CinematicShaders;

TextSystemHandle CR_TextInit(ID3D11Texture2D* deviceSourceTexture, const wchar_t* fontPath) {
    LogToFile("[Text] CR_TextInit called: deviceSourceTexture=%p, fontPath=%ls", deviceSourceTexture, fontPath ? fontPath : L"(null)");
    
    if (!deviceSourceTexture || !fontPath) {
        LogToFile("[Text] CR_TextInit FAILED: null argument (texture=%p, path=%ls)", deviceSourceTexture, fontPath ? L"valid" : L"null");
        return nullptr;
    }
    
    // Get device from texture
    ID3D11Device* device = nullptr;
    deviceSourceTexture->GetDevice(&device);
    if (!device) {
        LogToFile("[Text] CR_TextInit FAILED: GetDevice returned null");
        return nullptr;
    }
    LogToFile("[Text] Got device from texture: %p", device);
    
    TextSystem* ts = new TextSystem();
    LogToFile("[Text] Created TextSystem object: %p", ts);
    
    if (!ts->Init(device, fontPath)) {
        LogToFile("[Text] CR_TextInit FAILED: TextSystem::Init failed");
        delete ts;
        device->Release();
        return nullptr;
    }
    
    LogToFile("[Text] CR_TextInit SUCCESS: handle=%p", ts);
    device->Release();
    return ts;
}

void CR_TextShutdown(TextSystemHandle handle) {
    if (handle) {
        TextSystem* ts = static_cast<TextSystem*>(handle);
        delete ts;
    }
}

int CR_TextLayout(TextSystemHandle handle, const char* text, float fontSize, uint32_t color) {
    LogToFile("[Text] CR_TextLayout called: handle=%p, text='%s', fontSize=%.1f, color=0x%08X", handle, text ? text : "(null)", fontSize, color);
    
    if (!handle) {
        LogToFile("[Text] CR_TextLayout FAILED: null handle");
        return 0;
    }
    TextSystem* ts = static_cast<TextSystem*>(handle);
    int count = ts->LayoutString(text, fontSize, color);
    LogToFile("[Text] CR_TextLayout: LayoutString returned %d glyphs", count);
    return count;
}

ID3D11ShaderResourceView* CR_TextGetAtlasSRV(TextSystemHandle handle) {
    if (!handle) return nullptr;
    TextSystem* ts = static_cast<TextSystem*>(handle);
    return ts->GetAtlasSRV();
}

const GlyphInstance* CR_TextGetGlyphPtr(TextSystemHandle handle) {
    if (!handle) return nullptr;
    TextSystem* ts = static_cast<TextSystem*>(handle);
    return ts->GetGlyphPtr();
}

int CR_TextGetGlyphCount(TextSystemHandle handle) {
    if (!handle) return 0;
    TextSystem* ts = static_cast<TextSystem*>(handle);
    return ts->GetGlyphCount();
}
