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

// Star catalog entry - 44 bytes, 4-byte aligned for GPU StructuredBuffer
// Layout matches C# StarDataNative and HLSL StarData exactly
// Version 3: Added HipparcosID, DistancePc, and SpectralType
struct StarData {
    int32_t HipparcosID;   // 4 bytes - Hipparcos catalog ID (0 if not from real catalog)
    float DistancePc;      // 4 bytes - Distance in parsecs (0 if unknown)
    int32_t SpectralType;  // 4 bytes - 0=O,1=B,2=A,3=F,4=G,5=K,6=M,7=L,255=Unknown
    
    float DirectionX;      // 4 bytes
    float DirectionY;      // 4 bytes  
    float DirectionZ;      // 4 bytes
    float Magnitude;       // 4 bytes - Absolute magnitude (brightness)
    
    float ColorR;          // 4 bytes - RGB color (already blackbody-corrected)
    float ColorG;          // 4 bytes
    float ColorB;          // 4 bytes
    float Temperature;     // 4 bytes - Kelvin, for future PSF shader use
    
    // Utility constructor for C++ generation code
    StarData() : HipparcosID(0), DistancePc(0.0f), SpectralType(255), 
                 DirectionX(0), DirectionY(0), DirectionZ(0), Magnitude(10.0f),
                 ColorR(1.0f), ColorG(1.0f), ColorB(1.0f), Temperature(5778.0f) {}
                 
    StarData(int32_t hip, float dist, int32_t spectral, float dx, float dy, float dz, float mag, float r, float g, float b, float temp)
        : HipparcosID(hip), DistancePc(dist), SpectralType(spectral),
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
    float RedGiantRarity;
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
};

__declspec(dllexport) void CR_StarfieldSetCameraMatrices(
    ID3D11Texture2D* deviceSourceTexture,  // Any D3D11 texture to query device from (e.g., whiteTexture)
    int width, 
    int height,
    float verticalFOV,
    float aspectRatio,
    float3 cameraRight,
    float3 cameraUp,
    float3 cameraForward
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

#ifdef __cplusplus
}
#endif