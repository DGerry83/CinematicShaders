// Kartographer Pixel Shader - Holographic Grid Overlay
// Spherical coordinate grid with chromatic aberration, phosphor mask, and vignette
// Phase 1: Added debug SDF shapes (circle, rounded box)

struct PSInput {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

// Constant buffer - must match C++ KartographerParams struct exactly
// Total size: 256 bytes (Phase 1 expanded)
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
    
    // Debug shapes (32 bytes)
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
    float _pad10;
    
    // Selection animation (32 bytes) - reserved for future phases
    float2 SelectionCircleCenter;   // offset 176
    float SelectionCircleT;         // offset 184
    float SelectionCircleIntensity; // offset 188
    float SelectionCircleThickness; // offset 192
    float SelectionCircleRadius;    // offset 196
    float2 BoxCenter;               // offset 200
    float2 BoxHalfSize;             // offset 208
    float BoxCornerRadius;          // offset 216
    float BoxThickness;             // offset 220
    float BoxT;                     // offset 224
    float _pad11;
    
    // Text stub (16 bytes) - reserved for future phases
    float2 TextOrigin;              // offset 232
    float2 TextAreaSize;            // offset 240
    float SelectionTextT;           // offset 248
    float _pad12;                   // offset 252
};

// Grid colors: 0=Seafoam, 1=Amber, 2=White, 3=Green
static const float3 kGridColors[4] = {
    float3(0.1, 0.9, 0.7),   // Seafoam
    float3(1.0, 0.65, 0.0),  // Amber
    float3(0.85, 0.95, 1.0), // White
    float3(0.25, 1.0, 0.0)   // Green
};

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
    
    static const float focalLength = 1.732;
    
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
    // DEBUG SHAPES (Phase 1)
    // ============================================================================
    if (DebugShapesEnabled) {
        float3 shapeColor = kGridColors[GridColorIndex];
        
        // Circle SDF with per-channel chromatic aberration
        float2 circleCenter = DebugCircleCenter;
        float r = DebugCircleRadius;
        float thick = DebugCircleThickness;
        
        // Apply CA by offsetting center per-channel using perpendicular vector
        float2 caOffset = perp * r * 0.1;
        
        float dR = SDF_Circle(uvR.xy, circleCenter + caOffset, r);
        float dG = SDF_Circle(uvG.xy, circleCenter, r);
        float dB = SDF_Circle(uvB.xy, circleCenter - caOffset, r);
        
        float3 circleGlow = shapeColor * DebugShapeIntensity * float3(
            1.0 / (abs(dR) + thick),
            1.0 / (abs(dG) + thick),
            1.0 / (abs(dB) + thick)
        );
        
        // Rounded Box SDF with per-channel CA
        float2 boxCenter = DebugBoxTopLeft + DebugBoxSize * 0.5;
        float2 boxHalfSize = DebugBoxSize * 0.5;
        // Minimal rounding for sharp corners (user request)
        float boxCornerRad = 0.0005;
        
        float dbR = SDF_RoundedBox(uvR.xy, boxCenter + caOffset, boxHalfSize, boxCornerRad);
        float dbG = SDF_RoundedBox(uvG.xy, boxCenter, boxHalfSize, boxCornerRad);
        float dbB = SDF_RoundedBox(uvB.xy, boxCenter - caOffset, boxHalfSize, boxCornerRad);
        
        float3 boxGlow = shapeColor * DebugShapeIntensity * float3(
            1.0 / (abs(dbR) + DebugBoxThickness),
            1.0 / (abs(dbG) + DebugBoxThickness),
            1.0 / (abs(dbB) + DebugBoxThickness)
        );
        
        col += circleGlow + boxGlow;
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
