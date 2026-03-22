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

// Returns radius multiplier based on angle and star ID (stable per star)
float calculate_angular_jitter(float dist_pixels, float angle_rad, float jitter_strength, uint star_id)
{
    // Stable pseudo-random per star based on ID
    float star_rand = frac(sin(float(star_id) * 12.9898) * 43758.5453);
    
    // Low frequency amplitude modulation (2 cycles around circle)
    // Expanded range 0.15-1.0 for more dramatic spike length variation
    float amp_noise = frac(sin(angle_rad * 2.0 + star_rand * 3.14159) * 43758.5453);
    amp_noise = amp_noise * amp_noise * (3.0 - 2.0 * amp_noise); // Smoothstep for softer transitions
    amp_noise = lerp(0.15, 1.0, amp_noise);  // Some spikes only 15% length, others 100%
    
    // Sine wave based on angle (8 cycles around the circle)
    // Multiply by amp_noise to break perfect 8-fold symmetry
    float sine_wave = sin(angle_rad * 8.0 + star_rand * 6.28318) * amp_noise;
    
    // Only apply jitter at edges (beyond 1 sigma), falloff toward center
    float edge_factor = saturate((dist_pixels - 1.0) * 0.5);
    
    // Increased amplitude: up to +/- 25% radius variation (was 15%)
    return 1.0 + sine_wave * jitter_strength * 0.25 * edge_factor;
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
                // Enhanced mode: Dual-lobe PSF (classic-sized core + broad halo for bright stars)
                // CORE: Matches classic exactly (same sigma, Gaussian) so dim stars appear unchanged
                // HALO: Broad Moffat with jitter adds size/softness for bright stars only
                
                // Brightness determines halo presence: dim stars = core only (classic look)
                // Bright stars (flux 8+) get up to 40% halo addition
                float brightness_factor = saturate(flux / 8.0);
                float halo_weight = brightness_factor * PsfEnhancement * 0.40;
                
                // CORE: Classic Gaussian at exact same sigma (no size change for dim stars)
                float core_psf = calculate_psf(dist, sigma_pixels);
                
                // HALO: Broad Moffat (beta 2.0) with angular jitter
                // Sigma scales 3x to 7x based on brightness (subtle for average, huge for bright)
                float halo_sigma = sigma_pixels * (3.0 + brightness_factor * 4.0);
                float jitter_factor = calculate_angular_jitter(dist, angle, PsfEnhancement, star.HipparcosID);
                float jittered_dist = dist * jitter_factor;
                float halo_psf = calculate_moffat(jittered_dist, halo_sigma, 2.0);
                
                // Combine: Core (preserved) + additive Halo (bright stars get bigger/softter)
                psf = core_psf + halo_psf * halo_weight;
            }
            else
            {
                // Classic mode: Simple Gaussian
                psf = calculate_psf(dist, sigma_pixels);
            }
            
            // Radial edge fade: prevents hard truncation ring on the broad halo
            // Only affects enhanced mode's large radius splats
            if (PsfEnhancement > 0.001)
            {
                float edge_dist = dist / float(radius);
                float edge_fade = 1.0 - smoothstep(0.85, 1.0, edge_dist);
                psf *= edge_fade;
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