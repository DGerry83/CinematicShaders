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
    float _padRotation;
};

// ============================================
// MODULE 2: MATH UTILITIES (Converted from Godot)
// ============================================
float3 hash33(float3 p)
{
    float3 q = float3(
        dot(p, float3(127.1, 311.7, 74.7)),
        dot(p, float3(269.5, 183.3, 246.1)),
        dot(p, float3(113.5, 271.9, 124.6))
    );
    return frac(sin(q) * 43758.5453);
}

float hash13(float3 p)
{
    return frac(sin(dot(p, float3(12.9898, 78.233, 45.164))) * 43758.5453);
}

float value_noise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    
    return lerp(
        lerp(
            lerp(hash13(i + float3(0,0,0)), hash13(i + float3(1,0,0)), f.x),
            lerp(hash13(i + float3(0,1,0)), hash13(i + float3(1,1,0)), f.x),
            f.y
        ),
        lerp(
            lerp(hash13(i + float3(0,0,1)), hash13(i + float3(1,0,1)), f.x),
            lerp(hash13(i + float3(0,1,1)), hash13(i + float3(1,1,1)), f.x),
            f.y
        ),
        f.z
    );
}

float fbm_noise(float3 p, float scale)
{
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;
    
    [unroll]
    for(int i = 0; i < 3; i++)
    {
        value += amplitude * value_noise(p * frequency * scale);
        amplitude *= 0.5;
        frequency *= 2.0;
    }
    return value;
}

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
// MODULE 3: SIMPLEX NOISE
// ============================================
float3 mod289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float4 mod289v4(float4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float4 permute(float4 x) { return mod289v4(((x*34.0)+1.0)*x); }
float4 taylorInvSqrt(float4 r) { return 1.79284291400159 - 0.85373472095314 * r; }

float snoise(float3 v)
{
    const float2 C = float2(1.0/6.0, 1.0/3.0);
    const float4 D = float4(0.0, 0.5, 1.0, 2.0);
    
    float3 i  = floor(v + dot(v, C.yyy));
    float3 x0 = v - i + dot(i, C.xxx);
    
    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min(g.xyz, l.zxy);
    float3 i2 = max(g.xyz, l.zxy);
    
    float3 x1 = x0 - i1 + C.xxx;
    float3 x2 = x0 - i2 + C.yyy;
    float3 x3 = x0 - D.yyy;
    
    i = mod289(i);
    float4 p = permute(permute(permute(
        i.z + float4(0.0, i1.z, i2.z, 1.0))
        + i.y + float4(0.0, i1.y, i2.y, 1.0))
        + i.x + float4(0.0, i1.x, i2.x, 1.0));
        
    float n_ = 0.142857142857;
    float3 ns = n_ * D.wyz - D.xzx;
    
    float4 j = p - 49.0 * floor(p * ns.z * ns.z);
    
    float4 x_ = floor(j * ns.z);
    float4 y_ = floor(j - 7.0 * x_);
    
    float4 x = x_ *ns.x + ns.yyyy;
    float4 y = y_ *ns.x + ns.yyyy;
    float4 h = 1.0 - abs(x) - abs(y);
    
    float4 b0 = float4(x.xy, y.xy);
    float4 b1 = float4(x.zw, y.zw);
    
    float4 s0 = floor(b0)*2.0 + 1.0;
    float4 s1 = floor(b1)*2.0 + 1.0;
    float4 sh = -step(h, float4(0.0, 0.0, 0.0, 0.0));
    
    float4 a0 = b0.xzyw + s0.xzyw*sh.xxyy;
    float4 a1 = b1.xzyw + s1.xzyw*sh.zzww;
    
    float3 p0 = float3(a0.xy, h.x);
    float3 p1 = float3(a0.zw, h.y);
    float3 p2 = float3(a1.xy, h.z);
    float3 p3 = float3(a1.zw, h.w);
    
    float4 norm = taylorInvSqrt(float4(dot(p0,p0), dot(p1,p1), dot(p2,p2), dot(p3,p3)));
    p0 *= norm.x;
    p1 *= norm.y;
    p2 *= norm.z;
    p3 *= norm.w;
    
    float4 m = max(0.6 - float4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3)), 0.0);
    m = m * m;
    return 42.0 * dot(m*m, float4(dot(p0,x0), dot(p1,x1), dot(p2,x2), dot(p3,x3)));
}

// ============================================
// MODULE 4: CAMERA/RAY GENERATION (RADIANS VERSION)
// ============================================
float3 generate_view_ray(float2 uv, float fov_rad, float aspect)
{
    float tan_fov_y = tan(fov_rad * 0.5);
    float tan_fov_x = tan_fov_y * aspect;
    
    // View space ray (camera looks down -Z)
    float3 rd;
    rd.x = uv.x * tan_fov_x;
    rd.y = uv.y * tan_fov_y;
    rd.z = 1.0;
    rd = normalize(rd);
    
    // Transform to world space using camera basis vectors
    // WorldRay = rd.x * Right + rd.y * Up + rd.z * Forward
    float3 worldRay = rd.x * CameraRight + rd.y * CameraUp + rd.z * CameraForward;
    return normalize(worldRay);
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

// ============================================
// MODULE 6: GALAXY DENSITY
// ============================================
float get_galactic_density(float3 ray_direction, float flatness, float falloff, 
    float band_boost, float band_sharpness, float3 normal, float bulge_intensity,
    float3 bulge_center, float bulge_width, float bulge_height, float bulge_softness,
    float bulge_noise_scale, float bulge_noise_str)
{
    if(flatness <= 0.001) return 1.0;
    
    float3 n = normalize(normal);
    float sin_latitude = dot(ray_direction, n);
    float abs_sin_lat = abs(sin_latitude);
    float cos_latitude = sqrt(max(0.0, 1.0 - sin_latitude * sin_latitude));
    
    float exponent = falloff * flatness;
    float base_density = pow(max(cos_latitude, 0.0), exponent);
    float core_density = band_boost * pow(max(cos_latitude, 0.0), band_sharpness);
    
    float bulge_density = 0.0;
    if(bulge_intensity > 0.0)
    {
        float3 projected_ray = ray_direction - sin_latitude * n;
        float3 center_dir = normalize(bulge_center);
        float3 projected_center = center_dir - dot(center_dir, n) * n;
        
        float center_len = length(projected_center);
        if(center_len > 0.001)
        {
            projected_center /= center_len;
            float cos_long = dot(normalize(projected_ray), projected_center);
            // Fast approximation: 1-cos(θ) ≈ θ²/2, sufficient for soft bulge mask
            // Range [0,2] instead of [0,π], but bulge_width is tunable anyway
            float long_dist = 1.0 - cos_long;
            float lat_dist = abs_sin_lat;
            
            float dx = long_dist / bulge_width;
            float dy = lat_dist / bulge_height;
            float t = sqrt(dx*dx + dy*dy);
            
            float softness_curve = pow(max(bulge_softness, 0.0), 0.1);
            float edge_exponent = lerp(20.0, 0.1, softness_curve);
            float base_falloff = pow(max(0.0, 1.0 - t), edge_exponent);
            
            float n = fbm_noise(ray_direction, bulge_noise_scale * 0.1);
            float density_mod = 1.0 - (n * bulge_noise_str);
            float falloff_bulge = base_falloff * density_mod;
            
            bulge_density = bulge_intensity * falloff_bulge;
        }
    }
    
    return base_density + core_density + bulge_density;
}

// ============================================
// MODULE 7: SPATIAL CLUSTERING
// ============================================
float calculate_clustering(float3 ray_dir, float density, float strength)
{
    float cluster_noise = hash13(floor(ray_dir * density * 0.1));
    return 0.2 + cluster_noise * strength * 0.6;
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
    
    // Calculate splat radius (3.5 sigma covers 99.95% of Gaussian)
    // At minimum sigma=0.5, radius = ceil(1.75) = 2, giving ~3-4 pixel footprint
    int radius = ceil(sigma_pixels * 3.5);
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
            float dist = length(float2(pix.x - pixel_x, pix.y - pixel_y));
            
            // FIX 2: Normalized Gaussian PSF with flux conservation
            // Total deposited flux is independent of sigma, preventing brightness changes when zooming
            float psf = calculate_psf(dist, sigma_pixels);
            if (psf < 0.001) continue;
            
            // Calculate final contribution (flux * psf * exposure * color)
            // Exposure applied here: pow(2.0, Exposure) matches original shader
            float3 contribution = star.Color * flux * psf * pow(2.0, Exposure);
            
            // Additive blend (race conditions acceptable for Step 1)
            OutputHDR[pix] += contribution;
        }
    }
}