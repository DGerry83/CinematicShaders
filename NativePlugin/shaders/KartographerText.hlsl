// Kartographer Text Compute Shader
// Scatters bitmap glyphs from atlas into a render target for sampling by KartographerPS
// 
// Inputs:
//   t0: Atlas texture (R8_UNORM bitmap coverage)
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
    float smoothing; // unused in bitmap path
};

cbuffer TextParams : register(b0)
{
    int GlyphCount;
    float2 OutputSize;
    float Pad;
};

Texture2D<float> Atlas : register(t0);
StructuredBuffer<GlyphData> Glyphs : register(t1);
RWTexture2D<float4> Output : register(u0);

SamplerState PointClamp : register(s0);

// Unpack ARGB color to float4 RGBA
float4 UnpackColor(uint color)
{
    float a = float((color >> 24) & 0xFF) / 255.0;
    float r = float((color >> 16) & 0xFF) / 255.0;
    float g = float((color >> 8) & 0xFF) / 255.0;
    float b = float(color & 0xFF) / 255.0;
    return float4(r, g, b, a);
}

// Check if pixel is inside box outline (not filled, just outline)
bool IsInsideBoxOutline(int2 pixel, float2 tl, float2 br, float thickness)
{
    // Convert UV to pixel coordinates
    float2 pixelTL = tl * OutputSize;
    float2 pixelBR = br * OutputSize;
    float pixelThickness = thickness * OutputSize.x; // Use X for thickness
    
    float2 p = float2(pixel);
    
    // Check if inside outer bounds
    bool insideOuter = p.x >= pixelTL.x && p.x <= pixelBR.x && 
                       p.y >= pixelTL.y && p.y <= pixelBR.y;
    
    // Check if outside inner bounds (for outline)
    bool outsideInner = p.x < pixelTL.x + pixelThickness || p.x > pixelBR.x - pixelThickness ||
                        p.y < pixelTL.y + pixelThickness || p.y > pixelBR.y - pixelThickness;
    
    return insideOuter && outsideInner;
}

[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint idx = id.x;
    
    // Normal glyph rendering
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
            // Sample at destination pixel center for accurate point sampling
            float2 local = float2(x + 0.5, y + 0.5) - g.pos;
            float2 uv = local / g.size;
            
            // Clamp to avoid right/bottom edge spill due to precision
            uv = saturate(uv);
            
            // Sample bitmap atlas (coverage alpha, not SDF distance)
            float2 atlasUV = float2(g.uv.x, g.uv.y) + uv * float2(g.uv.z, g.uv.w);
            float alpha = Atlas.SampleLevel(PointClamp, atlasUV, 0);
            
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
