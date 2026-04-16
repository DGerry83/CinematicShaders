#include "../include/ConsoleConstants_hlsl.hlsl"

cbuffer ConsoleConstantsBuffer : register(b0) {
    ConsoleConstants g_consoleConstants;
};

struct VSInput {
    float2 LocalPos : POSITION;
    float2 LocalUV  : TEXCOORD0;
    float2 Pos      : TEXCOORD1;
    float2 Size     : TEXCOORD2;
    float4 UVRect   : TEXCOORD3;
    uint   Color    : TEXCOORD4;
};

struct PSInput {
    float4 Pos : SV_POSITION;
    float2 UV  : TEXCOORD0;
    float4 Col : COLOR;
};

PSInput Main(VSInput input) {
    PSInput output;
    float2 worldPos = input.Pos + input.LocalPos * input.Size;

    float4x4 Projection = float4x4(
        g_consoleConstants.ProjectionM00, g_consoleConstants.ProjectionM01, g_consoleConstants.ProjectionM02, g_consoleConstants.ProjectionM03,
        g_consoleConstants.ProjectionM10, g_consoleConstants.ProjectionM11, g_consoleConstants.ProjectionM12, g_consoleConstants.ProjectionM13,
        g_consoleConstants.ProjectionM20, g_consoleConstants.ProjectionM21, g_consoleConstants.ProjectionM22, g_consoleConstants.ProjectionM23,
        g_consoleConstants.ProjectionM30, g_consoleConstants.ProjectionM31, g_consoleConstants.ProjectionM32, g_consoleConstants.ProjectionM33
    );

    output.Pos = mul(float4(worldPos, 0.0, 1.0), Projection);
    output.UV = input.UVRect.xy + input.LocalUV * input.UVRect.zw;

    float4 col;
    col.a = float((input.Color >> 24) & 0xFF) / 255.0;
    col.r = float((input.Color >> 16) & 0xFF) / 255.0;
    col.g = float((input.Color >>  8) & 0xFF) / 255.0;
    col.b = float((input.Color      ) & 0xFF) / 255.0;
    output.Col = col;

    return output;
}
