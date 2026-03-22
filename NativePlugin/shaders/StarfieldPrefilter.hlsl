Texture2D<float3> StarHDRTexture : register(t0);
SamplerState linearSampler : register(s0);

cbuffer PrefilterParams : register(b0)
{
    float2 SourceSize;      // Full-res source dimensions
    float2 InvSourceSize;   // 1.0 / SourceSize
    float BloomThreshold;   // Brightness threshold (e.g., 1.0)
    float BloomKnee;        // Soft knee width (e.g., 0.5 for smooth transition)
    float2 OutputSize;      // Quarter-res target dimensions (SourceSize / 4)
    float2 InvOutputSize;   // 1.0 / OutputSize
};

struct PSInput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

// Hard threshold to match Classic bloom behavior exactly
// Fixed: Soft knee was leaking sub-threshold brightness (23% at knee=threshold)
float3 ApplyBloomThreshold(float3 color, float threshold, float knee)
{
    // Use hard threshold subtraction like Classic: max(color - threshold, 0)
    return max(color - threshold, 0.0);
}

float4 PSMain(PSInput input) : SV_Target
{
    float2 uv = input.uv;
    float2 texel = InvSourceSize;
    
    // Proper 4x4 tent filter - samples entire footprint, no sparse corners
    float3 c = 0.0;
    float totalWeight = 0.0;
    
    // Center (weight 4)
    c += StarHDRTexture.SampleLevel(linearSampler, uv, 0) * 4.0;
    totalWeight += 4.0;
    
    // Edge centers at distance 1 (weights 2)
    c += StarHDRTexture.SampleLevel(linearSampler, uv + float2(-texel.x, 0.0), 0) * 2.0;
    c += StarHDRTexture.SampleLevel(linearSampler, uv + float2( texel.x, 0.0), 0) * 2.0;
    c += StarHDRTexture.SampleLevel(linearSampler, uv + float2(0.0, -texel.y), 0) * 2.0;
    c += StarHDRTexture.SampleLevel(linearSampler, uv + float2(0.0,  texel.y), 0) * 2.0;
    totalWeight += 8.0; // 4 * 2
    
    // Corners at distance sqrt(2) (weights 1)
    c += StarHDRTexture.SampleLevel(linearSampler, uv + float2(-texel.x, -texel.y), 0);
    c += StarHDRTexture.SampleLevel(linearSampler, uv + float2( texel.x, -texel.y), 0);
    c += StarHDRTexture.SampleLevel(linearSampler, uv + float2(-texel.x,  texel.y), 0);
    c += StarHDRTexture.SampleLevel(linearSampler, uv + float2( texel.x,  texel.y), 0);
    totalWeight += 4.0; // 4 * 1
    
    c /= totalWeight; // Divide by 16
    
    // Hard threshold to match Classic exactly
    float3 thresholded = max(c - BloomThreshold, 0.0);
    
    return float4(thresholded, 1.0);
}