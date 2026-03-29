#pragma once
#include <d3d11.h>
#include <cstdint>
#include <vector>
#include <unordered_map>
#include <string>

// Forward declare stbtt_fontinfo to avoid including stb_truetype.h in header
struct stbtt_fontinfo;

namespace CinematicShaders {

// Glyph metric data - atlas UVs and pixel dimensions
struct GlyphMetric {
    float advance;      // Horizontal advance for layout
    float leftBearing;  // Left side bearing
    float topBearing;   // Top side bearing
    float u0, v0;       // Top-left UV in atlas [0-1]
    float u1, v1;       // Bottom-right UV in atlas [0-1]
    int width;          // Pixel width in atlas
    int height;         // Pixel height in atlas
    
    GlyphMetric() : advance(0), leftBearing(0), topBearing(0),
                    u0(0), v0(0), u1(0), v1(0), width(0), height(0) {}
};

// Instance data for GPU rendering - matches C# GlyphData struct
struct GlyphInstance {
    float posX;      // Pixel position X in text RT space
    float posY;      // Pixel position Y in text RT space  
    float sizeX;     // Output quad width in pixels
    float sizeY;     // Output quad height in pixels
    float uvX;       // Atlas UV rect X
    float uvY;       // Atlas UV rect Y
    float uvW;       // Atlas UV rect width
    float uvH;       // Atlas UV rect height
    uint32_t color;  // Packed ARGB
    float smoothing; // 1.0 / (spread * scale)
    
    GlyphInstance() : posX(0), posY(0), sizeX(0), sizeY(0),
                      uvX(0), uvY(0), uvW(0), uvH(0), color(0xFFFFFFFF), smoothing(1.0f) {}
};

// Text rendering system using SDF atlas
class TextSystem {
public:
    TextSystem();
    ~TextSystem();
    
    // Initialize with device and TTF file path
    // atlasSize: width/height of SDF atlas texture (e.g., 1024)
    bool Init(ID3D11Device* device, const wchar_t* ttfPath, int atlasSize = 1024);
    void Shutdown();
    
    // Returns true if initialized
    bool IsInitialized() const { return m_initialized; }
    
    // Layout a string for rendering. Returns glyph count.
    // fontSize: desired pixel height (e.g., 24.0f)
    // color: packed ARGB (e.g., 0xFFFFFFFF for white)
    int LayoutString(const char* text, float fontSize, uint32_t color);
    
    // Accessors for C# interop
    ID3D11ShaderResourceView* GetAtlasSRV() const { return m_atlasSRV; }
    ID3D11Texture2D* GetAtlasTexture() const { return m_atlasTex; }
    const GlyphInstance* GetGlyphPtr() const { return m_instances.empty() ? nullptr : m_instances.data(); }
    int GetGlyphCount() const { return static_cast<int>(m_instances.size()); }
    int GetAtlasSize() const { return m_atlasWidth; }
    
    // Create/update D3D11 buffer with current glyph instances for compute shader
    ID3D11Buffer* GetOrCreateGlyphBuffer();
    ID3D11ShaderResourceView* GetGlyphBufferSRV() { return m_glyphBufferSRV; }
    
    // Debug: Export atlas to PGM file
    void ExportAtlasToFile(const char* filename);
    
    // Debug: Export first glyph's intermediate steps
    void ExportGlyphDebug(const char* baseFilename);
    
private:
    // Ensure glyph is packed into atlas (rasterizes if needed)
    bool PackGlyph(int codepoint);
    
    // Rasterize glyph to bitmap using stb_truetype
    uint8_t* RasterizeGlyph(int codepoint, int& outW, int& outH);
    
    // Generate SDF from bitmap using 8SSEDT
    void GenerateSDF(const uint8_t* bitmap, int w, int h, uint8_t* outSDF, int outW, int outH);
    
    // Upload glyph SDF to atlas texture
    void UpdateAtlasRegion(int x, int y, int w, int h, const uint8_t* data);
    
private:
    bool m_initialized;
    ID3D11Device* m_device;
    ID3D11DeviceContext* m_context;
    
    // D3D11 resources
    ID3D11Texture2D* m_atlasTex;
    ID3D11ShaderResourceView* m_atlasSRV;
    
    // Font data
    std::vector<uint8_t> m_ttfData;
    stbtt_fontinfo* m_fontInfo;
    float m_fontScale;
    int m_ascent;
    int m_descent;
    int m_lineGap;
    
    // Atlas packing state
    std::vector<uint8_t> m_atlasPixels;  // CPU-side atlas for SDF generation
    int m_atlasWidth;
    int m_atlasHeight;
    int m_atlasX;        // Current packing position
    int m_atlasY;
    int m_atlasRowHeight;
    
    // Glyph cache
    std::unordered_map<int, GlyphMetric> m_glyphCache;
    
    // Per-layout instance buffer
    std::vector<GlyphInstance> m_instances;
    
    // D3D11 glyph buffer for compute shader (created on demand)
    ID3D11Buffer* m_glyphBuffer = nullptr;
    ID3D11ShaderResourceView* m_glyphBufferSRV = nullptr;
    int m_glyphBufferCapacity = 0;
    
    // SDF parameters
    static constexpr int SDF_PADDING = 4;      // Padding around glyph in atlas
    static constexpr int SDF_SPREAD = 4;       // Distance field spread in pixels
    static constexpr int SDF_DOWN_SAMPLE = 4;  // High-res bitmap is 4x final SDF size
};

} // namespace CinematicShaders

// C interface for extern "C" exports
extern "C" {
    // Opaque handle type
typedef void* TextSystemHandle;
    
    // Create/destroy text system
    __declspec(dllexport) TextSystemHandle CR_TextInit(ID3D11Texture2D* deviceSourceTexture, const wchar_t* fontPath);
    __declspec(dllexport) void CR_TextShutdown(TextSystemHandle handle);
    
    // Layout text
    __declspec(dllexport) int CR_TextLayout(TextSystemHandle handle, const char* text, float fontSize, uint32_t color);
    
    // Access results
    __declspec(dllexport) ID3D11ShaderResourceView* CR_TextGetAtlasSRV(TextSystemHandle handle);
    __declspec(dllexport) const CinematicShaders::GlyphInstance* CR_TextGetGlyphPtr(TextSystemHandle handle);
    __declspec(dllexport) int CR_TextGetGlyphCount(TextSystemHandle handle);
    
    // Debug export
    __declspec(dllexport) void CR_TextExportAtlas(TextSystemHandle handle, const char* filename);
    __declspec(dllexport) void CR_TextExportGlyphDebug(TextSystemHandle handle, const char* baseFilename);
}
