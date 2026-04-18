// Kartographer Pixel Shader - Holographic Grid Overlay
// Spherical coordinate grid with chromatic aberration, phosphor mask, and vignette
// Phase 1: Added debug SDF shapes (circle, rounded box)
// Phase 2: Added selection circle for star tracking
// Phase 3: Fixed tangent plane projection for grid labels
//
// COORDINATE SPACE NOTES:
// - Shader UV space: center = (0,0), +X = right, +Y = up
// - Screen UV [0,1] maps to: x = (u-0.5)*2*aspect, y = (v-0.5)*2
// - params.DebugBoxTopLeft is the actual top-left corner of the box in shader-UV
// - Box center in shader is computed as: topLeft + size*0.5

struct PSInput {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

#include "../include/KartographerParams_hlsl.hlsl"

cbuffer KartographerCB : register(b0) {
    KartographerParams params;
};

// Grid colors: 0=Seafoam, 1=Amber, 2=White, 3=Green
static const float3 kGridColors[4] = {
    float3(0.1, 0.9, 0.7),   // Seafoam
    float3(1.0, 0.65, 0.0),  // Amber
    float3(0.85, 0.95, 1.0), // White
    float3(0.25, 1.0, 0.0)   // Green
};

// Text textures (rendered by compute shader)
Texture2D<float4> TextTexture : register(t2);              // Star selector text
Texture2D<float4> GridLabelTextures[12] : register(t3);    // One texture per label slot (t3-t14)
Texture2D<float4> VesselTargetTextTexture : register(t15); // Vessel target text
Texture2DArray<float4> NavballIcons : register(t16);
Texture2D<float4> PointingIcon : register(t17);
Texture2D<float4> ManeuverTextTexture : register(t18);       // Navball icon MSDF textures (7 icons)
SamplerState TextSampler : register(s0);
SamplerState PointSampler : register(s1);                  // Point sampler for MSDF

// Grid size presets: 0=Jumbo, 1=Large, 2=Medium, 3=Small, 4=Tiny

static const float meridianNoise_Jumbo[8] = {
    0.920, 1.140, 0.780, 1.050, 1.220, 0.850, 0.960, 1.080
};

static const float parallelNoise_Jumbo[5] = {
    1.050, 0.820, 1.130, 0.910, 1.180
};

static const float meridianNoise_Large[12] = {
    0.920, 1.140, 0.780, 1.050, 1.220, 0.850,
    0.960, 1.080, 0.730, 1.180, 0.880, 1.020
};

static const float parallelNoise_Large[8] = {
    1.050, 0.820, 1.130, 0.910, 1.180, 0.760, 0.980, 1.060
};

static const float meridianNoise_Medium[16] = {
    0.920, 1.140, 0.780, 1.050, 1.220, 0.850,
    0.960, 1.080, 0.730, 1.180, 0.880, 1.020,
    0.810, 1.150, 0.940, 1.070
};

static const float parallelNoise_Medium[10] = {
    1.050, 0.820, 1.130, 0.910, 1.180,
    0.760, 0.980, 1.060, 0.890, 1.140
};

static const float meridianNoise_Small[24] = {
    0.920, 1.140, 0.780, 1.050, 1.220, 0.850,
    0.960, 1.080, 0.730, 1.180, 0.880, 1.020,
    0.810, 1.150, 0.940, 1.070, 0.990, 1.210,
    0.840, 1.030, 1.160, 0.910, 0.950, 1.100
};

static const float parallelNoise_Small[15] = {
    1.050, 0.820, 1.130, 0.910, 1.180,
    0.760, 0.980, 1.060, 0.890, 1.140,
    0.830, 1.010, 1.190, 0.930, 1.070
};

static const float meridianNoise_Tiny[32] = {
    0.920, 1.140, 0.780, 1.050, 1.220, 0.850,
    0.960, 1.080, 0.730, 1.180, 0.880, 1.020,
    0.810, 1.150, 0.940, 1.070, 0.990, 1.210,
    0.840, 1.030, 1.160, 0.910, 0.950, 1.100,
    0.800, 1.130, 0.870, 1.040, 1.200, 0.920, 0.970, 1.090
};

static const float parallelNoise_Tiny[20] = {
    1.050, 0.820, 1.130, 0.910, 1.180,
    0.760, 0.980, 1.060, 0.890, 1.140,
    0.830, 1.010, 1.190, 0.930, 1.070,
    0.790, 1.000, 1.170, 0.860, 1.120
};

// Helper: Transform view-space vector to world-space
float3 ViewToWorld(float3 v, float3 right, float3 up, float3 forward) {
    return v.x * right + v.y * up + v.z * forward;
}

// Helper: Apply yaw/pitch rotation to view ray
float3 ApplyPreRotation(float3 ray, float yaw, float pitch) {
    float cy = cos(yaw);
    float sy = sin(yaw);
    float cx = cos(pitch);
    float sx = sin(pitch);
    
    float3 r1 = float3(
        ray.x * cy - ray.z * sy,
        ray.y,
        ray.x * sy + ray.z * cy
    );
    
    float3 r2 = float3(
        r1.x,
        r1.y * cx - r1.z * sx,
        r1.y * sx + r1.z * cx
    );
    
    return normalize(r2);
}

// ============================================================================
// SDF Helpers for Debug Shapes (Phase 1)
// ============================================================================

float SDF_Circle(float2 p, float2 center, float radius) {
    return length(p - center) - radius;
}

// Flicker animation for fluorescent tube effect
// t = 0 -> always off, t = 1 -> always on
// In between: random on/off with duty cycle = t
float Flicker(float t, float time, float hash) {
    if (t <= 0.0) return 0.0;
    if (t >= 1.0) return 1.0;
    float noise = frac(sin(hash * 43758.5453) * 12.9898 + time * 30.0);
    return noise < t ? 1.0 : 0.0;
}

float SDF_RoundedBox(float2 p, float2 center, float2 halfSize, float radius) {
    float2 d = abs(p - center) - halfSize + radius;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - radius;
}

// ============================================================================
// Navball Icon Rendering (Phase 4c) - MSDF-based screen-space icons
// ============================================================================

// Median of three values (for MSDF decoding)
float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

// Render a single navball icon using MSDF texture sampling
float3 RenderNavballIcon(int iconIndex, float2 center, float intensity, uint colorOverride,
    float2 uv, float3 gridColor, float iconSize, float thickness)
{
    if (intensity <= 0.001) return float3(0, 0, 0);
    
    // Calculate local UV in icon space
    float2 localUV = (uv - center) / iconSize + 0.5;
    // localUV.y = 1.0 - localUV.y;  // Removed: +Y=up now aligns shader UV with texture V
    
    // Discard if outside icon bounds
    if (localUV.x < 0.0 || localUV.x > 1.0 || localUV.y < 0.0 || localUV.y > 1.0)
        return float3(0, 0, 0);
    
    // Sample MSDF from texture array
    float3 msd = NavballIcons.SampleLevel(PointSampler, float3(localUV, iconIndex), 0).rgb;
    
    // MSDF decoding: median of RGB channels minus 0.5 gives signed distance
    // where 0 = edge, positive = inside, negative = outside
    float sd = median(msd.r, msd.g, msd.b) - 0.5;
    
    // Apply thickness offset (negative = thinner lines, positive = thicker lines)
    // thickness range: -0.1 to +0.1 is reasonable, 0 = default
    sd = sd + thickness;
    
    // Anti-aliased edge using fwidth
    float edgeWidth = fwidth(sd);
    float alpha = smoothstep(-edgeWidth, edgeWidth, sd);
    
    // Get color (override or use grid color)
    float3 color = gridColor;
    if (colorOverride != 0) {
        float r = float((colorOverride >> 16) & 0xFF) / 255.0;
        float g = float((colorOverride >> 8) & 0xFF) / 255.0;
        float b = float(colorOverride & 0xFF) / 255.0;
        color = float3(r, g, b);
    }
    
    return color * alpha * intensity;
}

// Calculate grid glow for a given ray direction and preset
float3 CalculateGrid(float3 ray, int preset, int colorIdx) {
    float numLong, numLat;
    switch(preset) {
        case 0: numLong = 8.0;  numLat = 5.0;  break;
        case 1: numLong = 12.0; numLat = 8.0;  break;
        case 2: numLong = 16.0; numLat = 10.0; break;
        case 3: numLong = 24.0; numLat = 15.0; break;
        default: numLong = 32.0; numLat = 20.0; break;
    }
    
    float thetaStep = 6.2831853 / numLong;
    float phiStep = 3.1415927 / numLat;
    int maxMeridianIdx = int(numLong) - 1;
    int maxParallelIdx = int(numLat) - 1;
    
    // Convert to spherical coordinates
    float phi = acos(clamp(ray.y, -1.0, 1.0));
    float theta = atan2(ray.z, ray.x);
    
    float3 gridColor = kGridColors[colorIdx];
    
    // --- MERIDIANS ---
    float t = (theta + 3.14159265) / thetaStep - 0.5;
    int cellIdx = int(floor(t));
    int mLeft = cellIdx;
    int mRight = cellIdx + 1;
    if (mLeft < 0) mLeft = maxMeridianIdx;
    if (mRight > maxMeridianIdx) mRight = 0;
    
    float distLeft = abs(theta - (-3.14159265 + (float(mLeft) + 0.5) * thetaStep));
    distLeft = min(distLeft, 6.2831853 - distLeft);
    float distRight = abs(theta - (-3.14159265 + (float(mRight) + 0.5) * thetaStep));
    distRight = min(distRight, 6.2831853 - distRight);
    
    float surfLeft = sin(phi) * distLeft;
    float surfRight = sin(phi) * distRight;
    
    float noiseLeft, noiseRight;
    switch(preset) {
        case 0:  noiseLeft = meridianNoise_Jumbo[mLeft];  noiseRight = meridianNoise_Jumbo[mRight];  break;
        case 1:  noiseLeft = meridianNoise_Large[mLeft];  noiseRight = meridianNoise_Large[mRight];  break;
        case 2:  noiseLeft = meridianNoise_Medium[mLeft]; noiseRight = meridianNoise_Medium[mRight]; break;
        case 3:  noiseLeft = meridianNoise_Small[mLeft];  noiseRight = meridianNoise_Small[mRight];  break;
        default: noiseLeft = meridianNoise_Tiny[mLeft];   noiseRight = meridianNoise_Tiny[mRight];   break;
    }
    
    // Pole fade
    float poleFadeStart = 0.5 * phiStep;
    float poleFade = smoothstep(0.0, poleFadeStart, phi) * 
                     smoothstep(3.1415927, 3.1415927 - poleFadeStart, phi);
    
    float3 glowM = gridColor * params.GridIntensity * (
        noiseLeft / (surfLeft + params.GridThickness) + 
        noiseRight / (surfRight + params.GridThickness)
    ) * poleFade;
    
    // --- PARALLELS ---
    float p = phi / phiStep - 0.5;
    int pCell = int(floor(p));
    int pLow = max(0, min(maxParallelIdx, pCell));
    int pHigh = max(0, min(maxParallelIdx, pCell + 1));
    
    float distLow = abs(phi - (float(pLow) + 0.5) * phiStep);
    float distHigh = abs(phi - (float(pHigh) + 0.5) * phiStep);
    
    float noiseLow, noiseHigh;
    switch(preset) {
        case 0:  noiseLow = parallelNoise_Jumbo[pLow];  noiseHigh = parallelNoise_Jumbo[pHigh];  break;
        case 1:  noiseLow = parallelNoise_Large[pLow];  noiseHigh = parallelNoise_Large[pHigh];  break;
        case 2:  noiseLow = parallelNoise_Medium[pLow]; noiseHigh = parallelNoise_Medium[pHigh]; break;
        case 3:  noiseLow = parallelNoise_Small[pLow];  noiseHigh = parallelNoise_Small[pHigh];  break;
        default: noiseLow = parallelNoise_Tiny[pLow];   noiseHigh = parallelNoise_Tiny[pHigh];   break;
    }
    
    float3 glowP = gridColor * params.GridIntensity * (
        noiseLow / (distLow + params.GridThickness) + 
        noiseHigh / (distHigh + params.GridThickness)
    );
    
    return glowM + glowP;
}

// ============================================================================
// Grid Label Rendering - Fixed Tangent Plane Projection
// ============================================================================

// Render a single grid label using tangent plane projection
// labelPos: position on unit sphere (also the normal)
// labelTangent: tangent vector (east/west along parallel)
// sizeX, sizeY: world space size of the label quad
// texIndex: which texture slot to sample
// ray: view direction in world space
// gridColor: color to tint the label
float3 RenderGridLabel(
    float3 labelPos, float3 labelTangent, float sizeX, float sizeY,
    int texIndex, float3 ray, float3 gridColor,
    float2 caOffsetUV  // chromatic aberration offset in UV space
) {
    // Debug visualization: if debug bit is set for this label, show solid color
    if ((params.GridLabelDebugMask & (1u << texIndex)) != 0) {
        // Ray-plane intersection to determine if we hit the label area
        float3 labelNormal = normalize(labelPos);
        float3 labelBitangent = normalize(cross(labelNormal, labelTangent));
        
        float denom = dot(ray, labelNormal);
        if (abs(denom) < 0.001) return float3(0, 0, 0);
        
        float t = 1.0 / denom;
        if (t < 0.0) return float3(0, 0, 0);
        
        float3 hitPoint = ray * t;
        float3 localPos = hitPoint - labelPos;
        
        // Bottom-left anchored: u increases east, v increases south (flipped from texture)
        float u = dot(localPos, labelTangent) / sizeX;
        float v = -dot(localPos, labelBitangent) / sizeY + 1.0;
        
        // Return bright debug color (no texture sampling)
        if (u >= 0.0 && u <= 1.0 && v >= 0.0 && v <= 1.0) {
            // Different color per label for identification
            float3 debugColors[12] = {
                float3(1.0, 0.0, 0.0),    // Red (label 0 - HUCK)
                float3(0.0, 1.0, 0.0),    // Green (label 1)
                float3(0.0, 0.0, 1.0),    // Blue (label 2)
                float3(1.0, 1.0, 0.0),    // Yellow (label 3)
                float3(1.0, 0.0, 1.0),    // Magenta (label 4)
                float3(0.0, 1.0, 1.0),    // Cyan (label 5)
                float3(1.0, 0.5, 0.0),    // Orange (label 6)
                float3(1.0, 1.0, 1.0),    // White (label 7)
                float3(0.5, 0.0, 1.0),    // Purple (label 8)
                float3(0.0, 0.5, 0.0),    // Dark Green (label 9)
                float3(0.5, 0.5, 0.0),    // Olive (label 10)
                float3(0.0, 0.5, 0.5)     // Teal (label 11)
            };
            return debugColors[texIndex];
        }
        return float3(0, 0, 0);
    }
    
    // Camera is at origin in view space, which is also world origin
    // The view ray has already been transformed to world space
    float3 cameraPos = float3(0.0, 0.0, 0.0);
    
    // Normal at label position (for unit sphere at origin, normal = position)
    float3 labelNormal = normalize(labelPos);
    
    // Calculate bitangent = cross(normal, tangent)
    // This gives us a complete orthonormal frame: normal, tangent, bitangent
    float3 labelBitangent = normalize(cross(labelNormal, labelTangent));
    
    // Intersect ray with tangent plane at label position
    // Plane equation: dot(P - labelPos, labelNormal) = 0
    // Ray: P = cameraPos + t * rayDir = t * rayDir (since camera is at origin)
    // Solve: dot(t * rayDir - labelPos, labelNormal) = 0
    // t * dot(rayDir, labelNormal) - dot(labelPos, labelNormal) = 0
    // t = dot(labelPos, labelNormal) / dot(rayDir, labelNormal)
    // Since |labelPos| = 1 and labelNormal = normalize(labelPos):
    // dot(labelPos, labelNormal) = |labelPos| = 1 (approximately, due to normalization)
    
    float denom = dot(ray, labelNormal);
    
    // If denom is near zero, ray is parallel to plane - skip this label
    if (abs(denom) < 0.001) return float3(0, 0, 0);
    
    float t = 1.0 / denom;  // Since dot(labelPos, labelNormal) â‰ˆ 1 for unit sphere
    
    // If t < 0, intersection is behind camera
    if (t < 0.0) return float3(0, 0, 0);
    
    // Intersection point
    float3 hitPoint = ray * t;
    
    // Vector from label center to hit point
    float3 localPos = hitPoint - labelPos;
    
    // Project onto tangent frame to get UV coordinates
    // Bottom-left corner anchored: labelPos is the bottom-left of the quad
    // u increases east along tangent, v increases south (flipped so text reads top-to-bottom)
    float u = dot(localPos, labelTangent) / sizeX;
    float v = -dot(localPos, labelBitangent) / sizeY + 1.0;
    
    // Check if within label bounds [0, 1]
    if (u < 0.0 || u > 1.0 || v < 0.0 || v > 1.0) return float3(0, 0, 0);
    
    float2 uv = float2(u, v);
    
    // Apply chromatic aberration
    float2 uvR = uv + caOffsetUV * 0.5;
    float2 uvG = uv;
    float2 uvB = uv - caOffsetUV * 0.5;
    
    // Sample texture (clamp to avoid artifacts at edges)
    // Use switch since dynamic texture array indexing requires unroll
    float labelR = 0.0, labelG = 0.0, labelB = 0.0;
    switch(texIndex) {
        case 0:
            labelR = GridLabelTextures[0].SampleLevel(TextSampler, saturate(uvR), 0).r;
            labelG = GridLabelTextures[0].SampleLevel(TextSampler, saturate(uvG), 0).r;
            labelB = GridLabelTextures[0].SampleLevel(TextSampler, saturate(uvB), 0).r;
            break;
        case 1:
            labelR = GridLabelTextures[1].SampleLevel(TextSampler, saturate(uvR), 0).r;
            labelG = GridLabelTextures[1].SampleLevel(TextSampler, saturate(uvG), 0).r;
            labelB = GridLabelTextures[1].SampleLevel(TextSampler, saturate(uvB), 0).r;
            break;
        case 2:
            labelR = GridLabelTextures[2].SampleLevel(TextSampler, saturate(uvR), 0).r;
            labelG = GridLabelTextures[2].SampleLevel(TextSampler, saturate(uvG), 0).r;
            labelB = GridLabelTextures[2].SampleLevel(TextSampler, saturate(uvB), 0).r;
            break;
        case 3:
            labelR = GridLabelTextures[3].SampleLevel(TextSampler, saturate(uvR), 0).r;
            labelG = GridLabelTextures[3].SampleLevel(TextSampler, saturate(uvG), 0).r;
            labelB = GridLabelTextures[3].SampleLevel(TextSampler, saturate(uvB), 0).r;
            break;
        case 4:
            labelR = GridLabelTextures[4].SampleLevel(TextSampler, saturate(uvR), 0).r;
            labelG = GridLabelTextures[4].SampleLevel(TextSampler, saturate(uvG), 0).r;
            labelB = GridLabelTextures[4].SampleLevel(TextSampler, saturate(uvB), 0).r;
            break;
        case 5:
            labelR = GridLabelTextures[5].SampleLevel(TextSampler, saturate(uvR), 0).r;
            labelG = GridLabelTextures[5].SampleLevel(TextSampler, saturate(uvG), 0).r;
            labelB = GridLabelTextures[5].SampleLevel(TextSampler, saturate(uvB), 0).r;
            break;
        case 6:
            labelR = GridLabelTextures[6].SampleLevel(TextSampler, saturate(uvR), 0).r;
            labelG = GridLabelTextures[6].SampleLevel(TextSampler, saturate(uvG), 0).r;
            labelB = GridLabelTextures[6].SampleLevel(TextSampler, saturate(uvB), 0).r;
            break;
        case 7:
            labelR = GridLabelTextures[7].SampleLevel(TextSampler, saturate(uvR), 0).r;
            labelG = GridLabelTextures[7].SampleLevel(TextSampler, saturate(uvG), 0).r;
            labelB = GridLabelTextures[7].SampleLevel(TextSampler, saturate(uvB), 0).r;
            break;
        case 8:
            labelR = GridLabelTextures[8].SampleLevel(TextSampler, saturate(uvR), 0).r;
            labelG = GridLabelTextures[8].SampleLevel(TextSampler, saturate(uvG), 0).r;
            labelB = GridLabelTextures[8].SampleLevel(TextSampler, saturate(uvB), 0).r;
            break;
        case 9:
            labelR = GridLabelTextures[9].SampleLevel(TextSampler, saturate(uvR), 0).r;
            labelG = GridLabelTextures[9].SampleLevel(TextSampler, saturate(uvG), 0).r;
            labelB = GridLabelTextures[9].SampleLevel(TextSampler, saturate(uvB), 0).r;
            break;
        case 10:
            labelR = GridLabelTextures[10].SampleLevel(TextSampler, saturate(uvR), 0).r;
            labelG = GridLabelTextures[10].SampleLevel(TextSampler, saturate(uvG), 0).r;
            labelB = GridLabelTextures[10].SampleLevel(TextSampler, saturate(uvB), 0).r;
            break;
        case 11:
            labelR = GridLabelTextures[11].SampleLevel(TextSampler, saturate(uvR), 0).r;
            labelG = GridLabelTextures[11].SampleLevel(TextSampler, saturate(uvG), 0).r;
            labelB = GridLabelTextures[11].SampleLevel(TextSampler, saturate(uvB), 0).r;
            break;
    }
    
    // Apply per-label intensity and optional color override
    float intensity;
    uint colorOverride;
    switch(texIndex) {
        case 0: intensity = params.LabelIntensity0; colorOverride = params.LabelColor0; break;
        case 1: intensity = params.LabelIntensity1; colorOverride = params.LabelColor1; break;
        case 2: intensity = params.LabelIntensity2; colorOverride = params.LabelColor2; break;
        case 3: intensity = params.LabelIntensity3; colorOverride = params.LabelColor3; break;
        case 4: intensity = params.LabelIntensity4; colorOverride = params.LabelColor4; break;
        case 5: intensity = params.LabelIntensity5; colorOverride = params.LabelColor5; break;
        case 6: intensity = params.LabelIntensity6; colorOverride = params.LabelColor6; break;
        case 7: intensity = params.LabelIntensity7; colorOverride = params.LabelColor7; break;
        case 8: intensity = params.LabelIntensity8; colorOverride = params.LabelColor8; break;
        case 9: intensity = params.LabelIntensity9; colorOverride = params.LabelColor9; break;
        case 10: intensity = params.LabelIntensity10; colorOverride = params.LabelColor10; break;
        case 11: intensity = params.LabelIntensity11; colorOverride = params.LabelColor11; break;
        default: intensity = 1.0; colorOverride = 0; break;
    }
    
    float3 finalColor = gridColor;
    // If color override is non-zero, use it (unpack ARGB to RGB)
    if (colorOverride != 0) {
        float r = float((colorOverride >> 16) & 0xFF) / 255.0;
        float g = float((colorOverride >> 8) & 0xFF) / 255.0;
        float b = float(colorOverride & 0xFF) / 255.0;
        finalColor = float3(r, g, b);
    }
    
    return finalColor * float3(labelR, labelG, labelB) * intensity;
}

// ============================================================================
// Pointing Icon Rendering (Heading Indicator)
// ============================================================================

float3 RenderPointingIcon(float2 center, float rotation, float intensity, uint colorOverride,
    float2 uv, float3 gridColor, float iconSize)
{
    if (intensity <= 0.001) return float3(0, 0, 0);
    
    // Compute offset from center in screen space
    float2 delta = uv - center;
    
    // Map to square space (icon height = iconSize, icon width = 2*iconSize)
    float2 sq;
    sq.x = delta.x / iconSize;
    sq.y = delta.y / iconSize;
    
    // Inverse rotate in square space so the texture rotates correctly as a flat billboard
    float cosA = cos(-rotation);
    float sinA = sin(-rotation);
    float2 isq;
    isq.x = sq.x * cosA - sq.y * sinA;
    isq.y = sq.x * sinA + sq.y * cosA;
    
    // Map back to texture UV, accounting for 2:1 aspect
    float2 texUV;
    texUV.x = isq.x / 2.0 + 0.5;
    texUV.y = isq.y + 0.5;
    
    // Soft edge fade to hide the texture boundary (2-pixel margin on 256x128)
    float2 edgeFadeUV = saturate(texUV * 64.0) * saturate((1.0 - texUV) * 64.0);
    float edgeFade = min(edgeFadeUV.x, edgeFadeUV.y);
    
    // Clamp to valid range and sample MSDF texture
    texUV = saturate(texUV);
    float3 msd = PointingIcon.SampleLevel(PointSampler, texUV, 0).rgb;
    
    // MSDF decoding
    float sd = median(msd.r, msd.g, msd.b) - 0.5;
    float edgeWidth = fwidth(sd);
    float alpha = smoothstep(-edgeWidth, edgeWidth, sd) * edgeFade;
    
    // Get color
    float3 color = gridColor;
    if (colorOverride != 0) {
        float r = float((colorOverride >> 16) & 0xFF) / 255.0;
        float g = float((colorOverride >> 8) & 0xFF) / 255.0;
        float b = float(colorOverride & 0xFF) / 255.0;
        color = float3(r, g, b);
    }
    
    return color * alpha * intensity;
}

// ============================================================================
// Maneuver Text Rendering
// ============================================================================

float3 RenderManeuverText(float2 origin, float2 size, float intensity, float2 uv, float3 gridColor)
{
    if (intensity <= 0.001) return float3(0, 0, 0);
    
    float2 localUV = (uv - origin) / size;
    
    if (localUV.x < 0.0 || localUV.x > 1.0 || localUV.y < 0.0 || localUV.y > 1.0)
        return float3(0, 0, 0);
    
    float coverage = ManeuverTextTexture.SampleLevel(TextSampler, localUV, 0).r;
    return gridColor * coverage * intensity;
}

// ============================================================================
// Box Outline Drawing (Layer 3 Single-Texture Refactor)
// ============================================================================

// Draw a box outline for hover feedback on interactive elements
// Uses grid color for the outline, hard corners, 2px thickness
float3 DrawBoxOutline(float2 uv, float3 baseColor, float3 outlineColor)
{
    if (!params.BoxOutlineEnabled)
        return baseColor;
    
    // 2 pixel thickness in UV space
    float thickX = 2.0 / params.Resolution.x;
    float thickY = 2.0 / params.Resolution.y;
    
    // Check edges
    bool onLeftEdge = abs(uv.x - params.BoxTopLeft.x) < thickX;
    bool onRightEdge = abs(uv.x - params.BoxBottomRight.x) < thickX;
    bool onTopEdge = abs(uv.y - params.BoxTopLeft.y) < thickY;
    bool onBottomEdge = abs(uv.y - params.BoxBottomRight.y) < thickY;
    
    bool insideX = uv.x >= params.BoxTopLeft.x && uv.x <= params.BoxBottomRight.x;
    bool insideY = uv.y >= params.BoxTopLeft.y && uv.y <= params.BoxBottomRight.y;
    
    // Draw outline (hard corners, single line)
    if ((onLeftEdge || onRightEdge) && insideY)
    {
        return outlineColor;
    }
    if ((onTopEdge || onBottomEdge) && insideX)
    {
        return outlineColor;
    }
    
    return baseColor;
}

// ============================================================================
// Main Pixel Shader
// ============================================================================

float4 PSMain(PSInput input) : SV_Target {
    float2 fragCoord = input.uv * params.Resolution;
    
    float aspect = params.Resolution.x / params.Resolution.y;
    float2 uv = float2(
        (input.uv.x - 0.5) * 2.0 * aspect,
        (input.uv.y - 0.5) * 2.0
    );
    
    float2 perp = float2(-uv.y, uv.x) * params.ChromaticAberrationStrength;
    
    float2 uvR = uv + perp;
    float2 uvG = uv;
    float2 uvB = uv - perp;
    
    float focalLength = max(params.FocalLength, 0.001);
    
    float3 rayR = normalize(float3(uvR.x, uvR.y, focalLength));
    float3 rayG = normalize(float3(uvG.x, uvG.y, focalLength));
    float3 rayB = normalize(float3(uvB.x, uvB.y, focalLength));
    
    rayR = ViewToWorld(rayR, params.CameraRight, params.CameraUp, params.CameraForward);
    rayG = ViewToWorld(rayG, params.CameraRight, params.CameraUp, params.CameraForward);
    rayB = ViewToWorld(rayB, params.CameraRight, params.CameraUp, params.CameraForward);
    
    rayR = ApplyPreRotation(rayR, params.PreRotationYaw, params.PreRotationPitch);
    rayG = ApplyPreRotation(rayG, params.PreRotationYaw, params.PreRotationPitch);
    rayB = ApplyPreRotation(rayB, params.PreRotationYaw, params.PreRotationPitch);
    
    int preset = params.GridSizePreset;
    int colorIdx = params.GridColorIndex;
    float3 colR = CalculateGrid(rayR, preset, colorIdx);
    float3 colG = CalculateGrid(rayG, preset, colorIdx);
    float3 colB = CalculateGrid(rayB, preset, colorIdx);
    
    float3 col;
    col.r = colR.r;
    col.g = colG.g;
    col.b = colB.b;
    
    // ============================================================================
    // SELECTION CIRCLE + INFO BOX + TEXT
    // ============================================================================
    if (params.SelectionCircleEnabled) {
        float3 shapeColor = kGridColors[params.GridColorIndex];
        float3 shapeAccum = float3(0, 0, 0);
        
        float2 center = params.SelectionCircleCenter;
        float r = params.SelectionCircleRadius;
        float thick = params.SelectionCircleThickness;
        // Flicker animation: struggling fluorescent tube effect
        float flicker = Flicker(params.SelectionCircleT, params.Time, params.SelectionStarHash);
        
        // --- Info box black backing (FIRST - darkens background) ---
        float2 boxCenter = params.DebugBoxTopLeft + params.DebugBoxSize * 0.5;
        float2 boxHalfSize = params.DebugBoxSize * 0.5;
        float boxCornerRad = 0.005;
        
        float backSdf = SDF_RoundedBox(uv, boxCenter, boxHalfSize, boxCornerRad);
        float backMask = smoothstep(0.0, 0.003, -backSdf);
        col = lerp(col, col * 0.05, backMask);
        
        // --- Selection circle glow ---
        float2 caOffset = perp * r * 0.1;
        float dR = SDF_Circle(uvR.xy, center + caOffset, r);
        float dG = SDF_Circle(uvG.xy, center, r);
        float dB = SDF_Circle(uvB.xy, center - caOffset, r);
        
        shapeAccum += shapeColor * params.SelectionCircleIntensity * flicker * float3(
            1.0 / (abs(dR) + thick),
            1.0 / (abs(dG) + thick),
            1.0 / (abs(dB) + thick)
        );
        
        // --- Info box outline ---
        float dbR = SDF_RoundedBox(uvR.xy, boxCenter + caOffset, boxHalfSize, boxCornerRad);
        float dbG = SDF_RoundedBox(uvG.xy, boxCenter, boxHalfSize, boxCornerRad);
        float dbB = SDF_RoundedBox(uvB.xy, boxCenter - caOffset, boxHalfSize, boxCornerRad);
        
        shapeAccum += shapeColor * params.SelectionCircleIntensity * flicker * float3(
            1.0 / (abs(dbR) + params.DebugBoxThickness),
            1.0 / (abs(dbG) + params.DebugBoxThickness),
            1.0 / (abs(dbB) + params.DebugBoxThickness)
        );
        
        // --- Text rendering (on top of darkened background) ---
        // params.TextOrigin and params.TextAreaSize are in shader-uv space (center=0, +Y=UP)
        // Apply chromatic aberration: sample text 3 times with RGB offset UVs
        float2 textLocalR = (uvR - params.TextOrigin) / params.TextAreaSize;
        float2 textLocalG = (uvG - params.TextOrigin) / params.TextAreaSize;
        float2 textLocalB = (uvB - params.TextOrigin) / params.TextAreaSize;
        textLocalR.y = 1.0 - textLocalR.y;
        textLocalG.y = 1.0 - textLocalG.y;
        textLocalB.y = 1.0 - textLocalB.y;
        
        // Y-flip added: +Y=up means textLocal goes 0→1 bottom→top, texture V goes 0→1 top→bottom
        
        // Sample text coverage for each channel separately (chromatic aberration)
        float coverageR = 0.0, coverageG = 0.0, coverageB = 0.0;
        
        if (textLocalR.x >= 0.0 && textLocalR.x <= 1.0 && 
            textLocalR.y >= 0.0 && textLocalR.y <= 1.0)
            coverageR = TextTexture.SampleLevel(TextSampler, textLocalR, 0).r;
            
        if (textLocalG.x >= 0.0 && textLocalG.x <= 1.0 && 
            textLocalG.y >= 0.0 && textLocalG.y <= 1.0)
            coverageG = TextTexture.SampleLevel(TextSampler, textLocalG, 0).r;
            
        if (textLocalB.x >= 0.0 && textLocalB.x <= 1.0 && 
            textLocalB.y >= 0.0 && textLocalB.y <= 1.0)
            coverageB = TextTexture.SampleLevel(TextSampler, textLocalB, 0).r;
        
        // Add text with per-channel coverage for chromatic aberration effect
        shapeAccum += shapeColor * params.SelectionTextT * float3(coverageR, coverageG, coverageB);
        
        col += shapeAccum;
    }
    
    // ============================================================================
    // VESSEL TARGET SELECTOR - Separate from Star Selector
    // ============================================================================
    if (params.VesselTargetEnabled) {
        float3 shapeColor = kGridColors[params.GridColorIndex];
        float3 shapeAccum = float3(0, 0, 0);
        
        float2 center = float2(params.VesselTargetCircleCenter.x, params.VesselTargetCircleCenter.y);
        float r = params.VesselTargetCircleRadius;
        float thick = params.VesselTargetCircleThickness;
        // Flicker animation: struggling fluorescent tube effect
        float flicker = Flicker(params.VesselTargetCircleT, params.Time, params.VesselTargetHash);
        
        // --- Info box black backing (FIRST - darkens background) ---
        float2 boxCenter = float2(params.VesselTargetBoxTopLeft.x, params.VesselTargetBoxTopLeft.y) + 
                          float2(params.VesselTargetBoxSize.x, params.VesselTargetBoxSize.y) * 0.5;
        float2 boxHalfSize = float2(params.VesselTargetBoxSize.x, params.VesselTargetBoxSize.y) * 0.5;
        float boxCornerRad = 0.005;
        
        float backSdf = SDF_RoundedBox(uv, boxCenter, boxHalfSize, boxCornerRad);
        float backMask = smoothstep(0.0, 0.003, -backSdf);
        col = lerp(col, col * 0.05, backMask);
        
        // --- Selection circle glow ---
        float2 caOffset = perp * r * 0.1;
        float dR = SDF_Circle(uvR.xy, center + caOffset, r);
        float dG = SDF_Circle(uvG.xy, center, r);
        float dB = SDF_Circle(uvB.xy, center - caOffset, r);
        
        shapeAccum += shapeColor * params.VesselTargetCircleIntensity * flicker * float3(
            1.0 / (abs(dR) + thick),
            1.0 / (abs(dG) + thick),
            1.0 / (abs(dB) + thick)
        );
        
        // --- Info box outline ---
        float dbR = SDF_RoundedBox(uvR.xy, boxCenter + caOffset, boxHalfSize, boxCornerRad);
        float dbG = SDF_RoundedBox(uvG.xy, boxCenter, boxHalfSize, boxCornerRad);
        float dbB = SDF_RoundedBox(uvB.xy, boxCenter - caOffset, boxHalfSize, boxCornerRad);
        
        shapeAccum += shapeColor * params.VesselTargetCircleIntensity * flicker * float3(
            1.0 / (abs(dbR) + params.VesselTargetBoxThickness),
            1.0 / (abs(dbG) + params.VesselTargetBoxThickness),
            1.0 / (abs(dbB) + params.VesselTargetBoxThickness)
        );
        
        // --- Text rendering (on top of darkened background) ---
        float2 textLocalR = (uvR - float2(params.VesselTargetTextOrigin.x, params.VesselTargetTextOrigin.y)) / 
                            float2(params.VesselTargetTextAreaSize.x, params.VesselTargetTextAreaSize.y);
        float2 textLocalG = (uvG - float2(params.VesselTargetTextOrigin.x, params.VesselTargetTextOrigin.y)) / 
                            float2(params.VesselTargetTextAreaSize.x, params.VesselTargetTextAreaSize.y);
        float2 textLocalB = (uvB - float2(params.VesselTargetTextOrigin.x, params.VesselTargetTextOrigin.y)) / 
                            float2(params.VesselTargetTextAreaSize.x, params.VesselTargetTextAreaSize.y);
        textLocalR.y = 1.0 - textLocalR.y;
        textLocalG.y = 1.0 - textLocalG.y;
        textLocalB.y = 1.0 - textLocalB.y;
        
        // Sample text coverage for each channel separately (chromatic aberration)
        float coverageR = 0.0, coverageG = 0.0, coverageB = 0.0;
        
        if (textLocalR.x >= 0.0 && textLocalR.x <= 1.0 && 
            textLocalR.y >= 0.0 && textLocalR.y <= 1.0)
            coverageR = VesselTargetTextTexture.SampleLevel(TextSampler, textLocalR, 0).r;
            
        if (textLocalG.x >= 0.0 && textLocalG.x <= 1.0 && 
            textLocalG.y >= 0.0 && textLocalG.y <= 1.0)
            coverageG = VesselTargetTextTexture.SampleLevel(TextSampler, textLocalG, 0).r;
            
        if (textLocalB.x >= 0.0 && textLocalB.x <= 1.0 && 
            textLocalB.y >= 0.0 && textLocalB.y <= 1.0)
            coverageB = VesselTargetTextTexture.SampleLevel(TextSampler, textLocalB, 0).r;
        
        // Add text with per-channel coverage for chromatic aberration effect
        // params.AnimatedLabelIntensity controls visibility (0 during Circle/Box, 1 during Text/Complete)
        float textIntensity = params.AnimatedLabelIntensity;
        shapeAccum += shapeColor * float3(coverageR, coverageG, coverageB) * textIntensity;
        
        col += shapeAccum;
    }
    
    // ============================================================================
    // GRID LABELS (HUCK, SOI, etc.) - Tangent Plane Projection
    // Text is "painted" onto the sphere surface, rotates with grid
    // ============================================================================
    if (params.GridLabelEnabledMask != 0) {
        float3 gridColor = kGridColors[params.GridColorIndex];
        
        // Chromatic aberration offset for labels (smaller than main grid)
        float2 caOffsetLabel = perp * 0.02;
        
        // Process each enabled label
        for (int i = 0; i < 12; i++) {
            if ((params.GridLabelEnabledMask & (1u << i)) == 0) continue;
            
            float4 posTanX, tanY;
            switch(i) {
                case 0: posTanX = params.GridLabel0_PosTangentX; tanY = params.GridLabel0_TangentY; break;
                case 1: posTanX = params.GridLabel1_PosTangentX; tanY = params.GridLabel1_TangentY; break;
                case 2: posTanX = params.GridLabel2_PosTangentX; tanY = params.GridLabel2_TangentY; break;
                case 3: posTanX = params.GridLabel3_PosTangentX; tanY = params.GridLabel3_TangentY; break;
                case 4: posTanX = params.GridLabel4_PosTangentX; tanY = params.GridLabel4_TangentY; break;
                case 5: posTanX = params.GridLabel5_PosTangentX; tanY = params.GridLabel5_TangentY; break;
                case 6: posTanX = params.GridLabel6_PosTangentX; tanY = params.GridLabel6_TangentY; break;
                case 7: posTanX = params.GridLabel7_PosTangentX; tanY = params.GridLabel7_TangentY; break;
                case 8: posTanX = params.GridLabel8_PosTangentX; tanY = params.GridLabel8_TangentY; break;
                case 9: posTanX = params.GridLabel9_PosTangentX; tanY = params.GridLabel9_TangentY; break;
                case 10: posTanX = params.GridLabel10_PosTangentX; tanY = params.GridLabel10_TangentY; break;
                case 11: posTanX = params.GridLabel11_PosTangentX; tanY = params.GridLabel11_TangentY; break;
            }
            
            float3 labelPos = posTanX.xyz;
            float3 labelTangent = tanY.xyz;
            float sizeX = posTanX.w;
            float sizeY = tanY.w;
            
            // Render label (use green channel ray as base, with CA offset)
            col += RenderGridLabel(labelPos, labelTangent, sizeX, sizeY, i, rayG, gridColor, caOffsetLabel);
        }
    }
    
    // ============================================================================
    // NAVBALL ICONS - Screen-space orbit direction indicators (Phase 4c)
    // ============================================================================
    if (params.NavballEnabledMask != 0) {
        float3 navballAccum = float3(0, 0, 0);
        float3 gridColor = kGridColors[params.GridColorIndex];
        
        // Array of icon data for iteration (using float2 position fields)
        float2 positions[7] = {
            params.NavballIcon0,
            params.NavballIcon1,
            params.NavballIcon2,
            params.NavballIcon3,
            params.NavballIcon4,
            params.NavballIcon5,
            params.NavballIcon6
        };
        float intensities[7] = {
            params.NavballIcon0_Intensity,
            params.NavballIcon1_Intensity,
            params.NavballIcon2_Intensity,
            params.NavballIcon3_Intensity,
            params.NavballIcon4_Intensity,
            params.NavballIcon5_Intensity,
            params.NavballIcon6_Intensity
        };
        uint colors[7] = {
            params.NavballIcon0_Color,
            params.NavballIcon1_Color,
            params.NavballIcon2_Color,
            params.NavballIcon3_Color,
            params.NavballIcon4_Color,
            params.NavballIcon5_Color,
            params.NavballIcon6_Color
        };
        
        // Render each enabled icon
        for (int i = 0; i < 7; i++) {
            if ((params.NavballEnabledMask & (1 << i)) != 0) {
                navballAccum += RenderNavballIcon(i, positions[i], intensities[i], colors[i],
                    uv, gridColor, params.NavballIconSize, params.NavballIconThickness);
            }
        }
        
        col += navballAccum;
    }
    
    // ============================================================================
    // POINTING ICON (Heading Indicator)
    // ============================================================================
    if (params.PointingIconEnabled && params.PointingIconIntensity > 0.001) {
        float3 gridColor = kGridColors[params.GridColorIndex];
        float2 center = params.PointingIconPos;
        col += RenderPointingIcon(center, params.PointingIconRotation, params.PointingIconIntensity,
            params.PointingIconColor, uv, gridColor, params.PointingIconSize);
    }
    
    // ============================================================================
    // MANEUVER TEXT OVERLAY
    // ============================================================================
    if (params.ManeuverTextEnabled && params.ManeuverTextIntensity > 0.001) {
        float3 gridColor = kGridColors[params.GridColorIndex];
        float2 origin = params.ManeuverTextOrigin;
        float2 size = float2(params.ManeuverTextWidth, params.ManeuverTextHeight);
        col += RenderManeuverText(origin, size, params.ManeuverTextIntensity, uv, gridColor);
    }
    
    // ============================================================================
    // BOX OUTLINE FOR HOVER FEEDBACK (Layer 3 Single-Texture Refactor)
    // ============================================================================
    float3 gridColorBase = kGridColors[params.GridColorIndex];
    // Box outline disabled for Kartographer - feature is CRT UI only
    // col = DrawBoxOutline(input.uv, col, gridColorBase);
    
    float phase = frac(fragCoord.x / 3.0);
    float3 phosphor;
    if (phase < 0.33)       phosphor = float3(1.0, 0.3, 0.3);
    else if (phase < 0.66)  phosphor = float3(0.3, 1.0, 0.3);
    else                    phosphor = float3(0.3, 0.3, 1.0);
    
    col = col * phosphor * 1.4;
    col = tanh(col);
    
    float2 uvNormalized = input.uv * 2.0 - 1.0;
    uvNormalized.x *= aspect;
    float distFromCenter = length(uvNormalized);
    
    float vignette = 1.0 - smoothstep(params.VignetteStart, params.VignetteEnd, distFromCenter) * params.VignetteStrength;
    col *= vignette;
    
    return float4(col, 1.0);
}
