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

// 15-tap Gaussian normalized to sum to 1.0 (sigma=2.2)
static const float Weights[8] = { 
    0.1815, 0.1635, 0.120, 0.072, 0.035, 0.014, 0.0045, 0.001 
};
static const float Offsets[8] = { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0 };

float4 PSMain(PSInput input) : SV_Target
{
    float2 uv = input.uv;
    
    // Vertical Gaussian blur only (Y-axis offsets)
    float3 color = InputTexture.SampleLevel(linearSampler, uv, 0) * Weights[0];
    
    [unroll]
    for (int i = 1; i < 8; i++)
    {
        float2 offset = float2(0.0, Offsets[i] * TexelSize.y * BloomSpread);
        color += InputTexture.SampleLevel(linearSampler, uv + offset, 0) * Weights[i];
        color += InputTexture.SampleLevel(linearSampler, uv - offset, 0) * Weights[i];
    }
    
    return float4(color, 1.0);
}