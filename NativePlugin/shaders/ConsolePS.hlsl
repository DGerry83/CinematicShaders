Texture2D Atlas : register(t0);
SamplerState PointSampler : register(s0);

struct PSInput {
    float4 Pos : SV_POSITION;
    float2 UV  : TEXCOORD0;
    float4 Col : COLOR;
};

float4 Main(PSInput input) : SV_TARGET {
    float alpha = Atlas.Sample(PointSampler, input.UV).r;
    return float4(input.Col.rgb, input.Col.a * alpha);
}
