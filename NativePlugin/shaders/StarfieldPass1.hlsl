// Starfield Pass 1: Procedural Star Generation
// Outputs to R11G11B10_Float RenderTexture (Linear HDR, values > 1.0 for bright stars)

Texture2D<float> BlueNoiseTexture : register(t0);
SamplerState pointSampler : register(s0);

RWTexture2D<float3> OutputHDR : register(u0);

// StructuredBuffer containing pre-generated star catalog
// Populated by CPU via CR_StarfieldGenerateCatalog
// Version 4: 48 bytes including HipparcosID, Distance, SpectralType, and Flags
struct StarData
{
    int HipparcosID;     // 4 bytes  - Hipparcos catalog ID (0 if procedural)
    float DistancePc;    // 4 bytes  - Distance in parsecs (0 if unknown)
    int SpectralType;    // 4 bytes  - 0=O,1=B,2=A,3=F,4=G,5=K,6=M,7=L,255=Unknown
    uint Flags;          // 4 bytes  - Bit 0=IsHero (can be named/important)
    float3 Direction;    // 12 bytes - Normalized direction on celestial sphere
    float Magnitude;     // 4 bytes  - Absolute magnitude (lower = brighter)
    
    float3 Color;        // 12 bytes - RGB color (blackbody corrected)
    float Temperature;   // 4 bytes  - Kelvin (for future PSF effects)
};

StructuredBuffer<StarData> StarCatalog : register(t0);

cbuffer StarfieldParams : register(b0)
{
    // Camera - First 16-byte chunk (8 bytes used, 8 bytes padding)
    float VerticalFOV;   // Radians
    float AspectRatio;   // Width/Height (e.g., 1.77 for 16:9)
    float2 _padCamera0;  // Pad to 16 bytes
    
    // Camera basis vectors - Each float3(12 bytes) + float(4 bytes) = 16 bytes
    float3 CameraRight;
    float _padCamera1;
    float3 CameraUp;
    float _padCamera2;
    float3 CameraForward;
    float _padCamera3;
    
    // Star Distribution
    float MinMagnitude;
    float MaxMagnitude;
    float MagnitudeBias;
    int HeroCount;       // 16-1024, CPU-side only but kept for struct alignment
    
    float Clustering;
    float PopulationBias;
    
    float MainSequenceStrength;
    float RedGiantFrequency;
    float Exposure;      // EV stops
    float BlurPixels;
    
    float2 _pad2;        // Pad after removing StarDensity, HeroRarity, StaggerAmount
    
    // Galactic Structure
    float GalacticFlatness;
    float GalacticDiscFalloff;
    float BandCenterBoost;
    float BandCoreSharpness;
    
    float3 GalacticPlaneNormal;
    float BulgeIntensity;
    
    float3 BulgeCenterDirection;
    float BulgeWidth;
    
    float BulgeHeight;
    float BulgeSoftness;
    float BulgeNoiseScale;
    float BulgeNoiseStrength;
    
    // Screen
    float2 ScreenSize;
    float2 InvScreenSize;
    int FrameIndex;
    int CatalogSize;
    int2 _padEnd;
    
    // HYG Catalog Coordinate Rotation (degrees)
    float RotationX;
    float RotationY;
    float RotationZ;
    float PsfEnhancement;  // 0.0 = Classic Gaussian, 1.0 = Moffat+Jitter
};

// Tuning parameters for live PSF adjustment - matches C# struct exactly
// 16 floats = 64 bytes = 4 float4 registers at b1
cbuffer TuningParams : register(b1)
{
    // Core platform (neon tube body) - float4[0]
    float CorePlatformWidth;      // default: 1.8
    float CorePlatformAmp;        // default: 0.25
    float CoreNormalization;      // default: 1.0
    float MoffatBeta;             // default: 2.0
    
    // Halo/spike sizing - float4[1]
    float HaloSigmaMin;           // default: 3.0
    float HaloSigmaMax;           // default: 8.0
    float HaloWeightMax;          // default: 0.5
    float BrightnessDivisor;      // default: 6.0
    
    // Jitter controls - float4[2]
    float JitterAmplitudeMin;     // default: 0.1
    float JitterAmplitudeMax;     // default: 1.8
    float JitterStrength;         // default: 0.6
    float JitterEdgeStart;        // default: 1.0
    
    // Shape controls - float4[3]
    float SharpSinPower;          // default: 0.2
    float BrightnessCurvePower;   // default: 0.6
    float EdgeFadeStart;          // default: 0.85
    float EdgeFadeEnd;            // default: 1.0
};

// ============================================
// MODULE 2b: COORDINATE ROTATION FOR HYG CATALOG
// ============================================
float3 rotate3D(float3 v, float3 rotationDegrees)
{
    // Convert degrees to radians
    float3 r = radians(rotationDegrees);
    
    // Rotation around X axis
    float cosX = cos(r.x);
    float sinX = sin(r.x);
    float3 v1 = float3(
        v.x,
        v.y * cosX - v.z * sinX,
        v.y * sinX + v.z * cosX
    );
    
    // Rotation around Y axis
    float cosY = cos(r.y);
    float sinY = sin(r.y);
    float3 v2 = float3(
        v1.x * cosY + v1.z * sinY,
        v1.y,
        -v1.x * sinY + v1.z * cosY
    );
    
    // Rotation around Z axis
    float cosZ = cos(r.z);
    float sinZ = sin(r.z);
    float3 v3 = float3(
        v2.x * cosZ - v2.y * sinZ,
        v2.x * sinZ + v2.y * cosZ,
        v2.z
    );
    
    return v3;
}

// ============================================
// MODULE 5: POINT SPREAD FUNCTION
// ============================================
// Normalized Gaussian PSF with flux conservation
// Integral over all pixels equals 1.0 regardless of sigma
float calculate_psf(float dist_pixels, float sigma_pixels)
{
    float norm = 1.0 / (2.0 * 3.14159265 * sigma_pixels * sigma_pixels);
    return norm * exp(-0.5 * pow(dist_pixels / sigma_pixels, 2.0));
}

// Moffat PSF function for enhanced optical character
// Parameters: dist = distance from center, sigma = width parameter, beta = shape parameter (2.5 typical)
// Normalized so integral over infinite domain = 1.0
float calculate_moffat(float dist_pixels, float sigma_pixels, float beta)
{
    float alpha_sq = sigma_pixels * sigma_pixels;  // alpha^2 in Moffat notation
    float term = 1.0 + (dist_pixels * dist_pixels) / alpha_sq;
    // Normalization factor: (beta - 1) / (pi * alpha^2)
    float norm = (beta - 1.0) / (3.14159265 * alpha_sq);
    return norm * pow(term, -beta);
}


// Returns length scale for spikes (1.0 = normal, 2.0 = twice as long, 0.5 = half as long)
// Per-spike variation using star_id to ensure different stars have different patterns
float calculate_spike_length_scale(float angle_rad, float jitter_strength, uint star_id)
{
    // Stable per-star random phase offset (each star gets different spike lengths)
    float star_offset = frac(sin(float(star_id) * 12.9898) * 43758.5453) * 6.28318;
    
    // 8-fold symmetry angle
    float angle_8 = angle_rad * 8.0;
    
    // Spike mask: 1.0 at spike center, 0.0 between spikes
    float spike_mask = pow(abs(sin(angle_8)), max(SharpSinPower, 0.01));
    
    // Length varies per spike using star_offset to shift phase
    // Each of the 8 spikes falls at a different point on this wave due to star_offset
    float length_wave = sin(angle_8 + star_offset);
    length_wave = length_wave * 0.5 + 0.5; // 0 to 1
    float length_var = lerp(JitterAmplitudeMin, JitterAmplitudeMax, length_wave);
    
    // Edge start: only apply length variation outside the core
    // For radial evaluation, we pass dist from the caller, but here we compute the scale factor
    // The caller will handle edge_factor, we return the full scale here
    
    // Blend: no variation (strength=0) -> full variation (strength=1)
    // Scale ranges from 1.0 (normal) to length_var (varied)
    float scale = lerp(1.0, length_var, jitter_strength * JitterStrength);
    
    return max(scale, 0.2);
}

// ============================================
// MAIN ENTRY POINT (Compute Shader)
// ============================================

// ============================================================================
// Scatter Approach: One thread per star
// ============================================================================
[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)CatalogSize) return;
    
    StarData star = StarCatalog[id.x];
    
    // Apply HYG catalog coordinate rotation (if any)
    float3 rotatedDir = rotate3D(star.Direction, float3(RotationX, RotationY, RotationZ));
    
    // Transform star direction to view space (dot product with camera basis)
    float viewX = dot(rotatedDir, CameraRight);
    float viewY = dot(rotatedDir, CameraUp);
    float viewZ = dot(rotatedDir, CameraForward);
    
    // Cull if behind camera (viewZ <= 0)
    if (viewZ <= 0.001) return;
    
    // Calculate projection scale factors
    float tan_fov_y = tan(VerticalFOV * 0.5);
    float tan_fov_x = tan_fov_y * AspectRatio;
    
    // Project to UV space (-1 to 1) - use symmetric FOV for both axes
    float uv_x = viewX / (viewZ * tan_fov_y * AspectRatio);
    float uv_y = viewY / (viewZ * tan_fov_y);
    
    // Cull if outside view frustum (with margin for splat)
    if (uv_x < -1.2 || uv_x > 1.2 || uv_y < -1.2 || uv_y > 1.2) return;
    
    // Convert UV to pixel coordinates
    float pixel_x = (uv_x * 0.5 + 0.5) * ScreenSize.x - 0.5;
    float pixel_y = (uv_y * 0.5 + 0.5) * ScreenSize.y - 0.5;
    
    // Calculate flux from magnitude (same formula as original)
    float flux = pow(10.0, -0.4 * star.Magnitude);
    
    // FIX 1: Calculate pixels per radian at screen center for angular-to-pixel conversion
    // This maintains constant angular star size regardless of FOV zoom
    // SAFETY: Clamp FOV to avoid division by zero or extreme values
    float safe_fov = clamp(VerticalFOV, 0.001, 3.0);  // 0.001 to ~172 degrees
    float tan_half_fov = tan(safe_fov * 0.5);
    float pixels_per_rad = (ScreenSize.y * 0.5) / max(tan_half_fov, 0.0001);
    
    // FIX 1 & 3: Convert angular blur to pixel sigma, enforce minimum 0.5px to prevent flicker
    // BlurPixels is now interpreted as angular sigma in radians
    float sigma_pixels = BlurPixels * pixels_per_rad;
    sigma_pixels = max(sigma_pixels, 0.5);  // Anti-flicker: never smaller than 0.5 pixel sigma
    
    // Additional safety: ensure sigma is finite and not extreme
    if (!isfinite(sigma_pixels) || sigma_pixels > 100.0) sigma_pixels = 0.5;
    
    // Calculate splat radius based on maximum possible extent (core + halo)
    // Halo can extend to 7x sigma for brightest stars, so capture that
    float brightness_factor = saturate(flux / 8.0);
    float max_sigma = sigma_pixels;
    if (PsfEnhancement > 0.001)
    {
        // Halo sigma scales 3x to 7x, need radius to capture the broad wing
        max_sigma = sigma_pixels * (3.0 + brightness_factor * 4.0);
    }
    
    // Calculate splat radius based on mode
    // Classic: 3.5 sigma covers Gaussian well
    // Enhanced: 5.0 sigma needed for Moffat wings (heavier tails)
    int radius = ceil(max_sigma * (PsfEnhancement > 0.001 ? 5.0 : 3.5));
    if (radius < 1) radius = 1;
    if (radius > 50) radius = 50;  // Safety cap to prevent extreme loop counts
    
    int2 center = int2(floor(pixel_x + 0.5), floor(pixel_y + 0.5));
    
    // Splat to neighborhood
    for (int y = -radius; y <= radius; y++)
    {
        for (int x = -radius; x <= radius; x++)
        {
            int2 pix = center + int2(x, y);
            
            // Bounds check
            if (pix.x < 0 || pix.x >= (int)ScreenSize.x || pix.y < 0 || pix.y >= (int)ScreenSize.y) continue;
            
            // Distance from star center in pixels
            float2 delta = float2(pix.x - pixel_x, pix.y - pixel_y);
            float dist = length(delta);
            
            // Calculate angle for jitter (atan2 gives -pi to pi)
            float angle = atan2(delta.y, delta.x);
            
            // PSF selection based on PsfEnhancement slider
            float psf;
            if (PsfEnhancement > 0.001)
            {
                // Tunable enhanced mode: all parameters controlled via TuningParams cbuffer
                
                // Tunable brightness curve (BrightnessDivisor, BrightnessCurvePower)
                float brightness_factor = pow(saturate(flux / BrightnessDivisor), BrightnessCurvePower);
                float halo_weight = brightness_factor * PsfEnhancement * HaloWeightMax;
                
                // CORE: Tunable platform width and amplitude (CorePlatformWidth, CorePlatformAmp, CoreNormalization)
                float core_psf = calculate_psf(dist, sigma_pixels) + 
                                 calculate_psf(dist, sigma_pixels * CorePlatformWidth) * CorePlatformAmp;
                core_psf *= CoreNormalization;  // Tunable renormalization

                // HALO: Tunable beta (MoffatBeta), sigma range (HaloSigmaMin, HaloSigmaMax)
                float halo_range = HaloSigmaMax - HaloSigmaMin;
                float halo_sigma = sigma_pixels * (HaloSigmaMin + brightness_factor * halo_range);
                
                // Vary spike LENGTH by distorting radial coordinate
                // scale > 1.0 = evaluate further out = shorter spike
                // scale < 1.0 = evaluate closer to center = longer spike (but brighter!)
                float length_scale = calculate_spike_length_scale(angle, PsfEnhancement, star.HipparcosID);
                
                // Edge start: gradual fade from 1.0 at center to length_scale at JitterEdgeStart
                float edge_factor = saturate((dist - JitterEdgeStart * sigma_pixels) / (sigma_pixels * 2.0));
                float effective_scale = lerp(1.0, length_scale, edge_factor);
                
                // Coordinate distortion: divide dist by scale to sample Moffat
                // When scale < 1.0, we sample closer to center (brighter), making longer spike
                float jittered_dist = dist / effective_scale;
                
                float halo_psf = calculate_moffat(jittered_dist, halo_sigma, MoffatBeta);
                
                // Fix bulbous tips: compensate brightness when sampling closer to center
                // Moffat(r/s) ~ s^(2*beta) * Moffat(r) for large r, so divide by s^(2*beta)
                if (effective_scale < 1.0) {
                    halo_psf /= pow(effective_scale, 2.0 * MoffatBeta);
                }
                
                // Tunable edge fade (EdgeFadeStart, EdgeFadeEnd)
                // Normalize to expected maximum radius
                float max_expected_radius = sigma_pixels * HaloSigmaMax * max(length_scale, 2.0);
                float edge_dist = dist / max_expected_radius;
                float edge_fade = 1.0 - smoothstep(EdgeFadeStart, EdgeFadeEnd, edge_dist);
                
                psf = (core_psf + halo_psf * halo_weight) * edge_fade;
            }
            else
            {
                // Classic mode: Simple Gaussian
                psf = calculate_psf(dist, sigma_pixels);
            }
            
            if (psf < 0.0005) continue;
            
            // Calculate final contribution (flux * psf * exposure * color)
            // Exposure applied here: pow(2.0, Exposure) matches original shader
            float3 contribution = star.Color * flux * psf * pow(2.0, Exposure);
            
            // Additive blend (race conditions acceptable for Step 1)
            OutputHDR[pix] += contribution;
        }
    }
}