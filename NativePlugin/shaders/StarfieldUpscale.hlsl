Texture2D<float3> SourceTexture : register(t0);
SamplerState linearSampler : register(s0);

struct PSInput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

float4 PSMain(PSInput input) : SV_Target
{
    // Simple bilinear upscale using hardware filtering
    return float4(SourceTexture.SampleLevel(linearSampler, input.uv, 0), 1.0);
}