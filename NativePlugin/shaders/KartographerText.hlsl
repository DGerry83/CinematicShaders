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

[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint idx = id.x;
    if (idx >= (uint)GlyphCount)
        return;
    
    GlyphData g = Glyphs[idx];
    float4 col = UnpackColor(g.color);
    
    // Calculate pixel bounds for this glyph
    int2 minPixel = int2(g.pos);
    int2 maxPixel = int2(g.pos + g.size);
    
    // Clamp to output bounds
    minPixel = clamp(minPixel, int2(0, 0), int2(OutputSize));
    maxPixel = clamp(maxPixel, int2(0, 0), int2(OutputSize));
    
    // Rasterize glyph quad
    for (int y = minPixel.y; y < maxPixel.y; y++)
    {
        for (int x = minPixel.x; x < maxPixel.x; x++)
        {
            float2 pixelPos = float2(x, y);
            float2 uv = (pixelPos - g.pos) / g.size;
            
            // Sample atlas
            float2 atlasUV = float2(g.uv.x, g.uv.y) + uv * float2(g.uv.z, g.uv.w);
            float dist = Atlas.SampleLevel(LinearClamp, atlasUV, 0);
            
            // Convert SDF to alpha (0.5 = edge in normalized SDF)
            float alpha = smoothstep(0.5 - g.smoothing, 0.5 + g.smoothing, dist);
            
            if (alpha > 0.0)
            {
                uint2 coord = uint2(x, y);
                float4 src = col;
                src.a *= alpha;
                
                // Alpha blend with existing content
                float4 dst = Output[coord];
                Output[coord] = src + dst * (1.0 - src.a);
            }
        }
    }
}
