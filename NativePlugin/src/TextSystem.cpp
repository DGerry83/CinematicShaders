#include "TextSystem.h"
#include <cstring>
#include <cmath>
#include <fstream>
#include <algorithm>
#include <mutex>

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
// TextSystem Implementation
// ============================================================================

TextSystem::TextSystem()
    : m_initialized(false)
    , m_device(nullptr)
    , m_atlasTex(nullptr)
    , m_atlasSRV(nullptr)
    , m_fontInfo(nullptr)
    , m_fontScale(0)
    , m_ascent(0)
    , m_descent(0)
    , m_lineGap(0)
    , m_atlasWidth(0)
    , m_atlasHeight(0)
    , m_atlasX(GLYPH_PADDING)
    , m_atlasY(GLYPH_PADDING)
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
    std::fill(m_atlasPixels.begin(), m_atlasPixels.end(), static_cast<uint8_t>(0));  // Transparent background for bitmap atlas
    
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
    if (m_glyphBufferSRV) {
        m_glyphBufferSRV->Release();
        m_glyphBufferSRV = nullptr;
    }
    if (m_glyphBuffer) {
        m_glyphBuffer->Release();
        m_glyphBuffer = nullptr;
    }
    m_glyphBufferCapacity = 0;
    
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
    m_device = nullptr;
    m_ttfData.clear();
    m_atlasPixels.clear();
    m_glyphCache.clear();
    m_instances.clear();
    m_cachedFontPx = 0;
    m_initialized = false;
}

void TextSystem::UpdateAtlasRegion(int x, int y, int w, int h, const uint8_t* data) {
    // Defensive bounds check
    if (x < 0 || y < 0 || x >= m_atlasWidth || y >= m_atlasHeight || w <= 0 || h <= 0) {
        return;
    }
    
    // Update CPU-side atlas
    for (int row = 0; row < h; row++) {
        if (y + row >= m_atlasHeight) break;
        int copyW = std::min(w, m_atlasWidth - x);
        if (copyW <= 0) return;
        std::memcpy(&m_atlasPixels[(y + row) * m_atlasWidth + x], 
                    &data[row * w], 
                    copyW);
    }
    
    // Stage GPU update for render thread
    AtlasUpdateJob job;
    job.box.left = x;
    job.box.top = y;
    job.box.front = 0;
    job.box.right = x + w;
    job.box.bottom = y + h;
    job.box.back = 1;
    job.pixels.assign(data, data + (w * h));
    
    std::lock_guard<std::mutex> lock(m_atlasQueueMutex);
    m_atlasUpdateQueue.push_back(std::move(job));
}

bool TextSystem::PackGlyph(int codepoint) {
    // Check if already packed
    auto it = m_glyphCache.find(codepoint);
    if (it != m_glyphCache.end()) {
        return true;
    }

    // Find glyph index (0 = .notdef fallback, which is valid)
    int glyphIndex = stbtt_FindGlyphIndex(m_fontInfo, codepoint);

    // Get glyph bitmap bounding box
    int ix0, iy0, ix1, iy1;
    stbtt_GetCodepointBitmapBox(m_fontInfo, codepoint, m_fontScale, m_fontScale, &ix0, &iy0, &ix1, &iy1);
    
    int bmpW = ix1 - ix0;
    int bmpH = iy1 - iy0;
    int xoff = ix0;
    int yoff = iy0;
    
    // Get horizontal metrics for layout advance
    int advance = 0, leftBearing = 0;
    stbtt_GetGlyphHMetrics(m_fontInfo, glyphIndex, &advance, &leftBearing);
    
    if (bmpW <= 0 || bmpH <= 0) {
        // Zero-width glyph (space, etc) - still need metrics but no bitmap
        GlyphMetric metric = {};
        metric.advance = advance * m_fontScale;
        metric.xOffset = 0;
        metric.yOffset = 0;
        metric.width = 0;
        metric.height = 0;
        metric.u0 = metric.v0 = metric.u1 = metric.v1 = 0;
        
        m_glyphCache[codepoint] = metric;
        return true;
    }

    // Find position in atlas
    if (m_atlasX + bmpW > m_atlasWidth) {
        // Move to next row
        m_atlasX = GLYPH_PADDING;
        m_atlasY += m_atlasRowHeight + GLYPH_PADDING;
        m_atlasRowHeight = 0;
    }

    if (m_atlasY + bmpH > m_atlasHeight) {
        // Atlas full
        return false;
    }

    // Allocate temp buffer and render bitmap
    std::vector<unsigned char> bitmap(bmpW * bmpH, 0);
    
    // Render the glyph bitmap - this fills the buffer with 8-bit coverage values
    stbtt_MakeCodepointBitmap(
        m_fontInfo,
        bitmap.data(),
        bmpW,
        bmpH,
        bmpW, // stride
        m_fontScale,
        m_fontScale,
        codepoint
    );

    // Store metric
    GlyphMetric metric = {};
    metric.advance = advance * m_fontScale;
    metric.leftBearing = leftBearing * m_fontScale;  // metadata only; xOffset used for placement
    metric.topBearing = 0.0f;  // unused in bitmap path; yOffset from bitmap box is authoritative
    metric.xOffset = static_cast<float>(xoff);  // offset to bitmap left
    metric.yOffset = static_cast<float>(yoff);  // offset to bitmap top (negative)

    metric.u0 = static_cast<float>(m_atlasX) / static_cast<float>(m_atlasWidth);
    metric.v0 = static_cast<float>(m_atlasY) / static_cast<float>(m_atlasHeight);
    metric.u1 = static_cast<float>(m_atlasX + bmpW) / static_cast<float>(m_atlasWidth);
    metric.v1 = static_cast<float>(m_atlasY + bmpH) / static_cast<float>(m_atlasHeight);

    metric.width = bmpW;
    metric.height = bmpH;

    // Upload to atlas
    UpdateAtlasRegion(m_atlasX, m_atlasY, bmpW, bmpH, bitmap.data());

    // Cache metric
    m_glyphCache[codepoint] = metric;

    // Assign stable glyph ID for instanced rendering
    GetOrAssignGlyphID(codepoint);

    // Update packing state
    m_atlasX += bmpW + GLYPH_PADDING;
    m_atlasRowHeight = std::max(m_atlasRowHeight, bmpH);

    return true;
}

void TextSystem::ClearAtlasAndCache() {
    m_glyphCache.clear();
    m_atlasX = GLYPH_PADDING;
    m_atlasY = GLYPH_PADDING;
    m_atlasRowHeight = 0;
    std::fill(m_atlasPixels.begin(), m_atlasPixels.end(), static_cast<uint8_t>(0));
    
    // Stage full clear for render thread
    AtlasUpdateJob job;
    job.fullClear = true;
    
    std::lock_guard<std::mutex> lock(m_atlasQueueMutex);
    m_atlasUpdateQueue.push_back(std::move(job));
    
    m_glyphIDMap.clear();
    m_nextGlyphID = 0;
}

void TextSystem::FlushAtlasUpdates(ID3D11DeviceContext* context) {
    if (!context || !m_atlasTex)
        return;
    
    std::vector<AtlasUpdateJob> jobs;
    {
        std::lock_guard<std::mutex> lock(m_atlasQueueMutex);
        jobs.swap(m_atlasUpdateQueue);
    }
    
    for (auto& job : jobs) {
        if (job.fullClear) {
            context->UpdateSubresource(m_atlasTex, 0, nullptr, m_atlasPixels.data(), m_atlasWidth, 0);
        } else {
            context->UpdateSubresource(m_atlasTex, 0, &job.box, job.pixels.data(), job.box.right - job.box.left, 0);
        }
    }
}

int TextSystem::LayoutString(const char* text, float fontSize, uint32_t color) {
    return LayoutStringEx(text, fontSize, color, 0.0f, 0.0f);
}

int TextSystem::LayoutStringEx(const char* text, float fontSize, uint32_t color, float originX, float originY, float lineSpacing,
                               float aspectRatio) {
    if (!m_initialized || !text) {
        return 0;
    }
    
    // Quantize font size to integer pixels (pixel fonts should use integer sizes)
    int fontPx = static_cast<int>(std::round(fontSize));
    
    // Clear cache if font size changed (glyphs are size-specific)
    if (fontPx != m_cachedFontPx) {
        ClearAtlasAndCache();
        m_cachedFontPx = fontPx;
    }
    
    m_instances.clear();
    
    // Set font scale for this layout
    m_fontScale = stbtt_ScaleForPixelHeight(m_fontInfo, static_cast<float>(fontPx));
    
    float cursorX = originX;
    float cursorY = originY;
    float lineHeight = (m_ascent - m_descent + m_lineGap) * m_fontScale + lineSpacing;
    
    for (const char* p = text; *p; ) {
        // UTF-8 decode
        int codepoint = 0;
        unsigned char c = static_cast<unsigned char>(*p);
        
        if ((c & 0x80) == 0) {
            // 1-byte ASCII (0xxxxxxx)
            codepoint = c;
            ++p;
        } else if ((c & 0xE0) == 0xC0) {
            // 2-byte sequence (110xxxxx 10xxxxxx)
            codepoint = ((c & 0x1F) << 6) | (static_cast<unsigned char>(p[1]) & 0x3F);
            p += 2;
        } else if ((c & 0xF0) == 0xE0) {
            // 3-byte sequence (1110xxxx 10xxxxxx 10xxxxxx)
            codepoint = ((c & 0x0F) << 12) | ((static_cast<unsigned char>(p[1]) & 0x3F) << 6) | (static_cast<unsigned char>(p[2]) & 0x3F);
            p += 3;
        } else if ((c & 0xF8) == 0xF0) {
            // 4-byte sequence (11110xxx 10xxxxxx 10xxxxxx 10xxxxxx)
            codepoint = ((c & 0x07) << 18) | ((static_cast<unsigned char>(p[1]) & 0x3F) << 12) | ((static_cast<unsigned char>(p[2]) & 0x3F) << 6) | (static_cast<unsigned char>(p[3]) & 0x3F);
            p += 4;
        } else {
            // Invalid sequence, skip byte
            ++p;
            continue;
        }
        
        // Handle newline
        if (codepoint == '\n') {
            cursorX = originX;
            cursorY += lineHeight;
            continue;
        }
        
        // Handle escape sequence: ^| -> U+258C LEFT HALF BLOCK
        // Note: after UTF-8 decode, p points to next character, so check *p not *(p+1)
        if (codepoint == '^' && *p == '|') {
            codepoint = 0x258C;  // U+258C LEFT HALF BLOCK
            p++;  // Skip the '|' character
        }
        
        // Pack glyph into atlas
        if (!PackGlyph(codepoint)) {
            continue;  // Skip if can't pack
        }
        
        const GlyphMetric& m = m_glyphCache[codepoint];
        
        // Create instance using bitmap positioning
        GlyphInstance inst;
        // xOffset/yOffset are pixel offsets from baseline to bitmap top-left
        // Snap to integers for pixel-perfect rendering (critical for pixel fonts)
        inst.posX = std::round(cursorX + (m.xOffset * aspectRatio));
        inst.posY = std::round(cursorY + (m_ascent * m_fontScale) + m.yOffset);
        
        inst.sizeX = static_cast<float>(m.width) * aspectRatio;
        inst.sizeY = static_cast<float>(m.height);
        inst.uvX = m.u0;
        inst.uvY = m.v0;
        inst.uvW = m.u1 - m.u0;
        inst.uvH = m.v1 - m.v0;
        inst.color = color;
        // No smoothing for bitmap fonts - we want crisp pixels
        inst.smoothing = 0.0f;
        
        m_instances.push_back(inst);
        
        // Advance cursor by glyph advance
        cursorX += m.advance * aspectRatio;
        
        // Apply kerning with next character (peek next UTF-8 codepoint)
        if (*p && *p != '\n') {
            int nextCodepoint = 0;
            unsigned char nc = static_cast<unsigned char>(*p);
            
            if ((nc & 0x80) == 0) {
                nextCodepoint = nc;
            } else if ((nc & 0xE0) == 0xC0 && *(p+1)) {
                nextCodepoint = ((nc & 0x1F) << 6) | (static_cast<unsigned char>(p[1]) & 0x3F);
            } else if ((nc & 0xF0) == 0xE0 && *(p+1) && *(p+2)) {
                nextCodepoint = ((nc & 0x0F) << 12) | ((static_cast<unsigned char>(p[1]) & 0x3F) << 6) | (static_cast<unsigned char>(p[2]) & 0x3F);
            } else if ((nc & 0xF8) == 0xF0 && *(p+1) && *(p+2) && *(p+3)) {
                nextCodepoint = ((nc & 0x07) << 18) | ((static_cast<unsigned char>(p[1]) & 0x3F) << 12) | ((static_cast<unsigned char>(p[2]) & 0x3F) << 6) | (static_cast<unsigned char>(p[3]) & 0x3F);
            }
            
            if (nextCodepoint > 0) {
                cursorX += stbtt_GetCodepointKernAdvance(m_fontInfo, codepoint, nextCodepoint) * m_fontScale * aspectRatio;
            }
        }
    }
    
    return static_cast<int>(m_instances.size());
}

void TextSystem::GetTextBounds(float& outWidth, float& outHeight) const {
    outWidth = 0.0f;
    outHeight = 0.0f;
    
    if (m_instances.empty()) {
        return;
    }
    
    // Find min/max bounds of all glyph instances
    float minX = FLT_MAX;
    float minY = FLT_MAX;
    float maxX = -FLT_MAX;
    float maxY = -FLT_MAX;
    
    for (const auto& inst : m_instances) {
        minX = std::min(minX, inst.posX);
        minY = std::min(minY, inst.posY);
        maxX = std::max(maxX, inst.posX + inst.sizeX);
        maxY = std::max(maxY, inst.posY + inst.sizeY);
    }
    
    outWidth = maxX - minX;
    outHeight = maxY - minY;
}

void TextSystem::MeasureString(const char* text, float fontSize, float& outWidth, float& outHeight) {
    outWidth = 0.0f;
    outHeight = 0.0f;
    
    if (!m_initialized || !text) {
        return;
    }
    
    // Quantize font size
    int fontPx = static_cast<int>(std::round(fontSize));
    
    // Need to ensure font is set up for metrics
    float oldScale = m_fontScale;
    m_fontScale = stbtt_ScaleForPixelHeight(m_fontInfo, static_cast<float>(fontPx));
    
    float lineHeight = (m_ascent - m_descent + m_lineGap) * m_fontScale;
    float maxWidth = 0.0f;
    float currentLineWidth = 0.0f;
    int lineCount = 1;
    
    for (const char* p = text; *p; ++p) {
        int codepoint = static_cast<unsigned char>(*p);
        
        // Handle escape sequence: ^| -> U+258C LEFT HALF BLOCK
        if (codepoint == '^' && *(p+1) == '|') {
            codepoint = 0x258C;  // U+258C LEFT HALF BLOCK
            p++;  // Skip the '|' character
        }
        
        if (codepoint == '\n') {
            maxWidth = std::max(maxWidth, currentLineWidth);
            currentLineWidth = 0.0f;
            lineCount++;
        } else if (codepoint == 0x258C) {
            // Cursor glyph - use approximate advance (it's a half block)
            currentLineWidth += fontPx * 0.5f;
        } else {
            int glyphIndex = stbtt_FindGlyphIndex(m_fontInfo, codepoint);
            int advance = 0, leftBearing = 0;
            stbtt_GetGlyphHMetrics(m_fontInfo, glyphIndex, &advance, &leftBearing);
            currentLineWidth += advance * m_fontScale;
            if (*(p + 1) && *(p + 1) != '\n') {
                int nextCodepoint = static_cast<unsigned char>(*(p + 1));
                currentLineWidth += stbtt_GetCodepointKernAdvance(m_fontInfo, codepoint, nextCodepoint) * m_fontScale;
            }
        }
    }
    maxWidth = std::max(maxWidth, currentLineWidth);
    
    outWidth = maxWidth;
    outHeight = lineCount * lineHeight;
    
    // Restore old scale
    m_fontScale = oldScale;
}

uint16_t TextSystem::GetOrAssignGlyphID(int codepoint) {
    auto it = m_glyphIDMap.find(codepoint);
    if (it != m_glyphIDMap.end()) {
        return it->second;
    }
    uint16_t id = m_nextGlyphID++;
    m_glyphIDMap[codepoint] = id;
    return id;
}

bool TextSystem::GetGlyphUVRect(uint16_t glyphID, float* outU0, float* outV0, float* outU1, float* outV1) const {
    if (!outU0 || !outV0 || !outU1 || !outV1)
        return false;
    // Search for the codepoint that maps to this glyphID
    for (const auto& pair : m_glyphIDMap) {
        if (pair.second == glyphID) {
            auto cacheIt = m_glyphCache.find(pair.first);
            if (cacheIt != m_glyphCache.end()) {
                const GlyphMetric& m = cacheIt->second;
                *outU0 = m.u0;
                *outV0 = m.v0;
                *outU1 = m.u1;
                *outV1 = m.v1;
                return true;
            }
            break;
        }
    }
    return false;
}

void TextSystem::ExportAtlasToFile(const char* filename) {
    if (!m_initialized || m_atlasPixels.empty()) {
        LogToFile("[Text] ExportAtlasToFile: Atlas not initialized or empty");
        return;
    }
    
    // Save as PGM (grayscale) - simple format we can open
    std::ofstream file(filename, std::ios::binary);
    if (!file.is_open()) {
        LogToFile("[Text] ExportAtlasToFile: Failed to open %s", filename);
        return;
    }
    
    // PGM header
    file << "P5\n" << m_atlasWidth << " " << m_atlasHeight << "\n255\n";
    
    // Write pixel data
    file.write(reinterpret_cast<const char*>(m_atlasPixels.data()), m_atlasPixels.size());
    file.close();
    
    LogToFile("[Text] Atlas exported to %s (%dx%d)", filename, m_atlasWidth, m_atlasHeight);
}

void TextSystem::ExportGlyphDebug(const char* baseFilename) {
    if (!m_initialized || m_glyphCache.empty()) {
        LogToFile("[Text] ExportGlyphDebug: No glyphs to export");
        return;
    }
    
    // Get first cached glyph
    auto it = m_glyphCache.begin();
    int codepoint = it->first;
    const GlyphMetric& metric = it->second;
    
    LogToFile("[Text] ExportGlyphDebug: Exporting glyph %d (codepoint %d) size %dx%d", 
              stbtt_FindGlyphIndex(m_fontInfo, codepoint), codepoint, metric.width, metric.height);
    
    // Extract glyph bitmap from atlas
    if (metric.width > 0 && metric.height > 0) {
        // Convert UV coordinates back to pixel coordinates
        int atlasX = static_cast<int>(metric.u0 * m_atlasWidth);
        int atlasY = static_cast<int>(metric.v0 * m_atlasHeight);
        
        // Extract glyph from atlas
        std::vector<uint8_t> glyphBitmap(metric.width * metric.height);
        for (int y = 0; y < metric.height; y++) {
            int srcRow = atlasY + y;
            int dstRow = y;
            if (srcRow < m_atlasHeight) {
                std::memcpy(&glyphBitmap[dstRow * metric.width],
                           &m_atlasPixels[srcRow * m_atlasWidth + atlasX],
                           metric.width);
            }
        }
        
        // Export glyph bitmap
        char glyphName[256];
        snprintf(glyphName, sizeof(glyphName), "%s_glyph.pgm", baseFilename);
        std::ofstream glyphFile(glyphName, std::ios::binary);
        if (glyphFile.is_open()) {
            glyphFile << "P5\n" << metric.width << " " << metric.height << "\n255\n";
            glyphFile.write(reinterpret_cast<const char*>(glyphBitmap.data()), 
                           metric.width * metric.height);
            glyphFile.close();
            LogToFile("[Text] Glyph bitmap exported: %s (%dx%d)", 
                     glyphName, metric.width, metric.height);
        }
    }
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

extern "C" __declspec(dllexport)
int CR_TextLayout(TextSystemHandle handle, const char* text, float fontSize, uint32_t color) {
    // LogToFile("[Text] CR_TextLayout called: handle=%p, text='%s', fontSize=%.1f, color=0x%08X", 
    //           handle, text ? text : "(null)", fontSize, color);
    
    if (!handle) {
        LogToFile("[Text] CR_TextLayout FAILED: null handle");
        return 0;
    }
    TextSystem* ts = static_cast<TextSystem*>(handle);
    int count = ts->LayoutString(text, fontSize, color);
    // LogToFile("[Text] CR_TextLayout: LayoutString returned %d glyphs", count);
    return count;
}

extern "C" __declspec(dllexport)
int CR_TextLayoutEx(TextSystemHandle handle, const char* text, float fontSize, uint32_t color, float originX, float originY, float lineSpacing,
                    float aspectRatio) {
    auto* ts = static_cast<TextSystem*>(handle);
    if (!ts || !text) return 0;
    return ts->LayoutStringEx(text, fontSize, color, originX, originY, lineSpacing, aspectRatio);
}

extern "C" __declspec(dllexport)
void CR_TextGetBounds(TextSystemHandle handle, float* outWidth, float* outHeight) {
    if (!handle || !outWidth || !outHeight) {
        if (outWidth) *outWidth = 0.0f;
        if (outHeight) *outHeight = 0.0f;
        return;
    }
    TextSystem* ts = static_cast<TextSystem*>(handle);
    ts->GetTextBounds(*outWidth, *outHeight);
}

extern "C" __declspec(dllexport)
void CR_TextMeasure(TextSystemHandle handle, const char* text, float fontSize, float* outWidth, float* outHeight) {
    if (!handle || !outWidth || !outHeight) {
        if (outWidth) *outWidth = 0.0f;
        if (outHeight) *outHeight = 0.0f;
        return;
    }
    TextSystem* ts = static_cast<TextSystem*>(handle);
    ts->MeasureString(text, fontSize, *outWidth, *outHeight);
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

void CR_TextExportAtlas(TextSystemHandle handle, const char* filename) {
    if (!handle) return;
    TextSystem* ts = static_cast<TextSystem*>(handle);
    ts->ExportAtlasToFile(filename);
}

void CR_TextExportGlyphDebug(TextSystemHandle handle, const char* baseFilename) {
    if (!handle) return;
    TextSystem* ts = static_cast<TextSystem*>(handle);
    ts->ExportGlyphDebug(baseFilename);
}

uint16_t CR_TextGetGlyphID(TextSystemHandle handle, int codepoint) {
    if (!handle) return 0xFFFF; // Invalid glyph ID
    TextSystem* ts = static_cast<TextSystem*>(handle);
    return ts->GetOrAssignGlyphID(codepoint);
}
