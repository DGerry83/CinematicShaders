// Kartographer Pixel Shader - Holographic Grid Overlay
// Spherical coordinate grid with chromatic aberration, phosphor mask, and vignette

struct PSInput {
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

// Constant buffer - must match C++ KartographerParams struct exactly
cbuffer KartographerCB : register(b0) {
    float2 Resolution;          // Screen resolution
    float Time;                 // For animation/flicker
    float GridIntensity;        // Default: 0.002, Range: 0.001-0.003
    
    float GridThickness;        // Default: 0.0003, Range: 0.00015-0.00045
    float ChromaticAberrationStrength;  // Default: 0.004, Range: 0.002-0.006
    float VignetteStrength;     // Default: 0.7, Range: 0.35-1.0
    float VignetteStart;        // Default: 1.6, Range: 0.8-2.4
    
    float VignetteEnd;          // Default: 2.2, Range: 1.1-3.3
    float PreRotationYaw;       // For UI customization (like star rotation)
    float PreRotationPitch;     // For UI customization
    float _pad1;                // Alignment
    
    // Camera rotation matrix (3x3, row-major for HLSL)
    float3 CameraRight;         // Camera right vector in world space
    float _pad2;
    float3 CameraUp;            // Camera up vector in world space
    float _pad3;
    float3 CameraForward;       // Camera forward vector in world space
    float _pad4;
};

// Meridian noise values (same as ShadeRED)
static const float meridianNoise[16] = {
    0.920, 1.140, 0.780, 1.050, 1.220, 0.850,
    0.960, 1.080, 0.730, 1.180, 0.880, 1.020,
    0.810, 1.150, 0.940, 1.070
};

static const float parallelNoise[10] = {
    1.050, 0.820, 1.130, 0.910, 1.180, 
    0.760, 0.980, 1.060, 0.890, 1.140
};

// Helper: Transform view-space vector to world-space
// This is the inverse of the standard world-to-view transform
// For an orthonormal rotation matrix, inverse = transpose
// CRITICAL: Unity uses LEFT-HANDED camera space, but we need RIGHT-HANDED for correct rotation.
// We negate the UP vector to flip the handedness, otherwise pitch becomes roll.
float3 ViewToWorld(float3 v, float3 right, float3 up, float3 forward) {
    // Reconstruct world vector from view components and camera basis
    // In view space: +X = right, +Y = up, +Z = forward (into screen)
    // In world space: we map these to the camera's basis vectors
    // Negate up to convert from Unity's left-handed to right-handed system
    return v.x * right - v.y * up + v.z * forward;
}

// Helper: Apply yaw/pitch rotation to view ray
float3 ApplyPreRotation(float3 ray, float yaw, float pitch) {
    float cy = cos(yaw);
    float sy = sin(yaw);
    float cx = cos(pitch);
    float sx = sin(pitch);
    
    // Yaw (Y-axis rotation) - rotates around view UP axis
    float3 r1 = float3(
        ray.x * cy - ray.z * sy,
        ray.y,
        ray.x * sy + ray.z * cy
    );
    
    // Pitch (X-axis rotation) - rotates around view RIGHT axis
    // This should make the grid pitch up/down when the user adjusts pitch
    float3 r2 = float3(
        r1.x,
        r1.y * cx - r1.z * sx,
        r1.y * sx + r1.z * cx
    );
    
    return normalize(r2);
}

// Calculate grid glow for a given ray direction
float3 CalculateGrid(float3 ray) {
    // Convert to spherical coordinates
    float phi = acos(clamp(ray.y, -1.0, 1.0));
    float theta = atan2(ray.z, ray.x);
    
    static const float numLong = 16.0;
    static const float numLat = 10.0;
    static const float thetaStep = 6.2831853 / numLong;
    static const float phiStep = 3.1415927 / numLat;
    
    static const float3 seafoam = float3(0.1, 0.9, 0.7);
    
    // --- MERIDIANS ---
    float t = (theta + 3.14159265) / thetaStep - 0.5;
    int cellIdx = int(floor(t));
    int mLeft = cellIdx;
    int mRight = cellIdx + 1;
    if (mLeft < 0) mLeft = 15;
    if (mRight > 15) mRight = 0;
    
    float distLeft = abs(theta - (-3.14159265 + (float(mLeft) + 0.5) * thetaStep));
    distLeft = min(distLeft, 6.2831853 - distLeft);
    float distRight = abs(theta - (-3.14159265 + (float(mRight) + 0.5) * thetaStep));
    distRight = min(distRight, 6.2831853 - distRight);
    
    float surfLeft = sin(phi) * distLeft;
    float surfRight = sin(phi) * distRight;
    
    float noiseLeft = meridianNoise[mLeft];
    float noiseRight = meridianNoise[mRight];
    
    // Pole fade
    float poleFadeStart = 0.5 * phiStep;
    float poleFade = smoothstep(0.0, poleFadeStart, phi) * 
                     smoothstep(3.1415927, 3.1415927 - poleFadeStart, phi);
    
    float3 glowM = seafoam * GridIntensity * (
        noiseLeft / (surfLeft + GridThickness) + 
        noiseRight / (surfRight + GridThickness)
    ) * poleFade;
    
    // --- PARALLELS ---
    float p = phi / phiStep - 0.5;
    int pCell = int(floor(p));
    int pLow = max(0, min(9, pCell));
    int pHigh = max(0, min(9, pCell + 1));
    
    float distLow = abs(phi - (float(pLow) + 0.5) * phiStep);
    float distHigh = abs(phi - (float(pHigh) + 0.5) * phiStep);
    
    float noiseLow = parallelNoise[pLow];
    float noiseHigh = parallelNoise[pHigh];
    
    float3 glowP = seafoam * GridIntensity * (
        noiseLow / (distLow + GridThickness) + 
        noiseHigh / (distHigh + GridThickness)
    );
    
    return glowM + glowP;
}

float4 PSMain(PSInput input) : SV_Target {
    float2 fragCoord = input.uv * Resolution;
    
    // Convert UV to normalized device coordinates (-1 to 1)
    // Correct for aspect ratio to maintain spherical appearance
    float aspect = Resolution.x / Resolution.y;
    float2 uv = float2(
        (input.uv.x - 0.5) * 2.0 * aspect,
        (input.uv.y - 0.5) * 2.0
    );
    
    // CHROMATIC ABERRATION SETUP
    // Perpendicular vector scaled by distance from center
    float2 perp = float2(-uv.y, uv.x) * ChromaticAberrationStrength;
    
    // Offsets for R and B (G stays centered)
    float2 uvR = uv + perp;
    float2 uvG = uv;
    float2 uvB = uv - perp;
    
    // Generate view rays (Z-forward, matching Unity/KSP camera space)
    // tan(fov/2) = 1.0 at 90 deg FOV, but we need to match ShadeRED's 1.732 factor
    // 1.732 = tan(60 deg), which gives 120 deg FOV total
    static const float focalLength = 1.732;
    
    float3 rayR = normalize(float3(uvR.x, uvR.y, focalLength));
    float3 rayG = normalize(float3(uvG.x, uvG.y, focalLength));
    float3 rayB = normalize(float3(uvB.x, uvB.y, focalLength));
    
    // Transform from view space to world space using camera basis vectors
    // This makes the grid "infinitely far" and locked to camera orientation like stars
    rayR = ViewToWorld(rayR, CameraRight, CameraUp, CameraForward);
    rayG = ViewToWorld(rayG, CameraRight, CameraUp, CameraForward);
    rayB = ViewToWorld(rayB, CameraRight, CameraUp, CameraForward);
    
    // Apply pre-rotation in WORLD space (not view space)
    // This ensures yaw/pitch rotate the grid relative to world axes, not view axes
    rayR = ApplyPreRotation(rayR, PreRotationYaw, PreRotationPitch);
    rayG = ApplyPreRotation(rayG, PreRotationYaw, PreRotationPitch);
    rayB = ApplyPreRotation(rayB, PreRotationYaw, PreRotationPitch);
    
    // Calculate grid for each color channel
    float3 colR = CalculateGrid(rayR);
    float3 colG = CalculateGrid(rayG);
    float3 colB = CalculateGrid(rayB);
    
    // Combine channels
    float3 col;
    col.r = colR.r;
    col.g = colG.g;
    col.b = colB.b;
    
    // SCREEN-SPACE PHOSPHOR MASK
    float phase = frac(fragCoord.x / 3.0);
    float3 phosphor;
    if (phase < 0.33)       phosphor = float3(1.0, 0.3, 0.3);
    else if (phase < 0.66)  phosphor = float3(0.3, 1.0, 0.3);
    else                    phosphor = float3(0.3, 0.3, 1.0);
    
    col = col * phosphor * 1.4;
    
    // Tonemapping
    col = tanh(col);
    
    // VIGNETTE (aspect-ratio correct)
    // Calculate distance in normalized UV space (0-1 range) for aspect-correct vignette
    float2 uvNormalized = input.uv * 2.0 - 1.0;  // -1 to 1
    // Scale X to account for aspect ratio for circular vignette in screen space
    uvNormalized.x *= aspect;
    float distFromCenter = length(uvNormalized);
    
    float vignette = 1.0 - smoothstep(VignetteStart, VignetteEnd, distFromCenter) * VignetteStrength;
    col *= vignette;
    
    return float4(col, 1.0);
}
