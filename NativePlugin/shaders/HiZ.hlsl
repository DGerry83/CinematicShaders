// Hi-Z Generation Compute Shader
// Generates hierarchical Z-buffer from full-resolution depth

Texture2D<float> SourceDepth : register(t0);
RWTexture2D<float> OutputHiZ : register(u0);

cbuffer HiZParams : register(b0)
{
    int2 SourceDimensions;      // Dimensions of source mip
    int IsFirstIteration;       // 1 if reading from raw depth, 0 if reading from HiZ
    int __Pad;                  // Padding to 16 bytes (4 floats total = 16 bytes)
};

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    // Don't write outside output bounds
    if (id.x >= (uint)SourceDimensions.x / 2 || id.y >= (uint)SourceDimensions.y / 2)
        return;
    
    // Sample 2x2 block from source
    int2 baseCoord = int2(id.xy) * 2;
    
    float d0 = SourceDepth.Load(int3(baseCoord, 0));
    float d1 = SourceDepth.Load(int3(baseCoord + int2(1, 0), 0));
    float d2 = SourceDepth.Load(int3(baseCoord + int2(0, 1), 0));
    float d3 = SourceDepth.Load(int3(baseCoord + int2(1, 1), 0));
    
    // For reversed Z (Unity), max gives the closest depth
    float closestDepth = max(max(d0, d1), max(d2, d3));
    
    OutputHiZ[id.xy] = closestDepth;
}
// GTAO Compute Shader - XeGTAO-based implementation
// Optimized for Unity Deferred rendering

Texture2D<float> g_DepthTexture : register(t0);
Texture2D<float4> g_NormalTexture : register(t1);
RWTexture2D<float> g_AOTexture : register(u0);
RWTexture2D<float4> g_DebugViewNormals : register(u1);  // Debug: view-space normals

SamplerState pointSampler : register(s0);  // Point sampler for Hi-Z depth sampling

cbuffer GTAOParams : register(b0)
{
    // float4 #1 (offset 0)
    float2 NDCToViewMul;        // tanHalfFOV * float2(2, -2)
    float2 NDCToViewAdd;        // tanHalfFOV * float2(-1, 1)
    // float4 #2 (offset 16)
    float2 DepthUnpackConsts;   // x = (far*near)/(far-near), y = -near/(far-near)
    float2 ScreenSize;
    // float4 #3 (offset 32)
    float2 InvScreenSize;
    float EffectRadius;
    float FalloffRange;
    // float4 #4 (offset 48)
    float Intensity;
    float SampleDistributionPower;
    int SliceCount;
    int StepsPerSlice;
    // float4 #5 (offset 64)
    int NoiseIndex;
    float DepthMIPSamplingOffset; // Offset for Hi-Z mip level calculation (typically 1.0-2.0)
    float __pad1;               // Padding
    float __pad2;               // Padding
    // float4 #6, #7, #8 (offset 80, 96, 112)
    float4 WorldToViewRow0;     // .xyz = row 0 of world-to-view matrix
    float4 WorldToViewRow1;     // .xyz = row 1 of world-to-view matrix
    float4 WorldToViewRow2;     // .xyz = row 2 of world-to-view matrix
};

// Simple R1 sequence for noise
float R1Noise(float idx)
{
    return frac(idx * 0.6180339887498948482);
}

// XeGTAO depth linearization for reversed Z
// unpackConsts.x = (far*near)/(far-near), unpackConsts.y = -near/(far-near)
// Result: mul / (add - rawDepth) gives correct negative view-space Z
float LinearizeDepth(float rawDepth, float2 unpackConsts)
{
    return unpackConsts.x / (unpackConsts.y - rawDepth);
}

// Fast viewspace reconstruction
float3 ComputeViewspacePosition(float2 uv, float viewZ, float2 ndcMul, float2 ndcAdd)
{
    float3 pos;
    pos.xy = (ndcMul * uv + ndcAdd) * viewZ;
    pos.z = viewZ;
    return pos;
}

// Unpack normal from Deferred's custom format (WORLD SPACE)
float3 UnpackNormal(float4 normalData)
{
    float3 normal;
    normal.xy = normalData.xy * 2.0 - 1.0;
    normal.z = sqrt(1.0 - saturate(dot(normal.xy, normal.xy)));
    normal.z = normalData.w > 0.5 ? normal.z : -normal.z;
    return normalize(normal);
}

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint2 coord = id.xy;
    uint width = (uint)ScreenSize.x;
    uint height = (uint)ScreenSize.y;
    
    if (coord.x >= width || coord.y >= height)
        return;
    
    float2 uv = (float2(coord) + 0.5) * InvScreenSize;
    float rawDepth = g_DepthTexture[coord];
    float4 normalData = g_NormalTexture[coord];
    float3 worldNormal = UnpackNormal(normalData);
    
    // Skip sky/invalid pixels - rely on Deferred's normal alpha for sky detection
    // (depth range check removed as it fails with reversed-Z and large far planes)
    if (length(worldNormal) < 0.001 || normalData.a < 0.1)
    {
        g_AOTexture[coord] = 1.0;
        return;
    }
    
    // 1. Linearize depth
    float viewZ = LinearizeDepth(rawDepth, DepthUnpackConsts);
    
    // 2. Reconstruct view position
    float3 pos = ComputeViewspacePosition(uv, viewZ, NDCToViewMul, NDCToViewAdd);
    float3 viewVec = normalize(-pos);
    
    // 3. Transform normal to VIEW SPACE using proper matrix
    float3x3 worldToView = float3x3(
        WorldToViewRow0.xyz,
        WorldToViewRow1.xyz,
        WorldToViewRow2.xyz
    );
    float3 viewNormal = mul(worldToView, worldNormal);
    
    // Debug output: view-space normals (mapped to 0-1 for visualization)
    g_DebugViewNormals[coord] = float4(viewNormal * 0.5 + 0.5, 1.0);
    
    // 4. Push into surface (away from camera) for contact shadows
    viewZ *= 1.0008;
    pos = ComputeViewspacePosition(uv, viewZ, NDCToViewMul, NDCToViewAdd);
    
    // 5. Calculate screen-space radius (-pixelSizeAtViewZ.x since viewZ is negative)
    float2 pixelSizeAtViewZ = viewZ * NDCToViewMul * InvScreenSize;
    float screenSpaceRadius = EffectRadius / max(-pixelSizeAtViewZ.x, 0.0001);
    
    // 6. Precompute falloff constants (XeGTAO correct formula)
    float falloffFrom = EffectRadius * (1.0 - FalloffRange);
    float falloffMul = -1.0 / (EffectRadius * FalloffRange);
    float falloffAdd = falloffFrom / (EffectRadius * FalloffRange) + 1.0;
    
    // 7. Minimum sample distance (avoid self-sampling center pixel)
    const float pixelTooCloseThreshold = 1.3;
    float minS = pixelTooCloseThreshold / screenSpaceRadius;
    
    // 8. Noise for temporal stability
    float noiseSlice = R1Noise((float)NoiseIndex + (float)coord.x * 0.5 + (float)coord.y * 0.3);
    
    float visibility = 0;
    
    for (int s = 0; s < SliceCount; s++)
    {
        // Hemisphere only (PI radians, not 2*PI)
        float sliceK = ((float)s + noiseSlice) / (float)SliceCount;
        float phi = sliceK * 3.14159265359; // 180 degrees
        // XeGTAO: negate sin for Unity's coordinate system
        float2 omega = float2(cos(phi), -sin(phi));
        
        // Project omega onto view plane for correct slice plane
        float3 directionVec = float3(omega, 0.0);
        float3 orthoDirectionVec = directionVec - dot(directionVec, viewVec) * viewVec;
        float3 slicePlaneNormal = normalize(cross(orthoDirectionVec, viewVec));
        
        // Project normal to slice plane
        float3 projectedNormal = viewNormal - slicePlaneNormal * dot(viewNormal, slicePlaneNormal);
        float projectedNormalLength = length(projectedNormal);
        
        if (projectedNormalLength < 0.001)
            continue;
            
        // XeGTAO normal angle calculation (reuse orthoDirectionVec from slice plane calc)
        float3 projectedNormalDir = projectedNormal / projectedNormalLength;
        float signNorm = sign(dot(orthoDirectionVec, projectedNormal));
        float cosNorm = saturate(dot(projectedNormalDir, viewVec));
        float n = signNorm * acos(cosNorm);
        float cosN = cos(n);
        float sinN = sin(n);
        
        // Initialize horizons based on normal angle
        float lowHorizonCos0 = cos(n + 1.570796); // n + PI/2
        float lowHorizonCos1 = cos(n - 1.570796); // n - PI/2
        // XeGTAO: horizonCos[0] = +omega direction, horizonCos[1] = -omega direction
        float2 horizonCos = float2(lowHorizonCos0, lowHorizonCos1);
        float2 lowHorizonCos = float2(lowHorizonCos0, lowHorizonCos1);
        
        // Sample both directions
        for (int dir = 0; dir < 2; dir++)
        {
            float2 direction = (dir == 0) ? omega : -omega;
            float lowHorizonCosDir = lowHorizonCos[dir];
            
            for (int step = 0; step < StepsPerSlice; step++)
            {
                // Distribution with minS offset to avoid self-sampling
                float stepBase = (float(step) + 0.5) / (float)StepsPerSlice;
                float stepNoise = R1Noise((float)NoiseIndex + (float)s * 7 + (float)step * 13);
                float t = pow(stepBase + stepNoise * 0.1, SampleDistributionPower);
                t += minS; // Add offset to ensure first sample is at least 1.3 pixels away
                
                float2 sampleUV = uv + direction * t * screenSpaceRadius * InvScreenSize;
                
                if (any(sampleUV < 0.0) || any(sampleUV > 1.0))
                    break;
                    
                int2 sampleCoord = int2(sampleUV * ScreenSize);
                sampleCoord = clamp(sampleCoord, int2(0, 0), int2(width - 1, height - 1));
                
                // Hi-Z sampling: calculate mip level based on sample distance (in pixels)
                float sampleOffsetLength = t * screenSpaceRadius; // Distance in pixels
                float mipLevel = clamp(log2(sampleOffsetLength) - DepthMIPSamplingOffset, 0.0, 8.0);
                
                float sampleRawDepth = g_DepthTexture.SampleLevel(pointSampler, sampleUV, mipLevel);
                if (sampleRawDepth < 0.001 || sampleRawDepth > 0.999)
                    continue;
                    
                float sampleViewZ = LinearizeDepth(sampleRawDepth, DepthUnpackConsts);
                float3 samplePos = ComputeViewspacePosition(sampleUV, sampleViewZ, NDCToViewMul, NDCToViewAdd);
                
                // XeGTAO thin occluder compensation: pristine geometry for horizon, modified for falloff
                float3 delta = samplePos - pos;
                
                // 1. Calculate geometric direction from UNMODIFIED delta for accurate horizon angle
                float3 deltaDir = normalize(delta);
                float elevationCos = dot(deltaDir, viewVec);
                
                // 2. Apply thin occluder compensation ONLY to falloff distance
                const float thinOccluderCompensation = 0.5;
                float falloffBase = length(float3(delta.x, delta.y, delta.z * (1.0 + thinOccluderCompensation)));
                
                // 3. XeGTAO falloff - use falloffBase for weight, lerp with lowHorizonCos
                float weight = saturate(falloffBase * falloffMul + falloffAdd);
                elevationCos = lerp(lowHorizonCosDir, elevationCos, weight);
                
                horizonCos[dir] = max(horizonCos[dir], elevationCos);
            }
        }
        
        // XeGTAO horizon integration - CRITICAL: h0 negative, h1 positive
        // horizonCos[0] is from +omega direction, horizonCos[1] from -omega
        // h0 represents angle below horizontal (negative), h1 above (positive)
        float h0 = -acos(clamp(horizonCos[1], -1.0, 1.0)); // Negative! From -omega dir
        float h1 =  acos(clamp(horizonCos[0], -1.0, 1.0)); // Positive! From +omega dir
        
        float iarc0 = (cosN + 2.0 * h0 * sinN - cos(2.0 * h0 - n)) / 4.0;
        float iarc1 = (cosN + 2.0 * h1 * sinN - cos(2.0 * h1 - n)) / 4.0;
        
        float localVisibility = projectedNormalLength * (iarc0 + iarc1);
        visibility += localVisibility;  // XeGTAO: no saturate per slice
    }
    
    visibility /= (float)SliceCount;
    // Apply XeGTAO's final value power
    visibility = pow(visibility, 2.0);  // Default FinalValuePower = 2.0
    float ao = lerp(1.0, visibility, Intensity);
    g_AOTexture[coord] = saturate(ao);
}

