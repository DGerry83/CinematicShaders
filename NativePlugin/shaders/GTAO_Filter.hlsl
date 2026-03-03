// GTAO Normal-Aware Gather Filter
// 16 samples via 4 Gather calls (4x4 neighborhood)

Texture2D<float2>  g_RawAO    : register(t0);  // x=AO, y=LinearDepth
Texture2D<float4>  g_Normals  : register(t1);  // View-space normals (RGB10A2_UNORM)
RWTexture2D<float> g_Filtered : register(u0); 

SamplerState g_PointSampler : register(s0); // Point clamp

cbuffer FilterParams : register(b0)
{
    float2 InvScreenSize;
    float NormalPower;      // Try 32.0 (higher = sharper edges)
    float DepthSigma;       // Try 0.5 (meters, view space)
};

float3 UnpackNormal(float3 packed)
{
    return packed * 2.0 - 1.0;
}

// Gaussian weight for depth similarity
float DepthWeight(float centerDepth, float sampleDepth, float sigma)
{
    float diff = centerDepth - sampleDepth;
    return exp(-(diff * diff) / (2.0 * sigma * sigma));
}

// Cosine power weight for normal similarity  
float NormalWeight(float3 center, float3 sample, float power)
{
    return pow(saturate(dot(center, sample)), power);
}

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint2 coord = id.xy;
    float2 uv = (coord + 0.5) * InvScreenSize;
    
    // Load center data (not gathered, exact pixel)
    float2 centerData = g_RawAO[coord];
    float centerAO = centerData.x;
    float centerDepth = centerData.y;
    float3 centerNormal = UnpackNormal(g_Normals[coord].xyz);
    
    // Accumulators for weighted bilateral filter
    // Using regression approach: accumulate moments for linear fit ao = a + b*depth
    float sumW = 0.001; // Epsilon to prevent div-zero
    float sumW_AO = 0;
    float sumW_Depth = 0;
    float sumW_AOxDepth = 0;
    float sumW_DepthSq = 0;
    
    // Gather offsets for 4x4 neighborhood sampling
    // Each Gather samples 2x2 pixels. 4 Gathers = 16 unique samples.
    const int2 offsets[4] = { int2(-1,-1), int2(1,-1), int2(-1,1), int2(1,1) };
    
    for(int i = 0; i < 4; i++)
    {
        // Gather4: returns 4 samples in 2x2 block around (uv + offset*pixelSize)
        float4 ao4 = g_RawAO.GatherRed(g_PointSampler, uv, offsets[i]);
        float4 depth4 = g_RawAO.GatherGreen(g_PointSampler, uv, offsets[i]);
        float4 nx4 = g_Normals.GatherRed(g_PointSampler, uv, offsets[i]);
        float4 ny4 = g_Normals.GatherGreen(g_PointSampler, uv, offsets[i]);
        float4 nz4 = g_Normals.GatherBlue(g_PointSampler, uv, offsets[i]);
        
        for(int j = 0; j < 4; j++)
        {
            float sampleAO = ao4[j];
            float sampleDepth = depth4[j];
            float3 sampleNormal = UnpackNormal(float3(nx4[j], ny4[j], nz4[j]));
            
            // Combined edge-stopping weight
            float normalW = NormalWeight(centerNormal, sampleNormal, NormalPower);
            float depthW = DepthWeight(centerDepth, sampleDepth, DepthSigma);
            // If normals agree strongly (>0.95), ignore depth completely and blend fully
            // Otherwise, use standard bilateral weighting
            float w = (normalW > 0.95) ? 1.0 : normalW * depthW;
            
            // Accumulate for linear regression (bilateral moment preservation)
            sumW += w;
            sumW_AO += w * sampleAO;
            sumW_Depth += w * sampleDepth;
            sumW_AOxDepth += w * sampleAO * sampleDepth;
            sumW_DepthSq += w * sampleDepth * sampleDepth;
        }
    }
    
    // Solve weighted linear regression: AO = alpha + beta * Depth
    // Preserves gradients across smooth surfaces while normal/depth weights stop at edges
    // but the normal/depth weights stop the regression at edges
    float cov = sumW_AOxDepth - (sumW_Depth * sumW_AO / sumW);
    float var = sumW_DepthSq - (sumW_Depth * sumW_Depth / sumW);
    
    float beta = cov / max(var, 0.0001); // Slope
    float alpha = (sumW_AO / sumW) - beta * (sumW_Depth / sumW); // Intercept
    
    // Evaluate at center depth (bilateral filter result)
    float filteredAO = alpha + beta * centerDepth;
    
    // Fallback: if variance is too low (flat area), just use weighted average
    // This prevents numerical instability on perfectly flat walls
    if(var < 0.001)
        filteredAO = sumW_AO / sumW;
    
    g_Filtered[coord] = saturate(filteredAO);
}
