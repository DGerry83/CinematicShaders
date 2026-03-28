// Kartographer Vertex Shader - Fullscreen Triangle
// Outputs UVs and position for fullscreen pass

struct VSOutput
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
};

VSOutput Main(uint vertexID : SV_VertexID)
{
    VSOutput output;
    
    // Generate fullscreen triangle positions from vertex ID
    // ID 0: (-1, -1) bottom-left
    // ID 1: (-1,  3) top-left (extends above viewport)
    // ID 2: ( 3, -1) bottom-right (extends right of viewport)
    float2 pos = float2(
        (vertexID == 2) ? 3.0f : -1.0f,  // x: -1 for IDs 0,1; 3 for ID 2
        (vertexID == 1) ? 3.0f : -1.0f   // y: -1 for IDs 0,2; 3 for ID 1
    );
    
    output.Position = float4(pos, 0.0f, 1.0f);
    
    // UVs from [-1,1] position to [0,1] UV space
    output.UV = pos * 0.5f + 0.5f;
    
    return output;
}
