#pragma once
#include <d3d11.h>
#include <mutex>
#include "CinematicShadersNative.h"  // For UNITY_INTERFACE_API, UnityRenderingEvent


struct float2 {
    float x, y;
    float2() : x(0), y(0) {}
    float2(float _x, float _y) : x(_x), y(_y) {}
};
// Define HLSL-style vector types for C interface
struct float3 {
    float x, y, z;
    float3() : x(0), y(0), z(0) {}
    float3(float _x, float _y, float _z) : x(_x), y(_y), z(_z) {}
};

// 16-byte aligned float4 for HLSL constant buffer compatibility
__declspec(align(16)) struct float4 {
    float x, y, z, w;
    float4() : x(0), y(0), z(0), w(0) {}
    float4(float _x, float _y, float _z, float _w) : x(_x), y(_y), z(_z), w(_w) {}
};

// Star catalog entry - 48 bytes, 4-byte aligned for GPU StructuredBuffer
// Layout matches C# StarDataNative and HLSL StarData exactly
// Version 4: Added HipparcosID, DistancePc, SpectralType, and Flags
struct StarData {
    int32_t HipparcosID;   // 4 bytes - Hipparcos catalog ID (0 if not from real catalog)
    float DistancePc;      // 4 bytes - Distance in parsecs (0 if unknown)
    int32_t SpectralType;  // 4 bytes - 0=O,1=B,2=A,3=F,4=G,5=K,6=M,7=L,255=Unknown
    uint32_t Flags;        // 4 bytes - Bit 0=IsHero (can be named)
    
    float DirectionX;      // 4 bytes
    float DirectionY;      // 4 bytes  
    float DirectionZ;      // 4 bytes
    float Magnitude;       // 4 bytes - Absolute magnitude (brightness)
    
    float ColorR;          // 4 bytes - RGB color (already blackbody-corrected)
    float ColorG;          // 4 bytes
    float ColorB;          // 4 bytes
    float Temperature;     // 4 bytes - Kelvin, for future PSF shader use
    
    // Flag constants
    static constexpr uint32_t FLAG_IS_HERO = 1;  // Bit 0: Star can be named/is important
    
    // Utility constructor for C++ generation code
    StarData() : HipparcosID(0), DistancePc(0.0f), SpectralType(255), Flags(0),
                 DirectionX(0), DirectionY(0), DirectionZ(0), Magnitude(10.0f),
                 ColorR(1.0f), ColorG(1.0f), ColorB(1.0f), Temperature(5778.0f) {}
                 
    StarData(int32_t hip, float dist, int32_t spectral, uint32_t flags, float dx, float dy, float dz, float mag, float r, float g, float b, float temp)
        : HipparcosID(hip), DistancePc(dist), SpectralType(spectral), Flags(flags),
          DirectionX(dx), DirectionY(dy), DirectionZ(dz), Magnitude(mag),
          ColorR(r), ColorG(g), ColorB(b), Temperature(temp) {}
};

#ifdef __cplusplus
extern "C" {
#endif

// Settings struct matching C#
// NOTE: BlurPixels is interpreted as angular sigma in RADIANS by the shader
// (e.g., 0.001 = ~3.4 arcminutes). The shader converts to screen pixels based on FOV.
struct StarfieldSettingsNative {
    float Exposure;
    float BlurPixels;  // Angular sigma in radians, NOT screen pixels
    float MinMagnitude;
    float MaxMagnitude;
    float MagnitudeBias;
    int HeroCount;  // 16 to 1024
    float Clustering;
    float PopulationBias;
    float MainSequenceStrength;
    float RedGiantFrequency;
    float GalacticFlatness;
    float GalacticDiscFalloff;
    float BandCenterBoost;
    float BandCoreSharpness;
    float BulgeIntensity;
    float BulgeWidth;
    float BulgeHeight;
    float BulgeSoftness;
    float BulgeNoiseScale;
    float BulgeNoiseStrength;
    float BloomThreshold;
    float BloomIntensity;
    float ColorSaturation;  // 0.0-2.0: 0.5=realistic, 1.0=natural, 2.0=vivid

    int UseSoftBloom;  // 0 = Classic, 1 = Soft HDR
    
    // HYG Catalog Coordinate Rotation (degrees, applied to star directions before rendering)
    // Allows aligning the real sky catalog with the game's coordinate system
    float RotationX;  // Rotation around X axis (tilt forward/back)
    float RotationY;  // Rotation around Y axis (yaw left/right)
    float RotationZ;  // Rotation around Z axis (roll clockwise/counter-clockwise)
    
    // Galactic plane orientation (matches C# struct layout)
    float GalacticPlaneNormalX;
    float GalacticPlaneNormalY;
    float GalacticPlaneNormalZ;
    
    // Global scene dimming factors (per-frame calculated)
    float SunGlareDimming;      // 1.0 = full brightness, 0.0 = fully dimmed
    float PlanetaryDimming;     // 1.0 = full brightness, 0.0 = fully dimmed
    float GlobalDimming;        // min(Sun, Planetary) - calculated CPU-side
    
};

__declspec(dllexport) void CR_StarfieldSetCameraMatrices(
    ID3D11Texture2D* deviceSourceTexture,  // Any D3D11 texture to query device from (e.g., whiteTexture)
    int width, 
    int height,
    float verticalFOV,
    float aspectRatio,
    float3 cameraRight,
    float3 cameraUp,
    float3 cameraForward,
    // Atmospheric extinction parameters (per-frame)
    float extinctionZenith,     // Visibility at zenith (0-1)
    float extinctionHorizon,    // Visibility at horizon (0-1)
    float3 atmosphereUp,        // World-space up vector
    // Optional: explicit render target for cubemap rendering (nullptr = use current)
    ID3D11Texture2D* explicitRenderTarget = nullptr
);

__declspec(dllexport) void CR_StarfieldSetSettings(const StarfieldSettingsNative* settings);

__declspec(dllexport) UnityRenderingEvent CR_GetStarfieldRenderEventFunc();

__declspec(dllexport) void CR_StarfieldShutdown();

__declspec(dllexport) void CR_StarfieldGenerateCatalog(int seed, int count);

// Catalog save/load - for StarCatalogManager
// Returns number of stars copied, or 0 if buffer too small or no catalog loaded
__declspec(dllexport) int CR_StarfieldGetCatalogData(StarData* outBuffer, int maxCount);

// Load catalog directly from buffer (bypasses generation). Thread-safe.
__declspec(dllexport) void CR_StarfieldLoadCatalog(const StarData* buffer, int count, int heroCount);

// Get current catalog info
__declspec(dllexport) int CR_StarfieldGetCatalogSize();
__declspec(dllexport) int CR_StarfieldGetHeroCount();

// Check if D3D11 device is initialized and ready
__declspec(dllexport) unsigned char CR_StarfieldIsDeviceReady();

// Check if catalog needs reload (device was acquired but catalog empty). Resets flag after reading.
__declspec(dllexport) unsigned char CR_StarfieldCatalogNeedsReload();

// Invalidate GPU resources (call on scene change to force recreation, preserves catalog)
__declspec(dllexport) void CR_StarfieldInvalidateResources();

// Global scene dimming (per-frame update, separate from settings)
__declspec(dllexport) void CR_StarfieldSetDimming(float sunGlareDimming, float planetaryDimming);

// Kartographer holographic grid overlay enable/disable
__declspec(dllexport) void CR_StarfieldSetKartographerEnabled(unsigned char enabled);

// Kartographer visual parameters struct (Phase 2 - 8 Label Support + Vessel Target)
// Layout matches HLSL exactly - 704 bytes (16 × 44)
// Generated from ReferenceNotes/tools/generate_struct.py
// Do not modify without updating shader
struct KartographerParamsNative {
    // Field offsets (bytes):
    //     0: ResolutionX (float)
    //     4: ResolutionY (float)
    //     8: Time (float)
    //    12: GridIntensity (float)
    //    16: GridThickness (float)
    //    20: ChromaticAberrationStrength (float)
    //    24: VignetteStrength (float)
    //    28: VignetteStart (float)
    //    32: VignetteEnd (float)
    //    36: PreRotationYaw (float)
    //    40: PreRotationPitch (float)
    //    44: GridSizePreset (int)
    //    48: GridColorIndex (int)
    //    52: _pad1 (float)
    //    56: _pad2 (float)
    //    60: _padAlignCamera (float)
    //    64: CameraRightX (float)
    //    68: CameraRightY (float)
    //    72: CameraRightZ (float)
    //    76: _pad3 (float)
    //    80: CameraUpX (float)
    //    84: CameraUpY (float)
    //    88: CameraUpZ (float)
    //    92: _pad4 (float)
    //    96: CameraForwardX (float)
    //   100: CameraForwardY (float)
    //   104: CameraForwardZ (float)
    //   108: _pad5 (float)
    //   112: DebugShapesEnabled (int)
    //   116: _pad6 (float)
    //   120: _pad7 (float)
    //   124: _pad8 (float)
    //   128: DebugCircleCenterX (float)
    //   132: DebugCircleCenterY (float)
    //   136: DebugCircleRadius (float)
    //   140: DebugCircleThickness (float)
    //   144: DebugBoxTopLeftX (float)
    //   148: DebugBoxTopLeftY (float)
    //   152: DebugBoxSizeX (float)
    //   156: DebugBoxSizeY (float)
    //   160: DebugBoxThickness (float)
    //   164: DebugShapeIntensity (float)
    //   168: _pad9 (float)
    //   172: FocalLength (float)
    //   176: SelectionCircleEnabled (int)
    //   180: SelectionStarHash (float)
    //   184: _padSelection2 (float)
    //   188: _padSelection3 (float)
    //   192: SelectionCircleCenterX (float)
    //   196: SelectionCircleCenterY (float)
    //   200: SelectionCircleT (float)
    //   204: SelectionCircleIntensity (float)
    //   208: SelectionCircleThickness (float)
    //   212: SelectionCircleRadius (float)
    //   216: _padSelection4 (float)
    //   220: _padSelection5 (float)
    //   224: _padSelection6 (float)
    //   228: _padSelection7 (float)
    //   232: TextOriginX (float)
    //   236: TextOriginY (float)
    //   240: TextAreaSizeX (float)
    //   244: TextAreaSizeY (float)
    //   248: SelectionTextT (float)
    //   252: GridLabelEnabledMask (uint)
    //   256: _padGridMask1 (float)
    //   260: _padGridMask2 (float)
    //   264: _padGridMask3 (float)
    //   268: _padGridMask4 (float)
    //   272: GridLabel0_PosTangentX (float4)
    //   288: GridLabel0_TangentY (float4)
    //   304: GridLabel1_PosTangentX (float4)
    //   320: GridLabel1_TangentY (float4)
    //   336: GridLabel2_PosTangentX (float4)
    //   352: GridLabel2_TangentY (float4)
    //   368: GridLabel3_PosTangentX (float4)
    //   384: GridLabel3_TangentY (float4)
    //   400: GridLabel4_PosTangentX (float4)
    //   416: GridLabel4_TangentY (float4)
    //   432: GridLabel5_PosTangentX (float4)
    //   448: GridLabel5_TangentY (float4)
    //   464: GridLabel6_PosTangentX (float4)
    //   480: GridLabel6_TangentY (float4)
    //   496: GridLabel7_PosTangentX (float4)
    //   512: GridLabel7_TangentY (float4)
    //   528: GridLabelDebugMask (uint)
    //   532: LabelIntensity0 (float)
    //   536: LabelIntensity1 (float)
    //   540: LabelIntensity2 (float)
    //   544: LabelIntensity3 (float)
    //   548: LabelIntensity4 (float)
    //   552: LabelIntensity5 (float)
    //   556: LabelIntensity6 (float)
    //   560: LabelIntensity7 (float)
    //   564: LabelColor0 (uint)
    //   568: LabelColor1 (uint)
    //   572: LabelColor2 (uint)
    //   576: LabelColor3 (uint)
    //   580: LabelColor4 (uint)
    //   584: LabelColor5 (uint)
    //   588: LabelColor6 (uint)
    //   592: LabelColor7 (uint)

    float ResolutionX;
    float ResolutionY;
    float Time;
    float GridIntensity;
    float GridThickness;
    float ChromaticAberrationStrength;
    float VignetteStrength;
    float VignetteStart;
    float VignetteEnd;
    float PreRotationYaw;
    float PreRotationPitch;
    int GridSizePreset;
    int GridColorIndex;
    float _pad1;
    float _pad2;
    float _padAlignCamera;
    float CameraRightX;
    float CameraRightY;
    float CameraRightZ;
    float _pad3;
    float CameraUpX;
    float CameraUpY;
    float CameraUpZ;
    float _pad4;
    float CameraForwardX;
    float CameraForwardY;
    float CameraForwardZ;
    float _pad5;
    int DebugShapesEnabled;
    float _pad6;
    float _pad7;
    float _pad8;
    float DebugCircleCenterX;
    float DebugCircleCenterY;
    float DebugCircleRadius;
    float DebugCircleThickness;
    float DebugBoxTopLeftX;
    float DebugBoxTopLeftY;
    float DebugBoxSizeX;
    float DebugBoxSizeY;
    float DebugBoxThickness;
    float DebugShapeIntensity;
    float _pad9;
    float FocalLength;
    int SelectionCircleEnabled;
    float SelectionStarHash;
    float _padSelection2;
    float _padSelection3;
    float SelectionCircleCenterX;
    float SelectionCircleCenterY;
    float SelectionCircleT;
    float SelectionCircleIntensity;
    float SelectionCircleThickness;
    float SelectionCircleRadius;
    float _padSelection4;
    float _padSelection5;
    float _padSelection6;
    float _padSelection7;
    float TextOriginX;
    float TextOriginY;
    float TextAreaSizeX;
    float TextAreaSizeY;
    float SelectionTextT;
    uint32_t GridLabelEnabledMask;
    float _padGridMask1;
    float _padGridMask2;
    float _padGridMask3;
    float _padGridMask4;
    float4 GridLabel0_PosTangentX;
    float4 GridLabel0_TangentY;
    float4 GridLabel1_PosTangentX;
    float4 GridLabel1_TangentY;
    float4 GridLabel2_PosTangentX;
    float4 GridLabel2_TangentY;
    float4 GridLabel3_PosTangentX;
    float4 GridLabel3_TangentY;
    float4 GridLabel4_PosTangentX;
    float4 GridLabel4_TangentY;
    float4 GridLabel5_PosTangentX;
    float4 GridLabel5_TangentY;
    float4 GridLabel6_PosTangentX;
    float4 GridLabel6_TangentY;
    float4 GridLabel7_PosTangentX;
    float4 GridLabel7_TangentY;
    uint32_t GridLabelDebugMask;
    float LabelIntensity0;
    float LabelIntensity1;
    float LabelIntensity2;
    float LabelIntensity3;
    float LabelIntensity4;
    float LabelIntensity5;
    float LabelIntensity6;
    float LabelIntensity7;
    uint32_t LabelColor0;
    uint32_t LabelColor1;
    uint32_t LabelColor2;
    uint32_t LabelColor3;
    uint32_t LabelColor4;
    uint32_t LabelColor5;
    uint32_t LabelColor6;
    uint32_t LabelColor7;
    
    // Vessel Target Selector - separate from Star Selector (96 bytes)
    int VesselTargetEnabled;
    float VesselTargetHash;
    float _padVessel1;
    float _padVessel2;
    float VesselTargetCircleCenterX;
    float VesselTargetCircleCenterY;
    float VesselTargetCircleT;
    float VesselTargetCircleIntensity;
    float VesselTargetCircleThickness;
    float VesselTargetCircleRadius;
    float _padVessel3;
    float _padVessel4;
    float _padVessel5;
    float _padVessel6;
    float VesselTargetBoxTopLeftX;
    float VesselTargetBoxTopLeftY;
    float VesselTargetBoxSizeX;
    float VesselTargetBoxSizeY;
    float VesselTargetBoxThickness;
    float _padVessel7;
    float VesselTargetTextOriginX;
    float VesselTargetTextOriginY;
    float VesselTargetTextAreaSizeX;
    float VesselTargetTextAreaSizeY;
    float VesselTargetTextT;
};

static_assert(sizeof(KartographerParamsNative) == 704,
              "KartographerParamsNative size mismatch - expected 704 bytes");
static_assert(sizeof(KartographerParamsNative) % 16 == 0,
              "KartographerParamsNative must be 16-byte aligned for HLSL CB");

// Set Kartographer visual parameters
__declspec(dllexport) void CR_StarfieldSetKartographerParams(const KartographerParamsNative* params);

// Set grid label texture for a specific slot (0-7)
__declspec(dllexport) void CR_SetGridLabelTexture(int slot, ID3D11Texture2D* texture);

// Clear/reset a grid label slot to empty state (safe to call anytime)
__declspec(dllexport) void CR_ClearGridLabelSlot(int slot);

// Cubemap rendering - renders starfield to all 6 cubemap faces
// targetTextures: array of 6 D3D11 textures (one per face)
// faceSize: resolution of each face (e.g., 1024)
// Returns: 0 on success, non-zero on error
__declspec(dllexport) int CR_RenderStarfieldCubemap(ID3D11Texture2D* targetTextures[6], int faceSize);

#ifdef __cplusplus
}
#endif
