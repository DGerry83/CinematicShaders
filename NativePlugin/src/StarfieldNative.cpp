#include "StarfieldNative.h"
#include "../include/StarfieldPass1.h"
#include "../include/StarfieldPass2.h"
#include "../include/StarfieldVS.h"
#include "../include/StarfieldPrefilter.h"
#include "../include/StarfieldBlurX.h"
#include "../include/StarfieldBlur.h"
#include "../include/StarfieldPass2Soft.h"
#include "../include/StarfieldUpscale.h"
#include <vector>
#include <mutex>
#include <algorithm>
#include <cmath>

// External declarations from main module
extern void LogToFile(const char* fmt, ...);

static struct {
    ID3D11Device* device = nullptr;
    ID3D11Texture2D* hdrTexture = nullptr;
    ID3D11UnorderedAccessView* hdrUAV = nullptr;
    ID3D11ShaderResourceView* hdrSRV = nullptr;
    
    // Shaders
    ID3D11ComputeShader* pass1CS = nullptr;
    ID3D11VertexShader* pass2VS = nullptr;
    ID3D11PixelShader* pass2PS = nullptr;
    
    // States
    ID3D11SamplerState* linearSampler = nullptr;
    ID3D11DepthStencilState* depthState = nullptr;  // Depth test: draw if depth < epsilon (sky)
    ID3D11BlendState* blendState = nullptr;
    ID3D11RasterizerState* rasterState = nullptr;
    
    // Constant buffers
    ID3D11Buffer* pass1CB = nullptr;
    ID3D11Buffer* pass2CB = nullptr;

    // Soft bloom constant buffers (persistent, updated per-frame)
    ID3D11Buffer* prefilterCB = nullptr;
    ID3D11Buffer* blurCB = nullptr;
    ID3D11Buffer* compositeCB = nullptr;
    
    // Cached dimensions
    int width = 0;
    int height = 0;
    bool initialized = false;
    DXGI_FORMAT cachedHDRFormat = DXGI_FORMAT_UNKNOWN;
    
    // Current frame params
    float verticalFOV = 1.0f;  // Radians
    float aspectRatio = 16.0f/9.0f;
    float3 cameraRight;
    float _pad0;               // 16-byte alignment for constant buffer matching
    float3 cameraUp;
    float _pad1;               // 16-byte alignment
    float3 cameraForward;
    float _pad2;               // 16-byte alignment
    float exposure = 3.0f;
    float minMagnitude = -1.0f;
    float maxMagnitude = 10.0f;
    float magnitudeBias = 0.08f;
    int heroCount = 128;  // 16-1024, absolute count of hero stars
    float clustering = 0.6f;
    float populationBias = 0.0f;
    float mainSequenceStrength = 0.6f;
    float redGiantFrequency = 0.05f;
    float galacticFlatness = 0.85f;
    float galacticDiscFalloff = 3.0f;
    float bandCenterBoost = 0.0f;
    float bandCoreSharpness = 20.0f;
    float3 galacticPlaneNormal = float3(0.0f, 1.0f, 0.0f);  // Y-axis: galactic plane is X-Z
    float bulgeIntensity = 5.0f;
    float3 bulgeCenterDirection = float3(1.0f, 0.0f, 0.0f);
    float bulgeWidth = 0.5f;
    float bulgeHeight = 0.5f;
    float bulgeSoftness = 0.0f;
    float bulgeNoiseScale = 20.0f;
    float bulgeNoiseStrength = 0.0f;
    float bloomThreshold = 0.8f;
    float bloomIntensity = 2.0f;
    float colorSaturation = 1.0f;  // 0.5=realistic, 1.0=natural, 2.0=vivid
    float psfEnhancement = 0.0f;  // 0.0 = Classic Gaussian, 1.0 = Moffat+Jitter
    bool useSoftBloom = false;    // false = Classic (original), true = Soft HDR (2-pass)
    float blurPixels = 1.0f;
    int frameIndex = 0;
    
    // HYG Catalog Coordinate Rotation (degrees)
    float rotationX = 0.0f;
    float rotationY = 0.0f;
    float rotationZ = 0.0f;
    
    // Atmospheric extinction parameters
    float extinctionZenith = 1.0f;
    float extinctionHorizon = 1.0f;
    float3 atmosphereUp = float3(0.0f, 1.0f, 0.0f);
    
    // Catalog buffer management
    ID3D11Buffer* starCatalogBuffer = nullptr;
    int catalogSize = 0;
    int catalogCapacity = 0;     // Allocated capacity (may be larger than catalogSize)
    int catalogSeed = 0;         // Track current seed for debugging
    int catalogHeroCount = 0;    // Actual hero count in loaded/generated catalog
    
    // CPU-side copy for save operations (GPU buffer is DYNAMIC with WRITE-only access)
    std::vector<StarData> catalogDataCPU;
    
    // Flag set when device is acquired but catalog is empty - signals C# to reload
    bool catalogNeedsReload = false;
    
    std::mutex stateMutex;

    // Soft HDR bloom pathway resources
    ID3D11Texture2D* bloomTexture = nullptr;
    ID3D11RenderTargetView* bloomRTV = nullptr;
    ID3D11ShaderResourceView* bloomSRV = nullptr;
    ID3D11Texture2D* bloomTempTexture = nullptr;  // Ping-pong target for vertical blur
    ID3D11RenderTargetView* bloomTempRTV = nullptr;
    ID3D11ShaderResourceView* bloomTempSRV = nullptr;
    ID3D11Texture2D* bloomHalfTexture = nullptr;  // Half-res upscaled bloom
    ID3D11RenderTargetView* bloomHalfRTV = nullptr;
    ID3D11ShaderResourceView* bloomHalfSRV = nullptr;
    ID3D11PixelShader* prefilterPS = nullptr;
    ID3D11PixelShader* blurXPS = nullptr;      // Horizontal blur
    ID3D11PixelShader* blurPS = nullptr;       // Vertical blur (keep existing name)
    ID3D11PixelShader* softCompositePS = nullptr;
    ID3D11PixelShader* upscalePS = nullptr;
} g_StarfieldState;

// Constant buffer layouts (must match HLSL exactly, 16-byte aligned)
struct StarfieldPass1Params {
    float VerticalFOV;
    float AspectRatio;
    float _padCamera0[2];
    
    float CameraRight[3];
    float _padCamera1;
    
    float CameraUp[3];
    float _padCamera2;
    
    float CameraForward[3];
    float _padCamera3;
    
    float MinMagnitude;
    float MaxMagnitude;
    float MagnitudeBias;
    int HeroCount;      // 16-1024
    
    float Clustering;
    float PopulationBias;
    
    float MainSequenceStrength;
    float RedGiantFrequency;
    float Exposure;
    float BlurPixels;
    float _pad2[2];  // Pad after removing StarDensity, HeroRarity, StaggerAmount
    
    float GalacticFlatness;
    float GalacticDiscFalloff;
    float BandCenterBoost;
    float BandCoreSharpness;
    
    // float3 + float = 16 bytes
    float GalacticPlaneNormalX;
    float GalacticPlaneNormalY;
    float GalacticPlaneNormalZ;
    float BulgeIntensity;
    
    // float3 + float = 16 bytes  
    float BulgeCenterDirectionX;
    float BulgeCenterDirectionY;
    float BulgeCenterDirectionZ;
    float BulgeWidth;
    
    float BulgeHeight;
    float BulgeSoftness;
    float BulgeNoiseScale;
    float BulgeNoiseStrength;
    
    float ScreenSizeX;
    float ScreenSizeY;
    float InvScreenSizeX;
    float InvScreenSizeY;
    
    int FrameIndex;
    int CatalogSize;
    int Pad1[2];  // Pad to 16 bytes
    
    // HYG Catalog Coordinate Rotation (degrees converted to radians in shader)
    float RotationX;
    float RotationY;
    float RotationZ;
    float PsfEnhancement;  // 0.0 = Classic Gaussian, 1.0 = Moffat+Jitter
};

struct StarfieldPass2Params {
    float ScreenSizeX;
    float ScreenSizeY;
    float InvScreenSizeX;
    float InvScreenSizeY;
    float BloomThreshold;
    float BloomIntensity;
    float DepthThreshold;
    float ExposureEV;
    int EnableTonemapping;
    float Pad1[3];  // Pad to 16 bytes
    
    float ExtinctionZenith;
    float ExtinctionHorizon;
    float Pad2[2];  // Pad to 16 bytes
    
    float AtmosphereUpX;
    float AtmosphereUpY;
    float AtmosphereUpZ;
    float Pad3;     // Alignment padding ONLY - matches original shader
};

// Soft bloom constant buffer layouts (must match HLSL exactly)
struct PrefilterParams {
    float SourceSizeX, SourceSizeY;        // float4[0].xy
    float InvSourceSizeX, InvSourceSizeY;  // float4[0].zw
    float BloomThreshold;                  // float4[1].x
    float BloomKnee;                       // float4[1].y (NEW: was BloomSpread)
    float OutputSizeX, OutputSizeY;        // float4[1].zw
    float InvOutputSizeX, InvOutputSizeY;  // float4[2].xy (NEW: quarter-res inverse size)
    float Pad[2];                          // float4[2].zw (padding to 48 bytes)
};

struct BlurParams {
    float TexelSizeX, TexelSizeY;
    float BloomSpread;      // Match prefilter spread for consistency
    float Pad;
};

struct SoftCompositeParams {
    float ScreenSizeX, ScreenSizeY;
    float InvScreenSizeX, InvScreenSizeY;
    float BloomIntensity;     // Final intensity multiplier
    float ExposureEV;
    int EnableTonemapping;
    float Pad1;
    float ExtinctionZenith;
    float ExtinctionHorizon;
    float Pad2[2];
    float AtmosphereUpX, AtmosphereUpY, AtmosphereUpZ;
    float Pad3;
};

// ============================================================================
// Utility Functions for Star Generation
// ============================================================================

// Calculate spectral type enum from temperature
static int32_t TemperatureToSpectralType(float temp) {
    if (temp < 3500.0f) return 6;      // M-type (red)
    else if (temp < 4500.0f) return 5; // K-type (orange)
    else if (temp < 5778.0f) return 4; // G-type (yellow)
    else if (temp < 7200.0f) return 3; // F-type (yellow-white)
    else if (temp < 9500.0f) return 2; // A-type (white)
    else if (temp < 20000.0f) return 1; // B-type (blue-white)
    else return 0;                      // O-type (blue)
}

// Luminosity class enum
enum LuminosityClass {
    LUM_SUPERGIANT = 0,  // Ia, Ib
    LUM_GIANT = 1,       // II, III
    LUM_SUBGIANT = 2,    // IV
    LUM_DWARF = 3,       // V (Main Sequence) - 90% of stars
    LUM_COUNT = 4
};

// Absolute Magnitude (M_v) lookup table for Main Sequence (Dwarf) stars by spectral type
// Spectral types: O=0, B=1, A=2, F=3, G=4, K=5, M=6
static const float AbsMag_MainSequence[7] = {
    -4.0f,   // O-type (Blue) - Very luminous
    -1.5f,   // B-type (Blue-white)
    +0.7f,   // A-type (White) - Sirius-like
    +2.5f,   // F-type (Yellow-white)
    +4.8f,   // G-type (Yellow) - Sun-like
    +6.5f,   // K-type (Orange)
    +9.0f    // M-type (Red) - Very dim
};

// Absolute Magnitude for Giant stars (luminous evolved stars)
static const float AbsMag_Giant[7] = {
    -6.5f,   // O-type giants (rare)
    -4.0f,   // B-type giants
    -0.5f,   // A-type giants
    +1.0f,   // F-type giants
    +2.5f,   // G-type giants
    +4.0f,   // K-type giants
    +5.5f    // M-type giants (very luminous, e.g., Betelgeuse)
};

// Absolute Magnitude for Supergiant stars (extremely luminous)
static const float AbsMag_Supergiant[7] = {
    -7.5f,   // O-type supergiants
    -6.5f,   // B-type supergiants (e.g., Rigel)
    -3.0f,   // A-type supergiants
    -1.0f,   // F-type supergiants
    +1.0f,   // G-type supergiants
    +2.5f,   // K-type supergiants (e.g., Betelgeuse)
    +4.0f    // M-type supergiants (e.g., Antares)
};

// Assign luminosity class based on random hash and star properties
// 90% Dwarfs (main sequence), 9% Giants, 1% Supergiants
static LuminosityClass AssignLuminosityClass(float randomHash, int32_t spectralType, float normalizedBrightness) {
    // Bright red stars are likely giants/supergiants (red giant branch)
    if (spectralType >= 5 && normalizedBrightness < 0.2f) {
        // 30% chance of being a giant/supergiant if bright and red
        if (randomHash < 0.3f) {
            return (randomHash < 0.1f) ? LUM_SUPERGIANT : LUM_GIANT;
        }
    }
    
    // Standard distribution
    if (randomHash < 0.90f) return LUM_DWARF;        // 90% main sequence
    else if (randomHash < 0.99f) return LUM_GIANT;   // 9% giants
    else return LUM_SUPERGIANT;                       // 1% supergiants
}

// Get absolute magnitude based on spectral type and luminosity class
static float GetAbsoluteMagnitude(int32_t spectralType, LuminosityClass lumClass) {
    // Clamp spectral type to valid range
    if (spectralType < 0) spectralType = 0;
    if (spectralType > 6) spectralType = 6;
    
    switch (lumClass) {
        case LUM_SUPERGIANT:
            return AbsMag_Supergiant[spectralType];
        case LUM_GIANT:
            return AbsMag_Giant[spectralType];
        case LUM_SUBGIANT:
            // Subgiants are between dwarfs and giants
            return (AbsMag_MainSequence[spectralType] + AbsMag_Giant[spectralType]) * 0.5f;
        case LUM_DWARF:
        default:
            return AbsMag_MainSequence[spectralType];
    }
}

// Calculate distance in parsecs using the Distance Modulus
// d = 10^((m - M + 5) / 5)
// where m = apparent magnitude, M = absolute magnitude
// forcedLumClass: optional override for luminosity class (for realistic mode)
static float CalculateDistancePc(float apparentMag, int32_t spectralType, float randomHash, float normalizedBrightness, LuminosityClass forcedLumClass = LUM_COUNT, int heroIndex = -1) {
    // Assign luminosity class (or use forced class if provided)
    LuminosityClass lumClass = (forcedLumClass != LUM_COUNT) ? forcedLumClass : AssignLuminosityClass(randomHash, spectralType, normalizedBrightness);
    
    
    // Get absolute magnitude for this spectral type and luminosity class
    float absoluteMag = GetAbsoluteMagnitude(spectralType, lumClass);
    
    // Add some random variation to absolute magnitude (stars aren't all identical)
    // +/- 0.5 magnitude scatter
    absoluteMag += (randomHash - 0.5f) * 1.0f;
    
    // Distance Modulus: m - M = 5 * log10(d) - 5
    // Solving for d: d = 10^((m - M + 5) / 5)
    float distanceModulus = apparentMag - absoluteMag + 5.0f;
    float distance = powf(10.0f, distanceModulus / 5.0f);
    
    // Clamp to reasonable astronomical range
    // Nearest stars: ~1.3 pc (Proxima), Galaxy: ~50,000 pc
    return fmaxf(0.5f, fminf(50000.0f, distance));
}

// ============================================================================
// Catalog Generation Math (Ported from HLSL)
// ============================================================================

inline float Frac(float x) {
    return x - floorf(x);
}

inline float Dot(const float3& a, const float3& b) {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

inline float Length(const float3& v) {
    return sqrtf(v.x * v.x + v.y * v.y + v.z * v.z);
}

inline float3 Normalize(const float3& v) {
    float len = Length(v);
    if (len < 0.0001f) return float3(0, 0, 0);
    float inv = 1.0f / len;
    return float3(v.x * inv, v.y * inv, v.z * inv);
}

// Hash functions (must match HLSL exactly)
static float3 Hash33(const float3& p) {
    float3 q;
    q.x = Dot(p, float3(127.1f, 311.7f, 74.7f));
    q.y = Dot(p, float3(269.5f, 183.3f, 246.1f));
    q.z = Dot(p, float3(113.5f, 271.9f, 124.6f));
    
    return float3(
        Frac(sinf(q.x) * 43758.5453f),
        Frac(sinf(q.y) * 43758.5453f),
        Frac(sinf(q.z) * 43758.5453f)
    );
}

static float Hash13(const float3& p) {
    float q = Dot(p, float3(12.9898f, 78.233f, 45.164f));
    return Frac(sinf(q) * 43758.5453f);
}

// Value noise for bulge (simplified fbm)
static float ValueNoise(const float3& p) {
    float3 i = float3(floorf(p.x), floorf(p.y), floorf(p.z));
    float3 f = float3(Frac(p.x), Frac(p.y), Frac(p.z));
    f.x = f.x * f.x * (3.0f - 2.0f * f.x);
    f.y = f.y * f.y * (3.0f - 2.0f * f.y);
    f.z = f.z * f.z * (3.0f - 2.0f * f.z);
    
    // Simplified - just return hash of integer coords for now
    return Hash13(i);
}

// FBM (Fractal Brownian Motion) for organic hierarchical detail
// octaves: number of noise layers (4 recommended for bulge, 3 for clustering)
// lacunarity: frequency multiplier per octave (typically 2.0)
// gain: amplitude multiplier per octave (typically 0.5)
static float FBM(const float3& p, int octaves, float lacunarity, float gain) {
    float value = 0.0f;
    float amplitude = 0.5f;
    float frequency = 1.0f;
    
    for(int i = 0; i < octaves; i++) {
        value += amplitude * ValueNoise(float3(p.x * frequency, p.y * frequency, p.z * frequency));
        amplitude *= gain;
        frequency *= lacunarity;
    }
    
    return value;
}

// Galactic density calculation (matches HLSL get_galactic_density)
static float GetGalacticDensityCPU(const float3& rayDir, 
    float flatness, float falloff, float bandBoost, float bandSharpness,
    const float3& planeNormal, float bulgeIntensity, const float3& bulgeCenter,
    float bulgeWidth, float bulgeHeight, float bulgeSoftness, 
    float bulgeNoiseScale, float bulgeNoiseStr) 
{
    if (flatness <= 0.001f) return 1.0f;
    
    float3 n = Normalize(planeNormal);
    float sinLatitude = Dot(rayDir, n);
    float absSinLat = fabsf(sinLatitude);
    float cosLatitude = sqrtf(max(0.0f, 1.0f - sinLatitude * sinLatitude));
    
    float exponent = falloff * flatness;
    float baseDensity = powf(max(cosLatitude, 0.0f), exponent);
    float coreDensity = bandBoost * powf(max(cosLatitude, 0.0f), bandSharpness);
    
    float bulgeDensity = 0.0f;
    if (bulgeIntensity > 0.0f) {
        float3 projectedRay = float3(
            rayDir.x - sinLatitude * n.x,
            rayDir.y - sinLatitude * n.y,
            rayDir.z - sinLatitude * n.z
        );
        float3 centerDir = Normalize(bulgeCenter);
        float3 projectedCenter = float3(
            centerDir.x - Dot(centerDir, n) * n.x,
            centerDir.y - Dot(centerDir, n) * n.y,
            centerDir.z - Dot(centerDir, n) * n.z
        );
        
        float centerLen = Length(projectedCenter);
        if (centerLen > 0.001f) {
            float3 normProjCenter = float3(
                projectedCenter.x / centerLen,
                projectedCenter.y / centerLen,
                projectedCenter.z / centerLen
            );
            float3 normProjRay = Normalize(projectedRay);
            
            float cosLong = Dot(normProjRay, normProjCenter);
            float longDist = 1.0f - cosLong;
            float latDist = absSinLat;
            
            float dx = longDist / bulgeWidth;
            float dy = latDist / bulgeHeight;
            float t = sqrtf(dx*dx + dy*dy);
            
            float softnessCurve = powf(max(bulgeSoftness, 0.0f), 0.1f);
            float edgeExponent = 20.0f * (1.0f - softnessCurve) + 0.1f * softnessCurve;
            float baseFalloff = powf(max(0.0f, 1.0f - t), edgeExponent);
            
            // FBM for organic bulge edge breakup with hierarchical detail
            // 4 octaves gives big structural variation with fine detail
            float3 noisePos = float3(rayDir.x * bulgeNoiseScale * 0.1f, 
                                     rayDir.y * bulgeNoiseScale * 0.1f, 
                                     rayDir.z * bulgeNoiseScale * 0.1f);
            float noise = FBM(noisePos, 4, 2.0f, 0.5f);
            
            // Secondary detail layer for "tighter" breakup at smaller scales
            float3 detailPos = float3(noisePos.x * 2.0f, noisePos.y * 2.0f, noisePos.z * 2.0f);
            float detailNoise = FBM(detailPos, 3, 2.0f, 0.5f) * 0.5f;
            float combinedNoise = (noise + detailNoise) * 0.6667f;
            
            float densityMod = 1.0f - (combinedNoise * bulgeNoiseStr);
            float falloffBulge = baseFalloff * densityMod;
            
            bulgeDensity = bulgeIntensity * falloffBulge;
        }
    }
    
    return baseDensity + coreDensity + bulgeDensity;
}

// Blackbody color calculation (Tanner Helland algorithm)
// Returns RGB in range [0, 1] for given temperature in Kelvin
static float3 BlackbodyRGB(float temperature)
{
    float t = fmaxf(1000.0f, fminf(40000.0f, temperature));
    float tmp = t / 100.0f;
    
    float r, g, b;
    
    // Red
    if (tmp <= 66.0f) {
        r = 255.0f;
    } else {
        r = 329.698727446f * powf(tmp - 60.0f, -0.1332047592f);
        r = fmaxf(0.0f, fminf(255.0f, r));
    }
    
    // Green
    if (tmp <= 66.0f) {
        g = 99.4708025861f * logf(tmp) - 161.1195681661f;
        g = fmaxf(0.0f, fminf(255.0f, g));
    } else {
        g = 288.1221695283f * powf(tmp - 60.0f, -0.0755148492f);
        g = fmaxf(0.0f, fminf(255.0f, g));
    }
    
    // Blue
    if (tmp >= 66.0f) {
        b = 255.0f;
    } else if (tmp <= 19.0f) {
        b = 0.0f;
    } else {
        b = 138.5177312231f * logf(tmp - 10.0f) - 305.0447927307f;
        b = fmaxf(0.0f, fminf(255.0f, b));
    }
    
    return float3(r / 255.0f, g / 255.0f, b / 255.0f);
}

// Apply saturation to color: 0.5=realistic, 1.0=natural, 4.0=hyper-vivid
static float3 ApplySaturation(float3 baseColor, float sliderValue)
{
    // Map slider to effective saturation with curve
    // Keep exact: 0.5 -> 0.5 (realistic), 1.0 -> 1.0 (natural)
    // Curve above 1.0: 2.0 -> 1.6, 3.0 -> 2.2, 4.0 -> 2.8
    // This prevents abrupt clamping while keeping low-end linear
    
    float t;
    if (sliderValue <= 1.0f) {
        // Linear from 0 to 1 (0.5 stays exactly 0.5 for realistic)
        t = sliderValue;
    } else {
        // Curve above 1.0: t = 1 + (slider-1)^0.8
        // This compresses the high end so each slider step gives similar visual change
        t = 1.0f + powf(sliderValue - 1.0f, 0.8f);
    }
    
    // Calculate color: move away from white by factor t
    float r = 1.0f + (baseColor.x - 1.0f) * t;
    float g = 1.0f + (baseColor.y - 1.0f) * t;
    float b = 1.0f + (baseColor.z - 1.0f) * t;
    
    // Clamp to valid range
    r = fmaxf(0.0f, fminf(1.0f, r));
    g = fmaxf(0.0f, fminf(1.0f, g));
    b = fmaxf(0.0f, fminf(1.0f, b));
    
    return float3(r, g, b);
}

// Starfield Internal Functions
static void EnsureStarfieldResources(ID3D11Device* device, int width, int height)
{
    // Check dimensions AND format (TUFX HDR toggle changes format)
    if (g_StarfieldState.initialized && 
        g_StarfieldState.width == width && 
        g_StarfieldState.height == height &&
        g_StarfieldState.cachedHDRFormat == DXGI_FORMAT_R11G11B10_FLOAT)
        return;
    
    // Cleanup old resources
    if (g_StarfieldState.hdrTexture) {
        g_StarfieldState.hdrTexture->Release();
        g_StarfieldState.hdrUAV->Release();
        g_StarfieldState.hdrSRV->Release();
        g_StarfieldState.hdrTexture = nullptr;
        g_StarfieldState.hdrUAV = nullptr;
        g_StarfieldState.hdrSRV = nullptr;
    }
    
    // HDR Texture for Pass 1 output (R11G11B10_FLOAT)
    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R11G11B10_FLOAT;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_UNORDERED_ACCESS | D3D11_BIND_SHADER_RESOURCE;
    
    HRESULT hr = device->CreateTexture2D(&desc, nullptr, &g_StarfieldState.hdrTexture);
    if (FAILED(hr)) {
        LogToFile("[Starfield] Failed to create HDR texture (0x%08X)", hr);
        return;
    }
    
    hr = device->CreateUnorderedAccessView(g_StarfieldState.hdrTexture, nullptr, &g_StarfieldState.hdrUAV);
    if (FAILED(hr)) {
        LogToFile("[Starfield] Failed to create HDR UAV (0x%08X)", hr);
        g_StarfieldState.hdrTexture->Release();
        return;
    }
    
    hr = device->CreateShaderResourceView(g_StarfieldState.hdrTexture, nullptr, &g_StarfieldState.hdrSRV);
    if (FAILED(hr)) {
        LogToFile("[Starfield] Failed to create HDR SRV (0x%08X)", hr);
        g_StarfieldState.hdrUAV->Release();
        g_StarfieldState.hdrTexture->Release();
        return;
    }
    
    // Shaders
    if (!g_StarfieldState.pass1CS) {
        hr = device->CreateComputeShader(g_StarfieldPass1CS, sizeof(g_StarfieldPass1CS), nullptr, &g_StarfieldState.pass1CS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create Pass 1 CS (0x%08X)", hr);
    }
    
    if (!g_StarfieldState.pass2PS) {
        hr = device->CreatePixelShader(g_StarfieldPass2PS, sizeof(g_StarfieldPass2PS), nullptr, &g_StarfieldState.pass2PS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create Pass 2 PS (0x%08X)", hr);
    }
    
    if (!g_StarfieldState.pass2VS) {
        hr = device->CreateVertexShader(g_StarfieldVS, sizeof(g_StarfieldVS), nullptr, &g_StarfieldState.pass2VS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create Pass 2 VS (0x%08X)", hr);
    }
    
    // Soft HDR bloom resources (quarter resolution)
    int bloomWidth = width / 4;
    int bloomHeight = height / 4;
    if (bloomWidth < 1) bloomWidth = 1;
    if (bloomHeight < 1) bloomHeight = 1;
    
    // Recreate bloom texture if dimensions changed
    if (g_StarfieldState.bloomTexture && (g_StarfieldState.width != width || g_StarfieldState.height != height)) {
        g_StarfieldState.bloomTexture->Release();
        g_StarfieldState.bloomRTV->Release();
        g_StarfieldState.bloomSRV->Release();
        g_StarfieldState.bloomTexture = nullptr;
        g_StarfieldState.bloomRTV = nullptr;
        g_StarfieldState.bloomSRV = nullptr;
    }
    
    // Recreate half-res texture if dimensions changed
    if (g_StarfieldState.bloomHalfTexture && (g_StarfieldState.width != width || g_StarfieldState.height != height)) {
        g_StarfieldState.bloomHalfTexture->Release();
        g_StarfieldState.bloomHalfRTV->Release();
        g_StarfieldState.bloomHalfSRV->Release();
        g_StarfieldState.bloomHalfTexture = nullptr;
        g_StarfieldState.bloomHalfRTV = nullptr;
        g_StarfieldState.bloomHalfSRV = nullptr;
    }
    
    if (!g_StarfieldState.bloomTexture) {
        D3D11_TEXTURE2D_DESC bloomDesc = {};
        bloomDesc.Width = bloomWidth;
        bloomDesc.Height = bloomHeight;
        bloomDesc.MipLevels = 1;
        bloomDesc.ArraySize = 1;
        bloomDesc.Format = DXGI_FORMAT_R11G11B10_FLOAT;
        bloomDesc.SampleDesc.Count = 1;
        bloomDesc.Usage = D3D11_USAGE_DEFAULT;
        bloomDesc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        
        hr = device->CreateTexture2D(&bloomDesc, nullptr, &g_StarfieldState.bloomTexture);
        if (FAILED(hr)) {
            LogToFile("[Starfield] Failed to create bloom texture (0x%08X)", hr);
        } else {
            hr = device->CreateRenderTargetView(g_StarfieldState.bloomTexture, nullptr, &g_StarfieldState.bloomRTV);
            if (FAILED(hr)) LogToFile("[Starfield] Failed to create bloom RTV (0x%08X)", hr);
            
            hr = device->CreateShaderResourceView(g_StarfieldState.bloomTexture, nullptr, &g_StarfieldState.bloomSRV);
            if (FAILED(hr)) LogToFile("[Starfield] Failed to create bloom SRV (0x%08X)", hr);
        }
    }
    
    // Create ping-pong texture for vertical blur (same dimensions as bloomTexture)
    if (!g_StarfieldState.bloomTempTexture) {
        D3D11_TEXTURE2D_DESC tempDesc = {};
        tempDesc.Width = bloomWidth;
        tempDesc.Height = bloomHeight;
        tempDesc.MipLevels = 1;
        tempDesc.ArraySize = 1;
        tempDesc.Format = DXGI_FORMAT_R11G11B10_FLOAT;
        tempDesc.SampleDesc.Count = 1;
        tempDesc.Usage = D3D11_USAGE_DEFAULT;
        tempDesc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        
        hr = device->CreateTexture2D(&tempDesc, nullptr, &g_StarfieldState.bloomTempTexture);
        if (FAILED(hr)) {
            LogToFile("[Starfield] Failed to create bloom temp texture (0x%08X)", hr);
        } else {
            hr = device->CreateRenderTargetView(g_StarfieldState.bloomTempTexture, nullptr, &g_StarfieldState.bloomTempRTV);
            if (FAILED(hr)) LogToFile("[Starfield] Failed to create bloom temp RTV (0x%08X)", hr);
            
            hr = device->CreateShaderResourceView(g_StarfieldState.bloomTempTexture, nullptr, &g_StarfieldState.bloomTempSRV);
            if (FAILED(hr)) LogToFile("[Starfield] Failed to create bloom temp SRV (0x%08X)", hr);
        }
    }
    
    // Create half-res texture for upscaled bloom result (1/2 screen resolution)
    int halfWidth = width / 2;
    int halfHeight = height / 2;
    if (halfWidth < 1) halfWidth = 1;
    if (halfHeight < 1) halfHeight = 1;
    
    if (!g_StarfieldState.bloomHalfTexture) {
        D3D11_TEXTURE2D_DESC halfDesc = {};
        halfDesc.Width = halfWidth;
        halfDesc.Height = halfHeight;
        halfDesc.MipLevels = 1;
        halfDesc.ArraySize = 1;
        halfDesc.Format = DXGI_FORMAT_R11G11B10_FLOAT;
        halfDesc.SampleDesc.Count = 1;
        halfDesc.Usage = D3D11_USAGE_DEFAULT;
        halfDesc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        
        hr = device->CreateTexture2D(&halfDesc, nullptr, &g_StarfieldState.bloomHalfTexture);
        if (FAILED(hr)) {
            LogToFile("[Starfield] Failed to create bloom half-res texture (0x%08X)", hr);
        } else {
            hr = device->CreateRenderTargetView(g_StarfieldState.bloomHalfTexture, nullptr, &g_StarfieldState.bloomHalfRTV);
            if (FAILED(hr)) LogToFile("[Starfield] Failed to create bloom half-res RTV (0x%08X)", hr);
            
            hr = device->CreateShaderResourceView(g_StarfieldState.bloomHalfTexture, nullptr, &g_StarfieldState.bloomHalfSRV);
            if (FAILED(hr)) LogToFile("[Starfield] Failed to create bloom half-res SRV (0x%08X)", hr);
        }
    }
    
    // Load soft bloom shaders (if not already loaded)
    if (!g_StarfieldState.prefilterPS) {
        hr = device->CreatePixelShader(g_StarfieldPrefilterPS, sizeof(g_StarfieldPrefilterPS), nullptr, &g_StarfieldState.prefilterPS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create Prefilter PS (0x%08X)", hr);
    }
    if (!g_StarfieldState.blurXPS) {
        hr = device->CreatePixelShader(g_StarfieldBlurXPS, sizeof(g_StarfieldBlurXPS), nullptr, &g_StarfieldState.blurXPS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create BlurX PS (horizontal) (0x%08X)", hr);
    }
    if (!g_StarfieldState.blurPS) {
        hr = device->CreatePixelShader(g_StarfieldBlurPS, sizeof(g_StarfieldBlurPS), nullptr, &g_StarfieldState.blurPS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create Blur PS (vertical) (0x%08X)", hr);
    }
    if (!g_StarfieldState.softCompositePS) {
        hr = device->CreatePixelShader(g_StarfieldPass2SoftPS, sizeof(g_StarfieldPass2SoftPS), nullptr, &g_StarfieldState.softCompositePS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create SoftComposite PS (0x%08X)", hr);
    }
    if (!g_StarfieldState.upscalePS) {
        hr = device->CreatePixelShader(g_StarfieldUpscalePS, sizeof(g_StarfieldUpscalePS), nullptr, &g_StarfieldState.upscalePS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create Upscale PS (0x%08X)", hr);
    }
    
    // Samplers
    if (!g_StarfieldState.linearSampler) {
        D3D11_SAMPLER_DESC sampDesc = {};
        sampDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sampDesc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampDesc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampDesc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        device->CreateSamplerState(&sampDesc, &g_StarfieldState.linearSampler);
    }
    
    // Depth stencil state: Disabled - we handle masking in pixel shader via normal alpha
    if (!g_StarfieldState.depthState) {
        D3D11_DEPTH_STENCIL_DESC dsDesc = {};
        dsDesc.DepthEnable = FALSE;
        dsDesc.DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ZERO;
        dsDesc.StencilEnable = FALSE;
        device->CreateDepthStencilState(&dsDesc, &g_StarfieldState.depthState);
    }
    
// Blend state: Alpha blend - SrcAlpha/InvSrcAlpha 
// Sky (alpha=1.0): draw stars, Geometry (alpha=0.0): preserve existing pixel
if (!g_StarfieldState.blendState) {
    D3D11_BLEND_DESC blendDesc = {};
    blendDesc.RenderTarget[0].BlendEnable = TRUE;
    blendDesc.RenderTarget[0].SrcBlend = D3D11_BLEND_SRC_ALPHA;
    blendDesc.RenderTarget[0].DestBlend = D3D11_BLEND_INV_SRC_ALPHA;
    blendDesc.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
    blendDesc.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
    blendDesc.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ZERO;
    blendDesc.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
    blendDesc.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
    device->CreateBlendState(&blendDesc, &g_StarfieldState.blendState);
}
    
    // Rasterizer state
    if (!g_StarfieldState.rasterState) {
        D3D11_RASTERIZER_DESC rsDesc = {};
        rsDesc.FillMode = D3D11_FILL_SOLID;
        rsDesc.CullMode = D3D11_CULL_NONE;
        device->CreateRasterizerState(&rsDesc, &g_StarfieldState.rasterState);
    }
    
    // Constant buffers
    if (!g_StarfieldState.pass1CB) {
        D3D11_BUFFER_DESC cbDesc = {};
        cbDesc.ByteWidth = sizeof(StarfieldPass1Params);
        cbDesc.Usage = D3D11_USAGE_DYNAMIC;
        cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        device->CreateBuffer(&cbDesc, nullptr, &g_StarfieldState.pass1CB);
    }
    
    if (!g_StarfieldState.pass2CB) {
        D3D11_BUFFER_DESC cbDesc = {};
        cbDesc.ByteWidth = sizeof(StarfieldPass2Params);
        cbDesc.Usage = D3D11_USAGE_DYNAMIC;
        cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        device->CreateBuffer(&cbDesc, nullptr, &g_StarfieldState.pass2CB);
    }

        
    // Soft bloom constant buffers (created once, updated each frame)
    if (!g_StarfieldState.prefilterCB) {
        D3D11_BUFFER_DESC cbDesc = {};
        cbDesc.ByteWidth = sizeof(PrefilterParams);
        cbDesc.Usage = D3D11_USAGE_DYNAMIC;
        cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        device->CreateBuffer(&cbDesc, nullptr, &g_StarfieldState.prefilterCB);
    }
    
    if (!g_StarfieldState.blurCB) {
        D3D11_BUFFER_DESC cbDesc = {};
        cbDesc.ByteWidth = sizeof(BlurParams);
        cbDesc.Usage = D3D11_USAGE_DYNAMIC;
        cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        device->CreateBuffer(&cbDesc, nullptr, &g_StarfieldState.blurCB);
    }
    
    if (!g_StarfieldState.compositeCB) {
        D3D11_BUFFER_DESC cbDesc = {};
        cbDesc.ByteWidth = sizeof(SoftCompositeParams);
        cbDesc.Usage = D3D11_USAGE_DYNAMIC;
        cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        device->CreateBuffer(&cbDesc, nullptr, &g_StarfieldState.compositeCB);
    }
    
    if (!g_StarfieldState.device) {
        g_StarfieldState.device = device;
        g_StarfieldState.device->AddRef();
    }
    
    g_StarfieldState.width = width;
    g_StarfieldState.height = height;
    g_StarfieldState.initialized = true;
    g_StarfieldState.cachedHDRFormat = DXGI_FORMAT_R11G11B10_FLOAT;
    
    LogToFile("[Starfield] Resources initialized: %dx%d", width, height);
}

static void ExecuteSoftBloomRender(ID3D11DeviceContext* context, ID3D11RenderTargetView* finalRTV);

static void ExecuteStarfieldRender(ID3D11DeviceContext* context)
{
    if (!context) return;
    
    ID3D11Device* device = nullptr;
    context->GetDevice(&device);
    if (!device) return;
    
    // Ensure resources and catalog are ready
    if (!g_StarfieldState.initialized || !g_StarfieldState.starCatalogBuffer || g_StarfieldState.catalogSize == 0) {
        if (device) device->Release();
        return;
    }
    
    // Get current render target dimensions to verify match
    ID3D11RenderTargetView* currentRTV = nullptr;
    ID3D11DepthStencilView* currentDSV = nullptr;
    context->OMGetRenderTargets(1, &currentRTV, &currentDSV);
    
    if (!currentRTV) {
        device->Release();
        return;
    }
    
    // ===== PASS 1: Scatter Stars to HDR Texture =====
    // Clear HDR texture before scattering stars
    UINT clearColor[4] = {0, 0, 0, 0};
    context->ClearUnorderedAccessViewUint(g_StarfieldState.hdrUAV, clearColor);
    
    // Update constant buffer with current state
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(context->Map(g_StarfieldState.pass1CB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        StarfieldPass1Params* params = (StarfieldPass1Params*)mapped.pData;
        
        params->VerticalFOV = g_StarfieldState.verticalFOV;
        params->AspectRatio = g_StarfieldState.aspectRatio;
        params->_padCamera0[0] = 0.0f;
        params->_padCamera0[1] = 0.0f;
        
        params->CameraRight[0] = g_StarfieldState.cameraRight.x;
        params->CameraRight[1] = g_StarfieldState.cameraRight.y;
        params->CameraRight[2] = g_StarfieldState.cameraRight.z;
        params->_padCamera1 = 0.0f;
        
        params->CameraUp[0] = g_StarfieldState.cameraUp.x;
        params->CameraUp[1] = g_StarfieldState.cameraUp.y;
        params->CameraUp[2] = g_StarfieldState.cameraUp.z;
        params->_padCamera2 = 0.0f;
        
        params->CameraForward[0] = g_StarfieldState.cameraForward.x;
        params->CameraForward[1] = g_StarfieldState.cameraForward.y;
        params->CameraForward[2] = g_StarfieldState.cameraForward.z;
        params->_padCamera3 = 0.0f;
        
        params->MinMagnitude = g_StarfieldState.minMagnitude;
        params->MaxMagnitude = g_StarfieldState.maxMagnitude;
        params->MagnitudeBias = g_StarfieldState.magnitudeBias;
        
        params->HeroCount = g_StarfieldState.heroCount;
        params->Clustering = g_StarfieldState.clustering;
        params->PopulationBias = g_StarfieldState.populationBias;
        
        params->MainSequenceStrength = g_StarfieldState.mainSequenceStrength;
        params->RedGiantFrequency = g_StarfieldState.redGiantFrequency;
        params->Exposure = g_StarfieldState.exposure;
        params->BlurPixels = g_StarfieldState.blurPixels;
        
        params->GalacticFlatness = g_StarfieldState.galacticFlatness;
        params->GalacticDiscFalloff = g_StarfieldState.galacticDiscFalloff;
        params->BandCenterBoost = g_StarfieldState.bandCenterBoost;
        params->BandCoreSharpness = g_StarfieldState.bandCoreSharpness;
        
        params->GalacticPlaneNormalX = g_StarfieldState.galacticPlaneNormal.x;
        params->GalacticPlaneNormalY = g_StarfieldState.galacticPlaneNormal.y;
        params->GalacticPlaneNormalZ = g_StarfieldState.galacticPlaneNormal.z;
        params->BulgeIntensity = g_StarfieldState.bulgeIntensity;
        
        params->BulgeCenterDirectionX = g_StarfieldState.bulgeCenterDirection.x;
        params->BulgeCenterDirectionY = g_StarfieldState.bulgeCenterDirection.y;
        params->BulgeCenterDirectionZ = g_StarfieldState.bulgeCenterDirection.z;
        params->BulgeWidth = g_StarfieldState.bulgeWidth;
        
        params->BulgeHeight = g_StarfieldState.bulgeHeight;
        params->BulgeSoftness = g_StarfieldState.bulgeSoftness;
        params->BulgeNoiseScale = g_StarfieldState.bulgeNoiseScale;
        params->BulgeNoiseStrength = g_StarfieldState.bulgeNoiseStrength;
        
        params->ScreenSizeX = (float)g_StarfieldState.width;
        params->ScreenSizeY = (float)g_StarfieldState.height;
        params->InvScreenSizeX = 1.0f / g_StarfieldState.width;
        params->InvScreenSizeY = 1.0f / g_StarfieldState.height;
        
        params->FrameIndex = g_StarfieldState.frameIndex;
        params->CatalogSize = g_StarfieldState.catalogSize;
        params->Pad1[0] = params->Pad1[1] = 0;
        
        params->RotationX = g_StarfieldState.rotationX;
        params->RotationY = g_StarfieldState.rotationY;
        params->RotationZ = g_StarfieldState.rotationZ;
        params->PsfEnhancement = g_StarfieldState.psfEnhancement;
        
        context->Unmap(g_StarfieldState.pass1CB, 0);
    }
    
    // Create SRV for catalog buffer
    ID3D11ShaderResourceView* catalogSRV = nullptr;
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = DXGI_FORMAT_UNKNOWN;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
    srvDesc.Buffer.ElementOffset = 0;
    srvDesc.Buffer.ElementWidth = sizeof(StarData);
    srvDesc.Buffer.NumElements = g_StarfieldState.catalogSize;
    
    HRESULT hr = device->CreateShaderResourceView(g_StarfieldState.starCatalogBuffer, &srvDesc, &catalogSRV);
    if (FAILED(hr) || !catalogSRV) {
        LogToFile("[Starfield] Failed to create catalog SRV (0x%08X)", hr);
        if (catalogSRV) catalogSRV->Release();
        currentRTV->Release();
        if (currentDSV) currentDSV->Release();
        device->Release();
        return;
    }
    
    // Setup compute shader
    context->CSSetShader(g_StarfieldState.pass1CS, nullptr, 0);
    context->CSSetConstantBuffers(0, 1, &g_StarfieldState.pass1CB);
    context->CSSetShaderResources(0, 1, &catalogSRV);
    ID3D11UnorderedAccessView* uavs[1] = {g_StarfieldState.hdrUAV};
    context->CSSetUnorderedAccessViews(0, 1, uavs, nullptr);
    
    // Dispatch: One thread per star (64 threads per group)
    UINT dispatchX = (g_StarfieldState.catalogSize + 63) / 64;
    context->Dispatch(dispatchX, 1, 1);
    
    // Unbind compute resources
    ID3D11UnorderedAccessView* nullUAV[1] = {nullptr};
    ID3D11ShaderResourceView* nullSRV[1] = {nullptr};
    context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
    context->CSSetShaderResources(0, 1, nullSRV);
    context->CSSetShader(nullptr, nullptr, 0);
    catalogSRV->Release();
    
    // ===== PASS 2: Composite HDR to Screen =====
    // Update Pass 2 constant buffer
    if (SUCCEEDED(context->Map(g_StarfieldState.pass2CB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        StarfieldPass2Params* params = (StarfieldPass2Params*)mapped.pData;
        params->ScreenSizeX = (float)g_StarfieldState.width;
        params->ScreenSizeY = (float)g_StarfieldState.height;
        params->InvScreenSizeX = 1.0f / g_StarfieldState.width;
        params->InvScreenSizeY = 1.0f / g_StarfieldState.height;
        params->BloomThreshold = g_StarfieldState.bloomThreshold;
        params->BloomIntensity = g_StarfieldState.bloomIntensity;
        params->DepthThreshold = 0.5f;
        params->ExposureEV = g_StarfieldState.exposure;
        params->EnableTonemapping = 1;
        params->Pad1[0] = params->Pad1[1] = params->Pad1[2] = 0.0f;
        
        params->ExtinctionZenith = g_StarfieldState.extinctionZenith;
        params->ExtinctionHorizon = g_StarfieldState.extinctionHorizon;
        params->Pad2[0] = params->Pad2[1] = 0.0f;
        params->AtmosphereUpX = g_StarfieldState.atmosphereUp.x;
        params->AtmosphereUpY = g_StarfieldState.atmosphereUp.y;
        params->AtmosphereUpZ = g_StarfieldState.atmosphereUp.z;
        params->Pad3 = 0.0f; // Alignment padding, must be present but unused by original shader
        
        context->Unmap(g_StarfieldState.pass2CB, 0);
    }
    
    // Select rendering path based on bloom mode
    if (g_StarfieldState.useSoftBloom) {
        // Soft HDR 2-pass path
        ExecuteSoftBloomRender(context, currentRTV);
        
        // Cleanup and return early for soft bloom path
        currentRTV->Release();
        if (currentDSV) currentDSV->Release();
        device->Release();
        return;
    }
    
    // Setup output merger
    context->OMSetRenderTargets(1, &currentRTV, nullptr);
    context->OMSetDepthStencilState(g_StarfieldState.depthState, 0);
    context->OMSetBlendState(g_StarfieldState.blendState, nullptr, 0xFFFFFFFF);
    context->RSSetState(g_StarfieldState.rasterState);
    
    D3D11_VIEWPORT vp = {0, 0, (float)g_StarfieldState.width, (float)g_StarfieldState.height, 0, 1};
    context->RSSetViewports(1, &vp);
    
    // Bind shaders
    context->VSSetShader(g_StarfieldState.pass2VS, nullptr, 0);
    context->PSSetShader(g_StarfieldState.pass2PS, nullptr, 0);
    context->PSSetConstantBuffers(0, 1, &g_StarfieldState.pass2CB);
    context->PSSetSamplers(0, 1, &g_StarfieldState.linearSampler);
    
    ID3D11ShaderResourceView* srvs[1] = {g_StarfieldState.hdrSRV};
    context->PSSetShaderResources(0, 1, srvs);
    
    // Draw fullscreen triangle
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->IASetInputLayout(nullptr);
    context->IASetVertexBuffers(0, 0, nullptr, nullptr, nullptr);
    context->IASetIndexBuffer(nullptr, DXGI_FORMAT_UNKNOWN, 0);
    context->Draw(3, 0);
    
    // Cleanup bindings
    ID3D11ShaderResourceView* psNullSRV[1] = {nullptr};
    ID3D11RenderTargetView* nullRTV = nullptr;
    context->PSSetShaderResources(0, 1, psNullSRV);
    context->OMSetRenderTargets(1, &nullRTV, nullptr);
    
    currentRTV->Release();
    if (currentDSV) currentDSV->Release();
    device->Release();
}

static void ExecuteSoftBloomRender(ID3D11DeviceContext* context, ID3D11RenderTargetView* finalRTV)
{
    if (!context || !finalRTV) return;
    
    ID3D11Device* device = nullptr;
    context->GetDevice(&device);
    if (!device) return;
    
    // Validate resources
    if (!g_StarfieldState.bloomTexture || !g_StarfieldState.bloomTempTexture || 
        !g_StarfieldState.bloomHalfTexture || !g_StarfieldState.prefilterPS || 
        !g_StarfieldState.blurXPS || !g_StarfieldState.blurPS || 
        !g_StarfieldState.softCompositePS || !g_StarfieldState.upscalePS) {
        device->Release();
        return;
    }
    
    // Disable depth testing for all bloom passes
    context->OMSetDepthStencilState(g_StarfieldState.depthState, 0);
    
    // Quarter-res dimensions
    int bloomWidth = g_StarfieldState.width / 4;
    int bloomHeight = g_StarfieldState.height / 4;
    if (bloomWidth < 1) bloomWidth = 1;
    if (bloomHeight < 1) bloomHeight = 1;
    
    float clearColor[4] = {0, 0, 0, 0};
    
    // ========================================================================
    // PASS 1: Prefilter + Downsample (Full-res HDR -> bloomTempTexture)
    // ========================================================================
    D3D11_VIEWPORT quarterVP = {};
    quarterVP.Width = (float)bloomWidth;
    quarterVP.Height = (float)bloomHeight;
    quarterVP.MinDepth = 0.0f;
    quarterVP.MaxDepth = 1.0f;
    context->RSSetViewports(1, &quarterVP);
    
    context->OMSetRenderTargets(1, &g_StarfieldState.bloomTempRTV, nullptr);
    context->OMSetBlendState(nullptr, nullptr, 0xFFFFFFFF); // Disable blend
    context->ClearRenderTargetView(g_StarfieldState.bloomTempRTV, clearColor);
    
    // Update Prefilter CB
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(context->Map(g_StarfieldState.prefilterCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        float* data = (float*)mapped.pData;
        data[0] = (float)g_StarfieldState.width;      // SourceSizeX
        data[1] = (float)g_StarfieldState.height;     // SourceSizeY
        data[2] = 1.0f / g_StarfieldState.width;      // InvSourceSizeX
        data[3] = 1.0f / g_StarfieldState.height;     // InvSourceSizeY
        data[4] = g_StarfieldState.bloomThreshold;    // BloomThreshold
        data[5] = 0.0f; // BloomKnee disabled - using hard threshold to match Classic
        data[6] = (float)bloomWidth;                  // OutputSizeX
        data[7] = (float)bloomHeight;                 // OutputSizeY
        data[8] = 1.0f / bloomWidth;                  // InvOutputSizeX
        data[9] = 1.0f / bloomHeight;                 // InvOutputSizeY
        // Padding to fill 48 bytes (12 floats) - struct is 3 float4s
        context->Unmap(g_StarfieldState.prefilterCB, 0);
    }
    
    context->VSSetShader(g_StarfieldState.pass2VS, nullptr, 0);
    context->PSSetShader(g_StarfieldState.prefilterPS, nullptr, 0);
    context->PSSetConstantBuffers(0, 1, &g_StarfieldState.prefilterCB);
    context->PSSetSamplers(0, 1, &g_StarfieldState.linearSampler);
    ID3D11ShaderResourceView* prefilterSRV[1] = {g_StarfieldState.hdrSRV};
    context->PSSetShaderResources(0, 1, prefilterSRV);
    
    context->Draw(3, 0);
    
    // Unbind SRV
    ID3D11ShaderResourceView* nullSRV[2] = {nullptr, nullptr};
    context->PSSetShaderResources(0, 1, nullSRV);
    
    // ========================================================================
    // PASS 2: Horizontal Blur (bloomTemp -> bloomTexture)
    // ========================================================================
    context->OMSetRenderTargets(1, &g_StarfieldState.bloomRTV, nullptr);
    context->OMSetBlendState(nullptr, nullptr, 0xFFFFFFFF);
    context->ClearRenderTargetView(g_StarfieldState.bloomRTV, clearColor);
    
    // Update Blur CB (same struct for X and Y)
    if (SUCCEEDED(context->Map(g_StarfieldState.blurCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        float* data = (float*)mapped.pData;
        data[0] = 1.0f / bloomWidth;   // TexelSizeX
        data[1] = 1.0f / bloomHeight;  // TexelSizeY
        float t = g_StarfieldState.bloomIntensity / 2.0f; // 0-1 range
        data[2] = t * 0.20f; // 0.0 at slider 0, 0.10 at slider 2.0 (tight max)
        data[3] = 0.0f; // Pad
        context->Unmap(g_StarfieldState.blurCB, 0);
    }
    
    context->PSSetShader(g_StarfieldState.blurXPS, nullptr, 0); // Horizontal blur
    context->PSSetConstantBuffers(0, 1, &g_StarfieldState.blurCB);
    ID3D11ShaderResourceView* horizSRV[1] = {g_StarfieldState.bloomTempSRV};
    context->PSSetShaderResources(0, 1, horizSRV);
    
    context->Draw(3, 0);
    context->PSSetShaderResources(0, 1, nullSRV);
    
    // ========================================================================
    // PASS 3: Vertical Blur (bloomTexture -> bloomTempTexture)
    // Final bloom result ends up in bloomTempTexture
    // ========================================================================
    context->OMSetRenderTargets(1, &g_StarfieldState.bloomTempRTV, nullptr);
    context->OMSetBlendState(nullptr, nullptr, 0xFFFFFFFF);
    
    // Same CB values (TexelSize and Spread identical for symmetric blur)
    context->PSSetShader(g_StarfieldState.blurPS, nullptr, 0); // Vertical blur
    context->PSSetConstantBuffers(0, 1, &g_StarfieldState.blurCB);
    ID3D11ShaderResourceView* vertSRV[1] = {g_StarfieldState.bloomSRV};
    context->PSSetShaderResources(0, 1, vertSRV);
    
    context->Draw(3, 0);
    context->PSSetShaderResources(0, 1, nullSRV);
    
    // ========================================================================
    // PASS 3.5: Upscale to Half-Resolution (1/4 -> 1/2)
    // ========================================================================
    int halfWidth = g_StarfieldState.width / 2;
    int halfHeight = g_StarfieldState.height / 2;
    if (halfWidth < 1) halfWidth = 1;
    if (halfHeight < 1) halfHeight = 1;
    
    D3D11_VIEWPORT halfVP = {};
    halfVP.Width = (float)halfWidth;
    halfVP.Height = (float)halfHeight;
    halfVP.MinDepth = 0.0f;
    halfVP.MaxDepth = 1.0f;
    context->RSSetViewports(1, &halfVP);
    
    context->OMSetRenderTargets(1, &g_StarfieldState.bloomHalfRTV, nullptr);
    context->OMSetBlendState(nullptr, nullptr, 0xFFFFFFFF);
    context->ClearRenderTargetView(g_StarfieldState.bloomHalfRTV, clearColor);
    
    context->PSSetShader(g_StarfieldState.upscalePS, nullptr, 0);
    ID3D11ShaderResourceView* upscaleSRV[1] = {g_StarfieldState.bloomTempSRV};
    context->PSSetShaderResources(0, 1, upscaleSRV);
    
    context->Draw(3, 0);
    context->PSSetShaderResources(0, 1, nullSRV);
    
    // ========================================================================
    // PASS 4: Composite (Full-res stars + bloomHalf -> finalRTV)
    // ========================================================================
    D3D11_VIEWPORT fullVP = {};
    fullVP.Width = (float)g_StarfieldState.width;
    fullVP.Height = (float)g_StarfieldState.height;
    fullVP.MinDepth = 0.0f;
    fullVP.MaxDepth = 1.0f;
    context->RSSetViewports(1, &fullVP);
    
    context->OMSetRenderTargets(1, &finalRTV, nullptr);
    context->OMSetBlendState(g_StarfieldState.blendState, nullptr, 0xFFFFFFFF); // Restore blend for stars
    
    // Update Composite CB
    if (SUCCEEDED(context->Map(g_StarfieldState.compositeCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        SoftCompositeParams* params = (SoftCompositeParams*)mapped.pData;
        params->ScreenSizeX = (float)g_StarfieldState.width;
        params->ScreenSizeY = (float)g_StarfieldState.height;
        params->InvScreenSizeX = 1.0f / g_StarfieldState.width;
        params->InvScreenSizeY = 1.0f / g_StarfieldState.height;
        params->BloomIntensity = g_StarfieldState.bloomIntensity;
        params->ExposureEV = g_StarfieldState.exposure;
        params->EnableTonemapping = 1;
        params->Pad1 = 0.0f;
        params->ExtinctionZenith = g_StarfieldState.extinctionZenith;
        params->ExtinctionHorizon = g_StarfieldState.extinctionHorizon;
        params->Pad2[0] = params->Pad2[1] = 0.0f;
        params->AtmosphereUpX = g_StarfieldState.atmosphereUp.x;
        params->AtmosphereUpY = g_StarfieldState.atmosphereUp.y;
        params->AtmosphereUpZ = g_StarfieldState.atmosphereUp.z;
        params->Pad3 = 0.0f;
        context->Unmap(g_StarfieldState.compositeCB, 0);
    }
    
    context->VSSetShader(g_StarfieldState.pass2VS, nullptr, 0);
    context->PSSetShader(g_StarfieldState.softCompositePS, nullptr, 0);
    context->PSSetConstantBuffers(0, 1, &g_StarfieldState.compositeCB);
    context->PSSetSamplers(0, 1, &g_StarfieldState.linearSampler);
    
    // Composite reads full-res HDR (t0) and final bloom from bloomHalfSRV (t1, half-res)
    ID3D11ShaderResourceView* compositeSRVs[2] = {g_StarfieldState.hdrSRV, g_StarfieldState.bloomHalfSRV};
    context->PSSetShaderResources(0, 2, compositeSRVs);
    
    context->Draw(3, 0);
    
    // Cleanup
    context->PSSetShaderResources(0, 2, nullSRV);
    ID3D11RenderTargetView* nullRTV = nullptr;
    context->OMSetRenderTargets(1, &nullRTV, nullptr);
    
    device->Release();
}

static void UNITY_INTERFACE_API OnStarfieldRenderEvent(int eventId)
{
    if (!g_StarfieldState.device) return;
    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    ID3D11DeviceContext* context = nullptr;
    g_StarfieldState.device->GetImmediateContext(&context);
    if (!context) return;
    
    ExecuteStarfieldRender(context);
    
    context->Release();
    
    // Increment temporal frame index
    g_StarfieldState.frameIndex = (g_StarfieldState.frameIndex + 1) & 7;
}

extern "C" __declspec(dllexport)
void CR_StarfieldGenerateCatalog(int seed, int requestedCount)
{
    // Copy generation parameters to locals (brief lock)
    int heroCount;
    float clustering, minMagnitude, maxMagnitude, magnitudeBias, populationBias;
    float mainSequenceStrength, redGiantFrequency, colorSaturation;
    float galacticFlatness, galacticDiscFalloff, bandCenterBoost, bandCoreSharpness;
    float bulgeIntensity, bulgeWidth, bulgeHeight, bulgeSoftness, bulgeNoiseScale, bulgeNoiseStrength;
    float3 planeNormal, bulgeCenter;
    ID3D11Device* device;
    
    {
        std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
        if (!g_StarfieldState.device) {
            return;
        }
        device = g_StarfieldState.device;
        device->AddRef();
        
        heroCount = g_StarfieldState.heroCount;
        clustering = g_StarfieldState.clustering;
        minMagnitude = g_StarfieldState.minMagnitude;
        maxMagnitude = g_StarfieldState.maxMagnitude;
        magnitudeBias = g_StarfieldState.magnitudeBias;
        populationBias = g_StarfieldState.populationBias;
        mainSequenceStrength = g_StarfieldState.mainSequenceStrength;
        redGiantFrequency = g_StarfieldState.redGiantFrequency;
        colorSaturation = g_StarfieldState.colorSaturation;
        planeNormal = g_StarfieldState.galacticPlaneNormal;
        bulgeCenter = g_StarfieldState.bulgeCenterDirection;
        galacticFlatness = g_StarfieldState.galacticFlatness;
        galacticDiscFalloff = g_StarfieldState.galacticDiscFalloff;
        bandCenterBoost = g_StarfieldState.bandCenterBoost;
        bandCoreSharpness = g_StarfieldState.bandCoreSharpness;
        bulgeIntensity = g_StarfieldState.bulgeIntensity;
        bulgeWidth = g_StarfieldState.bulgeWidth;
        bulgeHeight = g_StarfieldState.bulgeHeight;
        bulgeSoftness = g_StarfieldState.bulgeSoftness;
        bulgeNoiseScale = g_StarfieldState.bulgeNoiseScale;
        bulgeNoiseStrength = g_StarfieldState.bulgeNoiseStrength;
    }
    
    // Use seed to offset hash calculations - apply to all axes with significant offset
    float seedOffsetX = (float)((seed * 12345) % 100000) * 0.01f;
    float seedOffsetY = (float)((seed * 54321) % 100000) * 0.01f;
    float seedOffsetZ = (float)((seed * 98765) % 100000) * 0.01f;
    
    std::vector<StarData> tempCatalog;
    tempCatalog.reserve(requestedCount * 2); // Rough estimate
    
    LogToFile("[Starfield] Generating catalog: popBias=%.2f, mainSeq=%.2f, colorSat=%.2f, seed=%d, count=%d",
        populationBias, mainSequenceStrength, colorSaturation, seed, requestedCount);
    
    // Galactic structure params
    // (planeNormal and bulgeCenter captured above)
    
    // Clamp hero count to valid range
    if (heroCount < 16) heroCount = 16;
    if (heroCount > 1024) heroCount = 1024;
    if (heroCount >= requestedCount) heroCount = requestedCount / 4; // Reserve at least 75% for regular stars
    
    LogToFile("[Starfield] Generating catalog: seed=%d, total=%d, heroes=%d", 
        seed, requestedCount, heroCount);
    
    // SPHERICAL SAMPLING: Generate random directions on sphere surface
    // Use seed to initialize random sequence
    unsigned int rngState = (unsigned int)seed * 0x9E3779B9u;
    auto randFloat = [&]() -> float {
        // PCG random number generator
        rngState = rngState * 747796405u + 2891336453u;
        unsigned int word = ((rngState >> ((rngState >> 28u) + 4u)) ^ rngState) * 277803737u;
        word = (word >> 22u) ^ word;
        return (float)word / 4294967295.0f; // [0, 1)
    };
    
    auto randFloatRange = [&](float min, float max) -> float {
        return min + randFloat() * (max - min);
    };
    
    // Generate uniform random point on sphere
    auto randomDirection = [&]() -> float3 {
        // Marsaglia method for uniform sphere distribution
        float u, v, s;
        do {
            u = randFloatRange(-1.0f, 1.0f);
            v = randFloatRange(-1.0f, 1.0f);
            s = u*u + v*v;
        } while (s >= 1.0f || s == 0.0f);
        
        float3 dir;
        dir.x = 2.0f * u * sqrtf(1.0f - s);
        dir.y = 2.0f * v * sqrtf(1.0f - s);
        dir.z = 1.0f - 2.0f * s;
        return dir;
    };
    
    // ============================================
    // PHASE 1: Generate Hero Stars (indices 0 to heroCount-1)
    // ============================================
    int heroesGenerated = 0;
    int heroAttempts = 0;
    const int maxHeroAttempts = heroCount * 100;
    int32_t nextProceduralID = 1;  // Sequential IDs for procedural stars
    
    while (heroesGenerated < heroCount && heroAttempts < maxHeroAttempts) {
        heroAttempts++;
        
        // Generate random direction
        float3 dir = randomDirection();
        
        // Heroes respect galactic density (user request)
        float galacticDensity = GetGalacticDensityCPU(dir,
            galacticFlatness,
            galacticDiscFalloff,
            bandCenterBoost,
            bandCoreSharpness,
            planeNormal,
            bulgeIntensity,
            bulgeCenter,
            bulgeWidth,
            bulgeHeight,
            bulgeSoftness,
            bulgeNoiseScale,
            bulgeNoiseStrength);
        
        if (randFloat() > galacticDensity) continue;
        
        // Generate hash for this position
        float3 hashInput(dir.x * 1000.0f + (float)seed * 0.01f, dir.y * 1000.0f, dir.z * 1000.0f);
        float3 h = Hash33(hashInput);
        
        // Hero magnitude: brightest range exclusively for heroes
        // Range: minMagnitude to minMagnitude + 1.5 (e.g., -2.0 to -0.5)
        float heroMagRange = 1.5f;
        float heroMag = minMagnitude + h.y * heroMagRange;
        float heroFlux = powf(10.0f, -0.4f * heroMag);
        
        // Determine if this hero is a red giant
        // Inverted logic: Frequency 0=none, 1=many (was Rarity 0=many, 1=none)
        bool isRedGiant = (h.x < (1.0f - redGiantFrequency));
        
        float3 heroColor;
        float heroTemp;
        // colorSaturation is already captured from state at function start
        LuminosityClass forcedLumClass = LUM_COUNT;  // Default (no override)
        
        if (isRedGiant) {
            // Red giant color - orange-red
            float3 baseColor = float3(1.0f, 0.5f, 0.3f);
            heroTemp = 3500.0f;
            heroColor = ApplySaturation(baseColor, colorSaturation);
        } else {
            // Regular star - use population bias and main sequence strength
            // For heroes, we want brighter stars to tend toward blue (higher temp)
            float brightnessNormalized = (heroMag - minMagnitude) / heroMagRange; // 0=bright, 1=dim
            float randomComponent = h.z;
            float sequenceComponent = (1.0f - brightnessNormalized);
            float tempHash = randomComponent * (1.0f - mainSequenceStrength) + sequenceComponent * mainSequenceStrength;
            tempHash = tempHash + populationBias * 0.3f;
            tempHash = fmaxf(0.0f, fminf(1.0f, tempHash));
            
            // ENFORCE REALISTIC SPECTRAL TYPE FOR MAGNITUDE (Main Sequence Strength)
            // Brighter stars must be hotter (O, B, A) - dimmer stars can be cooler (G, K, M)
            // At mainSequenceStrength=1.0: strict correlation
            // At mainSequenceStrength=0.0: any spectral type at any magnitude
            
            // Calculate max realistic spectral type for this magnitude
            // Mag -4: Type 0 (O), Mag -2: Type 1 (B), Mag 0: Type 2 (A), Mag 2: Type 3 (F)
            // Mag 4: Type 4 (G), Mag 6: Type 5 (K), Mag 8+: Type 6 (M)
            float maxRealisticSpectral = (heroMag + 4.0f) / 2.0f;
            maxRealisticSpectral = fmaxf(0.0f, fminf(6.0f, maxRealisticSpectral));
            
            // ENFORCE REALISTIC SPECTRAL TYPE FOR MAGNITUDE (Main Sequence Strength)
            // Bright stars must be hot (O, B, A) - directly set temperature based on magnitude
            // At mainSequenceStrength=1.0: strict correlation
            // At mainSequenceStrength=0.0: use randomized tempHash (wild west)
            
            // Base temperature from hash
            float baseTemp;
            if (tempHash < 0.10f) { baseTemp = 1500.0f; }
            else if (tempHash < 0.25f) { baseTemp = 3500.0f; }
            else if (tempHash < 0.45f) { baseTemp = 4500.0f; }
            else if (tempHash < 0.55f) { baseTemp = 5778.0f; }
            else if (tempHash < 0.75f) { baseTemp = 7200.0f; }
            else if (tempHash < 0.90f) { baseTemp = 9500.0f; }
            else { baseTemp = 20000.0f; }
            
            // Target temperature based on magnitude (for main sequence stars)
            // Mag -2 -> B-type (~20000K), Mag 0 -> A-type (~9500K), Mag 2 -> F-type (~7200K), etc.
            float targetTemp;
            if (heroMag < -2.0f) { targetTemp = 25000.0f; }      // O-type
            else if (heroMag < 0.0f) { targetTemp = 15000.0f; }  // B-type
            else if (heroMag < 1.5f) { targetTemp = 8500.0f; }   // A-type
            else if (heroMag < 3.0f) { targetTemp = 6500.0f; }   // F-type
            else if (heroMag < 5.0f) { targetTemp = 5500.0f; }   // G-type
            else if (heroMag < 7.0f) { targetTemp = 4000.0f; }   // K-type
            else { targetTemp = 3000.0f; }                       // M-type
            
            // Blend based on mainSequenceStrength
            heroTemp = baseTemp * (1.0f - mainSequenceStrength) + targetTemp * mainSequenceStrength;
            
            // If we're forcing a hot star (targetTemp > 8000K) with high mainSequenceStrength, 
            // we might need supergiant luminosity
            if (mainSequenceStrength > 0.9f && heroMag < -1.0f) {
                forcedLumClass = LUM_SUPERGIANT;
            } else if (mainSequenceStrength > 0.7f && heroMag < 1.0f && heroTemp < 6000.0f) {
                // Would be impossible dwarf - force at least giant
                forcedLumClass = LUM_GIANT;
            }
            
            
            // Apply variation and get blackbody color with saturation
            heroTemp = heroTemp * (0.9f + h.x * 0.2f);
            heroTemp = fmaxf(1000.0f, fminf(40000.0f, heroTemp));
            
            float3 blackbody = BlackbodyRGB(heroTemp);
            heroColor = ApplySaturation(blackbody, colorSaturation);
        }
        
        // Calculate brightness normalized for hero stars
        float heroBrightnessNormalized = (heroMag - minMagnitude) / heroMagRange;
        
        // Calculate spectral type and distance
        int32_t spectralType = TemperatureToSpectralType(heroTemp);
        float distancePc = CalculateDistancePc(heroMag, spectralType, h.x, heroBrightnessNormalized, forcedLumClass, heroesGenerated);
        
        // Add hero to catalog (at the end, we'll reverse to put heroes first)
        StarData hero;
        hero.HipparcosID = nextProceduralID++;  // Sequential procedural ID
        hero.DistancePc = distancePc;
        hero.SpectralType = spectralType;
        hero.Flags = StarData::FLAG_IS_HERO;  // Mark as hero (can be named)
        hero.DirectionX = dir.x;
        hero.DirectionY = dir.y;
        hero.DirectionZ = dir.z;
        hero.Magnitude = heroMag;
        hero.ColorR = heroColor.x;
        hero.ColorG = heroColor.y;
        hero.ColorB = heroColor.z;
        hero.Temperature = heroTemp;
        
        tempCatalog.push_back(hero);
        heroesGenerated++;
    }
    
    // ============================================
    // PHASE 2: Generate Regular Stars (fill remaining slots)
    // ============================================
    int regularGenerated = 0;
    int regularAttempts = 0;
    int regularCount = requestedCount - heroesGenerated;
    const int maxRegularAttempts = requestedCount * 100;
    
    while (regularGenerated < regularCount && regularAttempts < maxRegularAttempts) {
        regularAttempts++;
        
        // Generate random direction
        float3 dir = randomDirection();
        
        // Calculate galactic density
        float galacticDensity = GetGalacticDensityCPU(dir,
            galacticFlatness,
            galacticDiscFalloff,
            bandCenterBoost,
            bandCoreSharpness,
            planeNormal,
            bulgeIntensity,
            bulgeCenter,
            bulgeWidth,
            bulgeHeight,
            bulgeSoftness,
            bulgeNoiseScale,
            bulgeNoiseStrength);
        
        if (randFloat() > galacticDensity) continue;
        
        // Generate clustering noise with FBM for hierarchical filaments
        // Creates big groups containing smaller sub-groups for natural galactic structure
        float3 clusterPos(dir.x * 100.0f, dir.y * 100.0f, dir.z * 100.0f);
        float3 megaCell(floorf(clusterPos.x * 0.1f), floorf(clusterPos.y * 0.1f), floorf(clusterPos.z * 0.1f));
        
        // Base mega-cell hash for coarse clustering
        float clusterNoise = Hash13(megaCell);
        
        // FBM detail for hierarchical clustering (filaments within clouds)
        // Uses existing Clustering slider to control fractal influence
        float3 fbmPos = float3(megaCell.x * 0.5f, megaCell.y * 0.5f, megaCell.z * 0.5f);
        float fbmDetail = FBM(fbmPos, 3, 2.0f, 0.5f) * clustering;
        
        // Combine: base clustering determines location, FBM adds fractal substructure
        float clusterProb = 0.2f + (clusterNoise + fbmDetail * 0.3f) * clustering * 0.6f;
        
        if (randFloat() > clusterProb) continue;
        
        // Generate star properties
        float3 hashInput(dir.x * 1000.0f + (float)seed * 0.01f, dir.y * 1000.0f, dir.z * 1000.0f);
        float3 h = Hash33(hashInput);
        
        // Regular stars: start dimmer than heroes, with ~0.33 magnitude overlap
        // Hero max is minMagnitude + 1.5, so regular min is minMagnitude + 1.5 - 0.33
        float regularMinMag = minMagnitude + 1.17f; // 1.5 - 0.33 overlap
        
        // Generate magnitude in regular range using power curve
        float normalizedBrightness = powf(h.y, magnitudeBias);
        float magnitude = regularMinMag + (maxMagnitude - regularMinMag) * normalizedBrightness;
        
        // Calculate flux from magnitude
        float flux = powf(10.0f, -0.4f * magnitude);
        
        // Determine color based on magnitude and population bias
        // For regular stars, brighter stars tend toward blue
        float brightnessNormalized = (magnitude - regularMinMag) / (maxMagnitude - regularMinMag);
        float randomComponent = h.z;
        float sequenceComponent = (1.0f - brightnessNormalized);
        float tempHash = randomComponent * (1.0f - mainSequenceStrength) + sequenceComponent * mainSequenceStrength;
        // Apply population bias (shift toward red=-1 or blue=+1) and clamp to [0,1]
        tempHash = tempHash + populationBias * 0.3f;
        tempHash = fmaxf(0.0f, fminf(1.0f, tempHash));
        
        float3 color;
        float temp;
        // colorSaturation is already captured from state at function start
        LuminosityClass forcedLumClass = LUM_COUNT;  // Default (no override)
        
        // Red giants override (rare bright red stars)
        // Inverted logic: Frequency 0=none, 1=many (was Rarity 0=many, 1=none)
        if (h.x < (1.0f - redGiantFrequency) && normalizedBrightness < 0.3f) {
            float3 baseColor = float3(1.0f, 0.5f, 0.3f);
            temp = 3500.0f;
            color = ApplySaturation(baseColor, colorSaturation);
        } else {
            // ENFORCE REALISTIC SPECTRAL TYPE FOR MAGNITUDE (Main Sequence Strength)
            // Brighter stars must be hotter (O, B, A) - dimmer stars can be cooler (G, K, M)
            
            // Calculate max realistic spectral type for this magnitude
            float maxRealisticSpectral = (magnitude + 4.0f) / 2.0f;
            maxRealisticSpectral = fmaxf(0.0f, fminf(6.0f, maxRealisticSpectral));
            
            // Clamp tempHash to realistic range based on mainSequenceStrength
            float maxAllowedHash = maxRealisticSpectral / 6.0f;
            float clampedTempHash = fminf(tempHash, maxAllowedHash);
            bool wasClamped = tempHash > maxAllowedHash;
            tempHash = tempHash * (1.0f - mainSequenceStrength) + clampedTempHash * mainSequenceStrength;
            tempHash = fmaxf(0.0f, fminf(1.0f, tempHash));
            
            // If clamped and mainSequenceStrength is high, force giant/supergiant luminosity
            if (wasClamped && mainSequenceStrength > 0.7f) {
                forcedLumClass = (mainSequenceStrength > 0.9f && magnitude < 0.0f) ? LUM_SUPERGIANT : LUM_GIANT;
            }
            
            // Calculate temperature - symmetric distribution for PopulationBias effect
            // Young/blue (high bias) vs Old/red (low bias) - extremes at +/- 1.0
            if (tempHash < 0.10f) { temp = 1500.0f; }       // 10% - Deep red (M9 dwarf)
            else if (tempHash < 0.25f) { temp = 3500.0f; }  // 15% - Red-orange (M-type)
            else if (tempHash < 0.45f) { temp = 4500.0f; }  // 20% - Orange (K-type)
            else if (tempHash < 0.55f) { temp = 5778.0f; }  // 10% - Yellow (G-type, Sun)
            else if (tempHash < 0.75f) { temp = 7200.0f; }  // 20% - White (F-type)
            else if (tempHash < 0.90f) { temp = 9500.0f; }  // 15% - Blue-white (A-type)
            else { temp = 20000.0f; }                       // 10% - Deep blue (B/O-type)
            
            // Apply variation and get blackbody color with saturation
            temp = temp * (0.9f + h.x * 0.2f);
            temp = fmaxf(1000.0f, fminf(40000.0f, temp));
            float3 blackbody = BlackbodyRGB(temp);
            color = ApplySaturation(blackbody, colorSaturation);
        }
        
        // Regular stars acceptance based on magnitude bias (brighter = more likely)
        float existenceProb = powf((magnitude - regularMinMag) / (maxMagnitude - regularMinMag), magnitudeBias);
        if (h.x > existenceProb) continue;
        
        // Calculate spectral type and distance
        int32_t starSpectralType = TemperatureToSpectralType(temp);
        float starDistancePc = CalculateDistancePc(magnitude, starSpectralType, h.x, brightnessNormalized, forcedLumClass);
        
        // Add regular star to catalog
        StarData star;
        star.HipparcosID = nextProceduralID++;  // Sequential procedural ID
        star.DistancePc = starDistancePc;
        star.SpectralType = starSpectralType;
        star.Flags = 0;  // Not a hero
        star.DirectionX = dir.x;
        star.DirectionY = dir.y;
        star.DirectionZ = dir.z;
        star.Magnitude = magnitude;
        star.ColorR = color.x;
        star.ColorG = color.y;
        star.ColorB = color.z;
        star.Temperature = temp;
        
        tempCatalog.push_back(star);
        regularGenerated++;
    }
    
    int totalGenerated = heroesGenerated + regularGenerated;
    int totalAttempts = heroAttempts + regularAttempts;
    
    // Heroes are already at the front (generated first), but sort heroes by magnitude for consistency
    // Sort only the hero portion (indices 0 to heroesGenerated-1)
    if (heroesGenerated > 1) {
        std::sort(tempCatalog.begin(), tempCatalog.begin() + heroesGenerated,
            [](const StarData& a, const StarData& b) {
                return a.Magnitude < b.Magnitude;
            });
    }
    
    // Sort regular stars by magnitude (indices heroesGenerated to end)
    if (regularGenerated > 1) {
        std::sort(tempCatalog.begin() + heroesGenerated, tempCatalog.end(),
            [](const StarData& a, const StarData& b) {
                return a.Magnitude < b.Magnitude;
            });
    }
    
    // Trim to requested count
    int finalCount = min((int)tempCatalog.size(), requestedCount);
    if (finalCount == 0) {
        LogToFile("[Starfield] Warning: Generated 0 stars. Check galactic density parameters.");
        device->Release();
        return;
    }
    
    // Phase 3: GPU upload under lock
    {
        std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
        
        // Check if device is still valid (might have been shut down during generation)
        if (!g_StarfieldState.device) {
            device->Release();
            return;
        }
        
        // Ensure buffer capacity
        if (finalCount > g_StarfieldState.catalogCapacity || g_StarfieldState.starCatalogBuffer == nullptr) {
            if (g_StarfieldState.starCatalogBuffer) {
                g_StarfieldState.starCatalogBuffer->Release();
                g_StarfieldState.starCatalogBuffer = nullptr;
            }
            
            D3D11_BUFFER_DESC desc = {};
            desc.ByteWidth = sizeof(StarData) * finalCount;
            desc.Usage = D3D11_USAGE_DYNAMIC;
            desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
            desc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
            desc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
            desc.StructureByteStride = sizeof(StarData);
            
            HRESULT hr = device->CreateBuffer(&desc, nullptr, &g_StarfieldState.starCatalogBuffer);
            if (FAILED(hr)) {
                LogToFile("[Starfield] Failed to create catalog buffer (0x%08X)", hr);
                device->Release();
                return;
            }
            
            g_StarfieldState.catalogCapacity = finalCount;
        }
        
        // Upload data
        D3D11_MAPPED_SUBRESOURCE mapped;
        ID3D11DeviceContext* context = nullptr;
        device->GetImmediateContext(&context);
        
        if (context && SUCCEEDED(context->Map(g_StarfieldState.starCatalogBuffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
            memcpy(mapped.pData, tempCatalog.data(), sizeof(StarData) * finalCount);
            context->Unmap(g_StarfieldState.starCatalogBuffer, 0);
            context->Release();
            
            g_StarfieldState.catalogSize = finalCount;
            g_StarfieldState.catalogHeroCount = heroesGenerated;  // Store actual hero count
            g_StarfieldState.catalogSeed = seed;
            
            // Store CPU-side copy for save operations
            g_StarfieldState.catalogDataCPU.resize(finalCount);
            memcpy(g_StarfieldState.catalogDataCPU.data(), tempCatalog.data(), sizeof(StarData) * finalCount);
            
            LogToFile("[Starfield] Catalog generated: %d stars (%d heroes, %d regular, %d attempts)", finalCount, heroesGenerated, regularGenerated, totalAttempts);
        } else {
            LogToFile("[Starfield] Failed to map catalog buffer");
            if (context) context->Release();
        }
    }
    
    device->Release();
}

// Starfield Exports
extern "C" __declspec(dllexport)
void CR_StarfieldSetCameraMatrices(ID3D11Texture2D* deviceSourceTexture, int width, int height,
                                   float verticalFOV, float aspectRatio, float3 cameraRight, float3 cameraUp, float3 cameraForward,
                                   float extinctionZenith, float extinctionHorizon, float3 atmosphereUp)
{    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    g_StarfieldState.width = width;
    g_StarfieldState.height = height;
    g_StarfieldState.verticalFOV = verticalFOV;
    g_StarfieldState.aspectRatio = aspectRatio;
    g_StarfieldState.cameraRight = cameraRight;
    g_StarfieldState.cameraUp = cameraUp;
    g_StarfieldState.cameraForward = cameraForward;
    
    // Store atmospheric extinction parameters (per-frame update)
    g_StarfieldState.extinctionZenith = extinctionZenith;
    g_StarfieldState.extinctionHorizon = extinctionHorizon;
    g_StarfieldState.atmosphereUp = atmosphereUp;
    
    // Acquire device from any valid texture (we use whiteTexture from C#)
    if (deviceSourceTexture && !g_StarfieldState.device) {
        ID3D11Device* device = nullptr;
        deviceSourceTexture->GetDevice(&device);
        if (device) {
            g_StarfieldState.device = device;
            g_StarfieldState.device->AddRef();
            EnsureStarfieldResources(device, width, height);
            
            // If we just acquired device and catalog is empty, signal that we need a reload
            if (g_StarfieldState.catalogSize == 0) {
                g_StarfieldState.catalogNeedsReload = true;
                LogToFile("[Starfield] Device acquired with empty catalog, flagging for reload");
            }
        }
    }
}

extern "C" __declspec(dllexport)
void CR_StarfieldSetSettings(const StarfieldSettingsNative* settings)
{
    if (!settings) return;
    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    g_StarfieldState.exposure = settings->Exposure;
    g_StarfieldState.blurPixels = settings->BlurPixels;

    g_StarfieldState.minMagnitude = settings->MinMagnitude;
    g_StarfieldState.maxMagnitude = settings->MaxMagnitude;
    g_StarfieldState.magnitudeBias = settings->MagnitudeBias;
    g_StarfieldState.heroCount = settings->HeroCount;
    g_StarfieldState.clustering = settings->Clustering;
    g_StarfieldState.populationBias = settings->PopulationBias;
    g_StarfieldState.mainSequenceStrength = settings->MainSequenceStrength;
    g_StarfieldState.redGiantFrequency = settings->RedGiantFrequency;
    g_StarfieldState.galacticFlatness = settings->GalacticFlatness;
    g_StarfieldState.galacticDiscFalloff = settings->GalacticDiscFalloff;
    g_StarfieldState.bandCenterBoost = settings->BandCenterBoost;
    g_StarfieldState.bandCoreSharpness = settings->BandCoreSharpness;
    g_StarfieldState.bulgeIntensity = settings->BulgeIntensity;
    g_StarfieldState.bulgeWidth = settings->BulgeWidth;
    g_StarfieldState.bulgeHeight = settings->BulgeHeight;
    g_StarfieldState.bulgeSoftness = settings->BulgeSoftness;
    g_StarfieldState.bulgeNoiseScale = settings->BulgeNoiseScale;
    g_StarfieldState.bulgeNoiseStrength = settings->BulgeNoiseStrength;
    g_StarfieldState.bloomThreshold = settings->BloomThreshold;
    g_StarfieldState.bloomIntensity = settings->BloomIntensity;
    g_StarfieldState.colorSaturation = settings->ColorSaturation;
    g_StarfieldState.rotationX = settings->RotationX;
    g_StarfieldState.psfEnhancement = settings->PsfEnhancement;
    g_StarfieldState.useSoftBloom = (settings->UseSoftBloom != 0);
    g_StarfieldState.rotationY = settings->RotationY;
    g_StarfieldState.rotationZ = settings->RotationZ;
}

extern "C" __declspec(dllexport)
UnityRenderingEvent CR_GetStarfieldRenderEventFunc()
{
    return OnStarfieldRenderEvent;
}

extern "C" __declspec(dllexport)
void CR_StarfieldShutdown()
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    if (g_StarfieldState.hdrSRV) { g_StarfieldState.hdrSRV->Release(); g_StarfieldState.hdrSRV = nullptr; }
    if (g_StarfieldState.hdrUAV) { g_StarfieldState.hdrUAV->Release(); g_StarfieldState.hdrUAV = nullptr; }
    if (g_StarfieldState.hdrTexture) { g_StarfieldState.hdrTexture->Release(); g_StarfieldState.hdrTexture = nullptr; }
    if (g_StarfieldState.pass1CS) { g_StarfieldState.pass1CS->Release(); g_StarfieldState.pass1CS = nullptr; }
    if (g_StarfieldState.pass2VS) { g_StarfieldState.pass2VS->Release(); g_StarfieldState.pass2VS = nullptr; }
    if (g_StarfieldState.pass2PS) { g_StarfieldState.pass2PS->Release(); g_StarfieldState.pass2PS = nullptr; }
    if (g_StarfieldState.linearSampler) { g_StarfieldState.linearSampler->Release(); g_StarfieldState.linearSampler = nullptr; }
    if (g_StarfieldState.depthState) { g_StarfieldState.depthState->Release(); g_StarfieldState.depthState = nullptr; }
    if (g_StarfieldState.blendState) { g_StarfieldState.blendState->Release(); g_StarfieldState.blendState = nullptr; }
    if (g_StarfieldState.rasterState) { g_StarfieldState.rasterState->Release(); g_StarfieldState.rasterState = nullptr; }
    if (g_StarfieldState.pass1CB) { g_StarfieldState.pass1CB->Release(); g_StarfieldState.pass1CB = nullptr; }
        if (g_StarfieldState.starCatalogBuffer) { 
            g_StarfieldState.starCatalogBuffer->Release(); 
            g_StarfieldState.starCatalogBuffer = nullptr; 
            g_StarfieldState.catalogSize = 0;
            g_StarfieldState.catalogCapacity = 0;
        }
        g_StarfieldState.catalogDataCPU.clear();
    if (g_StarfieldState.pass2CB) { g_StarfieldState.pass2CB->Release(); g_StarfieldState.pass2CB = nullptr; }

    if (g_StarfieldState.prefilterCB) { g_StarfieldState.prefilterCB->Release(); g_StarfieldState.prefilterCB = nullptr; }
    if (g_StarfieldState.blurCB) { g_StarfieldState.blurCB->Release(); g_StarfieldState.blurCB = nullptr; }
    if (g_StarfieldState.compositeCB) { g_StarfieldState.compositeCB->Release(); g_StarfieldState.compositeCB = nullptr; }

        // Soft HDR bloom resources
    if (g_StarfieldState.bloomSRV) { g_StarfieldState.bloomSRV->Release(); g_StarfieldState.bloomSRV = nullptr; }
    if (g_StarfieldState.bloomRTV) { g_StarfieldState.bloomRTV->Release(); g_StarfieldState.bloomRTV = nullptr; }
    if (g_StarfieldState.bloomTexture) { g_StarfieldState.bloomTexture->Release(); g_StarfieldState.bloomTexture = nullptr; }
    if (g_StarfieldState.prefilterPS) { g_StarfieldState.prefilterPS->Release(); g_StarfieldState.prefilterPS = nullptr; }
    if (g_StarfieldState.blurXPS) { g_StarfieldState.blurXPS->Release(); g_StarfieldState.blurXPS = nullptr; }
    if (g_StarfieldState.blurPS) { g_StarfieldState.blurPS->Release(); g_StarfieldState.blurPS = nullptr; }
    if (g_StarfieldState.softCompositePS) { g_StarfieldState.softCompositePS->Release(); g_StarfieldState.softCompositePS = nullptr; }
    if (g_StarfieldState.bloomTempSRV) { g_StarfieldState.bloomTempSRV->Release(); g_StarfieldState.bloomTempSRV = nullptr; }
    if (g_StarfieldState.bloomTempRTV) { g_StarfieldState.bloomTempRTV->Release(); g_StarfieldState.bloomTempRTV = nullptr; }
    if (g_StarfieldState.bloomTempTexture) { g_StarfieldState.bloomTempTexture->Release(); g_StarfieldState.bloomTempTexture = nullptr; }
    if (g_StarfieldState.bloomHalfSRV) { g_StarfieldState.bloomHalfSRV->Release(); g_StarfieldState.bloomHalfSRV = nullptr; }
    if (g_StarfieldState.bloomHalfRTV) { g_StarfieldState.bloomHalfRTV->Release(); g_StarfieldState.bloomHalfRTV = nullptr; }
    if (g_StarfieldState.bloomHalfTexture) { g_StarfieldState.bloomHalfTexture->Release(); g_StarfieldState.bloomHalfTexture = nullptr; }
    if (g_StarfieldState.upscalePS) { g_StarfieldState.upscalePS->Release(); g_StarfieldState.upscalePS = nullptr; }
    
    if (g_StarfieldState.device) { g_StarfieldState.device->Release(); g_StarfieldState.device = nullptr; }
    
    g_StarfieldState.cachedHDRFormat = DXGI_FORMAT_UNKNOWN;
    g_StarfieldState.initialized = false;
}

extern "C" __declspec(dllexport)
unsigned char CR_StarfieldIsDeviceReady()
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    return (g_StarfieldState.device != nullptr && g_StarfieldState.initialized) ? 1 : 0;
}

extern "C" __declspec(dllexport)
unsigned char CR_StarfieldCatalogNeedsReload()
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    if (g_StarfieldState.catalogNeedsReload) {
        g_StarfieldState.catalogNeedsReload = false;  // Reset after reading
        return 1;
    }
    return 0;
}

extern "C" __declspec(dllexport)
void CR_StarfieldInvalidateResources()
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    // Release HDR texture resources (they'll be recreated on next render)
    if (g_StarfieldState.hdrSRV) { g_StarfieldState.hdrSRV->Release(); g_StarfieldState.hdrSRV = nullptr; }
    if (g_StarfieldState.hdrUAV) { g_StarfieldState.hdrUAV->Release(); g_StarfieldState.hdrUAV = nullptr; }
    if (g_StarfieldState.hdrTexture) { g_StarfieldState.hdrTexture->Release(); g_StarfieldState.hdrTexture = nullptr; }
    
    // Reset initialized flag so resources get recreated
    g_StarfieldState.initialized = false;
    
    LogToFile("[Starfield] Resources invalidated for recreation");
}

// ============================================================================
// CATALOG SAVE/LOAD EXPORTS
// ============================================================================

extern "C" __declspec(dllexport)
int CR_StarfieldGetCatalogData(StarData* outBuffer, int maxCount)
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    if (!g_StarfieldState.starCatalogBuffer || g_StarfieldState.catalogSize == 0) {
        return 0;
    }
    
    int countToCopy = (maxCount < g_StarfieldState.catalogSize) ? maxCount : g_StarfieldState.catalogSize;
    
    // Copy from CPU-side cache (GPU buffer is DYNAMIC with WRITE-only access, cannot be read)
    memcpy(outBuffer, g_StarfieldState.catalogDataCPU.data(), sizeof(StarData) * countToCopy);
    return countToCopy;
}

extern "C" __declspec(dllexport)
void CR_StarfieldLoadCatalog(const StarData* buffer, int count, int heroCount)
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    if (!g_StarfieldState.device || count <= 0 || !buffer) {
        // Silent fail - device not ready yet, will retry via CatalogNeedsReload flag
        return;
    }
    
    // Clamp hero count
    if (heroCount < 0) heroCount = 0;
    if (heroCount > count) heroCount = count;
    
    // Ensure buffer capacity
    if (count > g_StarfieldState.catalogCapacity || g_StarfieldState.starCatalogBuffer == nullptr) {
        if (g_StarfieldState.starCatalogBuffer) {
            g_StarfieldState.starCatalogBuffer->Release();
            g_StarfieldState.starCatalogBuffer = nullptr;
        }
        
        D3D11_BUFFER_DESC desc = {};
        desc.ByteWidth = sizeof(StarData) * count;
        desc.Usage = D3D11_USAGE_DYNAMIC;
        desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        desc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        desc.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
        desc.StructureByteStride = sizeof(StarData);
        
        HRESULT hr = g_StarfieldState.device->CreateBuffer(&desc, nullptr, &g_StarfieldState.starCatalogBuffer);
        if (FAILED(hr)) {
            LogToFile("[Starfield] Failed to create catalog buffer for loading");
            return;
        }
        
        g_StarfieldState.catalogCapacity = count;
    }
    
    // Upload data
    ID3D11DeviceContext* context = nullptr;
    g_StarfieldState.device->GetImmediateContext(&context);
    
    if (context) {
        D3D11_MAPPED_SUBRESOURCE mapped;
        if (SUCCEEDED(context->Map(g_StarfieldState.starCatalogBuffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
            memcpy(mapped.pData, buffer, sizeof(StarData) * count);
            context->Unmap(g_StarfieldState.starCatalogBuffer, 0);
            
            g_StarfieldState.catalogSize = count;
            g_StarfieldState.catalogHeroCount = heroCount;
            
            // Store CPU-side copy for save operations
            g_StarfieldState.catalogDataCPU.resize(count);
            memcpy(g_StarfieldState.catalogDataCPU.data(), buffer, sizeof(StarData) * count);
            
            LogToFile("[Starfield] Loaded catalog: %d stars, %d heroes", count, heroCount);
        }
        context->Release();
    }
}

extern "C" __declspec(dllexport)
int CR_StarfieldGetCatalogSize()
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    return g_StarfieldState.catalogSize;
}

extern "C" __declspec(dllexport)
int CR_StarfieldGetHeroCount()
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    return g_StarfieldState.catalogHeroCount;
}