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

// Kartographer visual parameters struct (Phase 2 - 8 Label Support)
// Layout matches HLSL exactly - 544 bytes (16 × 34)
// Do not modify without updating shader
struct KartographerParamsNative {
    // Base grid params (64 bytes) - offsets 0-63
    float ResolutionX;              // offset 0
    float ResolutionY;              // offset 4
    float Time;                     // offset 8
    float GridIntensity;            // offset 12
    float GridThickness;            // offset 16
    float ChromaticAberrationStrength;  // offset 20
    float VignetteStrength;         // offset 24
    float VignetteStart;            // offset 28
    float VignetteEnd;              // offset 32
    float PreRotationYaw;           // offset 36
    float PreRotationPitch;         // offset 40
    int GridSizePreset;             // offset 44
    int GridColorIndex;             // offset 48
    float _pad1;                    // offset 52
    float _pad2;                    // offset 56
    float _padAlignCamera;          // offset 60
    
    // Camera basis (48 bytes) - offsets 64-111
    float CameraRightX;             // offset 64
    float CameraRightY;             // offset 68
    float CameraRightZ;             // offset 72
    float _pad3;                    // offset 76
    float CameraUpX;                // offset 80
    float CameraUpY;                // offset 84
    float CameraUpZ;                // offset 88
    float _pad4;                    // offset 92
    float CameraForwardX;           // offset 96
    float CameraForwardY;           // offset 100
    float CameraForwardZ;           // offset 104
    float _pad5;                    // offset 108
    
    // Debug shapes (32 bytes) - offsets 112-143
    int DebugShapesEnabled;         // offset 112
    float _pad6;                    // offset 116
    float _pad7;                    // offset 120
    float _pad8;                    // offset 124
    float DebugCircleCenterX;       // offset 128
    float DebugCircleCenterY;       // offset 132
    float DebugCircleRadius;        // offset 136
    float DebugCircleThickness;     // offset 140
    float DebugBoxTopLeftX;         // offset 144
    float DebugBoxTopLeftY;         // offset 148
    float DebugBoxSizeX;            // offset 152
    float DebugBoxSizeY;            // offset 156
    float DebugBoxThickness;        // offset 160
    float DebugShapeIntensity;      // offset 164
    float _pad9;                    // offset 168
    float FocalLength;              // offset 172
    
    // Selection circle (32 bytes) - offsets 176-207
    int SelectionCircleEnabled;     // offset 176
    float SelectionStarHash;        // offset 180
    float _padSelection2;           // offset 184
    float _padSelection3;           // offset 188
    float SelectionCircleCenterX;   // offset 192
    float SelectionCircleCenterY;   // offset 196
    float SelectionCircleT;         // offset 200
    float SelectionCircleIntensity; // offset 204
    float SelectionCircleThickness; // offset 208
    float SelectionCircleRadius;    // offset 212
    float _padSelection4;           // offset 216
    float _padSelection5;           // offset 220
    float _padSelection6;           // offset 224
    float _padSelection7;           // offset 228
    
    // Text stub (16 bytes) - offsets 232-247
    float TextOriginX;              // offset 232
    float TextOriginY;              // offset 236
    float TextAreaSizeX;            // offset 240
    float TextAreaSizeY;            // offset 244
    float SelectionTextT;           // offset 248
    
    // Grid Labels (8 labels) - offsets 252-543
    // Bitmask for enabled labels (bit 0 = label 0, bit 1 = label 1, etc.)
    unsigned int GridLabelEnabledMask;  // offset 252
    float _padGridMask1;            // offset 256
    float _padGridMask2;            // offset 260
    float _padGridMask3;            // offset 264
    
    // Label 0 (32 bytes) - offsets 268-299
    float GridLabel0_PosX;          // offset 268
    float GridLabel0_PosY;          // offset 272
    float GridLabel0_PosZ;          // offset 276
    float GridLabel0_SizeX;         // offset 280
    float GridLabel0_TangentX;      // offset 284
    float GridLabel0_TangentY;      // offset 288
    float GridLabel0_TangentZ;      // offset 292
    float GridLabel0_SizeY;         // offset 296
    
    // Label 1 (32 bytes) - offsets 300-331
    float GridLabel1_PosX;          // offset 300
    float GridLabel1_PosY;          // offset 304
    float GridLabel1_PosZ;          // offset 308
    float GridLabel1_SizeX;         // offset 312
    float GridLabel1_TangentX;      // offset 316
    float GridLabel1_TangentY;      // offset 320
    float GridLabel1_TangentZ;      // offset 324
    float GridLabel1_SizeY;         // offset 328
    
    // Label 2 (32 bytes) - offsets 332-363
    float GridLabel2_PosX;          // offset 332
    float GridLabel2_PosY;          // offset 336
    float GridLabel2_PosZ;          // offset 340
    float GridLabel2_SizeX;         // offset 344
    float GridLabel2_TangentX;      // offset 348
    float GridLabel2_TangentY;      // offset 352
    float GridLabel2_TangentZ;      // offset 356
    float GridLabel2_SizeY;         // offset 360
    
    // Label 3 (32 bytes) - offsets 364-395
    float GridLabel3_PosX;          // offset 364
    float GridLabel3_PosY;          // offset 368
    float GridLabel3_PosZ;          // offset 372
    float GridLabel3_SizeX;         // offset 376
    float GridLabel3_TangentX;      // offset 380
    float GridLabel3_TangentY;      // offset 384
    float GridLabel3_TangentZ;      // offset 388
    float GridLabel3_SizeY;         // offset 392
    
    // Label 4 (32 bytes) - offsets 396-427
    float GridLabel4_PosX;          // offset 396
    float GridLabel4_PosY;          // offset 400
    float GridLabel4_PosZ;          // offset 404
    float GridLabel4_SizeX;         // offset 408
    float GridLabel4_TangentX;      // offset 412
    float GridLabel4_TangentY;      // offset 416
    float GridLabel4_TangentZ;      // offset 420
    float GridLabel4_SizeY;         // offset 424
    
    // Label 5 (32 bytes) - offsets 428-459
    float GridLabel5_PosX;          // offset 428
    float GridLabel5_PosY;          // offset 432
    float GridLabel5_PosZ;          // offset 436
    float GridLabel5_SizeX;         // offset 440
    float GridLabel5_TangentX;      // offset 444
    float GridLabel5_TangentY;      // offset 448
    float GridLabel5_TangentZ;      // offset 452
    float GridLabel5_SizeY;         // offset 456
    
    // Label 6 (32 bytes) - offsets 460-491
    float GridLabel6_PosX;          // offset 460
    float GridLabel6_PosY;          // offset 464
    float GridLabel6_PosZ;          // offset 468
    float GridLabel6_SizeX;         // offset 472
    float GridLabel6_TangentX;      // offset 476
    float GridLabel6_TangentY;      // offset 480
    float GridLabel6_TangentZ;      // offset 484
    float GridLabel6_SizeY;         // offset 488
    
    // Label 7 (32 bytes) - offsets 492-523
    float GridLabel7_PosX;          // offset 492
    float GridLabel7_PosY;          // offset 496
    float GridLabel7_PosZ;          // offset 500
    float GridLabel7_SizeX;         // offset 504
    float GridLabel7_TangentX;      // offset 508
    float GridLabel7_TangentY;      // offset 512
    float GridLabel7_TangentZ;      // offset 516
    float GridLabel7_SizeY;         // offset 520
    
    // Final padding to reach 544 bytes (16 × 34)
    float _padEnd1;                 // offset 524
    float _padEnd2;                 // offset 528
    float _padEnd3;                 // offset 532
    float _padEnd4;                 // offset 536
    float _padEnd5;                 // offset 540
    float _padEnd6;                 // offset 544
};

// Set Kartographer visual parameters
__declspec(dllexport) void CR_StarfieldSetKartographerParams(const KartographerParamsNative* params);

// Set grid label texture for a specific slot (0-7)
__declspec(dllexport) void CR_SetGridLabelTexture(int slot, ID3D11Texture2D* texture);

// Cubemap rendering - renders starfield to all 6 cubemap faces
// targetTextures: array of 6 D3D11 textures (one per face)
// faceSize: resolution of each face (e.g., 1024)
// Returns: 0 on success, non-zero on error
__declspec(dllexport) int CR_RenderStarfieldCubemap(ID3D11Texture2D* targetTextures[6], int faceSize);

#ifdef __cplusplus
}
#endif
