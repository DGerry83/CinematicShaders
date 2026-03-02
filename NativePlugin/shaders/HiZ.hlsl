// Hi-Z Generation Compute Shader
// Generates hierarchical Z-buffer from full-resolution depth

Texture2D<float> SourceDepth : register(t0);
RWTexture2D<float> OutputHiZ : register(u0);

cbuffer HiZParams : register(b0)
{
    int2 SourceDimensions;      // Dimensions of source mip
    int IsFirstIteration;       // 1 if reading from raw depth, 0 if reading from HiZ
    int __Pad;                  
};

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)SourceDimensions.x / 2 || id.y >= (uint)SourceDimensions.y / 2)
        return;
    
    int2 baseCoord = int2(id.xy) * 2;
    
    float d0 = SourceDepth.Load(int3(baseCoord, 0));
    float d1 = SourceDepth.Load(int3(baseCoord + int2(1, 0), 0));
    float d2 = SourceDepth.Load(int3(baseCoord + int2(0, 1), 0));
    float d3 = SourceDepth.Load(int3(baseCoord + int2(1, 1), 0));
    
    // For reversed Z (Unity), max gives the closest depth
    float closestDepth = max(max(d0, d1), max(d2, d3));
    
    OutputHiZ[id.xy] = closestDepth;
}