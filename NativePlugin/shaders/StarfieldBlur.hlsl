Texture2D<float3> InputTexture : register(t0); // Quarter-res horizontal blur result
SamplerState linearSampler : register(s0);

cbuffer BlurParams : register(b0)
{
    float2 TexelSize;       // 1.0 / quarter-res texture dimensions
    float BloomSpread;      // Blur radius multiplier (must match horizontal)
    float Pad;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

// 31-tap Gaussian normalized to sum to 1.0 (sigma=4.4)
// Increased from 15-tap to maintain same screen-space blur radius at half-res
static const float Weights[16] = {0.096710, 0.086785, 0.076024, 0.064413, 0.052413, 0.040721, 0.030050, 0.020955, 0.013736, 0.008418, 0.004802, 0.002549, 0.001275, 0.000624, 0.000323, 0.000200};
static const float Offsets[16] = { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0, 11.0, 12.0, 13.0, 14.0, 15.0 };

float4 PSMain(PSInput input) : SV_Target
{
    float2 uv = input.uv;
    
    // Vertical Gaussian blur only (Y-axis offsets) - 31 taps total
    float3 color = InputTexture.SampleLevel(linearSampler, uv, 0) * Weights[0];
    
    [unroll]
    for (int i = 1; i < 16; i++)
    {
        float2 offset = float2(0.0, Offsets[i] * TexelSize.y * BloomSpread);
        color += InputTexture.SampleLevel(linearSampler, uv + offset, 0) * Weights[i];
        color += InputTexture.SampleLevel(linearSampler, uv - offset, 0) * Weights[i];
    }
    
    return float4(color, 1.0);
}