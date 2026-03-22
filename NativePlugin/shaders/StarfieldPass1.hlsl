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

// Hardcoded tuning parameters (adjust these to your finalized values)
// Replace these #defines with your preferred values from testing

// Core platform
#define CorePlatformWidth 1.6
#define CorePlatformAmp 0.15
#define CoreNormalization 0.65
#define MoffatBeta 1.9

// Halo/spike sizing
#define HaloSigmaMin 1.3
#define HaloSigmaMax 15.0
#define HaloWeightMax 0.5
#define BrightnessDivisor 6.0

// Jitter controls
#define JitterAmplitudeMin 0.4
#define JitterAmplitudeMax 0.8
#define JitterStrength 0.6
#define JitterEdgeStart 0.0

// Shape controls
#define SharpSinPower 0.8
#define BrightnessCurvePower 0.7
#define EdgeFadeStart 0.5
#define EdgeFadeEnd 1.5

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
// Organic per-spike variation: each spike gets pseudorandom length based on angle + star_id
float calculate_spike_length_scale(float angle_rad, float dist_pixels, float jitter_strength, uint star_id)
{
    // Stable per-star random offset
    float star_offset = frac(sin(float(star_id) * 12.9898) * 43758.5453) * 6.28318;
    
    // 8-fold symmetry: quantize angle to 8 discrete spikes (0 to 7)
    float angle_norm = frac((angle_rad + star_offset) / 6.2831853); // 0-1 around circle
    uint spike_index = uint(angle_norm * 8.0 + 0.5) % 8u; // Which of 8 spikes (0-7)
    
    // Pseudorandom length for each spike index (deterministic but looks random)
    // Use different frequencies to get irregular pattern (not simple alternating)
    float r1 = frac(sin(float(spike_index) * 12.9898 + star_offset) * 43758.5453);
    float r2 = frac(sin(float(spike_index) * 43.1234 + star_offset * 2.0) * 23421.423);
    float length_wave = (r1 + r2) * 0.5; // Combine two hashes for smoother distribution
    
    float length_var = lerp(JitterAmplitudeMin, JitterAmplitudeMax, length_wave);
    
    // Edge start: fade in length variation starting at JitterEdgeStart * sigma
    float edge_start_dist = max(JitterEdgeStart * 2.0, 0.001);
    float edge_factor = saturate((dist_pixels - edge_start_dist) / (edge_start_dist * 2.0));
    float scale = lerp(1.0, length_var, edge_factor * jitter_strength * JitterStrength);
    
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
    float base_sigma = BlurPixels * pixels_per_rad;
    base_sigma = max(base_sigma, 0.5);  // Anti-flicker: never smaller than 0.5 pixel sigma
    
    // Calculate brightness factor early (needed for sigma growth and PSF)
    float brightness_factor = pow(saturate(flux / BrightnessDivisor), BrightnessCurvePower);
    
    // Bright stars GROW larger instead of saturating to white
    // At max brightness, star is 2x larger (sigma doubled) - spreads energy over more pixels
    float brightness_growth = 1.0 + brightness_factor * 1.0;  // Tune the 1.0 for more/less growth
    float sigma_pixels = base_sigma * brightness_growth;
    
    // Additional safety: ensure sigma is finite and not extreme
    if (!isfinite(sigma_pixels) || sigma_pixels > 100.0) sigma_pixels = 0.5;
    
    // Calculate splat radius based on maximum possible extent (core + halo + max spike length)
    float max_sigma = sigma_pixels;
    if (PsfEnhancement > 0.001)
    {
        // Base halo sigma scales from HaloSigmaMin to HaloSigmaMax
        // Then multiplied by max length scale (JitterAmplitudeMax)
        float base_halo_sigma = sigma_pixels * HaloSigmaMax;
        max_sigma = base_halo_sigma * max(JitterAmplitudeMax, 1.0);
    }
    
    // Calculate splat radius: brightness-scaled to prevent dim stars from hogging GPU
    // Bright stars (flux 8+) get full radius for long spikes, dim stars get tight radius
    float radius_mult = lerp(3.5, 6.0, brightness_factor * PsfEnhancement); // 3.5 to 6.0 based on brightness
    int radius = ceil(max_sigma * radius_mult);
    
    // Hard caps: dim stars capped at 25px radius, bright stars capped at 60px
    int max_radius = 20 + int(40 * brightness_factor); // 20 to 60
    if (radius > max_radius) radius = max_radius;
    if (radius < 1) radius = 1;
    
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
                // brightness_factor already calculated above for sigma growth
                float halo_weight = brightness_factor * PsfEnhancement * HaloWeightMax;
                
                // CORE only first (cheap)
                float core_psf = calculate_psf(dist, sigma_pixels) + 
                                 calculate_psf(dist, sigma_pixels * CorePlatformWidth) * CorePlatformAmp;
                core_psf *= CoreNormalization;
                
                // HALO: Tunable beta (MoffatBeta), sigma range (HaloSigmaMin, HaloSigmaMax)
                float halo_range = HaloSigmaMax - HaloSigmaMin;
                float halo_sigma = sigma_pixels * (HaloSigmaMin + brightness_factor * halo_range);
                
                // HALO: Skip expensive Moffat if not near a spike direction
                float angle_8 = angle * 8.0;
                float spike_mask = pow(abs(sin(angle_8)), max(SharpSinPower, 0.02));
                
                float halo_psf = 0.0;
                // Only calculate Moffat if spike_mask is significant (>1% contribution)
                if (spike_mask > 0.01 && dist > sigma_pixels * 0.5) {
                    float length_scale = calculate_spike_length_scale(angle, dist, PsfEnhancement, star.HipparcosID);
                    float jittered_dist = dist / length_scale;
                    
                    halo_psf = calculate_moffat(jittered_dist, halo_sigma, MoffatBeta);
                    
                    if (length_scale < 1.0) {
                        halo_psf /= pow(length_scale, 2.0 * MoffatBeta - 1.0);
                    }
                    halo_psf *= spike_mask;
                }
                
                // Tunable edge fade (EdgeFadeStart, EdgeFadeEnd)
                float max_expected_radius = sigma_pixels * HaloSigmaMax * max(JitterAmplitudeMax, 2.0);
                float edge_dist = dist / max_expected_radius;
                float edge_fade = 1.0 - smoothstep(EdgeFadeStart, EdgeFadeEnd, edge_dist);
                
                // Combine
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