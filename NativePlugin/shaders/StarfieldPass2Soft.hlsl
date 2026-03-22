Texture2D<float3> StarHDRTexture : register(t0);      // Full-res stars
Texture2D<float3> BloomTexture : register(t1);        // Quarter-res blurred bloom
SamplerState linearSampler : register(s0);

cbuffer SoftCompositeParams : register(b0)
{
    float2 ScreenSize;
    float2 InvScreenSize;
    float BloomIntensity;     // Final intensity multiplier
    float ExposureEV;
    int EnableTonemapping;
    float Pad1;
    
    // Atmospheric extinction
    float ExtinctionZenith;
    float ExtinctionHorizon;
    float2 Pad2;
    
    float3 AtmosphereUp;
    float Pad3;
};

// ACES Filmic Tonemapping
float3 aces_filmic(float3 x)
{
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return (x * (a * x + b)) / (x * (c * x + d) + e);
}

struct PSInput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float4 PSMain(PSInput input) : SV_Target
{
    float2 uv = input.uv;
    
    // Atmospheric extinction calculation (match classic shader exactly)
    float2 centerOffset = uv - 0.5;
    float distFromCenter = length(centerOffset) * 2.0;
    float t = saturate(distFromCenter);
    t = t * t; // Non-linear for airmass curve
    float extinction = lerp(ExtinctionZenith, ExtinctionHorizon, t);
    
    // Sample full-res stars with extinction
    float3 starColor = StarHDRTexture.Sample(linearSampler, uv) * extinction;
    
    // Sample bloom (quarter-res, hardware bilinear will upsample)
    // Fixed: Removed erroneous *2.0 that caused blowout at all intensity levels
    float3 bloom = BloomTexture.Sample(linearSampler, uv) * extinction * BloomIntensity;
    
    // Composite: stars + bloom
    // CRITICAL FIX: Exposure already applied in Compute Shader (Pass 1)
    // Classic mode does NOT re-apply exposure in pixel shader, so Soft shouldn't either
    float3 finalColor = starColor + bloom;
    
    // Tonemapping
    if (EnableTonemapping > 0)
    {
        finalColor = aces_filmic(finalColor);
        finalColor = pow(max(finalColor, 0.0), 1.0 / 2.2);
    }
    
    return float4(finalColor, 1.0);
}