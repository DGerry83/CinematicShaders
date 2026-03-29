// Kartographer Text Compute Shader
// Scatters SDF glyphs from atlas into a render target for sampling by KartographerPS
// 
// Inputs:
//   t0: Atlas texture (R8_UNORM SDF)
//   t1: Glyph instance structured buffer
//   u0: Output texture (RGBA8 UAV)
//
// Constant Buffer:
//   b0: TextParams (glyph count, output size)

struct GlyphData
{
    float2 pos;      // Pixel position in text-RT space (top-left of quad)
    float2 size;     // Output size in pixels
    float4 uv;       // Atlas UV rect (x, y, width, height)
    uint color;      // Packed ARGB
    float smoothing; // 1.0 / (spread * scale)
};

cbuffer TextParams : register(b0)
{
    int GlyphCount;
    float2 OutputSize;
    float2 Pad;
};

Texture2D<float> Atlas : register(t0);
StructuredBuffer<GlyphData> Glyphs : register(t1);
RWTexture2D<float4> Output : register(u0);

SamplerState LinearClamp : register(s0);

// Unpack ARGB color to float4 RGBA
float4 UnpackColor(uint color)
{
    float a = float((color >> 24) & 0xFF) / 255.0;
    float r = float((color >> 16) & 0xFF) / 255.0;
    float g = float((color >> 8) & 0xFF) / 255.0;
    float b = float(color & 0xFF) / 255.0;
    return float4(r, g, b, a);
}

// DEBUG: Fill entire output texture with red
[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x < (uint)OutputSize.x && id.y < (uint)OutputSize.y)
    {
        Output[id.xy] = float4(1.0, 0.0, 0.0, 1.0);
    }
}
