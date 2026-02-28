// GTAO_Output.hlsl
// Full-screen composite output with proper full-screen triangle

struct VSOut
{
    float4 pos : SV_Position;
    float2 uv  : TEXCOORD0;
};

cbuffer OutputParams : register(b0)
{
    int OutputMode;      // 0=Composite, 1=Raw AO
    int __padding[3];
};

// Full-screen triangle covering [-1,3] in clip space
// No vertex buffer required - uses SV_VertexID
VSOut VSMain(uint id : SV_VertexID)
{
    VSOut o;
    
    // Generate positions that cover the screen with one large triangle
    // id 0: (-1, -1) bottom-left
    // id 1: ( 3, -1) way right, bottom  
    // id 2: (-1,  3) top, way up
    float2 pos = float2(
        id == 1 ? 3.0 : -1.0,  // x: -1 or 3
        id == 2 ? 3.0 : -1.0   // y: -1 or 3
    );
    
    o.pos = float4(pos, 0.0, 1.0);
    
    // UVs: (0,0), (2,0), (0,2) - correct interpolation covers 0-1 range
    o.uv = float2(
        id == 1 ? 2.0 : 0.0,
        id == 2 ? 2.0 : 0.0
    );
    
    return o;
}

Texture2D<float>   g_AOTexture     : register(t0);  // Filtered AO (R32_FLOAT)
Texture2D<float4>  g_SceneTexture  : register(t1);  // Scene color (optional)

SamplerState g_LinearSampler : register(s0)
{
    Filter = MIN_MAG_MIP_LINEAR;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

float4 PSMain(VSOut i) : SV_Target0
{
    // Flip V coordinate to fix upside-down AO
    float2 uv = float2(i.uv.x, 1.0 - i.uv.y);
    
    float ao = g_AOTexture.Sample(g_LinearSampler, uv);
    
    if (OutputMode == 1)
    {
        // Raw AO Debug: Grayscale output
        return float4(ao, ao, ao, 1.0);
    }
    else
    {
        // Composite: Multiply AO into scene color
        float4 scene = g_SceneTexture.Sample(g_LinearSampler, uv);
        
        // Multiplicative occlusion
        return scene * ao;
    }
}
