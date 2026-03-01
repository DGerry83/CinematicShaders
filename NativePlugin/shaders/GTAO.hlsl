// GTAO Compute Shader - XeGTAO-based implementation
// Optimized for Unity Deferred rendering

Texture2D<float> g_DepthTexture : register(t0);
Texture2D<float4> g_NormalTexture : register(t1);
Texture2D<float> BlueNoiseTexture : register(t2);  // 256x256 R8 blue noise
RWTexture2D<float2> g_AOTexture : register(u0);  // x=AO, y=LinearDepth (viewZ)

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
    float MaxPixelRadius;       // NEW: Max sampling radius in pixels (was hardcoded 50.0)
    // float4 #4 (offset 48)
    float Intensity;
    float SampleDistributionPower;
    int SliceCount;
    int StepsPerSlice;
    // float4 #5 (offset 64)
    int FrameIndex;             // 0-7 temporal frame index
    float DepthMIPSamplingOffset; // Offset for Hi-Z mip level calculation
    float FadeStartDistance;    // NEW: Distance fade start (meters)
    float FadeEndDistance;      // NEW: Distance fade end (meters)
    // float4 #6 (offset 80)
    float FadeCurve;            // NEW: Fade curve power (1.0=linear)
    int DebugMode;              // 0=AO, 1=WorldNorm, 2=ViewNorm, 3=NormAlpha
    float __pad2;               // Padding
    float __pad3;               // Padding
    // float4 #7, #8, #9 (offset 96, 112, 128)
    float4 WorldToViewRow0;     // .xyz = row 0 of world-to-view matrix
    float4 WorldToViewRow1;     // .xyz = row 1 of world-to-view matrix
    float4 WorldToViewRow2;     // .xyz = row 2 of world-to-view matrix
};

// Blue noise for GTAO sampling
float2 GetGTANoise(uint2 pixelCoord, uint frameIndex)
{
    // Temporal shift: 31 is coprime with 256 for 8-frame cycle coverage
    uint temporalShift = (frameIndex * 31u) & 0xFFu;
    
    // Wrap to 256x256 texture coordinates
    uint2 baseCoord = (pixelCoord + temporalShift) & 0xFFu;
    
    // Spatial decorrelation offsets for slice vs step noise
    uint2 coordSlice = baseCoord;                          // For slice rotation
    uint2 coordStep = (baseCoord + uint2(37u, 17u)) & 0xFFu; // For step jitter
    
    // Load() returns 0.0-1.0 automatically for R8_UNORM
    float noiseSlice = BlueNoiseTexture.Load(int3(coordSlice, 0));
    float noiseStep = BlueNoiseTexture.Load(int3(coordStep, 0));
    
    return float2(noiseSlice, noiseStep);
}

// Stable reversed-Z linearization
// unpackConsts.x = near plane, unpackConsts.y = far plane
// Returns negative view-space Z (e.g., -0.21 to -750000)
float LinearizeDepth(float rawDepth, float2 nearFar)
{
    float n = nearFar.x;  // Near plane (positive)
    float f = nearFar.y;  // Far plane (positive)
    
    // For reversed Z: rawDepth 1.0 = near, 0.0 = far
    return -(n * f) / (rawDepth * (f - n) + n);
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
// Deferred mod stores full XYZ world normal in RGB
float3 UnpackNormal(float4 normalData)
{
    return normalData.rgb * 2.0 - 1.0;
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

    // DEBUG VISUALIZATION MODES
    if (DebugMode > 0)
    {
        if (DebugMode == 1) // World Normals
        {
            // Pack RGB into RG: R->R, G->G, B->(R+G)/2 or similar, or just store R and G
            float3 packed = worldNormal * 0.5 + 0.5; // 0-1 range
            g_AOTexture[coord] = float2(packed.r, packed.g); // Store RG, ignore B for now
            return;
        }
        else if (DebugMode == 2) // View Normals
        {
            if (length(worldNormal) < 0.001)
            {
                g_AOTexture[coord] = float2(0, 0); // Black for invalid
                return;
            }
            float3x3 worldToView = float3x3(
                WorldToViewRow0.xyz,
                WorldToViewRow1.xyz,
                WorldToViewRow2.xyz
            );
            float3 viewNormal = mul(worldToView, worldNormal);
            float3 packed = viewNormal * 0.5 + 0.5; // 0-1 range
            g_AOTexture[coord] = float2(packed.r, packed.g);
            return;
        }
        else if (DebugMode == 3) // Normal Alpha
        {
            g_AOTexture[coord] = float2(normalData.a, normalData.a);
            return;
        }
    }
    
    // Skip sky pixels using Deferred's normal alpha channel
    // Deferred mod: alpha = 0 for sky, alpha = 1 for geometry
    if (normalData.a < 0.5)
    {
        g_AOTexture[coord] = float2(1.0, 0.0);  // Sky: AO=1, depth=0
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
    
    // 4. Push toward camera (less negative Z) to avoid self-occlusion
    // Use 0.99999 for FP32 (0.01% offset = ~75 units at 750km far plane)
    viewZ *= 0.99999;
    pos = ComputeViewspacePosition(uv, viewZ, NDCToViewMul, NDCToViewAdd);
    
    // 5. Calculate screen-space radius (use abs to handle sign conventions)
    float2 pixelSizeAtViewZ = viewZ * NDCToViewMul * InvScreenSize;
    float screenSpaceRadius = EffectRadius / max(abs(pixelSizeAtViewZ.x), 0.0001);
    screenSpaceRadius = clamp(screenSpaceRadius, 2.0, MaxPixelRadius); // User-controlled limit

    if (MaxPixelRadius < 1.0) {
    g_AOTexture[coord] = float2(1.0, viewZ); // Bright red if MaxPixelRadius is broken
    return;
    }
    
    // 6. Minimum sample distance (avoid self-sampling center pixel)
    const float pixelTooCloseThreshold = 1.3;
    float minS = pixelTooCloseThreshold / screenSpaceRadius;
    
    // 8. Blue noise for temporal stability
    float2 localNoise = GetGTANoise(coord, (uint)FrameIndex);
    float noiseSlice = localNoise.x;    // For slice rotation
    float noiseStep = localNoise.y;     // For step distribution
    
    float visibility = 0;
    
    for (int s = 0; s < SliceCount; s++)
    {
        // Hemisphere only (PI radians, not 2*PI)
        float sliceK = ((float)s + noiseSlice) / (float)SliceCount;
        float phi = sliceK * 3.14159265359; // 180 degrees
        // XeGTAO: negate sin for Unity's coordinate system
        float2 omega = float2(cos(phi), -sin(phi));  // For sampling direction
        
        // Slice plane orientation - directionVec matches omega for consistency
        float3 directionVec = float3(omega.x, omega.y, 0.0);
        float3 orthoDirectionVec = directionVec - dot(directionVec, viewVec) * viewVec;
        float3 slicePlaneNormal = normalize(cross(orthoDirectionVec, viewVec));
        
        // Project normal to slice plane
        float3 projectedNormal = viewNormal - slicePlaneNormal * dot(viewNormal, slicePlaneNormal);
        float projectedNormalLength = length(projectedNormal);
        
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
                // Golden ratio progression with blue noise base
                float stepBaseNoise = frac(noiseStep + (float)step * 0.6180339887498948482);
                float stepBase = (float(step) + stepBaseNoise) / (float)StepsPerSlice;
                float t = pow(stepBase, SampleDistributionPower);
                t += minS; // Add offset to ensure first sample is at least 1.3 pixels away
                
                float2 sampleUV = uv + direction * t * screenSpaceRadius * InvScreenSize;
                
                if (any(sampleUV < 0.0) || any(sampleUV > 1.0))
                    break;
                    
                int2 sampleCoord = int2(sampleUV * ScreenSize);
                sampleCoord = clamp(sampleCoord, int2(0, 0), int2(width - 1, height - 1));
                
                // Hi-Z sampling: calculate mip level based on sample distance (in pixels)
                float sampleOffsetLength = t * screenSpaceRadius; // Distance in pixels
                // Sample depth directly (no threshold check - rely on falloff weight)
                float sampleRawDepth = g_DepthTexture[sampleCoord];
                float sampleViewZ = LinearizeDepth(sampleRawDepth, DepthUnpackConsts);
                
                float3 samplePos = ComputeViewspacePosition(sampleUV, sampleViewZ, NDCToViewMul, NDCToViewAdd);
                
                // REFERENCE-style falloff (inverse square, no thin occluder compensation)
                float3 delta = samplePos - pos;
                float distSq = dot(delta, delta);
                float3 deltaDir = normalize(delta);
                float elevationCos = dot(deltaDir, viewVec);
                
                // Inverse square falloff (REFERENCE style)
                float falloff = 1.0 / (1.0 + distSq / (EffectRadius * EffectRadius));
                elevationCos = lerp(lowHorizonCosDir, elevationCos, falloff);
                
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
    
    // REFERENCE-style intensity application
    float ao = lerp(1.0, visibility, saturate(Intensity));
    if (Intensity > 1.0)
        ao = lerp(ao, ao * ao, saturate(Intensity - 1.0));
    
    // Distance-based soft fade (replaces hard visual cutoff)
    float absViewZ = abs(viewZ);
    float fadeRange = FadeEndDistance - FadeStartDistance;
    float fadeFactor = 0.0;
    
    if (absViewZ > FadeStartDistance && fadeRange > 0.001) {
        fadeFactor = saturate((absViewZ - FadeStartDistance) / fadeRange);
        // Apply curve: <1.0 = soft start, 1.0 = linear, >1.0 = sharp edge
        fadeFactor = pow(fadeFactor, FadeCurve);
    }
    
    // Fade AO to white (1.0) as distance increases
    ao = lerp(ao, 1.0, fadeFactor);
    
    g_AOTexture[coord] = float2(saturate(ao), viewZ);  // Pack AO + linear depth
}