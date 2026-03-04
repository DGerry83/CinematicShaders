// Starfield Pass 2: Bloom Composite with Depth Masking
// Samples HDR star texture, applies Gaussian bloom to bright values, masks by depth

Texture2D<float3> StarHDRTexture : register(t0);
Texture2D<float4> NormalTexture : register(t1);  // ARGB2101010, alpha = sky mask (0=sky, 1=geom)
Texture2D<float> DepthTexture : register(t2);    // RFloat depth buffer for debug visualization
SamplerState linearSampler : register(s0);

cbuffer CompositeParams : register(b0)
{
    float2 ScreenSize;
    float2 InvScreenSize;
    float BloomThreshold;    // Values > this get bloom (typically 1.0)
    float BloomIntensity;    // Multiplier for bloom contribution
    float DepthThreshold;    // Alpha < this = sky (0.0), Alpha >= this = geometry (1.0)
    float ExposureEV;        // Final exposure adjustment (if needed)
    int EnableTonemapping;   // 0 = linear, 1 = ACES
    int Pad[3];  // Pad to 16 bytes
};

// ============================================
// ACES Filmic Tonemapping (from Godot)
// ============================================
float3 aces_filmic(float3 x)
{
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return (x * (a * x + b)) / (x * (c * x + d) + e);
}

// ============================================
// Gaussian Blur (5-tap approximation)
// ============================================
static const float Weights[3] = { 0.38774, 0.24477, 0.06136 }; // Center, +1, +2 (normalized)
static const float Offsets[3] = { 0.0, 1.3846153846, 3.2307692308 };

float3 SampleBloom(Texture2D<float3> tex, SamplerState samp, float2 uv, float2 direction)
{
    float3 color = tex.SampleLevel(samp, uv, 0) * Weights[0];
    
    [unroll]
    for (int i = 1; i < 3; i++)
    {
        float2 offset = direction * Offsets[i] * InvScreenSize;
        color += tex.SampleLevel(samp, uv + offset, 0) * Weights[i];
        color += tex.SampleLevel(samp, uv - offset, 0) * Weights[i];
    }
    
    return color;
}

// ============================================
// MAIN ENTRY POINT (Pixel Shader)
// ============================================
struct PSInput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float4 PSMain(PSInput input) : SV_Target
{
    float2 uv = input.uv;
    
    // DEBUG: Raw depth buffer visualization - no interpretation
    float rawDepth = DepthTexture.Sample(linearSampler, uv);
    
    // Output raw depth as grayscale (multiplied by 100 to see subtle variations)
    // In reverse-z: 0.0 = far (sky), 1.0 = near (close geometry)
    float depthVis = rawDepth * 100.0f; // Amplify by 100x to see 0.00-0.01 range
    
    float4 debugColor = float4(depthVis, depthVis, depthVis, 1.0f);
    
    // Also output raw value to alpha for debugging if needed
    // Uncomment the next line to see debug visualization instead of stars
    return debugColor;
    
    // Sample normal texture alpha
    float normalAlpha = NormalTexture.Sample(linearSampler, uv).a;
    float skyMask = (normalAlpha < DepthThreshold) ? 1.0 : 0.0;
    
    // Early exit for geometry - output transparent to preserve original pixel
    if (skyMask < 0.001)
        return float4(0.0, 0.0, 0.0, 0.0);
    
    // Sample base star color
    float3 starColor = StarHDRTexture.Sample(linearSampler, uv);
    
    // Extract bright values for bloom
    float3 bright = max(starColor - BloomThreshold, 0.0);
    
    // Apply separable Gaussian blur
    float3 bloomH = SampleBloom(StarHDRTexture, linearSampler, uv, float2(1.0, 0.0));
    float3 bloomV = SampleBloom(StarHDRTexture, linearSampler, uv, float2(0.0, 1.0));
    float3 bloom = (bloomH + bloomV) * 0.5;
    
    // Add bloom to base
    float3 finalStarColor = starColor + bloom * BloomIntensity * (length(bright) > 0.0 ? 1.0 : 0.0);
    
    // Apply ACES tonemapping if enabled
    if (EnableTonemapping > 0)
    {
        finalStarColor = aces_filmic(finalStarColor);
        finalStarColor = pow(max(finalStarColor, 0.0), 1.0 / 2.2);
    }
    
    // Return stars with full alpha (skyMask=1 means fully opaque star pixel)
    return float4(finalStarColor, 1.0);
}