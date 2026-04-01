// Kartographer Pixel Shader - Holographic Grid Overlay
// Spherical coordinate grid with chromatic aberration, phosphor mask, and vignette
// Phase 1: Added debug SDF shapes (circle, rounded box)
// Phase 2: Added selection circle for star tracking
// Phase 3: Fixed tangent plane projection for grid labels
//
// COORDINATE SPACE NOTES:
// - Shader UV space: center = (0,0), +X = right, +Y = up
// - Screen UV [0,1] maps to: x = (u-0.5)*2*aspect, y = (v-0.5)*2
// - DebugBoxTopLeft is the actual top-left corner of the box in shader-UV
// - Box center in shader is computed as: topLeft + size*0.5

struct PSInput {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

// Constant buffer - must match C++ KartographerParams struct exactly
// Total size: 880 bytes (16 × 55)
// Updated for 12-label support (Phase 2-4)
cbuffer KartographerCB : register(b0) {
    // Base grid params (64 bytes)
    float2 Resolution;          // offset 0
    float Time;                 // offset 8
    float GridIntensity;        // offset 12
    
    float GridThickness;        // offset 16
    float ChromaticAberrationStrength;  // offset 20
    float VignetteStrength;     // offset 24
    float VignetteStart;        // offset 28
    
    float VignetteEnd;          // offset 32
    float PreRotationYaw;       // offset 36
    float PreRotationPitch;     // offset 40
    int GridSizePreset;         // offset 44
    int GridColorIndex;         // offset 48
    float _pad1;                // offset 52
    float _pad2;                // offset 56
    float _padAlignCamera;      // offset 60
    
    // Camera basis (48 bytes)
    float3 CameraRight;         // offset 64
    float _pad3;
    float3 CameraUp;            // offset 80
    float _pad4;
    float3 CameraForward;       // offset 96
    float _pad5;
    
    // Debug shapes / Info box (48 bytes) - offsets 112-159
    int DebugShapesEnabled;     // offset 112
    float _pad6;
    float _pad7;
    float _pad8;
    float2 DebugCircleCenter;   // offset 128
    float DebugCircleRadius;    // offset 136
    float DebugCircleThickness; // offset 140
    float2 DebugBoxTopLeft;     // offset 144
    float2 DebugBoxSize;        // offset 152
    float DebugBoxThickness;    // offset 160
    float DebugShapeIntensity;  // offset 164
    float _pad9;
    float FocalLength;          // offset 172
    
    // Selection circle (56 bytes) - offsets 176-231
    int SelectionCircleEnabled;     // offset 176
    float SelectionStarHash;        // offset 180 - for flicker variation
    float _padSelection2;           // offset 184
    float _padSelection3;           // offset 188
    float2 SelectionCircleCenter;   // offset 192
    float SelectionCircleT;         // offset 200
    float SelectionCircleIntensity; // offset 204
    float SelectionCircleThickness; // offset 208
    float SelectionCircleRadius;    // offset 212
    float _padSelection4;           // offset 216
    float _padSelection5;           // offset 220
    float _padSelection6;           // offset 224
    float _padSelection7;           // offset 228
    
    // Text stub (20 bytes) - offsets 232-251
    float2 TextOrigin;              // offset 232
    float2 TextAreaSize;            // offset 240
    float SelectionTextT;           // offset 248
    
    // Grid Labels (12 labels) - offsets 252-655
    uint GridLabelEnabledMask;      // offset 252 - bit i = label i enabled
    float _padGridMask1;            // offset 256
    float _padGridMask2;            // offset 260
    float _padGridMask3;            // offset 264
    float _padGridMask4;            // offset 268
    
    // Label data packed as float4 to save constant buffer space
    // Each label: float4(pos.xyz, sizeX), float4(tangent.xyz, sizeY)
    // Bitangent = normalize(cross(pos, tangent))  (for unit sphere, normal = pos)
    
    // Label 0 - offsets 272-303
    float4 GridLabel0_PosTangentX;  // xyz=pos, w=sizeX
    float4 GridLabel0_TangentY;     // xyz=tangent, w=sizeY
    
    // Label 1 - offsets 304-335
    float4 GridLabel1_PosTangentX;
    float4 GridLabel1_TangentY;
    
    // Label 2 - offsets 336-367
    float4 GridLabel2_PosTangentX;
    float4 GridLabel2_TangentY;
    
    // Label 3 - offsets 368-399
    float4 GridLabel3_PosTangentX;
    float4 GridLabel3_TangentY;
    
    // Label 4 - offsets 400-431
    float4 GridLabel4_PosTangentX;
    float4 GridLabel4_TangentY;
    
    // Label 5 - offsets 432-463
    float4 GridLabel5_PosTangentX;
    float4 GridLabel5_TangentY;
    
    // Label 6 - offsets 464-495
    float4 GridLabel6_PosTangentX;
    float4 GridLabel6_TangentY;
    
    // Label 7 - offsets 496-527
    float4 GridLabel7_PosTangentX;
    float4 GridLabel7_TangentY;
    
    // Label 8 - offsets 528-559
    float4 GridLabel8_PosTangentX;
    float4 GridLabel8_TangentY;
    
    // Label 9 - offsets 560-591
    float4 GridLabel9_PosTangentX;
    float4 GridLabel9_TangentY;
    
    // Label 10 - offsets 592-623
    float4 GridLabel10_PosTangentX;
    float4 GridLabel10_TangentY;
    
    // Label 11 - offsets 624-655
    float4 GridLabel11_PosTangentX;
    float4 GridLabel11_TangentY;
    
    // Debug mask and per-label visual params (96 bytes) - offsets 656-751
    uint GridLabelDebugMask;        // offset 656 - bit mask for debug visualization
    
    // Label intensities (12 floats = 48 bytes)
    float LabelIntensity0;          // offset 660
    float LabelIntensity1;          // offset 664
    float LabelIntensity2;          // offset 668
    float LabelIntensity3;          // offset 672
    float LabelIntensity4;          // offset 676
    float LabelIntensity5;          // offset 680
    float LabelIntensity6;          // offset 684
    float LabelIntensity7;          // offset 688
    float LabelIntensity8;          // offset 692
    float LabelIntensity9;          // offset 696
    float LabelIntensity10;         // offset 700
    float LabelIntensity11;         // offset 704
    
    // Label color overrides (12 uints = 48 bytes, packed ARGB)
    uint LabelColor0;               // offset 708
    uint LabelColor1;               // offset 712
    uint LabelColor2;               // offset 716
    uint LabelColor3;               // offset 720
    uint LabelColor4;               // offset 724
    uint LabelColor5;               // offset 728
    uint LabelColor6;               // offset 732
    uint LabelColor7;               // offset 736
    uint LabelColor8;               // offset 740
    uint LabelColor9;               // offset 744
    uint LabelColor10;              // offset 748
    uint LabelColor11;              // offset 752
    
    // Vessel Target Selector - separate from Star Selector (96 bytes) - offsets 596-691
    int VesselTargetEnabled;        // offset 596
    float VesselTargetHash;         // offset 600
    float _padVessel1;              // offset 604
    float _padVessel2;              // offset 608
    float VesselTargetCircleCenterX;    // offset 612
    float VesselTargetCircleCenterY;    // offset 616
    float VesselTargetCircleT;          // offset 620
    float VesselTargetCircleIntensity;  // offset 624
    float VesselTargetCircleThickness;  // offset 628
    float VesselTargetCircleRadius;     // offset 632
    float _padVessel3;              // offset 636
    float _padVessel4;              // offset 640
    float _padVessel5;              // offset 644
    float _padVessel6;              // offset 648
    float VesselTargetBoxTopLeftX;      // offset 652
    float VesselTargetBoxTopLeftY;      // offset 656
    float VesselTargetBoxSizeX;         // offset 660
    float VesselTargetBoxSizeY;         // offset 664
    float VesselTargetBoxThickness;     // offset 668
    float _padVessel7;              // offset 672
    float VesselTargetTextOriginX;      // offset 676
    float VesselTargetTextOriginY;      // offset 680
    float VesselTargetTextAreaSizeX;    // offset 684
    float VesselTargetTextAreaSizeY;    // offset 688
    float VesselTargetTextT;            // offset 692
    
    // Animated label intensity for type-on animation systems
    float AnimatedLabelIntensity;       // offset 696
    float _padAnimated1;                // offset 700
    float _padAnimated2;                // offset 704
    float _padAnimated3;                // offset 708
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
SamplerState TextSampler : register(s0);

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
    return v.x * right - v.y * up + v.z * forward;
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
    
    float3 glowM = gridColor * GridIntensity * (
        noiseLeft / (surfLeft + GridThickness) + 
        noiseRight / (surfRight + GridThickness)
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
    
    float3 glowP = gridColor * GridIntensity * (
        noiseLow / (distLow + GridThickness) + 
        noiseHigh / (distHigh + GridThickness)
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
    if ((GridLabelDebugMask & (1u << texIndex)) != 0) {
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
    
    float t = 1.0 / denom;  // Since dot(labelPos, labelNormal) ≈ 1 for unit sphere
    
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
        case 0: intensity = LabelIntensity0; colorOverride = LabelColor0; break;
        case 1: intensity = LabelIntensity1; colorOverride = LabelColor1; break;
        case 2: intensity = LabelIntensity2; colorOverride = LabelColor2; break;
        case 3: intensity = LabelIntensity3; colorOverride = LabelColor3; break;
        case 4: intensity = LabelIntensity4; colorOverride = LabelColor4; break;
        case 5: intensity = LabelIntensity5; colorOverride = LabelColor5; break;
        case 6: intensity = LabelIntensity6; colorOverride = LabelColor6; break;
        case 7: intensity = LabelIntensity7; colorOverride = LabelColor7; break;
        case 8: intensity = LabelIntensity8; colorOverride = LabelColor8; break;
        case 9: intensity = LabelIntensity9; colorOverride = LabelColor9; break;
        case 10: intensity = LabelIntensity10; colorOverride = LabelColor10; break;
        case 11: intensity = LabelIntensity11; colorOverride = LabelColor11; break;
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
// Main Pixel Shader
// ============================================================================

float4 PSMain(PSInput input) : SV_Target {
    float2 fragCoord = input.uv * Resolution;
    
    float aspect = Resolution.x / Resolution.y;
    float2 uv = float2(
        (input.uv.x - 0.5) * 2.0 * aspect,
        (input.uv.y - 0.5) * 2.0
    );
    
    float2 perp = float2(-uv.y, uv.x) * ChromaticAberrationStrength;
    
    float2 uvR = uv + perp;
    float2 uvG = uv;
    float2 uvB = uv - perp;
    
    float focalLength = max(FocalLength, 0.001);
    
    float3 rayR = normalize(float3(uvR.x, uvR.y, focalLength));
    float3 rayG = normalize(float3(uvG.x, uvG.y, focalLength));
    float3 rayB = normalize(float3(uvB.x, uvB.y, focalLength));
    
    rayR = ViewToWorld(rayR, CameraRight, CameraUp, CameraForward);
    rayG = ViewToWorld(rayG, CameraRight, CameraUp, CameraForward);
    rayB = ViewToWorld(rayB, CameraRight, CameraUp, CameraForward);
    
    rayR = ApplyPreRotation(rayR, PreRotationYaw, PreRotationPitch);
    rayG = ApplyPreRotation(rayG, PreRotationYaw, PreRotationPitch);
    rayB = ApplyPreRotation(rayB, PreRotationYaw, PreRotationPitch);
    
    int preset = GridSizePreset;
    int colorIdx = GridColorIndex;
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
    if (SelectionCircleEnabled) {
        float3 shapeColor = kGridColors[GridColorIndex];
        float3 shapeAccum = float3(0, 0, 0);
        
        float2 center = SelectionCircleCenter;
        float r = SelectionCircleRadius;
        float thick = SelectionCircleThickness;
        // Flicker animation: struggling fluorescent tube effect
        float flicker = Flicker(SelectionCircleT, Time, SelectionStarHash);
        
        // --- Info box black backing (FIRST - darkens background) ---
        float2 boxCenter = DebugBoxTopLeft + DebugBoxSize * 0.5;
        float2 boxHalfSize = DebugBoxSize * 0.5;
        float boxCornerRad = 0.005;
        
        float backSdf = SDF_RoundedBox(uv, boxCenter, boxHalfSize, boxCornerRad);
        float backMask = smoothstep(0.0, 0.003, -backSdf);
        col = lerp(col, col * 0.05, backMask);
        
        // --- Selection circle glow ---
        float2 caOffset = perp * r * 0.1;
        float dR = SDF_Circle(uvR.xy, center + caOffset, r);
        float dG = SDF_Circle(uvG.xy, center, r);
        float dB = SDF_Circle(uvB.xy, center - caOffset, r);
        
        shapeAccum += shapeColor * SelectionCircleIntensity * flicker * float3(
            1.0 / (abs(dR) + thick),
            1.0 / (abs(dG) + thick),
            1.0 / (abs(dB) + thick)
        );
        
        // --- Info box outline ---
        float dbR = SDF_RoundedBox(uvR.xy, boxCenter + caOffset, boxHalfSize, boxCornerRad);
        float dbG = SDF_RoundedBox(uvG.xy, boxCenter, boxHalfSize, boxCornerRad);
        float dbB = SDF_RoundedBox(uvB.xy, boxCenter - caOffset, boxHalfSize, boxCornerRad);
        
        shapeAccum += shapeColor * SelectionCircleIntensity * flicker * float3(
            1.0 / (abs(dbR) + DebugBoxThickness),
            1.0 / (abs(dbG) + DebugBoxThickness),
            1.0 / (abs(dbB) + DebugBoxThickness)
        );
        
        // --- Text rendering (on top of darkened background) ---
        // TextOrigin and TextAreaSize are in shader-uv space (center=0, +Y=UP)
        // Apply chromatic aberration: sample text 3 times with RGB offset UVs
        float2 textLocalR = (uvR - TextOrigin) / TextAreaSize;
        float2 textLocalG = (uvG - TextOrigin) / TextAreaSize;
        float2 textLocalB = (uvB - TextOrigin) / TextAreaSize;
        
        // No Y-flip needed - texture is rendered right-side-up by compute shader
        
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
        shapeAccum += shapeColor * SelectionTextT * float3(coverageR, coverageG, coverageB);
        
        col += shapeAccum;
    }
    
    // ============================================================================
    // VESSEL TARGET SELECTOR - Separate from Star Selector
    // ============================================================================
    if (VesselTargetEnabled) {
        float3 shapeColor = kGridColors[GridColorIndex];
        float3 shapeAccum = float3(0, 0, 0);
        
        float2 center = float2(VesselTargetCircleCenterX, VesselTargetCircleCenterY);
        float r = VesselTargetCircleRadius;
        float thick = VesselTargetCircleThickness;
        // Flicker animation: struggling fluorescent tube effect
        float flicker = Flicker(VesselTargetCircleT, Time, VesselTargetHash);
        
        // --- Info box black backing (FIRST - darkens background) ---
        float2 boxCenter = float2(VesselTargetBoxTopLeftX, VesselTargetBoxTopLeftY) + 
                          float2(VesselTargetBoxSizeX, VesselTargetBoxSizeY) * 0.5;
        float2 boxHalfSize = float2(VesselTargetBoxSizeX, VesselTargetBoxSizeY) * 0.5;
        float boxCornerRad = 0.005;
        
        float backSdf = SDF_RoundedBox(uv, boxCenter, boxHalfSize, boxCornerRad);
        float backMask = smoothstep(0.0, 0.003, -backSdf);
        col = lerp(col, col * 0.05, backMask);
        
        // --- Selection circle glow ---
        float2 caOffset = perp * r * 0.1;
        float dR = SDF_Circle(uvR.xy, center + caOffset, r);
        float dG = SDF_Circle(uvG.xy, center, r);
        float dB = SDF_Circle(uvB.xy, center - caOffset, r);
        
        shapeAccum += shapeColor * VesselTargetCircleIntensity * flicker * float3(
            1.0 / (abs(dR) + thick),
            1.0 / (abs(dG) + thick),
            1.0 / (abs(dB) + thick)
        );
        
        // --- Info box outline ---
        float dbR = SDF_RoundedBox(uvR.xy, boxCenter + caOffset, boxHalfSize, boxCornerRad);
        float dbG = SDF_RoundedBox(uvG.xy, boxCenter, boxHalfSize, boxCornerRad);
        float dbB = SDF_RoundedBox(uvB.xy, boxCenter - caOffset, boxHalfSize, boxCornerRad);
        
        shapeAccum += shapeColor * VesselTargetCircleIntensity * flicker * float3(
            1.0 / (abs(dbR) + VesselTargetBoxThickness),
            1.0 / (abs(dbG) + VesselTargetBoxThickness),
            1.0 / (abs(dbB) + VesselTargetBoxThickness)
        );
        
        // --- Text rendering (on top of darkened background) ---
        float2 textLocalR = (uvR - float2(VesselTargetTextOriginX, VesselTargetTextOriginY)) / 
                            float2(VesselTargetTextAreaSizeX, VesselTargetTextAreaSizeY);
        float2 textLocalG = (uvG - float2(VesselTargetTextOriginX, VesselTargetTextOriginY)) / 
                            float2(VesselTargetTextAreaSizeX, VesselTargetTextAreaSizeY);
        float2 textLocalB = (uvB - float2(VesselTargetTextOriginX, VesselTargetTextOriginY)) / 
                            float2(VesselTargetTextAreaSizeX, VesselTargetTextAreaSizeY);
        
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
        // AnimatedLabelIntensity controls visibility (0 during Circle/Box, 1 during Text/Complete)
        float textIntensity = AnimatedLabelIntensity;
        shapeAccum += shapeColor * float3(coverageR, coverageG, coverageB) * textIntensity;
        
        col += shapeAccum;
    }
    
    // ============================================================================
    // GRID LABELS (HUCK, SOI, etc.) - Tangent Plane Projection
    // Text is "painted" onto the sphere surface, rotates with grid
    // ============================================================================
    if (GridLabelEnabledMask != 0) {
        float3 gridColor = kGridColors[GridColorIndex];
        
        // Chromatic aberration offset for labels (smaller than main grid)
        float2 caOffsetLabel = perp * 0.02;
        
        // Process each enabled label
        for (int i = 0; i < 12; i++) {
            if ((GridLabelEnabledMask & (1u << i)) == 0) continue;
            
            float4 posTanX, tanY;
            switch(i) {
                case 0: posTanX = GridLabel0_PosTangentX; tanY = GridLabel0_TangentY; break;
                case 1: posTanX = GridLabel1_PosTangentX; tanY = GridLabel1_TangentY; break;
                case 2: posTanX = GridLabel2_PosTangentX; tanY = GridLabel2_TangentY; break;
                case 3: posTanX = GridLabel3_PosTangentX; tanY = GridLabel3_TangentY; break;
                case 4: posTanX = GridLabel4_PosTangentX; tanY = GridLabel4_TangentY; break;
                case 5: posTanX = GridLabel5_PosTangentX; tanY = GridLabel5_TangentY; break;
                case 6: posTanX = GridLabel6_PosTangentX; tanY = GridLabel6_TangentY; break;
                case 7: posTanX = GridLabel7_PosTangentX; tanY = GridLabel7_TangentY; break;
                case 8: posTanX = GridLabel8_PosTangentX; tanY = GridLabel8_TangentY; break;
                case 9: posTanX = GridLabel9_PosTangentX; tanY = GridLabel9_TangentY; break;
                case 10: posTanX = GridLabel10_PosTangentX; tanY = GridLabel10_TangentY; break;
                case 11: posTanX = GridLabel11_PosTangentX; tanY = GridLabel11_TangentY; break;
            }
            
            float3 labelPos = posTanX.xyz;
            float3 labelTangent = tanY.xyz;
            float sizeX = posTanX.w;
            float sizeY = tanY.w;
            
            // Render label (use green channel ray as base, with CA offset)
            col += RenderGridLabel(labelPos, labelTangent, sizeX, sizeY, i, rayG, gridColor, caOffsetLabel);
        }
    }
    
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
    
    float vignette = 1.0 - smoothstep(VignetteStart, VignetteEnd, distFromCenter) * VignetteStrength;
    col *= vignette;
    
    return float4(col, 1.0);
}
