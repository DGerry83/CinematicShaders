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
    float SpikeIntensity;    // 0.0-1.0, strength of spikes
    float SpikePad[3];       // Pad to 16 bytes
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

float3 SampleBloom(Texture2D<float3> tex, SamplerState samp, float2 uv, float2 direction, float threshold)
{
    // Threshold the center sample
    float3 tap = tex.SampleLevel(samp, uv, 0);
    float3 color = max(tap - threshold, 0.0) * Weights[0];
    
    [unroll]
    for (int i = 1; i < 3; i++)
    {
        float2 offset = direction * Offsets[i] * InvScreenSize;
        
        // Threshold positive offset sample
        tap = tex.SampleLevel(samp, uv + offset, 0);
        color += max(tap - threshold, 0.0) * Weights[i];
        
        // Threshold negative offset sample  
        tap = tex.SampleLevel(samp, uv - offset, 0);
        color += max(tap - threshold, 0.0) * Weights[i];
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
    
    // Sample normal texture alpha (dummy texture returns alpha=0, skyMask=1.0, stars render everywhere)
    float normalAlpha = NormalTexture.Sample(linearSampler, uv).a;
    float skyMask = (normalAlpha < DepthThreshold) ? 1.0 : 0.0;
    
    // Early exit for geometry - output transparent to preserve original pixel
    if (skyMask < 0.001)
        return float4(0.0, 0.0, 0.0, 0.0);
    
    // Sample base star color
    float3 starColor = StarHDRTexture.Sample(linearSampler, uv);
    
    // // Calculate diffraction spikes in Pass 2 (brightness-based, no random variation)
    // float3 spikeAccum = float3(0.0, 0.0, 0.0);
    // float spikeThreshold = 0.5; // Minimum brightness for spikes
    // float spikeRange = 8.0; // Brightness range for falloff calculation
    // int spikeLength = 25; // Maximum reach (pixels)
    // float baseDecay = 0.12; // Base falloff
    
    // // Sample neighbors for spikes (horizontal and vertical)
    // [loop]
    // for(int i = 1; i < spikeLength; i++)
    // {
    //     float decay = exp(-i * baseDecay);
    //     float2 hOffset = float2(i * InvScreenSize.x, 0.0);
    //     float2 vOffset = float2(0.0, i * InvScreenSize.y);
        
    //     // Horizontal spikes (left/right) - SampleLevel to avoid gradient warning in loop
    //     float3 leftStar = StarHDRTexture.SampleLevel(linearSampler, uv - hOffset, 0);
    //     float3 rightStar = StarHDRTexture.SampleLevel(linearSampler, uv + hOffset, 0);
        
    //     // Vertical spikes (up/down) - SampleLevel to avoid gradient warning in loop
    //     float3 upStar = StarHDRTexture.SampleLevel(linearSampler, uv - vOffset, 0);
    //     float3 downStar = StarHDRTexture.SampleLevel(linearSampler, uv + vOffset, 0);
        
    //     float leftLum = length(leftStar);
    //     float rightLum = length(rightStar);
    //     float upLum = length(upStar);
    //     float downLum = length(downStar);
        
    //     // Brightness-based scaling: top stars get full spikes, mid-bright get partial
    //     // pow(x, 2.0) creates steep falloff: 1.0 -> 1.0, 0.5 -> 0.25, 0.1 -> 0.01
    //     float leftScale = (leftLum > spikeThreshold) ? pow(saturate((leftLum - spikeThreshold) / spikeRange), 2.0) : 0.0;
    //     float rightScale = (rightLum > spikeThreshold) ? pow(saturate((rightLum - spikeThreshold) / spikeRange), 2.0) : 0.0;
    //     float upScale = (upLum > spikeThreshold) ? pow(saturate((upLum - spikeThreshold) / spikeRange), 2.0) : 0.0;
    //     float downScale = (downLum > spikeThreshold) ? pow(saturate((downLum - spikeThreshold) / spikeRange), 2.0) : 0.0;
        
    //     // Accumulate with intensity control and brightness scaling
    //     spikeAccum += leftStar * decay * SpikeIntensity * leftScale;
    //     spikeAccum += rightStar * decay * SpikeIntensity * rightScale;
    //     spikeAccum += upStar * decay * SpikeIntensity * upScale;
    //     spikeAccum += downStar * decay * SpikeIntensity * downScale;
    // }
    
    // // Add spikes to base color (before bloom)
    // starColor += spikeAccum;
    
    // Apply separable Gaussian blur on THRESHOLDED values only (bright stars)
    float3 bloomH = SampleBloom(StarHDRTexture, linearSampler, uv, float2(1.0, 0.0), BloomThreshold);
    float3 bloomV = SampleBloom(StarHDRTexture, linearSampler, uv, float2(0.0, 1.0), BloomThreshold);
    float3 bloom = (bloomH + bloomV) * 0.5;
    
    // Add bloom to base (no conditional needed - bloom already contains only bright contributions)
    float3 finalStarColor = starColor + bloom * BloomIntensity;
    
    // Apply ACES tonemapping if enabled
    if (EnableTonemapping > 0)
    {
        finalStarColor = aces_filmic(finalStarColor);
        finalStarColor = pow(max(finalStarColor, 0.0), 1.0 / 2.2);
    }
    
    // Return stars with full alpha (skyMask=1 means fully opaque star pixel)
    return float4(finalStarColor, 1.0);
}