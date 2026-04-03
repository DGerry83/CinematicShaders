#include "StarfieldNative.h"
#include "GalaxyCamCompositor.h"
#include "TextSystem.h"
#include "../include/StarfieldPass1.h"
#include "../include/StarfieldPass2.h"
#include "../include/StarfieldVS.h"
#include "../include/StarfieldPrefilter.h"
#include "../include/StarfieldBlurX.h"
#include "../include/StarfieldBlur.h"
#include "../include/StarfieldPass2Soft.h"
#include "../include/StarfieldUpscale.h"
#include "../include/KartographerVS.h"
#include "../include/KartographerPS.h"
#include "../include/KartographerText.h"
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
    ID3D11SamplerState* pointSampler = nullptr;     // Point sampler for MSDF textures (navball icons)
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
    
    // Global scene dimming factors (per-frame calculated)
    float sunGlareDimming = 1.0f;
    float planetaryDimming = 1.0f;
    float globalDimming = 1.0f;
    
    // Explicit render target for cubemap rendering (nullptr = use current RT from context)
    ID3D11Texture2D* explicitRenderTarget = nullptr;
    
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
    
    // Kartographer holographic grid overlay
    bool kartographerEnabled = false;
    ID3D11VertexShader* kartographerVS = nullptr;
    ID3D11PixelShader* kartographerPS = nullptr;
    ID3D11BlendState* kartographerBlendState = nullptr;
    ID3D11Buffer* kartographerCB = nullptr;
    
    // Kartographer visual parameters (cached)
    float kartographerGridIntensity = 0.002f;
    float kartographerGridThickness = 0.0003f;
    float kartographerCAStrength = 0.004f;
    float kartographerVignetteStrength = 0.7f;
    float kartographerVignetteStart = 1.6f;
    float kartographerVignetteEnd = 2.2f;
    float kartographerPreRotationYaw = 0.0f;
    float kartographerPreRotationPitch = 0.0f;
    int kartographerGridSizePreset = 2;  // 0=Jumbo, 1=Large, 2=Medium, 3=Small, 4=Tiny
    int kartographerGridColor = 0;       // 0=Seafoam, 1=Amber, 2=White, 3=Green
    int kartographerDebugShapesEnabled = 0;  // Phase 1: Debug SDF shapes toggle
    float kartographerFocalLength = 1.732f;  // Matches 60° vertical FOV
    float kartographerDebugBoxTopLeftX = 0.0f;
    float kartographerDebugBoxTopLeftY = 0.0f;
    float kartographerDebugBoxSizeX = 0.0f;
    float kartographerDebugBoxSizeY = 0.0f;
    float kartographerDebugBoxThickness = 0.001f;
    
    // Kartographer selection circle (cached from C#)
    int kartographerSelectionCircleEnabled = 0;
    float kartographerStarHash = 0.0f;
    float kartographerSelectionCircleCenterX = 0.0f;
    float kartographerSelectionCircleCenterY = 0.0f;
    float kartographerSelectionCircleT = 0.0f;
    float kartographerSelectionCircleIntensity = 0.0f;
    float kartographerSelectionCircleThickness = 0.0f;
    float kartographerSelectionCircleRadius = 0.0f;
    
    // Kartographer text params (cached from C#)
    float kartographerTextOriginX = 0.0f;
    float kartographerTextOriginY = 0.0f;
    float kartographerTextAreaSizeX = 0.0f;
    float kartographerTextAreaSizeY = 0.0f;
    float kartographerSelectionTextT = 0.0f;
    // Grid labels (8 slots) - stored as arrays for compactness
    unsigned int kartographerGridLabelEnabledMask = 0;
    unsigned int kartographerGridLabelDebugMask = 0;
    float kartographerGridLabelPosX[12] = {0};
    float kartographerGridLabelPosY[12] = {0};
    float kartographerGridLabelPosZ[12] = {0,0,0,0,0,0,0,1,0,0,0,1};  // Default Z=1
    float kartographerGridLabelTangentX[12] = {1,1,1,1,1,1,1,1,1,1,1,1};  // Default X=1
    float kartographerGridLabelTangentY[12] = {0};
    float kartographerGridLabelTangentZ[12] = {0};
    float kartographerGridLabelWorldSizeX[12] = {0.1f,0.1f,0.1f,0.1f,0.1f,0.1f,0.1f,0.1f,0.1f,0.1f,0.1f,0.1f};
    float kartographerGridLabelWorldSizeY[12] = {0.1f,0.1f,0.1f,0.1f,0.1f,0.1f,0.1f,0.1f,0.1f,0.1f,0.1f,0.1f};
    float kartographerGridLabelIntensity[12] = {1.0f,1.0f,1.0f,1.0f,1.0f,1.0f,1.0f,1.0f,1.0f,1.0f,1.0f,1.0f};
    uint32_t kartographerGridLabelColor[12] = {0,0,0,0,0,0,0,0,0,0,0,0};
    
    // Text rendering system resources
    ID3D11ComputeShader* textCS = nullptr;
    ID3D11Buffer* textCB = nullptr;
    ID3D11SamplerState* textSampler = nullptr;
    ID3D11ShaderResourceView* textTextureSRV = nullptr;           // t2 - Star selector text
    ID3D11ShaderResourceView* vesselTargetTextTextureSRV = nullptr; // t11 - Vessel target text
    ID3D11ShaderResourceView* gridLabelTextureSRV[12] = {};       // t3-t14 - Grid labels
    
    // Vessel target parameters (separate from star selector)
    int kartographerVesselTargetEnabled = 0;
    float kartographerVesselTargetHash = 0.0f;
    float kartographerVesselTargetCircleCenterX = 0.0f;
    float kartographerVesselTargetCircleCenterY = 0.0f;
    float kartographerVesselTargetCircleT = 0.0f;
    float kartographerVesselTargetCircleIntensity = 0.002f;
    float kartographerVesselTargetCircleThickness = 0.001f;
    float kartographerVesselTargetCircleRadius = 0.02f;
    float kartographerVesselTargetBoxTopLeftX = 0.0f;
    float kartographerVesselTargetBoxTopLeftY = 0.0f;
    float kartographerVesselTargetBoxSizeX = 0.0f;
    float kartographerVesselTargetBoxSizeY = 0.0f;
    float kartographerVesselTargetBoxThickness = 0.001f;
    float kartographerVesselTargetTextOriginX = 0.0f;
    float kartographerVesselTargetTextOriginY = 0.0f;
    float kartographerVesselTargetTextAreaSizeX = 0.0f;
    float kartographerVesselTargetTextAreaSizeY = 0.0f;
    float kartographerVesselTargetTextT = 0.0f;
    float kartographerAnimatedLabelIntensity = 0.0f;
    
    // Navball icon parameters (7 icons: prograde, retrograde, normal, antinormal, radial_in, radial_out, maneuver)
    int kartographerNavballEnabledMask = 0;
    int kartographerNavballOffscreenMode = 0;
    float kartographerNavballIconSize = 0.05f;
    float kartographerNavballIconThickness = 0.0f;  // 0 = default thickness, positive = thicker, negative = thinner
    float kartographerNavballMinIntensity = 0.33f;
    float kartographerNavballMaxAngle = 90.0f;
    float kartographerNavballHysteresisMargin = 0.05f;
    float kartographerNavballIconPosX[7] = {0};
    float kartographerNavballIconPosY[7] = {0};
    float kartographerNavballIconIntensity[7] = {0};
    uint32_t kartographerNavballIconColor[7] = {0};
    
    // Pointing icon (heading indicator)
    int kartographerPointingIconEnabled = 0;
    float kartographerPointingIconPosX = 0.0f;
    float kartographerPointingIconPosY = 0.0f;
    float kartographerPointingIconRotation = 0.0f;
    float kartographerPointingIconIntensity = 0.0f;
    float kartographerPointingIconSize = 0.05f;
    uint32_t kartographerPointingIconColor = 0;
    
    // Maneuver text overlay
    int kartographerManeuverTextEnabled = 0;
    float kartographerManeuverTextOriginX = 0.0f;
    float kartographerManeuverTextOriginY = 0.0f;
    float kartographerManeuverTextWidth = 0.0f;
    float kartographerManeuverTextHeight = 0.0f;
    float kartographerManeuverTextIntensity = 0.0f;
    
    // Navball icon texture array (MSDF textures)
    ID3D11Texture2D* navballIconArray = nullptr;
    ID3D11ShaderResourceView* navballIconArraySRV = nullptr;
    ID3D11ShaderResourceView* pointingIconSRV = nullptr;
    ID3D11ShaderResourceView* maneuverTextSRV = nullptr;
    bool navballTexturesInvalidated = false;  // Set to true when textures are released, false when re-uploaded
    
    // Grid label slot state management (Phase 1 Refactor)
    // Each slot tracks its own active state and SRV to prevent crashes from garbage data
    struct GridLabelSlot {
        ID3D11ShaderResourceView* textureSRV = nullptr;
        bool isActive = false;
        // Cached parameters for slot
        float posX = 0.0f, posY = 0.0f, posZ = 1.0f;
        float tangentX = 1.0f, tangentY = 0.0f, tangentZ = 0.0f;
        float worldSizeX = 0.1f, worldSizeY = 0.1f;
        float intensity = 1.0f;
        uint32_t color = 0;
    };
    GridLabelSlot gridLabelSlots[12];
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
    float _padRotation;    // Restore padding to match shader
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
    
    // Global dimming factors (new - 16 bytes added)
    float SunGlareDimming;
    float PlanetaryDimming;
    float GlobalDimming;
    float _padFinal;  // Ensure 16-byte alignment (96 bytes total)
};

// KartographerParams struct is defined in the generated header included via StarfieldNative.h

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
    
    // Global dimming factors (new - 16 bytes added)
    float SunGlareDimming;
    float PlanetaryDimming;
    float GlobalDimming;
    float _padFinal;  // Ensure 16-byte alignment (80 bytes total)
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

// Apply saturation to color: 0.5=realistic, 1.0=natural, 4.0=hyper-vivid (max for this star)
// catalogID provides per-star variation in saturation rate
static float3 ApplySaturation(float3 baseColor, float sliderValue, int32_t catalogID)
{
    // Per-star variation hash (0-1)
    float variationHash = Hash13(float3((float)catalogID * 12.9898f, (float)catalogID * 78.233f, (float)catalogID * 45.164f));
    
    // Calculate max saturation this specific star can reach before clipping to black in any channel
    // From: 1 + (c - 1) * t = 0  →  t = 1 / (1 - c) for any channel where c < 1
    float maxT = 100.0f;
    if (baseColor.x < 0.999f) maxT = fminf(maxT, 1.0f / (1.0f - baseColor.x));
    if (baseColor.y < 0.999f) maxT = fminf(maxT, 1.0f / (1.0f - baseColor.y));
    if (baseColor.z < 0.999f) maxT = fminf(maxT, 1.0f / (1.0f - baseColor.z));
    
    // Clamp maxT to reasonable bounds (prevents division issues, caps extreme extrapolation)
    maxT = fminf(maxT, 10.0f);
    
    // Add per-star variation to the max (0.85x to 1.15x so some stars max out earlier/later)
    maxT *= (0.85f + variationHash * 0.3f);
    
    // Overshoot: Push 50% past first-channel clip so red stars crush green channel
    // to reach hard red (1,0,0) at slider 4.0 without affecting blue star behavior
    maxT *= 1.5f;
    
    // Map slider [0.5, 4.0] to saturation factor t
    // 0.5 -> 0.5 (white), 1.0 -> 1.0 (original), 4.0 -> maxT (fully saturated for this star)
    float t;
    if (sliderValue <= 1.0f) {
        // Linear from white (t=0) to natural (t=1) 
        // Actually we want 0.5 to give 0.5, 1.0 to give 1.0
        t = sliderValue;
    } else {
        // Map [1.0, 4.0] to [1.0, maxT] with smooth curve
        float normalized = (sliderValue - 1.0f) / 3.0f; // 0 to 1 as slider goes 1->4
        // Smooth step for natural feel
        normalized = normalized * normalized * (3.0f - 2.0f * normalized);
        t = 1.0f + normalized * (maxT - 1.0f);
    }
    
    // Calculate color: move away from white by factor t
    float r = 1.0f + (baseColor.x - 1.0f) * t;
    float g = 1.0f + (baseColor.y - 1.0f) * t;
    float b = 1.0f + (baseColor.z - 1.0f) * t;
    
    // Clamp to valid range (safety)
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
    
    // Soft HDR bloom resources (half resolution - increased from quarter)
    int bloomWidth = width / 2;
    int bloomHeight = height / 2;
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
    
    // Recreate upscale target texture if dimensions changed (now FULL res instead of half)
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
    
    // Create ping-pong texture for vertical blur (half-res dimensions)
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
    
    // Create upscale target texture - now FULL resolution (was half-res)
    int targetWidth = width;    // Full res upscale target
    int targetHeight = height;  // Full res upscale target
    if (targetWidth < 1) targetWidth = 1;
    if (targetHeight < 1) targetHeight = 1;
    
    if (!g_StarfieldState.bloomHalfTexture) {
        D3D11_TEXTURE2D_DESC halfDesc = {};
        halfDesc.Width = targetWidth;
        halfDesc.Height = targetHeight;
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
    
    // Point sampler for MSDF textures (navball icons) - must use CLAMP to avoid wrapping artifacts
    if (!g_StarfieldState.pointSampler) {
        D3D11_SAMPLER_DESC pointDesc = {};
        pointDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
        pointDesc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;  // CRITICAL: Prevents wrap-around artifacts
        pointDesc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
        pointDesc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        device->CreateSamplerState(&pointDesc, &g_StarfieldState.pointSampler);
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
    
    // Kartographer resources (created on-demand when enabled)
    if (!g_StarfieldState.kartographerVS) {
        hr = device->CreateVertexShader(g_KartographerVS, sizeof(g_KartographerVS), nullptr, &g_StarfieldState.kartographerVS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create Kartographer VS (0x%08X)", hr);
    }
    if (!g_StarfieldState.kartographerPS) {
        hr = device->CreatePixelShader(g_KartographerPS, sizeof(g_KartographerPS), nullptr, &g_StarfieldState.kartographerPS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create Kartographer PS (0x%08X)", hr);
    }
    if (!g_StarfieldState.kartographerBlendState) {
        D3D11_BLEND_DESC blendDesc = {};
        blendDesc.RenderTarget[0].BlendEnable = TRUE;
        blendDesc.RenderTarget[0].SrcBlend = D3D11_BLEND_ONE;
        blendDesc.RenderTarget[0].DestBlend = D3D11_BLEND_ONE;
        blendDesc.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
        blendDesc.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
        blendDesc.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ONE;
        blendDesc.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
        blendDesc.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
        device->CreateBlendState(&blendDesc, &g_StarfieldState.kartographerBlendState);
    }
    
    // Kartographer constant buffer (256 bytes for expanded Phase 1 layout)
    if (!g_StarfieldState.kartographerCB) {
        D3D11_BUFFER_DESC cbDesc = {};
        cbDesc.ByteWidth = sizeof(KartographerParams);
        cbDesc.Usage = D3D11_USAGE_DYNAMIC;
        cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        device->CreateBuffer(&cbDesc, nullptr, &g_StarfieldState.kartographerCB);
        LogToFile("[Starfield] Kartographer CB created: %zu bytes", sizeof(KartographerParams));
    }
    
    if (!g_StarfieldState.device) {
        g_StarfieldState.device = device;
        g_StarfieldState.device->AddRef();
    }
    
    g_StarfieldState.width = width;
    g_StarfieldState.height = height;
    g_StarfieldState.initialized = true;
    g_StarfieldState.cachedHDRFormat = DXGI_FORMAT_R11G11B10_FLOAT;
    
    // Initialize all grid label slots to empty state (Phase 1 Refactor)
    // This ensures no garbage SRV pointers exist in unused slots
    for (int i = 0; i < 12; i++) {
        g_StarfieldState.gridLabelSlots[i].isActive = false;
        if (g_StarfieldState.gridLabelSlots[i].textureSRV) {
            g_StarfieldState.gridLabelSlots[i].textureSRV->Release();
            g_StarfieldState.gridLabelSlots[i].textureSRV = nullptr;
        }
    }
    
    LogToFile("[Starfield] Resources initialized: %dx%d", width, height);
}

static void ExecuteSoftBloomRender(ID3D11DeviceContext* context, ID3D11RenderTargetView* finalRTV);
static void MapKartographerConstantBuffer(ID3D11DeviceContext* context);

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
    
    // Get or create render target view
    ID3D11RenderTargetView* currentRTV = nullptr;
    ID3D11DepthStencilView* currentDSV = nullptr;
    bool usingExplicitRT = false;
    
    if (g_StarfieldState.explicitRenderTarget) {
        // Use explicit render target for cubemap rendering
        // Get texture description to handle TYPELESS formats
        D3D11_TEXTURE2D_DESC texDesc;
        g_StarfieldState.explicitRenderTarget->GetDesc(&texDesc);
        
        // If format is TYPELESS, we need to specify a concrete format for the RTV
        DXGI_FORMAT rtvFormat = texDesc.Format;
        if (texDesc.Format == DXGI_FORMAT_R8G8B8A8_TYPELESS) {
            rtvFormat = DXGI_FORMAT_R8G8B8A8_UNORM;
        }
        
        D3D11_RENDER_TARGET_VIEW_DESC rtvDesc = {};
        rtvDesc.Format = rtvFormat;
        rtvDesc.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D;
        rtvDesc.Texture2D.MipSlice = 0;
        
        HRESULT hr = device->CreateRenderTargetView(g_StarfieldState.explicitRenderTarget, &rtvDesc, &currentRTV);
        if (SUCCEEDED(hr)) {
            usingExplicitRT = true;
        } else {
            // Fallback: try with null desc (let D3D11 infer from texture)
            hr = device->CreateRenderTargetView(g_StarfieldState.explicitRenderTarget, nullptr, &currentRTV);
            if (SUCCEEDED(hr)) {
                usingExplicitRT = true;
            }
        }
        
        if (!usingExplicitRT) {
            context->OMGetRenderTargets(1, &currentRTV, &currentDSV);
        }
    } else {
        // Use current render target from context (normal gameplay)
        context->OMGetRenderTargets(1, &currentRTV, &currentDSV);
    }
    
    if (!currentRTV) {
        LogToFile("[ExecuteStarfieldRender] No render target, aborting");
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
        
        // Global scene dimming (new)
        params->SunGlareDimming = g_StarfieldState.sunGlareDimming;
        params->PlanetaryDimming = g_StarfieldState.planetaryDimming;
        params->GlobalDimming = g_StarfieldState.globalDimming;
        params->_padFinal = 0.0f;
        
        context->Unmap(g_StarfieldState.pass2CB, 0);
    }
    
    // Select rendering path based on bloom mode
    if (g_StarfieldState.useSoftBloom) {
        // Soft HDR 2-pass path
        ExecuteSoftBloomRender(context, currentRTV);
        
        // Cleanup and return early for soft bloom path
        if (usingExplicitRT && currentRTV) {
            currentRTV->Release();
        }
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
    
    // Cleanup SRV bindings (but keep RTV bound for Kartographer)
    ID3D11ShaderResourceView* psNullSRV[1] = {nullptr};
    context->PSSetShaderResources(0, 1, psNullSRV);
    
    // Kartographer overlay pass (if enabled)
    if (g_StarfieldState.kartographerEnabled && g_StarfieldState.kartographerVS && g_StarfieldState.kartographerPS) {
        // Save current state
        ID3D11BlendState* oldBlend = nullptr;
        float oldBlendFactor[4];
        UINT oldSampleMask;
        context->OMGetBlendState(&oldBlend, oldBlendFactor, &oldSampleMask);
        
        // Set additive blend for grid overlay
        context->OMSetBlendState(g_StarfieldState.kartographerBlendState, nullptr, 0xFFFFFFFF);
        context->OMSetDepthStencilState(g_StarfieldState.depthState, 0);
        context->RSSetState(g_StarfieldState.rasterState);
        
        // Set Kartographer shaders and constant buffer
        context->VSSetShader(g_StarfieldState.kartographerVS, nullptr, 0);
        context->PSSetShader(g_StarfieldState.kartographerPS, nullptr, 0);
        MapKartographerConstantBuffer(context);
        context->PSSetConstantBuffers(0, 1, &g_StarfieldState.kartographerCB);
        
        // Bind text texture to slot t2 if available
        if (g_StarfieldState.textTextureSRV) {
            LogToFile("[Text] Binding text texture SRV %p to PS slot t2", g_StarfieldState.textTextureSRV);
            context->PSSetShaderResources(2, 1, &g_StarfieldState.textTextureSRV);
        } else {
            LogToFile("[Text] No text texture SRV available (null), nothing bound to t2");
        }
        
        // Bind grid label textures to slots t3-t14
        for (int i = 0; i < 12; i++) {
            const auto& slot = g_StarfieldState.gridLabelSlots[i];
            if (slot.isActive && slot.textureSRV) {
                context->PSSetShaderResources(3 + i, 1, &slot.textureSRV);
            }
        }
        
        // Bind vessel target text texture to slot t15
        if (g_StarfieldState.vesselTargetTextTextureSRV) {
            context->PSSetShaderResources(15, 1, &g_StarfieldState.vesselTargetTextTextureSRV);
        }
        
        // Bind navball icon texture array to slot t16
        if (g_StarfieldState.navballIconArraySRV) {
            context->PSSetShaderResources(16, 1, &g_StarfieldState.navballIconArraySRV);
        }
        
        // Bind pointing icon texture to slot t17
        if (g_StarfieldState.pointingIconSRV) {
            context->PSSetShaderResources(17, 1, &g_StarfieldState.pointingIconSRV);
        }
        // Bind maneuver text texture to slot t18
        if (g_StarfieldState.maneuverTextSRV) {
            context->PSSetShaderResources(18, 1, &g_StarfieldState.maneuverTextSRV);
        }
        
        // Bind samplers: s0 = linear (for text/grid), s1 = point (for MSDF navball icons)
        ID3D11SamplerState* samplers[2] = {g_StarfieldState.linearSampler, g_StarfieldState.pointSampler};
        context->PSSetSamplers(0, 2, samplers);
        
        // RTV is still bound from main pass - no need to rebind
        
        // Draw fullscreen triangle
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->IASetInputLayout(nullptr);
        context->IASetVertexBuffers(0, 0, nullptr, nullptr, nullptr);
        context->IASetIndexBuffer(nullptr, DXGI_FORMAT_UNKNOWN, 0);
        context->Draw(3, 0);
        
        // Restore blend state
        context->OMSetBlendState(oldBlend, oldBlendFactor, oldSampleMask);
        if (oldBlend) oldBlend->Release();
    }
    
    // NOW unbind RTV after Kartographer is done (consistent with SoftBloom path)
    ID3D11RenderTargetView* nullRTV = nullptr;
    context->OMSetRenderTargets(1, &nullRTV, nullptr);
    
    // Release RTV (always, regardless of whether it was explicit or from OMGetRenderTargets)
    if (currentRTV) {
        currentRTV->Release();
    }
    if (currentDSV) currentDSV->Release();
    device->Release();
    
    // Clear explicit render target after use (it's per-frame only)
    if (g_StarfieldState.explicitRenderTarget) {
        g_StarfieldState.explicitRenderTarget->Release();
        g_StarfieldState.explicitRenderTarget = nullptr;
    }
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
    
    // Half-res dimensions (matching texture creation)
    int bloomWidth = g_StarfieldState.width / 2;
    int bloomHeight = g_StarfieldState.height / 2;
    if (bloomWidth < 1) bloomWidth = 1;
    if (bloomHeight < 1) bloomHeight = 1;
    
    float clearColor[4] = {0, 0, 0, 0};
    
    // ========================================================================
    // PASS 1: Prefilter + Downsample (Full-res HDR -> bloomTempTexture)
    // Rendering to half-res
    // ========================================================================
    D3D11_VIEWPORT halfResVP = {};
    halfResVP.Width = (float)bloomWidth;      // width/2
    halfResVP.Height = (float)bloomHeight;    // height/2
    halfResVP.MinDepth = 0.0f;
    halfResVP.MaxDepth = 1.0f;
    context->RSSetViewports(1, &halfResVP);
    
    context->OMSetRenderTargets(1, &g_StarfieldState.bloomTempRTV, nullptr);
    context->OMSetBlendState(nullptr, nullptr, 0xFFFFFFFF); // Disable blend
    context->ClearRenderTargetView(g_StarfieldState.bloomTempRTV, clearColor);
    
    // Update Prefilter CB
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(context->Map(g_StarfieldState.prefilterCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        float* data = (float*)mapped.pData;
        data[0] = (float)g_StarfieldState.width;       // SourceSizeX (full)
        data[1] = (float)g_StarfieldState.height;      // SourceSizeY (full)
        data[2] = 1.0f / g_StarfieldState.width;       // InvSourceSizeX
        data[3] = 1.0f / g_StarfieldState.height;      // InvSourceSizeY
        data[4] = g_StarfieldState.bloomThreshold;     // BloomThreshold
        data[5] = 0.65f;                                // BloomKnee
        data[6] = (float)bloomWidth;                   // OutputSizeX - NOW width/2 (half-res)
        data[7] = (float)bloomHeight;                  // OutputSizeY - NOW height/2 (half-res)
        data[8] = 1.0f / bloomWidth;                   // InvOutputSizeX - NOW 2/width
        data[9] = 1.0f / bloomHeight;                  // InvOutputSizeY - NOW 2/height
        // Padding to fill 48 bytes
        context->Unmap(g_StarfieldState.prefilterCB, 0);
    }
    
    context->VSSetShader(g_StarfieldState.pass2VS, nullptr, 0);
    context->PSSetShader(g_StarfieldState.prefilterPS, nullptr, 0);
    context->PSSetConstantBuffers(0, 1, &g_StarfieldState.prefilterCB);
    context->PSSetSamplers(0, 1, &g_StarfieldState.linearSampler);
    ID3D11ShaderResourceView* prefilterSRV[1] = {g_StarfieldState.hdrSRV};
    context->PSSetShaderResources(0, 1, prefilterSRV);
    
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->IASetInputLayout(nullptr);
    context->IASetVertexBuffers(0, 0, nullptr, nullptr, nullptr);
    context->IASetIndexBuffer(nullptr, DXGI_FORMAT_UNKNOWN, 0);
    context->RSSetState(g_StarfieldState.rasterState);
    
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
    // CRITICAL: TexelSize is now for half-res textures (1/2 of full res pixel size)
    if (SUCCEEDED(context->Map(g_StarfieldState.blurCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        float* data = (float*)mapped.pData;
        data[0] = 1.0f / bloomWidth;   // TexelSizeX - NOW 2/width (half-res texel size)
        data[1] = 1.0f / bloomHeight;  // TexelSizeY - NOW 2/height (half-res texel size)
        float t = g_StarfieldState.bloomIntensity / 2.0f; // 0-1 range
        data[2] = t * 0.65f; // BloomSpread
        data[3] = 0.0f; // Pad
        context->Unmap(g_StarfieldState.blurCB, 0);
    }
    
    context->PSSetShader(g_StarfieldState.blurXPS, nullptr, 0); // Horizontal blur
    context->PSSetConstantBuffers(0, 1, &g_StarfieldState.blurCB);
    ID3D11ShaderResourceView* horizSRV[1] = {g_StarfieldState.bloomTempSRV};
    context->PSSetShaderResources(0, 1, horizSRV);
    
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->IASetInputLayout(nullptr);
    context->IASetVertexBuffers(0, 0, nullptr, nullptr, nullptr);
    context->IASetIndexBuffer(nullptr, DXGI_FORMAT_UNKNOWN, 0);
    context->RSSetState(g_StarfieldState.rasterState);
    
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
    
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->IASetInputLayout(nullptr);
    context->IASetVertexBuffers(0, 0, nullptr, nullptr, nullptr);
    context->IASetIndexBuffer(nullptr, DXGI_FORMAT_UNKNOWN, 0);
    context->RSSetState(g_StarfieldState.rasterState);
    
    context->Draw(3, 0);
    context->PSSetShaderResources(0, 1, nullSRV);
    
    // ========================================================================
    // PASS 3.5: Upscale to Full-Resolution (1/2 -> Full)
    // Changed from 1/4 -> 1/2 to 1/2 -> Full
    // ========================================================================
    D3D11_VIEWPORT fullResVP = {};
    fullResVP.Width = (float)g_StarfieldState.width;   // Full width
    fullResVP.Height = (float)g_StarfieldState.height; // Full height
    fullResVP.MinDepth = 0.0f;
    fullResVP.MaxDepth = 1.0f;
    context->RSSetViewports(1, &fullResVP);
    
    context->OMSetRenderTargets(1, &g_StarfieldState.bloomHalfRTV, nullptr);
    context->OMSetBlendState(nullptr, nullptr, 0xFFFFFFFF);
    context->ClearRenderTargetView(g_StarfieldState.bloomHalfRTV, clearColor);
    
    context->PSSetShader(g_StarfieldState.upscalePS, nullptr, 0);
    ID3D11ShaderResourceView* upscaleSRV[1] = {g_StarfieldState.bloomTempSRV};
    context->PSSetShaderResources(0, 1, upscaleSRV);
    
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->IASetInputLayout(nullptr);
    context->IASetVertexBuffers(0, 0, nullptr, nullptr, nullptr);
    context->IASetIndexBuffer(nullptr, DXGI_FORMAT_UNKNOWN, 0);
    context->RSSetState(g_StarfieldState.rasterState);
    
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
        
        // Global scene dimming (new)
        params->SunGlareDimming = g_StarfieldState.sunGlareDimming;
        params->PlanetaryDimming = g_StarfieldState.planetaryDimming;
        params->GlobalDimming = g_StarfieldState.globalDimming;
        params->_padFinal = 0.0f;
        
        context->Unmap(g_StarfieldState.compositeCB, 0);
    }
    
    context->VSSetShader(g_StarfieldState.pass2VS, nullptr, 0);
    context->PSSetShader(g_StarfieldState.softCompositePS, nullptr, 0);
    context->PSSetConstantBuffers(0, 1, &g_StarfieldState.compositeCB);
    context->PSSetSamplers(0, 1, &g_StarfieldState.linearSampler);
    
    // Composite reads full-res HDR (t0) and final bloom from bloomHalfSRV (t1, half-res)
    ID3D11ShaderResourceView* compositeSRVs[2] = {g_StarfieldState.hdrSRV, g_StarfieldState.bloomHalfSRV};
    context->PSSetShaderResources(0, 2, compositeSRVs);
    
    // Restore IA state for fullscreen triangle (matches Classic path)
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->IASetInputLayout(nullptr);
    context->IASetVertexBuffers(0, 0, nullptr, nullptr, nullptr);
    context->IASetIndexBuffer(nullptr, DXGI_FORMAT_UNKNOWN, 0);
    context->RSSetState(g_StarfieldState.rasterState);
    
    context->Draw(3, 0);
    
    // Kartographer overlay pass (if enabled)
    if (g_StarfieldState.kartographerEnabled && g_StarfieldState.kartographerVS && g_StarfieldState.kartographerPS) {
        // Save current state
        ID3D11BlendState* oldBlend = nullptr;
        float oldBlendFactor[4];
        UINT oldSampleMask;
        context->OMGetBlendState(&oldBlend, oldBlendFactor, &oldSampleMask);
        
        // Set additive blend for grid overlay
        context->OMSetBlendState(g_StarfieldState.kartographerBlendState, nullptr, 0xFFFFFFFF);
        
        // Update and set Kartographer constant buffer
        MapKartographerConstantBuffer(context);
        context->PSSetConstantBuffers(0, 1, &g_StarfieldState.kartographerCB);
        
        // Set Kartographer shaders
        context->VSSetShader(g_StarfieldState.kartographerVS, nullptr, 0);
        context->PSSetShader(g_StarfieldState.kartographerPS, nullptr, 0);
        
        // Bind text texture to slot t2 if available
        if (g_StarfieldState.textTextureSRV) {
            context->PSSetShaderResources(2, 1, &g_StarfieldState.textTextureSRV);
        }
        
        // Bind grid label textures to slots t3-t14 (Phase 1: Use new slot state with isActive check)
        for (int i = 0; i < 12; i++) {
            const auto& slot = g_StarfieldState.gridLabelSlots[i];
            if (slot.isActive && slot.textureSRV) {
                context->PSSetShaderResources(3 + i, 1, &slot.textureSRV);
            }
        }
        
        // Bind vessel target text texture to slot t15
        if (g_StarfieldState.vesselTargetTextTextureSRV) {
            context->PSSetShaderResources(15, 1, &g_StarfieldState.vesselTargetTextTextureSRV);
        }
        
        // Bind navball icon texture array to slot t16
        if (g_StarfieldState.navballIconArraySRV) {
            context->PSSetShaderResources(16, 1, &g_StarfieldState.navballIconArraySRV);
        }
        
        // Bind pointing icon texture to slot t17
        if (g_StarfieldState.pointingIconSRV) {
            context->PSSetShaderResources(17, 1, &g_StarfieldState.pointingIconSRV);
        }
        // Bind maneuver text texture to slot t18
        if (g_StarfieldState.maneuverTextSRV) {
            context->PSSetShaderResources(18, 1, &g_StarfieldState.maneuverTextSRV);
        }
        
        // Bind samplers: s0 = linear (for text/grid), s1 = point (for MSDF navball icons)
        ID3D11SamplerState* samplers[2] = {g_StarfieldState.linearSampler, g_StarfieldState.pointSampler};
        context->PSSetSamplers(0, 2, samplers);
        
        // Draw fullscreen triangle
        context->Draw(3, 0);
        
        // Restore blend state
        context->OMSetBlendState(oldBlend, oldBlendFactor, oldSampleMask);
        if (oldBlend) oldBlend->Release();
    }
    
    // Cleanup
    context->PSSetShaderResources(0, 2, nullSRV);
    ID3D11RenderTargetView* nullRTV = nullptr;
    context->OMSetRenderTargets(1, &nullRTV, nullptr);
}

static void UNITY_INTERFACE_API OnStarfieldRenderEvent(int eventId)
{
    if (!g_StarfieldState.device) return;
    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    ID3D11DeviceContext* context = nullptr;
    g_StarfieldState.device->GetImmediateContext(&context);
    if (!context) return;
    
    ExecuteStarfieldRender(context);
    
    // Render any registered compositor layers on top of the starfield
    if (GalaxyCamCompositor_HasLayers()) {
        ID3D11RenderTargetView* currentRTV = nullptr;
        ID3D11DepthStencilView* currentDSV = nullptr;
        context->OMGetRenderTargets(1, &currentRTV, &currentDSV);
        
        if (currentRTV) {
            GalaxyCamCompositor_RenderLayers(context, currentRTV, g_StarfieldState.width, g_StarfieldState.height);
            currentRTV->Release();
        }
        if (currentDSV) currentDSV->Release();
    }
    
    context->Release();
    
    // Increment temporal frame index
    g_StarfieldState.frameIndex = (g_StarfieldState.frameIndex + 1) & 7;
}

extern "C" __declspec(dllexport)
void CR_StarfieldSetDimming(float sunGlareDimming, float planetaryDimming)
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    g_StarfieldState.sunGlareDimming = sunGlareDimming;
    g_StarfieldState.planetaryDimming = planetaryDimming;
    // Use whichever dims more (the darker/minimum value)
    g_StarfieldState.globalDimming = (sunGlareDimming < planetaryDimming) ? sunGlareDimming : planetaryDimming;
    
    // Safety clamp - never allow complete blackness from dimming alone
    if (g_StarfieldState.globalDimming < 0.05f)
        g_StarfieldState.globalDimming = 0.05f;
}

extern "C" __declspec(dllexport)
void CR_StarfieldSetKartographerEnabled(unsigned char enabled)
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    g_StarfieldState.kartographerEnabled = (enabled != 0);
    // LogToFile("[Starfield] Kartographer %s", g_StarfieldState.kartographerEnabled ? "enabled" : "disabled");
}

static void MapKartographerConstantBuffer(ID3D11DeviceContext* context)
{
    D3D11_MAPPED_SUBRESOURCE mappedKart;
    if (SUCCEEDED(context->Map(g_StarfieldState.kartographerCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mappedKart))) {
        KartographerParams* params = (KartographerParams*)mappedKart.pData;
        
        // Base grid params
        params->ResolutionX = (float)g_StarfieldState.width;
        params->ResolutionY = (float)g_StarfieldState.height;
        params->Time = (float)g_StarfieldState.frameIndex * 0.016f;
        params->GridIntensity = g_StarfieldState.kartographerGridIntensity;
        params->GridThickness = g_StarfieldState.kartographerGridThickness;
        params->ChromaticAberrationStrength = g_StarfieldState.kartographerCAStrength;
        params->VignetteStrength = g_StarfieldState.kartographerVignetteStrength;
        params->VignetteStart = g_StarfieldState.kartographerVignetteStart;
        params->VignetteEnd = g_StarfieldState.kartographerVignetteEnd;
        params->PreRotationYaw = g_StarfieldState.kartographerPreRotationYaw;
        params->PreRotationPitch = g_StarfieldState.kartographerPreRotationPitch;
        params->GridSizePreset = g_StarfieldState.kartographerGridSizePreset;
        params->GridColorIndex = g_StarfieldState.kartographerGridColor;
        params->_pad1 = 0.0f;
        params->_pad2 = 0.0f;
        params->_padAlignCamera = 0.0f;
        
        // Camera basis
        params->CameraRightX = g_StarfieldState.cameraRight.x;
        params->CameraRightY = g_StarfieldState.cameraRight.y;
        params->CameraRightZ = g_StarfieldState.cameraRight.z;
        params->_pad3 = 0.0f;
        params->CameraUpX = g_StarfieldState.cameraUp.x;
        params->CameraUpY = g_StarfieldState.cameraUp.y;
        params->CameraUpZ = g_StarfieldState.cameraUp.z;
        params->_pad4 = 0.0f;
        params->CameraForwardX = g_StarfieldState.cameraForward.x;
        params->CameraForwardY = g_StarfieldState.cameraForward.y;
        params->CameraForwardZ = g_StarfieldState.cameraForward.z;
        params->_pad5 = 0.0f;
        
        // Debug shapes - hard-coded test values (independent of grid settings)
        params->DebugShapesEnabled = g_StarfieldState.kartographerDebugShapesEnabled;
        params->_pad6 = 0.0f;
        params->_pad7 = 0.0f;
        params->_pad8 = 0.0f;
        params->DebugCircleCenterX = 0.0f;  // Screen center in shader-uv space
        params->DebugCircleCenterY = 0.0f;
        params->DebugCircleRadius = 0.05f;
        params->DebugCircleThickness = 0.001f;
        params->DebugBoxTopLeftX = g_StarfieldState.kartographerDebugBoxTopLeftX;
        params->DebugBoxTopLeftY = g_StarfieldState.kartographerDebugBoxTopLeftY;
        params->DebugBoxSizeX = g_StarfieldState.kartographerDebugBoxSizeX;
        params->DebugBoxSizeY = g_StarfieldState.kartographerDebugBoxSizeY;
        params->DebugBoxThickness = g_StarfieldState.kartographerDebugBoxThickness;
        params->DebugShapeIntensity = 0.002f;
        params->_pad9 = 0.0f;
        params->FocalLength = g_StarfieldState.kartographerFocalLength > 0.001f ? g_StarfieldState.kartographerFocalLength : 1.732f;
        
        // Selection circle (filled from cached state set by CR_StarfieldSetKartographerParams)
        params->SelectionCircleEnabled = g_StarfieldState.kartographerSelectionCircleEnabled;
        params->SelectionStarHash = g_StarfieldState.kartographerStarHash;
        params->_padSelection2 = 0.0f;
        params->_padSelection3 = 0.0f;
        params->SelectionCircleCenterX = g_StarfieldState.kartographerSelectionCircleCenterX;
        params->SelectionCircleCenterY = g_StarfieldState.kartographerSelectionCircleCenterY;
        params->SelectionCircleT = g_StarfieldState.kartographerSelectionCircleT;
        params->SelectionCircleIntensity = g_StarfieldState.kartographerSelectionCircleIntensity;
        params->SelectionCircleThickness = g_StarfieldState.kartographerSelectionCircleThickness;
        params->SelectionCircleRadius = g_StarfieldState.kartographerSelectionCircleRadius;
        params->_padSelection4 = 0.0f;
        params->_padSelection5 = 0.0f;
        params->_padSelection6 = 0.0f;
        
        // Text params
        params->TextOriginX = g_StarfieldState.kartographerTextOriginX;
        params->TextOriginY = g_StarfieldState.kartographerTextOriginY;
        params->TextAreaSizeX = g_StarfieldState.kartographerTextAreaSizeX;
        params->TextAreaSizeY = g_StarfieldState.kartographerTextAreaSizeY;
        params->SelectionTextT = g_StarfieldState.kartographerSelectionTextT;
        // Copy all 8 grid labels from state to CB (as float4)
        params->GridLabelEnabledMask = g_StarfieldState.kartographerGridLabelEnabledMask;
        params->_padGridMask1 = 0.0f;
        params->_padGridMask2 = 0.0f;
        params->_padGridMask3 = 0.0f;
        params->_padGridMask4 = 0.0f;
        
        params->GridLabel0_PosTangentX = float4(
            g_StarfieldState.kartographerGridLabelPosX[0],
            g_StarfieldState.kartographerGridLabelPosY[0],
            g_StarfieldState.kartographerGridLabelPosZ[0],
            g_StarfieldState.kartographerGridLabelWorldSizeX[0]);
        params->GridLabel0_TangentY = float4(
            g_StarfieldState.kartographerGridLabelTangentX[0],
            g_StarfieldState.kartographerGridLabelTangentY[0],
            g_StarfieldState.kartographerGridLabelTangentZ[0],
            g_StarfieldState.kartographerGridLabelWorldSizeY[0]);
        
        params->GridLabel1_PosTangentX = float4(
            g_StarfieldState.kartographerGridLabelPosX[1],
            g_StarfieldState.kartographerGridLabelPosY[1],
            g_StarfieldState.kartographerGridLabelPosZ[1],
            g_StarfieldState.kartographerGridLabelWorldSizeX[1]);
        params->GridLabel1_TangentY = float4(
            g_StarfieldState.kartographerGridLabelTangentX[1],
            g_StarfieldState.kartographerGridLabelTangentY[1],
            g_StarfieldState.kartographerGridLabelTangentZ[1],
            g_StarfieldState.kartographerGridLabelWorldSizeY[1]);
        
        params->GridLabel2_PosTangentX = float4(
            g_StarfieldState.kartographerGridLabelPosX[2],
            g_StarfieldState.kartographerGridLabelPosY[2],
            g_StarfieldState.kartographerGridLabelPosZ[2],
            g_StarfieldState.kartographerGridLabelWorldSizeX[2]);
        params->GridLabel2_TangentY = float4(
            g_StarfieldState.kartographerGridLabelTangentX[2],
            g_StarfieldState.kartographerGridLabelTangentY[2],
            g_StarfieldState.kartographerGridLabelTangentZ[2],
            g_StarfieldState.kartographerGridLabelWorldSizeY[2]);
        
        params->GridLabel3_PosTangentX = float4(
            g_StarfieldState.kartographerGridLabelPosX[3],
            g_StarfieldState.kartographerGridLabelPosY[3],
            g_StarfieldState.kartographerGridLabelPosZ[3],
            g_StarfieldState.kartographerGridLabelWorldSizeX[3]);
        params->GridLabel3_TangentY = float4(
            g_StarfieldState.kartographerGridLabelTangentX[3],
            g_StarfieldState.kartographerGridLabelTangentY[3],
            g_StarfieldState.kartographerGridLabelTangentZ[3],
            g_StarfieldState.kartographerGridLabelWorldSizeY[3]);
        
        params->GridLabel4_PosTangentX = float4(
            g_StarfieldState.kartographerGridLabelPosX[4],
            g_StarfieldState.kartographerGridLabelPosY[4],
            g_StarfieldState.kartographerGridLabelPosZ[4],
            g_StarfieldState.kartographerGridLabelWorldSizeX[4]);
        params->GridLabel4_TangentY = float4(
            g_StarfieldState.kartographerGridLabelTangentX[4],
            g_StarfieldState.kartographerGridLabelTangentY[4],
            g_StarfieldState.kartographerGridLabelTangentZ[4],
            g_StarfieldState.kartographerGridLabelWorldSizeY[4]);
        
        params->GridLabel5_PosTangentX = float4(
            g_StarfieldState.kartographerGridLabelPosX[5],
            g_StarfieldState.kartographerGridLabelPosY[5],
            g_StarfieldState.kartographerGridLabelPosZ[5],
            g_StarfieldState.kartographerGridLabelWorldSizeX[5]);
        params->GridLabel5_TangentY = float4(
            g_StarfieldState.kartographerGridLabelTangentX[5],
            g_StarfieldState.kartographerGridLabelTangentY[5],
            g_StarfieldState.kartographerGridLabelTangentZ[5],
            g_StarfieldState.kartographerGridLabelWorldSizeY[5]);
        
        params->GridLabel6_PosTangentX = float4(
            g_StarfieldState.kartographerGridLabelPosX[6],
            g_StarfieldState.kartographerGridLabelPosY[6],
            g_StarfieldState.kartographerGridLabelPosZ[6],
            g_StarfieldState.kartographerGridLabelWorldSizeX[6]);
        params->GridLabel6_TangentY = float4(
            g_StarfieldState.kartographerGridLabelTangentX[6],
            g_StarfieldState.kartographerGridLabelTangentY[6],
            g_StarfieldState.kartographerGridLabelTangentZ[6],
            g_StarfieldState.kartographerGridLabelWorldSizeY[6]);
        
        params->GridLabel7_PosTangentX = float4(
            g_StarfieldState.kartographerGridLabelPosX[7],
            g_StarfieldState.kartographerGridLabelPosY[7],
            g_StarfieldState.kartographerGridLabelPosZ[7],
            g_StarfieldState.kartographerGridLabelWorldSizeX[7]);
        params->GridLabel7_TangentY = float4(
            g_StarfieldState.kartographerGridLabelTangentX[7],
            g_StarfieldState.kartographerGridLabelTangentY[7],
            g_StarfieldState.kartographerGridLabelTangentZ[7],
            g_StarfieldState.kartographerGridLabelWorldSizeY[7]);
        
        params->GridLabel8_PosTangentX = float4(
            g_StarfieldState.kartographerGridLabelPosX[8],
            g_StarfieldState.kartographerGridLabelPosY[8],
            g_StarfieldState.kartographerGridLabelPosZ[8],
            g_StarfieldState.kartographerGridLabelWorldSizeX[8]);
        params->GridLabel8_TangentY = float4(
            g_StarfieldState.kartographerGridLabelTangentX[8],
            g_StarfieldState.kartographerGridLabelTangentY[8],
            g_StarfieldState.kartographerGridLabelTangentZ[8],
            g_StarfieldState.kartographerGridLabelWorldSizeY[8]);
        
        params->GridLabel9_PosTangentX = float4(
            g_StarfieldState.kartographerGridLabelPosX[9],
            g_StarfieldState.kartographerGridLabelPosY[9],
            g_StarfieldState.kartographerGridLabelPosZ[9],
            g_StarfieldState.kartographerGridLabelWorldSizeX[9]);
        params->GridLabel9_TangentY = float4(
            g_StarfieldState.kartographerGridLabelTangentX[9],
            g_StarfieldState.kartographerGridLabelTangentY[9],
            g_StarfieldState.kartographerGridLabelTangentZ[9],
            g_StarfieldState.kartographerGridLabelWorldSizeY[9]);
        
        params->GridLabel10_PosTangentX = float4(
            g_StarfieldState.kartographerGridLabelPosX[10],
            g_StarfieldState.kartographerGridLabelPosY[10],
            g_StarfieldState.kartographerGridLabelPosZ[10],
            g_StarfieldState.kartographerGridLabelWorldSizeX[10]);
        params->GridLabel10_TangentY = float4(
            g_StarfieldState.kartographerGridLabelTangentX[10],
            g_StarfieldState.kartographerGridLabelTangentY[10],
            g_StarfieldState.kartographerGridLabelTangentZ[10],
            g_StarfieldState.kartographerGridLabelWorldSizeY[10]);
        
        params->GridLabel11_PosTangentX = float4(
            g_StarfieldState.kartographerGridLabelPosX[11],
            g_StarfieldState.kartographerGridLabelPosY[11],
            g_StarfieldState.kartographerGridLabelPosZ[11],
            g_StarfieldState.kartographerGridLabelWorldSizeX[11]);
        params->GridLabel11_TangentY = float4(
            g_StarfieldState.kartographerGridLabelTangentX[11],
            g_StarfieldState.kartographerGridLabelTangentY[11],
            g_StarfieldState.kartographerGridLabelTangentZ[11],
            g_StarfieldState.kartographerGridLabelWorldSizeY[11]);
        
        // Debug mask and per-label visual params
        params->GridLabelDebugMask = g_StarfieldState.kartographerGridLabelDebugMask;
        params->LabelIntensity0 = g_StarfieldState.kartographerGridLabelIntensity[0];
        params->LabelIntensity1 = g_StarfieldState.kartographerGridLabelIntensity[1];
        params->LabelIntensity2 = g_StarfieldState.kartographerGridLabelIntensity[2];
        params->LabelIntensity3 = g_StarfieldState.kartographerGridLabelIntensity[3];
        params->LabelIntensity4 = g_StarfieldState.kartographerGridLabelIntensity[4];
        params->LabelIntensity5 = g_StarfieldState.kartographerGridLabelIntensity[5];
        params->LabelIntensity6 = g_StarfieldState.kartographerGridLabelIntensity[6];
        params->LabelIntensity7 = g_StarfieldState.kartographerGridLabelIntensity[7];
        params->LabelIntensity8 = g_StarfieldState.kartographerGridLabelIntensity[8];
        params->LabelIntensity9 = g_StarfieldState.kartographerGridLabelIntensity[9];
        params->LabelIntensity10 = g_StarfieldState.kartographerGridLabelIntensity[10];
        params->LabelIntensity11 = g_StarfieldState.kartographerGridLabelIntensity[11];
        params->LabelColor0 = g_StarfieldState.kartographerGridLabelColor[0];
        params->LabelColor1 = g_StarfieldState.kartographerGridLabelColor[1];
        params->LabelColor2 = g_StarfieldState.kartographerGridLabelColor[2];
        params->LabelColor3 = g_StarfieldState.kartographerGridLabelColor[3];
        params->LabelColor4 = g_StarfieldState.kartographerGridLabelColor[4];
        params->LabelColor5 = g_StarfieldState.kartographerGridLabelColor[5];
        params->LabelColor6 = g_StarfieldState.kartographerGridLabelColor[6];
        params->LabelColor7 = g_StarfieldState.kartographerGridLabelColor[7];
        params->LabelColor8 = g_StarfieldState.kartographerGridLabelColor[8];
        params->LabelColor9 = g_StarfieldState.kartographerGridLabelColor[9];
        params->LabelColor10 = g_StarfieldState.kartographerGridLabelColor[10];
        params->LabelColor11 = g_StarfieldState.kartographerGridLabelColor[11];
        
        // Vessel target parameters (separate from star selector)
        params->VesselTargetEnabled = g_StarfieldState.kartographerVesselTargetEnabled;
        params->VesselTargetHash = g_StarfieldState.kartographerVesselTargetHash;
        params->VesselTargetCircleCenterX = g_StarfieldState.kartographerVesselTargetCircleCenterX;
        params->VesselTargetCircleCenterY = g_StarfieldState.kartographerVesselTargetCircleCenterY;
        params->VesselTargetCircleT = g_StarfieldState.kartographerVesselTargetCircleT;
        params->VesselTargetCircleIntensity = g_StarfieldState.kartographerVesselTargetCircleIntensity;
        params->VesselTargetCircleThickness = g_StarfieldState.kartographerVesselTargetCircleThickness;
        params->VesselTargetCircleRadius = g_StarfieldState.kartographerVesselTargetCircleRadius;
        params->VesselTargetBoxTopLeftX = g_StarfieldState.kartographerVesselTargetBoxTopLeftX;
        params->VesselTargetBoxTopLeftY = g_StarfieldState.kartographerVesselTargetBoxTopLeftY;
        params->VesselTargetBoxSizeX = g_StarfieldState.kartographerVesselTargetBoxSizeX;
        params->VesselTargetBoxSizeY = g_StarfieldState.kartographerVesselTargetBoxSizeY;
        params->VesselTargetBoxThickness = g_StarfieldState.kartographerVesselTargetBoxThickness;
        params->VesselTargetTextOriginX = g_StarfieldState.kartographerVesselTargetTextOriginX;
        params->VesselTargetTextOriginY = g_StarfieldState.kartographerVesselTargetTextOriginY;
        params->VesselTargetTextAreaSizeX = g_StarfieldState.kartographerVesselTargetTextAreaSizeX;
        params->VesselTargetTextAreaSizeY = g_StarfieldState.kartographerVesselTargetTextAreaSizeY;
        params->VesselTargetTextT = g_StarfieldState.kartographerVesselTargetTextT;
        params->AnimatedLabelIntensity = g_StarfieldState.kartographerAnimatedLabelIntensity;
        
        // Navball icon parameters
        params->NavballEnabledMask = g_StarfieldState.kartographerNavballEnabledMask;
        params->NavballOffscreenMode = g_StarfieldState.kartographerNavballOffscreenMode;
        params->NavballIconSize = g_StarfieldState.kartographerNavballIconSize;
        params->NavballIconThickness = g_StarfieldState.kartographerNavballIconThickness;
        params->NavballMinIntensity = g_StarfieldState.kartographerNavballMinIntensity;
        params->NavballMaxAngle = g_StarfieldState.kartographerNavballMaxAngle;
        params->NavballHysteresisMargin = g_StarfieldState.kartographerNavballHysteresisMargin;
        params->_padNavball1 = 0.0f;
        
        // Navball icon 0: Prograde
        params->NavballIcon0_X = g_StarfieldState.kartographerNavballIconPosX[0];
        params->NavballIcon0_Y = g_StarfieldState.kartographerNavballIconPosY[0];
        params->NavballIcon0_Intensity = g_StarfieldState.kartographerNavballIconIntensity[0];
        params->NavballIcon0_Color = g_StarfieldState.kartographerNavballIconColor[0];
        
        // Navball icon 1: Retrograde
        params->NavballIcon1_X = g_StarfieldState.kartographerNavballIconPosX[1];
        params->NavballIcon1_Y = g_StarfieldState.kartographerNavballIconPosY[1];
        params->NavballIcon1_Intensity = g_StarfieldState.kartographerNavballIconIntensity[1];
        params->NavballIcon1_Color = g_StarfieldState.kartographerNavballIconColor[1];
        
        // Navball icon 2: Normal
        params->NavballIcon2_X = g_StarfieldState.kartographerNavballIconPosX[2];
        params->NavballIcon2_Y = g_StarfieldState.kartographerNavballIconPosY[2];
        params->NavballIcon2_Intensity = g_StarfieldState.kartographerNavballIconIntensity[2];
        params->NavballIcon2_Color = g_StarfieldState.kartographerNavballIconColor[2];
        
        // Navball icon 3: AntiNormal
        params->NavballIcon3_X = g_StarfieldState.kartographerNavballIconPosX[3];
        params->NavballIcon3_Y = g_StarfieldState.kartographerNavballIconPosY[3];
        params->NavballIcon3_Intensity = g_StarfieldState.kartographerNavballIconIntensity[3];
        params->NavballIcon3_Color = g_StarfieldState.kartographerNavballIconColor[3];
        
        // Navball icon 4: Radial In
        params->NavballIcon4_X = g_StarfieldState.kartographerNavballIconPosX[4];
        params->NavballIcon4_Y = g_StarfieldState.kartographerNavballIconPosY[4];
        params->NavballIcon4_Intensity = g_StarfieldState.kartographerNavballIconIntensity[4];
        params->NavballIcon4_Color = g_StarfieldState.kartographerNavballIconColor[4];
        
        // Navball icon 5: Radial Out
        params->NavballIcon5_X = g_StarfieldState.kartographerNavballIconPosX[5];
        params->NavballIcon5_Y = g_StarfieldState.kartographerNavballIconPosY[5];
        params->NavballIcon5_Intensity = g_StarfieldState.kartographerNavballIconIntensity[5];
        params->NavballIcon5_Color = g_StarfieldState.kartographerNavballIconColor[5];
        
        // Navball icon 6: Maneuver
        params->NavballIcon6_X = g_StarfieldState.kartographerNavballIconPosX[6];
        params->NavballIcon6_Y = g_StarfieldState.kartographerNavballIconPosY[6];
        params->NavballIcon6_Intensity = g_StarfieldState.kartographerNavballIconIntensity[6];
        params->NavballIcon6_Color = g_StarfieldState.kartographerNavballIconColor[6];
        
        // Pointing icon
        params->PointingIconEnabled = g_StarfieldState.kartographerPointingIconEnabled;
        params->PointingIconPosX = g_StarfieldState.kartographerPointingIconPosX;
        params->PointingIconPosY = g_StarfieldState.kartographerPointingIconPosY;
        params->PointingIconRotation = g_StarfieldState.kartographerPointingIconRotation;
        params->PointingIconIntensity = g_StarfieldState.kartographerPointingIconIntensity;
        params->PointingIconSize = g_StarfieldState.kartographerPointingIconSize;
        params->PointingIconColor = g_StarfieldState.kartographerPointingIconColor;
        
        // Maneuver text
        params->ManeuverTextEnabled = g_StarfieldState.kartographerManeuverTextEnabled;
        params->ManeuverTextOriginX = g_StarfieldState.kartographerManeuverTextOriginX;
        params->ManeuverTextOriginY = g_StarfieldState.kartographerManeuverTextOriginY;
        params->ManeuverTextWidth = g_StarfieldState.kartographerManeuverTextWidth;
        params->ManeuverTextHeight = g_StarfieldState.kartographerManeuverTextHeight;
        params->ManeuverTextIntensity = g_StarfieldState.kartographerManeuverTextIntensity;
        
        context->Unmap(g_StarfieldState.kartographerCB, 0);
    }
}

void CR_StarfieldSetKartographerParams(const KartographerParamsNative* params)
{
    if (!params) return;
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    g_StarfieldState.kartographerGridIntensity = params->GridIntensity;
    g_StarfieldState.kartographerGridThickness = params->GridThickness;
    g_StarfieldState.kartographerCAStrength = params->ChromaticAberrationStrength;
    g_StarfieldState.kartographerVignetteStrength = params->VignetteStrength;
    g_StarfieldState.kartographerVignetteStart = params->VignetteStart;
    g_StarfieldState.kartographerVignetteEnd = params->VignetteEnd;
    g_StarfieldState.kartographerPreRotationYaw = params->PreRotationYaw;
    g_StarfieldState.kartographerPreRotationPitch = params->PreRotationPitch;
    g_StarfieldState.kartographerGridSizePreset = params->GridSizePreset;
    g_StarfieldState.kartographerGridColor = params->GridColorIndex;
    g_StarfieldState.kartographerDebugShapesEnabled = params->DebugShapesEnabled;
    g_StarfieldState.kartographerFocalLength = params->FocalLength;
    g_StarfieldState.kartographerDebugBoxTopLeftX = params->DebugBoxTopLeftX;
    g_StarfieldState.kartographerDebugBoxTopLeftY = params->DebugBoxTopLeftY;
    g_StarfieldState.kartographerDebugBoxSizeX = params->DebugBoxSizeX;
    g_StarfieldState.kartographerDebugBoxSizeY = params->DebugBoxSizeY;
    g_StarfieldState.kartographerDebugBoxThickness = params->DebugBoxThickness;
    
    // Selection circle parameters (cached for CB update)
    g_StarfieldState.kartographerSelectionCircleEnabled = params->SelectionCircleEnabled;
    g_StarfieldState.kartographerStarHash = params->SelectionStarHash;
    g_StarfieldState.kartographerSelectionCircleCenterX = params->SelectionCircleCenterX;
    g_StarfieldState.kartographerSelectionCircleCenterY = params->SelectionCircleCenterY;
    g_StarfieldState.kartographerSelectionCircleT = params->SelectionCircleT;
    g_StarfieldState.kartographerSelectionCircleIntensity = params->SelectionCircleIntensity;
    g_StarfieldState.kartographerSelectionCircleThickness = params->SelectionCircleThickness;
    g_StarfieldState.kartographerSelectionCircleRadius = params->SelectionCircleRadius;
    
    // Text parameters (cached for CB update)
    g_StarfieldState.kartographerTextOriginX = params->TextOriginX;
    g_StarfieldState.kartographerTextOriginY = params->TextOriginY;
    g_StarfieldState.kartographerTextAreaSizeX = params->TextAreaSizeX;
    g_StarfieldState.kartographerTextAreaSizeY = params->TextAreaSizeY;
    g_StarfieldState.kartographerSelectionTextT = params->SelectionTextT;
    
    // Vessel target parameters (separate from star selector)
    g_StarfieldState.kartographerVesselTargetEnabled = params->VesselTargetEnabled;
    g_StarfieldState.kartographerVesselTargetHash = params->VesselTargetHash;
    g_StarfieldState.kartographerVesselTargetCircleCenterX = params->VesselTargetCircleCenterX;
    g_StarfieldState.kartographerVesselTargetCircleCenterY = params->VesselTargetCircleCenterY;
    g_StarfieldState.kartographerVesselTargetCircleT = params->VesselTargetCircleT;
    g_StarfieldState.kartographerVesselTargetCircleIntensity = params->VesselTargetCircleIntensity;
    g_StarfieldState.kartographerVesselTargetCircleThickness = params->VesselTargetCircleThickness;
    g_StarfieldState.kartographerVesselTargetCircleRadius = params->VesselTargetCircleRadius;
    g_StarfieldState.kartographerVesselTargetBoxTopLeftX = params->VesselTargetBoxTopLeftX;
    g_StarfieldState.kartographerVesselTargetBoxTopLeftY = params->VesselTargetBoxTopLeftY;
    g_StarfieldState.kartographerVesselTargetBoxSizeX = params->VesselTargetBoxSizeX;
    g_StarfieldState.kartographerVesselTargetBoxSizeY = params->VesselTargetBoxSizeY;
    g_StarfieldState.kartographerVesselTargetBoxThickness = params->VesselTargetBoxThickness;
    g_StarfieldState.kartographerVesselTargetTextOriginX = params->VesselTargetTextOriginX;
    g_StarfieldState.kartographerVesselTargetTextOriginY = params->VesselTargetTextOriginY;
    g_StarfieldState.kartographerVesselTargetTextAreaSizeX = params->VesselTargetTextAreaSizeX;
    g_StarfieldState.kartographerVesselTargetTextAreaSizeY = params->VesselTargetTextAreaSizeY;
    g_StarfieldState.kartographerVesselTargetTextT = params->VesselTargetTextT;
    g_StarfieldState.kartographerAnimatedLabelIntensity = params->AnimatedLabelIntensity;
    
    // Copy all 12 grid labels from params to state (extract from float4)
    g_StarfieldState.kartographerGridLabelEnabledMask = params->GridLabelEnabledMask;
    g_StarfieldState.kartographerGridLabelDebugMask = params->GridLabelDebugMask;
    g_StarfieldState.kartographerGridLabelPosX[0] = params->GridLabel0_PosTangentX.x;
    g_StarfieldState.kartographerGridLabelPosY[0] = params->GridLabel0_PosTangentX.y;
    g_StarfieldState.kartographerGridLabelPosZ[0] = params->GridLabel0_PosTangentX.z;
    g_StarfieldState.kartographerGridLabelWorldSizeX[0] = params->GridLabel0_PosTangentX.w;
    g_StarfieldState.kartographerGridLabelTangentX[0] = params->GridLabel0_TangentY.x;
    g_StarfieldState.kartographerGridLabelTangentY[0] = params->GridLabel0_TangentY.y;
    g_StarfieldState.kartographerGridLabelTangentZ[0] = params->GridLabel0_TangentY.z;
    g_StarfieldState.kartographerGridLabelWorldSizeY[0] = params->GridLabel0_TangentY.w;
    
    g_StarfieldState.kartographerGridLabelPosX[1] = params->GridLabel1_PosTangentX.x;
    g_StarfieldState.kartographerGridLabelPosY[1] = params->GridLabel1_PosTangentX.y;
    g_StarfieldState.kartographerGridLabelPosZ[1] = params->GridLabel1_PosTangentX.z;
    g_StarfieldState.kartographerGridLabelWorldSizeX[1] = params->GridLabel1_PosTangentX.w;
    g_StarfieldState.kartographerGridLabelTangentX[1] = params->GridLabel1_TangentY.x;
    g_StarfieldState.kartographerGridLabelTangentY[1] = params->GridLabel1_TangentY.y;
    g_StarfieldState.kartographerGridLabelTangentZ[1] = params->GridLabel1_TangentY.z;
    g_StarfieldState.kartographerGridLabelWorldSizeY[1] = params->GridLabel1_TangentY.w;
    
    g_StarfieldState.kartographerGridLabelPosX[2] = params->GridLabel2_PosTangentX.x;
    g_StarfieldState.kartographerGridLabelPosY[2] = params->GridLabel2_PosTangentX.y;
    g_StarfieldState.kartographerGridLabelPosZ[2] = params->GridLabel2_PosTangentX.z;
    g_StarfieldState.kartographerGridLabelWorldSizeX[2] = params->GridLabel2_PosTangentX.w;
    g_StarfieldState.kartographerGridLabelTangentX[2] = params->GridLabel2_TangentY.x;
    g_StarfieldState.kartographerGridLabelTangentY[2] = params->GridLabel2_TangentY.y;
    g_StarfieldState.kartographerGridLabelTangentZ[2] = params->GridLabel2_TangentY.z;
    g_StarfieldState.kartographerGridLabelWorldSizeY[2] = params->GridLabel2_TangentY.w;
    
    g_StarfieldState.kartographerGridLabelPosX[3] = params->GridLabel3_PosTangentX.x;
    g_StarfieldState.kartographerGridLabelPosY[3] = params->GridLabel3_PosTangentX.y;
    g_StarfieldState.kartographerGridLabelPosZ[3] = params->GridLabel3_PosTangentX.z;
    g_StarfieldState.kartographerGridLabelWorldSizeX[3] = params->GridLabel3_PosTangentX.w;
    g_StarfieldState.kartographerGridLabelTangentX[3] = params->GridLabel3_TangentY.x;
    g_StarfieldState.kartographerGridLabelTangentY[3] = params->GridLabel3_TangentY.y;
    g_StarfieldState.kartographerGridLabelTangentZ[3] = params->GridLabel3_TangentY.z;
    g_StarfieldState.kartographerGridLabelWorldSizeY[3] = params->GridLabel3_TangentY.w;
    
    g_StarfieldState.kartographerGridLabelPosX[4] = params->GridLabel4_PosTangentX.x;
    g_StarfieldState.kartographerGridLabelPosY[4] = params->GridLabel4_PosTangentX.y;
    g_StarfieldState.kartographerGridLabelPosZ[4] = params->GridLabel4_PosTangentX.z;
    g_StarfieldState.kartographerGridLabelWorldSizeX[4] = params->GridLabel4_PosTangentX.w;
    g_StarfieldState.kartographerGridLabelTangentX[4] = params->GridLabel4_TangentY.x;
    g_StarfieldState.kartographerGridLabelTangentY[4] = params->GridLabel4_TangentY.y;
    g_StarfieldState.kartographerGridLabelTangentZ[4] = params->GridLabel4_TangentY.z;
    g_StarfieldState.kartographerGridLabelWorldSizeY[4] = params->GridLabel4_TangentY.w;
    
    g_StarfieldState.kartographerGridLabelPosX[5] = params->GridLabel5_PosTangentX.x;
    g_StarfieldState.kartographerGridLabelPosY[5] = params->GridLabel5_PosTangentX.y;
    g_StarfieldState.kartographerGridLabelPosZ[5] = params->GridLabel5_PosTangentX.z;
    g_StarfieldState.kartographerGridLabelWorldSizeX[5] = params->GridLabel5_PosTangentX.w;
    g_StarfieldState.kartographerGridLabelTangentX[5] = params->GridLabel5_TangentY.x;
    g_StarfieldState.kartographerGridLabelTangentY[5] = params->GridLabel5_TangentY.y;
    g_StarfieldState.kartographerGridLabelTangentZ[5] = params->GridLabel5_TangentY.z;
    g_StarfieldState.kartographerGridLabelWorldSizeY[5] = params->GridLabel5_TangentY.w;
    
    g_StarfieldState.kartographerGridLabelPosX[6] = params->GridLabel6_PosTangentX.x;
    g_StarfieldState.kartographerGridLabelPosY[6] = params->GridLabel6_PosTangentX.y;
    g_StarfieldState.kartographerGridLabelPosZ[6] = params->GridLabel6_PosTangentX.z;
    g_StarfieldState.kartographerGridLabelWorldSizeX[6] = params->GridLabel6_PosTangentX.w;
    g_StarfieldState.kartographerGridLabelTangentX[6] = params->GridLabel6_TangentY.x;
    g_StarfieldState.kartographerGridLabelTangentY[6] = params->GridLabel6_TangentY.y;
    g_StarfieldState.kartographerGridLabelTangentZ[6] = params->GridLabel6_TangentY.z;
    g_StarfieldState.kartographerGridLabelWorldSizeY[6] = params->GridLabel6_TangentY.w;
    
    g_StarfieldState.kartographerGridLabelPosX[7] = params->GridLabel7_PosTangentX.x;
    g_StarfieldState.kartographerGridLabelPosY[7] = params->GridLabel7_PosTangentX.y;
    g_StarfieldState.kartographerGridLabelPosZ[7] = params->GridLabel7_PosTangentX.z;
    g_StarfieldState.kartographerGridLabelWorldSizeX[7] = params->GridLabel7_PosTangentX.w;
    g_StarfieldState.kartographerGridLabelTangentX[7] = params->GridLabel7_TangentY.x;
    g_StarfieldState.kartographerGridLabelTangentY[7] = params->GridLabel7_TangentY.y;
    g_StarfieldState.kartographerGridLabelTangentZ[7] = params->GridLabel7_TangentY.z;
    g_StarfieldState.kartographerGridLabelWorldSizeY[7] = params->GridLabel7_TangentY.w;
    
    g_StarfieldState.kartographerGridLabelPosX[8] = params->GridLabel8_PosTangentX.x;
    g_StarfieldState.kartographerGridLabelPosY[8] = params->GridLabel8_PosTangentX.y;
    g_StarfieldState.kartographerGridLabelPosZ[8] = params->GridLabel8_PosTangentX.z;
    g_StarfieldState.kartographerGridLabelWorldSizeX[8] = params->GridLabel8_PosTangentX.w;
    g_StarfieldState.kartographerGridLabelTangentX[8] = params->GridLabel8_TangentY.x;
    g_StarfieldState.kartographerGridLabelTangentY[8] = params->GridLabel8_TangentY.y;
    g_StarfieldState.kartographerGridLabelTangentZ[8] = params->GridLabel8_TangentY.z;
    g_StarfieldState.kartographerGridLabelWorldSizeY[8] = params->GridLabel8_TangentY.w;
    
    g_StarfieldState.kartographerGridLabelPosX[9] = params->GridLabel9_PosTangentX.x;
    g_StarfieldState.kartographerGridLabelPosY[9] = params->GridLabel9_PosTangentX.y;
    g_StarfieldState.kartographerGridLabelPosZ[9] = params->GridLabel9_PosTangentX.z;
    g_StarfieldState.kartographerGridLabelWorldSizeX[9] = params->GridLabel9_PosTangentX.w;
    g_StarfieldState.kartographerGridLabelTangentX[9] = params->GridLabel9_TangentY.x;
    g_StarfieldState.kartographerGridLabelTangentY[9] = params->GridLabel9_TangentY.y;
    g_StarfieldState.kartographerGridLabelTangentZ[9] = params->GridLabel9_TangentY.z;
    g_StarfieldState.kartographerGridLabelWorldSizeY[9] = params->GridLabel9_TangentY.w;
    
    g_StarfieldState.kartographerGridLabelPosX[10] = params->GridLabel10_PosTangentX.x;
    g_StarfieldState.kartographerGridLabelPosY[10] = params->GridLabel10_PosTangentX.y;
    g_StarfieldState.kartographerGridLabelPosZ[10] = params->GridLabel10_PosTangentX.z;
    g_StarfieldState.kartographerGridLabelWorldSizeX[10] = params->GridLabel10_PosTangentX.w;
    g_StarfieldState.kartographerGridLabelTangentX[10] = params->GridLabel10_TangentY.x;
    g_StarfieldState.kartographerGridLabelTangentY[10] = params->GridLabel10_TangentY.y;
    g_StarfieldState.kartographerGridLabelTangentZ[10] = params->GridLabel10_TangentY.z;
    g_StarfieldState.kartographerGridLabelWorldSizeY[10] = params->GridLabel10_TangentY.w;
    
    g_StarfieldState.kartographerGridLabelPosX[11] = params->GridLabel11_PosTangentX.x;
    g_StarfieldState.kartographerGridLabelPosY[11] = params->GridLabel11_PosTangentX.y;
    g_StarfieldState.kartographerGridLabelPosZ[11] = params->GridLabel11_PosTangentX.z;
    g_StarfieldState.kartographerGridLabelWorldSizeX[11] = params->GridLabel11_PosTangentX.w;
    g_StarfieldState.kartographerGridLabelTangentX[11] = params->GridLabel11_TangentY.x;
    g_StarfieldState.kartographerGridLabelTangentY[11] = params->GridLabel11_TangentY.y;
    g_StarfieldState.kartographerGridLabelTangentZ[11] = params->GridLabel11_TangentY.z;
    g_StarfieldState.kartographerGridLabelWorldSizeY[11] = params->GridLabel11_TangentY.w;
    
    g_StarfieldState.kartographerGridLabelIntensity[0] = params->LabelIntensity0;
    g_StarfieldState.kartographerGridLabelIntensity[1] = params->LabelIntensity1;
    g_StarfieldState.kartographerGridLabelIntensity[2] = params->LabelIntensity2;
    g_StarfieldState.kartographerGridLabelIntensity[3] = params->LabelIntensity3;
    g_StarfieldState.kartographerGridLabelIntensity[4] = params->LabelIntensity4;
    g_StarfieldState.kartographerGridLabelIntensity[5] = params->LabelIntensity5;
    g_StarfieldState.kartographerGridLabelIntensity[6] = params->LabelIntensity6;
    g_StarfieldState.kartographerGridLabelIntensity[7] = params->LabelIntensity7;
    g_StarfieldState.kartographerGridLabelIntensity[8] = params->LabelIntensity8;
    g_StarfieldState.kartographerGridLabelIntensity[9] = params->LabelIntensity9;
    g_StarfieldState.kartographerGridLabelIntensity[10] = params->LabelIntensity10;
    g_StarfieldState.kartographerGridLabelIntensity[11] = params->LabelIntensity11;
    g_StarfieldState.kartographerGridLabelColor[0] = params->LabelColor0;
    g_StarfieldState.kartographerGridLabelColor[1] = params->LabelColor1;
    g_StarfieldState.kartographerGridLabelColor[2] = params->LabelColor2;
    g_StarfieldState.kartographerGridLabelColor[3] = params->LabelColor3;
    g_StarfieldState.kartographerGridLabelColor[4] = params->LabelColor4;
    g_StarfieldState.kartographerGridLabelColor[5] = params->LabelColor5;
    g_StarfieldState.kartographerGridLabelColor[6] = params->LabelColor6;
    g_StarfieldState.kartographerGridLabelColor[7] = params->LabelColor7;
    g_StarfieldState.kartographerGridLabelColor[8] = params->LabelColor8;
    g_StarfieldState.kartographerGridLabelColor[9] = params->LabelColor9;
    g_StarfieldState.kartographerGridLabelColor[10] = params->LabelColor10;
    g_StarfieldState.kartographerGridLabelColor[11] = params->LabelColor11;
    
    // Navball icon parameters
    g_StarfieldState.kartographerNavballEnabledMask = params->NavballEnabledMask;
    g_StarfieldState.kartographerNavballOffscreenMode = params->NavballOffscreenMode;
    g_StarfieldState.kartographerNavballIconSize = params->NavballIconSize;
    g_StarfieldState.kartographerNavballIconThickness = params->NavballIconThickness;
    g_StarfieldState.kartographerNavballMinIntensity = params->NavballMinIntensity;
    g_StarfieldState.kartographerNavballMaxAngle = params->NavballMaxAngle;
    g_StarfieldState.kartographerNavballHysteresisMargin = params->NavballHysteresisMargin;
    
    // Navball icon 0: Prograde
    g_StarfieldState.kartographerNavballIconPosX[0] = params->NavballIcon0_X;
    g_StarfieldState.kartographerNavballIconPosY[0] = params->NavballIcon0_Y;
    g_StarfieldState.kartographerNavballIconIntensity[0] = params->NavballIcon0_Intensity;
    g_StarfieldState.kartographerNavballIconColor[0] = params->NavballIcon0_Color;
    
    // Navball icon 1: Retrograde
    g_StarfieldState.kartographerNavballIconPosX[1] = params->NavballIcon1_X;
    g_StarfieldState.kartographerNavballIconPosY[1] = params->NavballIcon1_Y;
    g_StarfieldState.kartographerNavballIconIntensity[1] = params->NavballIcon1_Intensity;
    g_StarfieldState.kartographerNavballIconColor[1] = params->NavballIcon1_Color;
    
    // Navball icon 2: Normal
    g_StarfieldState.kartographerNavballIconPosX[2] = params->NavballIcon2_X;
    g_StarfieldState.kartographerNavballIconPosY[2] = params->NavballIcon2_Y;
    g_StarfieldState.kartographerNavballIconIntensity[2] = params->NavballIcon2_Intensity;
    g_StarfieldState.kartographerNavballIconColor[2] = params->NavballIcon2_Color;
    
    // Navball icon 3: AntiNormal
    g_StarfieldState.kartographerNavballIconPosX[3] = params->NavballIcon3_X;
    g_StarfieldState.kartographerNavballIconPosY[3] = params->NavballIcon3_Y;
    g_StarfieldState.kartographerNavballIconIntensity[3] = params->NavballIcon3_Intensity;
    g_StarfieldState.kartographerNavballIconColor[3] = params->NavballIcon3_Color;
    
    // Navball icon 4: Radial In
    g_StarfieldState.kartographerNavballIconPosX[4] = params->NavballIcon4_X;
    g_StarfieldState.kartographerNavballIconPosY[4] = params->NavballIcon4_Y;
    g_StarfieldState.kartographerNavballIconIntensity[4] = params->NavballIcon4_Intensity;
    g_StarfieldState.kartographerNavballIconColor[4] = params->NavballIcon4_Color;
    
    // Navball icon 5: Radial Out
    g_StarfieldState.kartographerNavballIconPosX[5] = params->NavballIcon5_X;
    g_StarfieldState.kartographerNavballIconPosY[5] = params->NavballIcon5_Y;
    g_StarfieldState.kartographerNavballIconIntensity[5] = params->NavballIcon5_Intensity;
    g_StarfieldState.kartographerNavballIconColor[5] = params->NavballIcon5_Color;
    
    // Navball icon 6: Maneuver
    g_StarfieldState.kartographerNavballIconPosX[6] = params->NavballIcon6_X;
    g_StarfieldState.kartographerNavballIconPosY[6] = params->NavballIcon6_Y;
    g_StarfieldState.kartographerNavballIconIntensity[6] = params->NavballIcon6_Intensity;
    g_StarfieldState.kartographerNavballIconColor[6] = params->NavballIcon6_Color;
    
    // Pointing icon
    g_StarfieldState.kartographerPointingIconEnabled = params->PointingIconEnabled;
    g_StarfieldState.kartographerPointingIconPosX = params->PointingIconPosX;
    g_StarfieldState.kartographerPointingIconPosY = params->PointingIconPosY;
    g_StarfieldState.kartographerPointingIconRotation = params->PointingIconRotation;
    g_StarfieldState.kartographerPointingIconIntensity = params->PointingIconIntensity;
    g_StarfieldState.kartographerPointingIconSize = params->PointingIconSize;
    g_StarfieldState.kartographerPointingIconColor = params->PointingIconColor;
    
    // Maneuver text
    g_StarfieldState.kartographerManeuverTextEnabled = params->ManeuverTextEnabled;
    g_StarfieldState.kartographerManeuverTextOriginX = params->ManeuverTextOriginX;
    g_StarfieldState.kartographerManeuverTextOriginY = params->ManeuverTextOriginY;
    g_StarfieldState.kartographerManeuverTextWidth = params->ManeuverTextWidth;
    g_StarfieldState.kartographerManeuverTextHeight = params->ManeuverTextHeight;
    g_StarfieldState.kartographerManeuverTextIntensity = params->ManeuverTextIntensity;
}

// ============================================================================
// Text Rendering System Exports (Phase 4)
// ============================================================================

struct TextParams {
    int GlyphCount;
    float OutputSizeX;
    float OutputSizeY;
    float Pad;
};

extern "C" __declspec(dllexport)
void CR_TextDispatch(
    void* textSystem,
    ID3D11Texture2D* outputTexture,
    int glyphCount,
    int outputWidth,
    int outputHeight)
{
    // Early exit checks (no logging for normal calls)
    if (!textSystem || !outputTexture) {
        return;
    }
    
    CinematicShaders::TextSystem* ts = static_cast<CinematicShaders::TextSystem*>(textSystem);
    
    // Ensure glyph buffer is created and populated
    ID3D11Buffer* glyphBuffer = ts->GetOrCreateGlyphBuffer();
    if (!glyphBuffer) {
        return;
    }
    
    ID3D11ShaderResourceView* glyphSRV = ts->GetGlyphBufferSRV();
    if (!glyphSRV) {
        return;
    }
    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    if (!g_StarfieldState.device) {
        return;
    }
    
    ID3D11DeviceContext* context = nullptr;
    g_StarfieldState.device->GetImmediateContext(&context);
    if (!context) {
        return;
    }
    
    // Create compute shader if not already created
    if (!g_StarfieldState.textCS) {
        HRESULT hr = g_StarfieldState.device->CreateComputeShader(
            g_KartographerTextCS, sizeof(g_KartographerTextCS), nullptr, &g_StarfieldState.textCS);
        if (FAILED(hr)) {
            context->Release();
            return;
        }
    }
    
    // Create constant buffer if not already created
    if (!g_StarfieldState.textCB) {
        D3D11_BUFFER_DESC cbDesc = {};
        cbDesc.ByteWidth = sizeof(TextParams);
        cbDesc.Usage = D3D11_USAGE_DYNAMIC;
        cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        g_StarfieldState.device->CreateBuffer(&cbDesc, nullptr, &g_StarfieldState.textCB);
    }
    
    // Create sampler if not already created
    if (!g_StarfieldState.textSampler) {
        D3D11_SAMPLER_DESC sampDesc = {};
        sampDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
        sampDesc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampDesc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampDesc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        g_StarfieldState.device->CreateSamplerState(&sampDesc, &g_StarfieldState.textSampler);
    }
    
    // Create UAV for output texture
    ID3D11UnorderedAccessView* outputUAV = nullptr;
    D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
    uavDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    uavDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2D;
    HRESULT hr = g_StarfieldState.device->CreateUnorderedAccessView(outputTexture, &uavDesc, &outputUAV);
    if (FAILED(hr)) {
        context->Release();
        return;
    }
    
    // Get atlas texture from text system
    ID3D11ShaderResourceView* atlasSRV = ts->GetAtlasSRV();
    if (!atlasSRV) {
        outputUAV->Release();
        context->Release();
        return;
    }
    
    // Update constant buffer
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(context->Map(g_StarfieldState.textCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        TextParams* params = (TextParams*)mapped.pData;
        params->GlyphCount = glyphCount;
        params->OutputSizeX = (float)outputWidth;
        params->OutputSizeY = (float)outputHeight;
        params->Pad = 0.0f;
        context->Unmap(g_StarfieldState.textCB, 0);
    }
    
    // Clear output texture
    UINT clearColor[4] = {0, 0, 0, 0};
    context->ClearUnorderedAccessViewUint(outputUAV, clearColor);
    
    // Set compute shader and resources
    context->CSSetShader(g_StarfieldState.textCS, nullptr, 0);
    context->CSSetConstantBuffers(0, 1, &g_StarfieldState.textCB);
    ID3D11ShaderResourceView* srvs[2] = {atlasSRV, glyphSRV}; // t0 = atlas, t1 = glyph buffer
    context->CSSetShaderResources(0, 2, srvs);
    context->CSSetSamplers(0, 1, &g_StarfieldState.textSampler);
    
    ID3D11UnorderedAccessView* uavs[1] = {outputUAV};
    context->CSSetUnorderedAccessViews(0, 1, uavs, nullptr);
    
    // Dispatch compute shader
    UINT dispatchX = (outputWidth + 15) / 16;
    UINT dispatchY = (outputHeight + 15) / 16;
    context->Dispatch(dispatchX, dispatchY, 1);
    
    // Unbind resources
    ID3D11UnorderedAccessView* nullUAV[1] = {nullptr};
    ID3D11ShaderResourceView* nullSRV[2] = {nullptr, nullptr};
    context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
    context->CSSetShaderResources(0, 2, nullSRV);
    context->CSSetShader(nullptr, nullptr, 0);
    
    outputUAV->Release();
    context->Release();
}

extern "C" __declspec(dllexport)
void CR_TextDispatchEx(
    void* textSystem,
    ID3D11Texture2D* outputTexture,
    int glyphCount,
    int outputWidth,
    int outputHeight,
    int clearOutput)
{
    // Early exit checks (no logging for normal calls)
    if (!textSystem || !outputTexture) {
        return;
    }
    
    CinematicShaders::TextSystem* ts = static_cast<CinematicShaders::TextSystem*>(textSystem);
    
    // Ensure glyph buffer is created and populated
    ID3D11Buffer* glyphBuffer = ts->GetOrCreateGlyphBuffer();
    if (!glyphBuffer) {
        return;
    }
    
    ID3D11ShaderResourceView* glyphSRV = ts->GetGlyphBufferSRV();
    if (!glyphSRV) {
        return;
    }
    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    if (!g_StarfieldState.device) {
        return;
    }
    
    ID3D11DeviceContext* context = nullptr;
    g_StarfieldState.device->GetImmediateContext(&context);
    if (!context) {
        return;
    }
    
    // Create compute shader if not already created
    if (!g_StarfieldState.textCS) {
        HRESULT hr = g_StarfieldState.device->CreateComputeShader(
            g_KartographerTextCS, sizeof(g_KartographerTextCS), nullptr, &g_StarfieldState.textCS);
        if (FAILED(hr)) {
            context->Release();
            return;
        }
    }
    
    // Create constant buffer if not already created
    if (!g_StarfieldState.textCB) {
        D3D11_BUFFER_DESC cbDesc = {};
        cbDesc.ByteWidth = sizeof(TextParams);
        cbDesc.Usage = D3D11_USAGE_DYNAMIC;
        cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        g_StarfieldState.device->CreateBuffer(&cbDesc, nullptr, &g_StarfieldState.textCB);
    }
    
    // Create sampler if not already created
    if (!g_StarfieldState.textSampler) {
        D3D11_SAMPLER_DESC sampDesc = {};
        sampDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
        sampDesc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampDesc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampDesc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        g_StarfieldState.device->CreateSamplerState(&sampDesc, &g_StarfieldState.textSampler);
    }
    
    // Create UAV for output texture
    ID3D11UnorderedAccessView* outputUAV = nullptr;
    D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
    uavDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    uavDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2D;
    HRESULT hr = g_StarfieldState.device->CreateUnorderedAccessView(outputTexture, &uavDesc, &outputUAV);
    if (FAILED(hr)) {
        context->Release();
        return;
    }
    
    // Get atlas texture from text system
    ID3D11ShaderResourceView* atlasSRV = ts->GetAtlasSRV();
    if (!atlasSRV) {
        outputUAV->Release();
        context->Release();
        return;
    }
    
    // Update constant buffer
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(context->Map(g_StarfieldState.textCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        TextParams* params = (TextParams*)mapped.pData;
        params->GlyphCount = glyphCount;
        params->OutputSizeX = (float)outputWidth;
        params->OutputSizeY = (float)outputHeight;
        params->Pad = 0.0f;
        context->Unmap(g_StarfieldState.textCB, 0);
    }
    
    // Clear output texture only if requested
    if (clearOutput != 0) {
        UINT clearColor[4] = {0, 0, 0, 0};
        context->ClearUnorderedAccessViewUint(outputUAV, clearColor);
    }
    
    // Set compute shader and resources
    context->CSSetShader(g_StarfieldState.textCS, nullptr, 0);
    context->CSSetConstantBuffers(0, 1, &g_StarfieldState.textCB);
    ID3D11ShaderResourceView* srvs[2] = {atlasSRV, glyphSRV}; // t0 = atlas, t1 = glyph buffer
    context->CSSetShaderResources(0, 2, srvs);
    context->CSSetSamplers(0, 1, &g_StarfieldState.textSampler);
    
    ID3D11UnorderedAccessView* uavs[1] = {outputUAV};
    context->CSSetUnorderedAccessViews(0, 1, uavs, nullptr);
    
    // Dispatch compute shader
    UINT dispatchX = (outputWidth + 15) / 16;
    UINT dispatchY = (outputHeight + 15) / 16;
    context->Dispatch(dispatchX, dispatchY, 1);
    
    // Unbind resources
    ID3D11UnorderedAccessView* nullUAV[1] = {nullptr};
    ID3D11ShaderResourceView* nullSRV[2] = {nullptr, nullptr};
    context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
    context->CSSetShaderResources(0, 2, nullSRV);
    context->CSSetShader(nullptr, nullptr, 0);
    
    outputUAV->Release();
    context->Release();
}

extern "C" __declspec(dllexport)
void CR_SetTextTexture(ID3D11Texture2D* texture)
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    // Release old SRV if exists
    if (g_StarfieldState.textTextureSRV) {
        g_StarfieldState.textTextureSRV->Release();
        g_StarfieldState.textTextureSRV = nullptr;
    }
    
    if (texture && g_StarfieldState.device) {
        // Create SRV for the texture
        D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Texture2D.MipLevels = 1;
        g_StarfieldState.device->CreateShaderResourceView(texture, &srvDesc, &g_StarfieldState.textTextureSRV);
    }
}

extern "C" __declspec(dllexport)
void CR_SetVesselTargetTextTexture(ID3D11Texture2D* texture)
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    // Release old SRV if exists
    if (g_StarfieldState.vesselTargetTextTextureSRV) {
        g_StarfieldState.vesselTargetTextTextureSRV->Release();
        g_StarfieldState.vesselTargetTextTextureSRV = nullptr;
    }
    
    if (texture && g_StarfieldState.device) {
        // Create SRV for the texture
        D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Texture2D.MipLevels = 1;
        g_StarfieldState.device->CreateShaderResourceView(texture, &srvDesc, &g_StarfieldState.vesselTargetTextTextureSRV);
    }
}

extern "C" __declspec(dllexport)
void CR_SetGridLabelTexture(int slot, ID3D11Texture2D* texture)
{
    // Set texture for grid label slot
    
    if (slot < 0 || slot >= 12) {
        LogToFile("[GridLabel]   -> INVALID SLOT %d", slot);
        return;
    }
    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    // Update slot state
    auto& slotState = g_StarfieldState.gridLabelSlots[slot];
    
    // Release old SRV if exists
    if (slotState.textureSRV) {
        slotState.textureSRV->Release();
        slotState.textureSRV = nullptr;
    }
    
    // Create new SRV if texture provided
    if (texture && g_StarfieldState.device) {
        D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Texture2D.MipLevels = 1;
        HRESULT hr = g_StarfieldState.device->CreateShaderResourceView(texture, &srvDesc, &slotState.textureSRV);
        if (FAILED(hr)) {
            // SRV creation failed
            slotState.isActive = false;
            return;
        }
        slotState.isActive = true;
        // SRV created successfully
    } else {
        slotState.isActive = false;
        // Slot cleared
    }
}

extern "C" __declspec(dllexport)
void CR_ClearGridLabelSlot(int slot)
{
    if (slot < 0 || slot >= 12) {
        return;
    }
    
    // Clear grid label slot
    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    auto& slotState = g_StarfieldState.gridLabelSlots[slot];
    
    // Release SRV and mark inactive
    if (slotState.textureSRV) {
        slotState.textureSRV->Release();
        slotState.textureSRV = nullptr;
    }
    slotState.isActive = false;
    
    // Also clear from the legacy array for compatibility during transition
    if (g_StarfieldState.gridLabelTextureSRV[slot]) {
        g_StarfieldState.gridLabelTextureSRV[slot]->Release();
        g_StarfieldState.gridLabelTextureSRV[slot] = nullptr;
    }
}

// ============================================================================
// Navball Icon Texture Array (Phase 4c)
// ============================================================================

extern "C" __declspec(dllexport)
int CR_SetNavballIconTextures(ID3D11Texture2D* sourceTextures[7], int width, int height)
{
    LogToFile("[Navball] CR_SetNavballIconTextures called: width=%d, height=%d", width, height);
    
    if (!g_StarfieldState.device) {
        LogToFile("[Navball] Error: Device not ready");
        return -1;
    }
    if (!sourceTextures) {
        LogToFile("[Navball] Error: sourceTextures is null");
        return -2;
    }
    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    // Release existing texture array and SRV
    if (g_StarfieldState.navballIconArraySRV) {
        g_StarfieldState.navballIconArraySRV->Release();
        g_StarfieldState.navballIconArraySRV = nullptr;
    }
    if (g_StarfieldState.navballIconArray) {
        g_StarfieldState.navballIconArray->Release();
        g_StarfieldState.navballIconArray = nullptr;
    }
    
    // Check if all source textures are valid
    bool hasValidTextures = false;
    for (int i = 0; i < 7; i++) {
        if (sourceTextures[i]) {
            hasValidTextures = true;
            LogToFile("[Navball] Texture %d: valid ptr=%p", i, sourceTextures[i]);
        } else {
            LogToFile("[Navball] Texture %d: NULL", i);
        }
    }
    if (!hasValidTextures) {
        LogToFile("[Navball] Error: No valid textures provided");
        return -3;  // No valid textures provided
    }
    
    // Create texture array
    D3D11_TEXTURE2D_DESC arrayDesc = {};
    arrayDesc.Width = width;
    arrayDesc.Height = height;
    arrayDesc.MipLevels = 1;
    arrayDesc.ArraySize = 7;  // 7 navball icons
    arrayDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;  // MSDF textures are RGBA
    arrayDesc.SampleDesc.Count = 1;
    arrayDesc.Usage = D3D11_USAGE_DEFAULT;
    arrayDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    
    LogToFile("[Navball] Creating texture array: %dx%d x 7", width, height);
    HRESULT hr = g_StarfieldState.device->CreateTexture2D(&arrayDesc, nullptr, &g_StarfieldState.navballIconArray);
    if (FAILED(hr)) {
        LogToFile("[Navball] Failed to create texture array (0x%08X)", hr);
        return -4;
    }
    LogToFile("[Navball] Texture array created successfully");
    
    // Copy each source texture to the corresponding array slice
    ID3D11DeviceContext* context = nullptr;
    g_StarfieldState.device->GetImmediateContext(&context);
    if (!context) {
        LogToFile("[Navball] Error: Failed to get immediate context");
        g_StarfieldState.navballIconArray->Release();
        g_StarfieldState.navballIconArray = nullptr;
        return -5;
    }
    
    LogToFile("[Navball] Copying textures to array...");
    for (int i = 0; i < 7; i++) {
        if (sourceTextures[i]) {
            context->CopySubresourceRegion(
                g_StarfieldState.navballIconArray,
                D3D11CalcSubresource(0, i, 1),  // Mip 0, Array slice i
                0, 0, 0,
                sourceTextures[i],
                0, nullptr
            );
        }
    }
    
    context->Release();
    LogToFile("[Navball] Textures copied, creating SRV...");
    
    // Create SRV for the texture array
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2DARRAY;
    srvDesc.Texture2DArray.MostDetailedMip = 0;
    srvDesc.Texture2DArray.MipLevels = 1;
    srvDesc.Texture2DArray.FirstArraySlice = 0;
    srvDesc.Texture2DArray.ArraySize = 7;
    
    hr = g_StarfieldState.device->CreateShaderResourceView(g_StarfieldState.navballIconArray, &srvDesc, &g_StarfieldState.navballIconArraySRV);
    if (FAILED(hr)) {
        LogToFile("[Navball] Failed to create texture array SRV (0x%08X)", hr);
        g_StarfieldState.navballIconArray->Release();
        g_StarfieldState.navballIconArray = nullptr;
        return -6;
    }
    
    // Clear the invalidated flag since textures are now uploaded
    g_StarfieldState.navballTexturesInvalidated = false;
    
    LogToFile("[Navball] Texture array created: %dx%d x 7 slices, invalidated flag cleared", width, height);
    return 0;  // Success
}

extern "C" __declspec(dllexport)
int CR_SetPointingIconTexture(ID3D11Texture2D* sourceTexture)
{
    LogToFile("[Navball] CR_SetPointingIconTexture called");
    if (!g_StarfieldState.device) {
        LogToFile("[Navball] Error: Device not ready");
        return -1;
    }
    if (!sourceTexture) {
        LogToFile("[Navball] Warning: null pointing icon texture");
        return -2;
    }
    if (g_StarfieldState.pointingIconSRV) {
        g_StarfieldState.pointingIconSRV->Release();
        g_StarfieldState.pointingIconSRV = nullptr;
    }
    D3D11_TEXTURE2D_DESC desc = {};
    sourceTexture->GetDesc(&desc);
    HRESULT hr = g_StarfieldState.device->CreateShaderResourceView(sourceTexture, nullptr, &g_StarfieldState.pointingIconSRV);
    if (FAILED(hr)) {
        LogToFile("[Navball] Failed to create pointing icon SRV (0x%08X)", hr);
        return -3;
    }
    LogToFile("[Navball] Pointing icon texture uploaded: %dx%d", desc.Width, desc.Height);
    return 0;
}

extern "C" __declspec(dllexport)
int CR_SetManeuverTextTexture(ID3D11Texture2D* sourceTexture)
{
    LogToFile("[Navball] CR_SetManeuverTextTexture called");
    if (!g_StarfieldState.device) {
        LogToFile("[Navball] Error: Device not ready");
        return -1;
    }
    if (!sourceTexture) {
        LogToFile("[Navball] Warning: null maneuver text texture");
        return -2;
    }
    if (g_StarfieldState.maneuverTextSRV) {
        g_StarfieldState.maneuverTextSRV->Release();
        g_StarfieldState.maneuverTextSRV = nullptr;
    }
    D3D11_TEXTURE2D_DESC desc = {};
    sourceTexture->GetDesc(&desc);
    HRESULT hr = g_StarfieldState.device->CreateShaderResourceView(sourceTexture, nullptr, &g_StarfieldState.maneuverTextSRV);
    if (FAILED(hr)) {
        LogToFile("[Navball] Failed to create maneuver text SRV (0x%08X)", hr);
        return -3;
    }
    LogToFile("[Navball] Maneuver text texture uploaded: %dx%d", desc.Width, desc.Height);
    return 0;
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
            heroColor = ApplySaturation(baseColor, colorSaturation, nextProceduralID);
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
            heroColor = ApplySaturation(blackbody, colorSaturation, nextProceduralID);
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
            color = ApplySaturation(baseColor, colorSaturation, nextProceduralID);
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
            color = ApplySaturation(blackbody, colorSaturation, nextProceduralID);
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
                                   float extinctionZenith, float extinctionHorizon, float3 atmosphereUp,
                                   ID3D11Texture2D* explicitRenderTarget)
{    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    // Store atmospheric extinction parameters (per-frame update)
    g_StarfieldState.extinctionZenith = extinctionZenith;
    g_StarfieldState.extinctionHorizon = extinctionHorizon;
    g_StarfieldState.atmosphereUp = atmosphereUp;
    
    // Store explicit render target for cubemap rendering (if provided)
    if (explicitRenderTarget != g_StarfieldState.explicitRenderTarget) {
        if (g_StarfieldState.explicitRenderTarget) {
            g_StarfieldState.explicitRenderTarget->Release();
        }
        g_StarfieldState.explicitRenderTarget = explicitRenderTarget;
        if (g_StarfieldState.explicitRenderTarget) {
            g_StarfieldState.explicitRenderTarget->AddRef();
        }
    }
    
    // Check if dimensions changed (needed for cubemap rendering where resolution varies per frame)
    bool dimensionsChanged = (g_StarfieldState.width != width || g_StarfieldState.height != height);
    
    // Update state
    g_StarfieldState.width = width;
    g_StarfieldState.height = height;
    g_StarfieldState.verticalFOV = verticalFOV;
    g_StarfieldState.aspectRatio = aspectRatio;
    g_StarfieldState.cameraRight = cameraRight;
    g_StarfieldState.cameraUp = cameraUp;
    g_StarfieldState.cameraForward = cameraForward;
    
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
    // If device is already acquired but dimensions changed, recreate resources
    else if (g_StarfieldState.device && dimensionsChanged) {
        EnsureStarfieldResources(g_StarfieldState.device, width, height);
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
    if (g_StarfieldState.pointSampler) { g_StarfieldState.pointSampler->Release(); g_StarfieldState.pointSampler = nullptr; }
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
    
    // Cleanup grid label textures (all 12 slots)
    for (int i = 0; i < 12; i++) {
        if (g_StarfieldState.gridLabelTextureSRV[i]) {
            g_StarfieldState.gridLabelTextureSRV[i]->Release();
            g_StarfieldState.gridLabelTextureSRV[i] = nullptr;
        }
    }
    
    if (g_StarfieldState.pointingIconSRV) { g_StarfieldState.pointingIconSRV->Release(); g_StarfieldState.pointingIconSRV = nullptr; }
    if (g_StarfieldState.maneuverTextSRV) { g_StarfieldState.maneuverTextSRV->Release(); g_StarfieldState.maneuverTextSRV = nullptr; }
    
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
    
    // Release navball icon textures so they get re-uploaded
    if (g_StarfieldState.navballIconArraySRV) { 
        g_StarfieldState.navballIconArraySRV->Release(); 
        g_StarfieldState.navballIconArraySRV = nullptr; 
        g_StarfieldState.navballTexturesInvalidated = true;
    }
    if (g_StarfieldState.navballIconArray) { 
        g_StarfieldState.navballIconArray->Release(); 
        g_StarfieldState.navballIconArray = nullptr; 
        g_StarfieldState.navballTexturesInvalidated = true;
    }
    if (g_StarfieldState.pointingIconSRV) { 
        g_StarfieldState.pointingIconSRV->Release(); 
        g_StarfieldState.pointingIconSRV = nullptr; 
    }
    if (g_StarfieldState.maneuverTextSRV) { 
        g_StarfieldState.maneuverTextSRV->Release(); 
        g_StarfieldState.maneuverTextSRV = nullptr; 
    }
    
    // Reset initialized flag so resources get recreated
    g_StarfieldState.initialized = false;
    
    LogToFile("[Starfield] Resources invalidated for recreation (navballTexturesInvalidated=%s)", 
        g_StarfieldState.navballTexturesInvalidated ? "true" : "false");
}

extern "C" __declspec(dllexport)
byte CR_NavballTexturesNeedReupload()
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    return g_StarfieldState.navballTexturesInvalidated ? 1 : 0;
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

// ============================================================================
// CUBEMAP RENDERING
// ============================================================================

// Helper to set up camera basis vectors for each cubemap face
// faceIndex: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
static void GetCubemapFaceOrientation(int faceIndex, float3& outRight, float3& outUp, float3& outForward)
{
    switch (faceIndex) {
        case 0: // +X (Right)
            outForward = float3(1.0f, 0.0f, 0.0f);
            outUp = float3(0.0f, 1.0f, 0.0f);
            outRight = float3(0.0f, 0.0f, -1.0f);
            break;
        case 1: // -X (Left)
            outForward = float3(-1.0f, 0.0f, 0.0f);
            outUp = float3(0.0f, 1.0f, 0.0f);
            outRight = float3(0.0f, 0.0f, 1.0f);
            break;
        case 2: // +Y (Top)
            outForward = float3(0.0f, 1.0f, 0.0f);
            outUp = float3(0.0f, 0.0f, 1.0f);   // Flipped to fix 180° rotation
            outRight = float3(-1.0f, 0.0f, 0.0f); // Flipped to match
            break;
        case 3: // -Y (Bottom)
            outForward = float3(0.0f, -1.0f, 0.0f);
            outUp = float3(0.0f, 0.0f, 1.0f);
            outRight = float3(1.0f, 0.0f, 0.0f);
            break;
        case 4: // +Z (Front)
            outForward = float3(0.0f, 0.0f, 1.0f);
            outUp = float3(0.0f, 1.0f, 0.0f);
            outRight = float3(1.0f, 0.0f, 0.0f);
            break;
        case 5: // -Z (Back)
            outForward = float3(0.0f, 0.0f, -1.0f);
            outUp = float3(0.0f, 1.0f, 0.0f);
            outRight = float3(-1.0f, 0.0f, 0.0f);
            break;
        default:
            outForward = float3(0.0f, 0.0f, 1.0f);
            outUp = float3(0.0f, 1.0f, 0.0f);
            outRight = float3(1.0f, 0.0f, 0.0f);
            break;
    }
}

// ============================================================================
// SOFT BLOOM RENDER FOR CUBEMAP (uses temporary resources)
// ============================================================================

static void ExecuteSoftBloomRenderCubemap(
    ID3D11DeviceContext* context,
    ID3D11RenderTargetView* finalRTV,
    ID3D11ShaderResourceView* hdrSRV,
    ID3D11RenderTargetView* bloomRTV,
    ID3D11ShaderResourceView* bloomSRV,
    ID3D11RenderTargetView* bloomTempRTV,
    ID3D11ShaderResourceView* bloomTempSRV,
    ID3D11RenderTargetView* bloomHalfRTV,
    ID3D11ShaderResourceView* bloomHalfSRV,
    int width, int height)
{
    if (!context || !finalRTV) return;
    
    ID3D11Device* device = nullptr;
    context->GetDevice(&device);
    if (!device) return;
    
    int bloomWidth = width / 2;
    int bloomHeight = height / 2;
    if (bloomWidth < 1) bloomWidth = 1;
    if (bloomHeight < 1) bloomHeight = 1;
    
    float clearColor[4] = {0, 0, 0, 0};
    
    // Pass 1: Prefilter + Downsample
    D3D11_VIEWPORT halfResVP = {};
    halfResVP.Width = (float)bloomWidth;
    halfResVP.Height = (float)bloomHeight;
    halfResVP.MinDepth = 0.0f;
    halfResVP.MaxDepth = 1.0f;
    context->RSSetViewports(1, &halfResVP);
    
    context->OMSetRenderTargets(1, &bloomTempRTV, nullptr);
    context->OMSetBlendState(nullptr, nullptr, 0xFFFFFFFF);
    context->ClearRenderTargetView(bloomTempRTV, clearColor);
    
    // Update Prefilter CB
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(context->Map(g_StarfieldState.prefilterCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        float* data = (float*)mapped.pData;
        data[0] = (float)width;
        data[1] = (float)height;
        data[2] = 1.0f / width;
        data[3] = 1.0f / height;
        data[4] = g_StarfieldState.bloomThreshold;
        data[5] = 0.65f;
        data[6] = (float)bloomWidth;
        data[7] = (float)bloomHeight;
        data[8] = 1.0f / bloomWidth;
        data[9] = 1.0f / bloomHeight;
        context->Unmap(g_StarfieldState.prefilterCB, 0);
    }
    
    context->VSSetShader(g_StarfieldState.pass2VS, nullptr, 0);
    context->PSSetShader(g_StarfieldState.prefilterPS, nullptr, 0);
    context->PSSetConstantBuffers(0, 1, &g_StarfieldState.prefilterCB);
    context->PSSetSamplers(0, 1, &g_StarfieldState.linearSampler);
    ID3D11ShaderResourceView* prefilterSRV[1] = {hdrSRV};
    context->PSSetShaderResources(0, 1, prefilterSRV);
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->RSSetState(g_StarfieldState.rasterState);
    context->Draw(3, 0);
    
    // Pass 2: Horizontal Blur
    context->OMSetRenderTargets(1, &bloomRTV, nullptr);
    context->ClearRenderTargetView(bloomRTV, clearColor);
    
    if (SUCCEEDED(context->Map(g_StarfieldState.blurCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        float* data = (float*)mapped.pData;
        data[0] = 1.0f / bloomWidth;
        data[1] = 1.0f / bloomHeight;
        float t = g_StarfieldState.bloomIntensity / 2.0f;
        data[2] = t * 0.65f;
        data[3] = 0.0f;
        context->Unmap(g_StarfieldState.blurCB, 0);
    }
    
    context->PSSetShader(g_StarfieldState.blurXPS, nullptr, 0);
    context->PSSetConstantBuffers(0, 1, &g_StarfieldState.blurCB);
    ID3D11ShaderResourceView* horizSRV[1] = {bloomTempSRV};
    context->PSSetShaderResources(0, 1, horizSRV);
    context->Draw(3, 0);
    
    ID3D11ShaderResourceView* nullSRV[2] = {nullptr, nullptr};
    context->PSSetShaderResources(0, 1, nullSRV);
    
    // Pass 3: Vertical Blur
    context->OMSetRenderTargets(1, &bloomTempRTV, nullptr);
    context->PSSetShader(g_StarfieldState.blurPS, nullptr, 0);
    ID3D11ShaderResourceView* vertSRV[1] = {bloomSRV};
    context->PSSetShaderResources(0, 1, vertSRV);
    context->Draw(3, 0);
    context->PSSetShaderResources(0, 1, nullSRV);
    
    // Pass 4: Upscale to full res
    D3D11_VIEWPORT fullResVP = {};
    fullResVP.Width = (float)width;
    fullResVP.Height = (float)height;
    fullResVP.MinDepth = 0.0f;
    fullResVP.MaxDepth = 1.0f;
    context->RSSetViewports(1, &fullResVP);
    
    context->OMSetRenderTargets(1, &bloomHalfRTV, nullptr);
    context->ClearRenderTargetView(bloomHalfRTV, clearColor);
    context->PSSetShader(g_StarfieldState.upscalePS, nullptr, 0);
    ID3D11ShaderResourceView* upscaleSRV[1] = {bloomTempSRV};
    context->PSSetShaderResources(0, 1, upscaleSRV);
    context->Draw(3, 0);
    context->PSSetShaderResources(0, 1, nullSRV);
    
    // Pass 5: Final composite
    context->OMSetRenderTargets(1, &finalRTV, nullptr);
    context->OMSetBlendState(g_StarfieldState.blendState, nullptr, 0xFFFFFFFF);
    
    if (SUCCEEDED(context->Map(g_StarfieldState.compositeCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        SoftCompositeParams* params = (SoftCompositeParams*)mapped.pData;
        params->ScreenSizeX = (float)width;
        params->ScreenSizeY = (float)height;
        params->InvScreenSizeX = 1.0f / width;
        params->InvScreenSizeY = 1.0f / height;
        params->BloomIntensity = g_StarfieldState.bloomIntensity;
        params->ExposureEV = g_StarfieldState.exposure;
        params->EnableTonemapping = 1;
        params->Pad1 = 0.0f;
        params->ExtinctionZenith = 1.0f;
        params->ExtinctionHorizon = 1.0f;
        params->Pad2[0] = params->Pad2[1] = 0.0f;
        params->AtmosphereUpX = 0.0f;
        params->AtmosphereUpY = 1.0f;
        params->AtmosphereUpZ = 0.0f;
        params->Pad3 = 0.0f;
        params->SunGlareDimming = 1.0f;
        params->PlanetaryDimming = 1.0f;
        params->GlobalDimming = 1.0f;
        params->_padFinal = 0.0f;
        context->Unmap(g_StarfieldState.compositeCB, 0);
    }
    
    context->VSSetShader(g_StarfieldState.pass2VS, nullptr, 0);
    context->PSSetShader(g_StarfieldState.softCompositePS, nullptr, 0);
    context->PSSetConstantBuffers(0, 1, &g_StarfieldState.compositeCB);
    ID3D11ShaderResourceView* compositeSRVs[2] = {hdrSRV, bloomHalfSRV};
    context->PSSetShaderResources(0, 2, compositeSRVs);
    context->Draw(3, 0);
    
    context->PSSetShaderResources(0, 2, nullSRV);
    device->Release();
}

// ============================================================================
// ISOLATED CUBEMAP FACE RENDER
// Completely separate from g_StarfieldState - creates its own resources
// ============================================================================

static void ExecuteCubemapFaceRender(
    ID3D11DeviceContext* context,
    ID3D11Texture2D* targetTexture,
    int faceSize,
    const float3& cameraRight,
    const float3& cameraUp,
    const float3& cameraForward)
{
    if (!context || !targetTexture || faceSize <= 0) return;
    
    ID3D11Device* device = nullptr;
    context->GetDevice(&device);
    if (!device) return;
    
    // Verify catalog is loaded
    if (!g_StarfieldState.starCatalogBuffer || g_StarfieldState.catalogSize == 0) {
        device->Release();
        return;
    }
    
    // =========================================================================
    // STEP 1: Create temporary HDR texture for this face (not using g_StarfieldState)
    // =========================================================================
    D3D11_TEXTURE2D_DESC hdrDesc = {};
    hdrDesc.Width = faceSize;
    hdrDesc.Height = faceSize;
    hdrDesc.MipLevels = 1;
    hdrDesc.ArraySize = 1;
    hdrDesc.Format = DXGI_FORMAT_R11G11B10_FLOAT;
    hdrDesc.SampleDesc.Count = 1;
    hdrDesc.Usage = D3D11_USAGE_DEFAULT;
    hdrDesc.BindFlags = D3D11_BIND_UNORDERED_ACCESS | D3D11_BIND_SHADER_RESOURCE;
    
    ID3D11Texture2D* tempHDR = nullptr;
    ID3D11UnorderedAccessView* tempHDR_UAV = nullptr;
    ID3D11ShaderResourceView* tempHDR_SRV = nullptr;
    
    HRESULT hr = device->CreateTexture2D(&hdrDesc, nullptr, &tempHDR);
    if (SUCCEEDED(hr)) {
        hr = device->CreateUnorderedAccessView(tempHDR, nullptr, &tempHDR_UAV);
        if (SUCCEEDED(hr)) {
            hr = device->CreateShaderResourceView(tempHDR, nullptr, &tempHDR_SRV);
        }
    }
    
    if (FAILED(hr) || !tempHDR || !tempHDR_UAV || !tempHDR_SRV) {
        if (tempHDR_SRV) tempHDR_SRV->Release();
        if (tempHDR_UAV) tempHDR_UAV->Release();
        if (tempHDR) tempHDR->Release();
        device->Release();
        return;
    }
    
    // =========================================================================
    // STEP 2: Clear HDR texture
    // =========================================================================
    UINT clearColor[4] = {0, 0, 0, 0};
    context->ClearUnorderedAccessViewUint(tempHDR_UAV, clearColor);
    
    // =========================================================================
    // STEP 3: Pass 1 - Scatter stars to HDR (compute shader)
    // =========================================================================
    {
        D3D11_MAPPED_SUBRESOURCE mapped;
        if (SUCCEEDED(context->Map(g_StarfieldState.pass1CB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
            StarfieldPass1Params* params = (StarfieldPass1Params*)mapped.pData;
            
            // Camera setup - 90° FOV, 1:1 aspect for cubemap
            params->VerticalFOV = 1.570796f; // 90 degrees in radians
            params->AspectRatio = 1.0f;
            params->_padCamera0[0] = 0.0f;
            params->_padCamera0[1] = 0.0f;
            
            // Camera basis vectors (explicit per-face)
            params->CameraRight[0] = cameraRight.x;
            params->CameraRight[1] = cameraRight.y;
            params->CameraRight[2] = cameraRight.z;
            params->_padCamera1 = 0.0f;
            
            params->CameraUp[0] = cameraUp.x;
            params->CameraUp[1] = cameraUp.y;
            params->CameraUp[2] = cameraUp.z;
            params->_padCamera2 = 0.0f;
            
            params->CameraForward[0] = cameraForward.x;
            params->CameraForward[1] = cameraForward.y;
            params->CameraForward[2] = cameraForward.z;
            params->_padCamera3 = 0.0f;
            
            // Use g_StarfieldState for non-dimension settings
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
            params->_pad2[0] = 0.0f;
            params->_pad2[1] = 0.0f;
            
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
            
            // EXPLICIT face dimensions (not from g_StarfieldState)
            params->ScreenSizeX = (float)faceSize;
            params->ScreenSizeY = (float)faceSize;
            params->InvScreenSizeX = 1.0f / faceSize;
            params->InvScreenSizeY = 1.0f / faceSize;
            
            params->FrameIndex = 0; // No temporal AA for cubemap
            params->CatalogSize = g_StarfieldState.catalogSize;
            params->Pad1[0] = params->Pad1[1] = 0;
            
            params->RotationX = g_StarfieldState.rotationX;
            params->RotationY = g_StarfieldState.rotationY;
            params->RotationZ = g_StarfieldState.rotationZ;
            
            context->Unmap(g_StarfieldState.pass1CB, 0);
        }
        
        // Create temporary SRV for catalog buffer
        D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Format = DXGI_FORMAT_UNKNOWN; // Structured buffer
        srvDesc.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
        srvDesc.Buffer.FirstElement = 0;
        srvDesc.Buffer.NumElements = g_StarfieldState.catalogSize;
        
        ID3D11ShaderResourceView* catalogSRV = nullptr;
        HRESULT hr = device->CreateShaderResourceView(g_StarfieldState.starCatalogBuffer, &srvDesc, &catalogSRV);
        if (FAILED(hr) || !catalogSRV) {
            if (catalogSRV) catalogSRV->Release();
            tempHDR_SRV->Release();
            tempHDR_UAV->Release();
            tempHDR->Release();
            device->Release();
            return;
        }
        
        // Dispatch compute shader
        context->CSSetShader(g_StarfieldState.pass1CS, nullptr, 0);
        context->CSSetConstantBuffers(0, 1, &g_StarfieldState.pass1CB);
        context->CSSetUnorderedAccessViews(0, 1, &tempHDR_UAV, nullptr);
        context->CSSetShaderResources(0, 1, &catalogSRV);
        
        int threadGroups = (g_StarfieldState.catalogSize + 255) / 256;
        context->Dispatch(threadGroups, 1, 1);
        
        // Unbind UAV and cleanup catalog SRV
        ID3D11UnorderedAccessView* nullUAV = nullptr;
        context->CSSetUnorderedAccessViews(0, 1, &nullUAV, nullptr);
        ID3D11ShaderResourceView* nullSRV = nullptr;
        context->CSSetShaderResources(0, 1, &nullSRV);
        context->CSSetShader(nullptr, nullptr, 0);
        catalogSRV->Release();
    }
    
    // =========================================================================
    // STEP 4: Pass 2 - Composite to target texture (pixel shader)
    // Use user's selected bloom mode (classic or soft HDR)
    // =========================================================================
    {
        // Create RTV for target texture
        D3D11_TEXTURE2D_DESC targetDesc;
        targetTexture->GetDesc(&targetDesc);
        
        // Handle TYPELESS format
        DXGI_FORMAT rtvFormat = targetDesc.Format;
        if (targetDesc.Format == DXGI_FORMAT_R8G8B8A8_TYPELESS) {
            rtvFormat = DXGI_FORMAT_R8G8B8A8_UNORM;
        }
        
        D3D11_RENDER_TARGET_VIEW_DESC rtvDesc = {};
        rtvDesc.Format = rtvFormat;
        rtvDesc.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D;
        rtvDesc.Texture2D.MipSlice = 0;
        
        ID3D11RenderTargetView* targetRTV = nullptr;
        hr = device->CreateRenderTargetView(targetTexture, &rtvDesc, &targetRTV);
        if (FAILED(hr)) {
            // Try with null desc
            hr = device->CreateRenderTargetView(targetTexture, nullptr, &targetRTV);
        }
        
        if (SUCCEEDED(hr) && targetRTV) {
            // Check user's bloom preference
            if (g_StarfieldState.useSoftBloom) {
                // Soft HDR bloom path - create temporary bloom resources
                int bloomWidth = faceSize / 2;
                int bloomHeight = faceSize / 2;
                if (bloomWidth < 1) bloomWidth = 1;
                if (bloomHeight < 1) bloomHeight = 1;
                
                // Create temporary bloom textures
                D3D11_TEXTURE2D_DESC bloomDesc = {};
                bloomDesc.Width = bloomWidth;
                bloomDesc.Height = bloomHeight;
                bloomDesc.MipLevels = 1;
                bloomDesc.ArraySize = 1;
                bloomDesc.Format = DXGI_FORMAT_R11G11B10_FLOAT;
                bloomDesc.SampleDesc.Count = 1;
                bloomDesc.Usage = D3D11_USAGE_DEFAULT;
                bloomDesc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
                
                ID3D11Texture2D* tempBloomTex = nullptr;
                ID3D11RenderTargetView* tempBloomRTV = nullptr;
                ID3D11ShaderResourceView* tempBloomSRV = nullptr;
                
                ID3D11Texture2D* tempBloomTempTex = nullptr;
                ID3D11RenderTargetView* tempBloomTempRTV = nullptr;
                ID3D11ShaderResourceView* tempBloomTempSRV = nullptr;
                
                ID3D11Texture2D* tempBloomHalfTex = nullptr;
                ID3D11RenderTargetView* tempBloomHalfRTV = nullptr;
                ID3D11ShaderResourceView* tempBloomHalfSRV = nullptr;
                
                bool bloomResourcesCreated = false;
                
                if (SUCCEEDED(device->CreateTexture2D(&bloomDesc, nullptr, &tempBloomTex)) &&
                    SUCCEEDED(device->CreateRenderTargetView(tempBloomTex, nullptr, &tempBloomRTV)) &&
                    SUCCEEDED(device->CreateShaderResourceView(tempBloomTex, nullptr, &tempBloomSRV)) &&
                    SUCCEEDED(device->CreateTexture2D(&bloomDesc, nullptr, &tempBloomTempTex)) &&
                    SUCCEEDED(device->CreateRenderTargetView(tempBloomTempTex, nullptr, &tempBloomTempRTV)) &&
                    SUCCEEDED(device->CreateShaderResourceView(tempBloomTempTex, nullptr, &tempBloomTempSRV))) {
                    
                    // Create half-res texture for final bloom
                    D3D11_TEXTURE2D_DESC halfDesc = bloomDesc;
                    halfDesc.Width = faceSize;
                    halfDesc.Height = faceSize;
                    
                    if (SUCCEEDED(device->CreateTexture2D(&halfDesc, nullptr, &tempBloomHalfTex)) &&
                        SUCCEEDED(device->CreateRenderTargetView(tempBloomHalfTex, nullptr, &tempBloomHalfRTV)) &&
                        SUCCEEDED(device->CreateShaderResourceView(tempBloomHalfTex, nullptr, &tempBloomHalfSRV))) {
                        bloomResourcesCreated = true;
                    }
                }
                
                if (bloomResourcesCreated) {
                    // Execute soft bloom render with temporary resources
                    ExecuteSoftBloomRenderCubemap(context, targetRTV, tempHDR_SRV, 
                        tempBloomRTV, tempBloomSRV, tempBloomTempRTV, tempBloomTempSRV,
                        tempBloomHalfRTV, tempBloomHalfSRV, faceSize, faceSize);
                    
                    // Cleanup temporary bloom resources
                    tempBloomHalfSRV->Release();
                    tempBloomHalfRTV->Release();
                    tempBloomHalfTex->Release();
                }
                
                if (tempBloomTempSRV) tempBloomTempSRV->Release();
                if (tempBloomTempRTV) tempBloomTempRTV->Release();
                if (tempBloomTempTex) tempBloomTempTex->Release();
                if (tempBloomSRV) tempBloomSRV->Release();
                if (tempBloomRTV) tempBloomRTV->Release();
                if (tempBloomTex) tempBloomTex->Release();
                
                // Cleanup RTV binding
                ID3D11RenderTargetView* nullRTV = nullptr;
                context->OMSetRenderTargets(1, &nullRTV, nullptr);
            } else {
                // Classic bloom path (original simple composite)
                // Update Pass 2 constant buffer
                D3D11_MAPPED_SUBRESOURCE mapped;
                if (SUCCEEDED(context->Map(g_StarfieldState.pass2CB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
                    StarfieldPass2Params* params = (StarfieldPass2Params*)mapped.pData;
                    
                    // EXPLICIT face dimensions
                    params->ScreenSizeX = (float)faceSize;
                    params->ScreenSizeY = (float)faceSize;
                    params->InvScreenSizeX = 1.0f / faceSize;
                    params->InvScreenSizeY = 1.0f / faceSize;
                    
                    params->BloomThreshold = g_StarfieldState.bloomThreshold;
                    params->BloomIntensity = g_StarfieldState.bloomIntensity;
                    params->DepthThreshold = 0.001f;
                    params->ExposureEV = g_StarfieldState.exposure;
                    params->EnableTonemapping = 1;
                    params->Pad1[0] = params->Pad1[1] = params->Pad1[2] = 0.0f;
                    
                    params->ExtinctionZenith = 1.0f;
                    params->ExtinctionHorizon = 1.0f;
                    params->Pad2[0] = params->Pad2[1] = 0.0f;
                    
                    params->AtmosphereUpX = 0.0f;
                    params->AtmosphereUpY = 1.0f;
                    params->AtmosphereUpZ = 0.0f;
                    params->Pad3 = 0.0f;
                    
                    params->SunGlareDimming = 1.0f;
                    params->PlanetaryDimming = 1.0f;
                    params->GlobalDimming = 1.0f;
                    params->_padFinal = 0.0f;
                    
                    context->Unmap(g_StarfieldState.pass2CB, 0);
                }
                
                // Set up output merger
                context->OMSetRenderTargets(1, &targetRTV, nullptr);
                context->OMSetDepthStencilState(g_StarfieldState.depthState, 0);
                context->OMSetBlendState(g_StarfieldState.blendState, nullptr, 0xFFFFFFFF);
                context->RSSetState(g_StarfieldState.rasterState);
                
                // Set viewport to face dimensions
                D3D11_VIEWPORT vp = {0, 0, (float)faceSize, (float)faceSize, 0, 1};
                context->RSSetViewports(1, &vp);
                
                // Bind shaders
                context->VSSetShader(g_StarfieldState.pass2VS, nullptr, 0);
                context->PSSetShader(g_StarfieldState.pass2PS, nullptr, 0);
                context->PSSetConstantBuffers(0, 1, &g_StarfieldState.pass2CB);
                context->PSSetSamplers(0, 1, &g_StarfieldState.linearSampler);
                
                ID3D11ShaderResourceView* srvs[1] = {tempHDR_SRV};
                context->PSSetShaderResources(0, 1, srvs);
                
                // Draw fullscreen triangle
                context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
                context->IASetInputLayout(nullptr);
                context->IASetVertexBuffers(0, 0, nullptr, nullptr, nullptr);
                context->IASetIndexBuffer(nullptr, DXGI_FORMAT_UNKNOWN, 0);
                
                context->Draw(3, 0);
                
                // Cleanup
                ID3D11ShaderResourceView* nullSRV[2] = {nullptr, nullptr};
                context->PSSetShaderResources(0, 2, nullSRV);
                ID3D11RenderTargetView* nullRTV = nullptr;
                context->OMSetRenderTargets(1, &nullRTV, nullptr);
            }
            
            targetRTV->Release();
        }
    }
    
    // =========================================================================
    // STEP 5: Cleanup temporary resources
    // =========================================================================
    tempHDR_SRV->Release();
    tempHDR_UAV->Release();
    tempHDR->Release();
    device->Release();
}

// Simple test function - just clears textures to different colors
// Does NOT modify global state
extern "C" __declspec(dllexport)
int CR_RenderStarfieldCubemap(ID3D11Texture2D* targetTextures[6], int faceSize)
{
    if (!targetTextures || faceSize <= 0) {
        LogToFile("[StarfieldCubemap] Error: Invalid parameters");
        return -1;
    }
    
    // Don't take the main lock to avoid blocking, but check state
    if (!g_StarfieldState.device) {
        LogToFile("[StarfieldCubemap] Error: Device not initialized");
        return -2;
    }
    
    // Get immediate context
    ID3D11DeviceContext* context = nullptr;
    g_StarfieldState.device->GetImmediateContext(&context);
    if (!context) {
        LogToFile("[StarfieldCubemap] Error: Failed to get device context");
        return -4;
    }
    
    // Create GPU timestamp queries for accurate timing
    D3D11_QUERY_DESC timestampDesc = { D3D11_QUERY_TIMESTAMP, 0 };
    D3D11_QUERY_DESC disjointDesc = { D3D11_QUERY_TIMESTAMP_DISJOINT, 0 };
    
    ID3D11Query* queryStart = nullptr;
    ID3D11Query* queryEnd = nullptr;
    ID3D11Query* queryDisjoint = nullptr;
    
    bool timingAvailable = false;
    if (SUCCEEDED(g_StarfieldState.device->CreateQuery(&timestampDesc, &queryStart)) &&
        SUCCEEDED(g_StarfieldState.device->CreateQuery(&timestampDesc, &queryEnd)) &&
        SUCCEEDED(g_StarfieldState.device->CreateQuery(&disjointDesc, &queryDisjoint))) {
        timingAvailable = true;
        // Start timing
        context->Begin(queryDisjoint);
        context->End(queryStart);
    }
    
    LogToFile("[StarfieldCubemap] Rendering %dx%d cubemap...", faceSize, faceSize);
    
    // Store original state to restore later
    ID3D11RenderTargetView* oldRTV = nullptr;
    ID3D11DepthStencilView* oldDSV = nullptr;
    D3D11_VIEWPORT oldViewport;
    UINT numViewports = 1;
    context->OMGetRenderTargets(1, &oldRTV, &oldDSV);
    context->RSGetViewports(&numViewports, &oldViewport);
    
    // Clear each face to a different debug color
    for (int face = 0; face < 6; face++) {
        if (!targetTextures[face]) {
            LogToFile("[StarfieldCubemap] Warning: Face %d texture is null, skipping", face);
            continue;
        }
        
        // Verify the texture is a valid render target
        D3D11_TEXTURE2D_DESC texDesc;
        targetTextures[face]->GetDesc(&texDesc);
        
        // Face format log removed
        
        // Check dimensions match
        if ((int)texDesc.Width != faceSize || (int)texDesc.Height != faceSize) {
            LogToFile("[StarfieldCubemap] Face %d dimension mismatch: expected %dx%d, got %dx%d", 
                      face, faceSize, faceSize, texDesc.Width, texDesc.Height);
            continue;
        }
        
        // Check if it has RTV bind flag - if not, we can still use it for output
        bool hasRTVFlag = (texDesc.BindFlags & D3D11_BIND_RENDER_TARGET) != 0;
        
        // Create RTV for this face
        // If format is TYPELESS, we need to specify a concrete format for the RTV
        DXGI_FORMAT rtvFormat = texDesc.Format;
        if (texDesc.Format == DXGI_FORMAT_R8G8B8A8_TYPELESS) {
            rtvFormat = DXGI_FORMAT_R8G8B8A8_UNORM; // Use UNORM view of TYPELESS texture
        }
        
        D3D11_RENDER_TARGET_VIEW_DESC rtvDesc = {};
        rtvDesc.Format = rtvFormat;
        rtvDesc.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D;
        rtvDesc.Texture2D.MipSlice = 0;
        
        ID3D11RenderTargetView* rtv = nullptr;
        HRESULT hr = g_StarfieldState.device->CreateRenderTargetView(targetTextures[face], &rtvDesc, &rtv);
        if (FAILED(hr)) {
            // Try with null desc (let D3D11 infer from texture)
            hr = g_StarfieldState.device->CreateRenderTargetView(targetTextures[face], nullptr, &rtv);
            if (FAILED(hr)) {
                LogToFile("[StarfieldCubemap] Failed to create RTV for face %d (hr=0x%08X, Format=%d, RTVFormat=%d)", face, hr, texDesc.Format, rtvFormat);
                continue;
            }
        }
        
        // Get face orientation
        float3 right, up, forward;
        GetCubemapFaceOrientation(face, right, up, forward);
        
        // Release our RTV - the isolated render function creates its own
        rtv->Release();
        
        // Execute the starfield render using isolated function (does NOT modify g_StarfieldState)
        ExecuteCubemapFaceRender(context, targetTextures[face], faceSize, right, up, forward);
    }
    
    // Restore original state
    context->OMSetRenderTargets(1, &oldRTV, oldDSV);
    context->RSSetViewports(numViewports, &oldViewport);
    if (oldRTV) oldRTV->Release();
    if (oldDSV) oldDSV->Release();
    
    // End GPU timing
    if (timingAvailable) {
        context->End(queryEnd);
        context->End(queryDisjoint);
        
        // Wait for data with longer timeout
        D3D11_QUERY_DATA_TIMESTAMP_DISJOINT disjointData;
        UINT64 startTime = 0, endTime = 0;
        bool gotData = false;
        
        for (int i = 0; i < 500; i++) {  // 5 second max wait
            HRESULT hr = context->GetData(queryDisjoint, &disjointData, sizeof(disjointData), 0);
            if (hr == S_OK) {
                if (!disjointData.Disjoint && 
                    context->GetData(queryStart, &startTime, sizeof(startTime), 0) == S_OK &&
                    context->GetData(queryEnd, &endTime, sizeof(endTime), 0) == S_OK) {
                    double gpuTimeMs = (double)(endTime - startTime) * 1000.0 / disjointData.Frequency;
                    LogToFile("[StarfieldCubemap] GPU render time: %.2f ms", gpuTimeMs);
                    gotData = true;
                    break;
                }
            } else if (hr != S_FALSE) {
                // Error
                break;
            }
            Sleep(10);
        }
        
        if (!gotData) {
            LogToFile("[StarfieldCubemap] GPU timing data not available");
        }
        
        queryStart->Release();
        queryEnd->Release();
        queryDisjoint->Release();
    }
    
    if (context) {
        context->Release();
    }
    
    LogToFile("[StarfieldCubemap] Render complete");
    return 0;
}
