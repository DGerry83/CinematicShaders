// Kartographer Pixel Shader - Test version (orange additive)
// Renders a simple orange color to verify the rendering pipeline

cbuffer KartographerConstants : register(b0)
{
    float4x4 ViewMatrix;
    float4x4 ProjMatrix;
    float4 CameraRight;
    float4 CameraUp;
    float4 CameraForward;
    float2 Resolution;
    float Time;
    float Padding;
};

struct PSInput
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
};

float4 PSMain(PSInput input) : SV_Target
{
    // Test: Solid bright orange for pipeline verification
    // This will blend additively over the starfield
    float3 orange = float3(1.0f, 0.5f, 0.0f);
    return float4(orange, 1.0f);
}
