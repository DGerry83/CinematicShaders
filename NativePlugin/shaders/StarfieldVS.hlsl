struct PSInput {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

PSInput Main(uint vertexID : SV_VertexID) {
    PSInput output;
    float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
    output.position = float4(uv * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    output.uv = uv;
    return output;
}