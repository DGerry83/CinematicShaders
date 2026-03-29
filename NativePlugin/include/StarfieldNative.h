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

// Kartographer visual parameters struct (Phase 2 expanded - 256 bytes)
// Layout matches HLSL exactly - do not modify without updating shader
struct KartographerParamsNative {
    // Base grid params (64 bytes) - offsets 0-63
    float ResolutionX;              // offset 0
    float ResolutionY;              // offset 4
    float Time;                     // offset 8
    float GridIntensity;            // offset 12
    float GridThickness;            // offset 16
    float ChromaticAberrationStrength; // offset 20
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
    float _pad10;                   // offset 172
    
    // Selection circle (32 bytes) - offsets 176-207
    int SelectionCircleEnabled;     // offset 176
    float _padSelection1;           // offset 180
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
    
    // Text stub (16 bytes) - offsets 232-247
    float TextOriginX;              // offset 232
    float TextOriginY;              // offset 236
    float TextAreaSizeX;            // offset 240
    float TextAreaSizeY;            // offset 244
    float SelectionTextT;           // offset 248
    float _pad12;                   // offset 252
};

// Set Kartographer visual parameters
__declspec(dllexport) void CR_StarfieldSetKartographerParams(const KartographerParamsNative* params);

// Cubemap rendering - renders starfield to all 6 cubemap faces
// targetTextures: array of 6 D3D11 textures (one per face)
// faceSize: resolution of each face (e.g., 1024)
// Returns: 0 on success, non-zero on error
__declspec(dllexport) int CR_RenderStarfieldCubemap(ID3D11Texture2D* targetTextures[6], int faceSize);

#ifdef __cplusplus
}
#endif