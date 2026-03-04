#pragma once
#include <d3d11.h>
#include <mutex>
#include "CinematicShadersNative.h"  // For UNITY_INTERFACE_API, UnityRenderingEvent

// Define HLSL-style vector types for C interface
struct float3 {
    float x, y, z;
    float3() : x(0), y(0), z(0) {}
    float3(float _x, float _y, float _z) : x(_x), y(_y), z(_z) {}
};

#ifdef __cplusplus
extern "C" {
#endif

// Settings struct matching C#
struct StarfieldSettingsNative {
    float Exposure;
    float BlurPixels;
    float StarDensity;
    float MinMagnitude;
    float MaxMagnitude;
    float MagnitudeBias;
    float HeroRarity;
    float Clustering;
    float StaggerAmount;
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
};

__declspec(dllexport) void CR_StarfieldSetCameraMatrices(
    ID3D11Texture2D* depthTex,
    ID3D11Texture2D* normalTex,
    int width, 
    int height,
    float verticalFOV,
    float aspectRatio,
    float3 cameraRight,      // New: World-space right vector
    float3 cameraUp,         // New: World-space up vector  
    float3 cameraForward     // New: World-space forward vector (direction camera looks)
);

__declspec(dllexport) void CR_StarfieldSetSettings(const StarfieldSettingsNative* settings);

__declspec(dllexport) UnityRenderingEvent CR_GetStarfieldRenderEventFunc();

__declspec(dllexport) void CR_StarfieldShutdown();

#ifdef __cplusplus
}
#endif