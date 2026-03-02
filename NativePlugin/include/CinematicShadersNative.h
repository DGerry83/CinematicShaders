// CinematicShadersNative.h
// Public C interface for Cinematic Shaders native plugin
#pragma once

#include <Windows.h>
#include <d3d11.h>

#define UNITY_INTERFACE_API __stdcall
#define UNITY_INTERFACE_EXPORT __declspec(dllexport)

typedef void (UNITY_INTERFACE_API * UnityRenderingEvent)(int eventId);

#ifdef __cplusplus
extern "C" {
#endif

__declspec(dllexport)
void CR_GTAODebugSetInput(
    ID3D11Texture2D* depthTex, 
    ID3D11Texture2D* normalTex, 
    int width, 
    int height,
    const float* worldToView, 
    const float* fovParams,  // tanHalfFOV [x, y]
    float nearPlane, 
    float farPlane,
    int frameIndex
);

__declspec(dllexport)
UnityRenderingEvent CR_GetGTAORenderEventFunc();

__declspec(dllexport)
void CR_GTAOSetOutputMode(int mode);

__declspec(dllexport)
void CR_GTAOShutdown();

typedef struct {
    float EffectRadius;
    float Intensity;
    int SliceCount;
    int StepsPerSlice;
    float SampleDistributionPower;
    float NormalPower;
    float DepthSigma;
    float MaxPixelRadius;
    float FadeStartDistance;
    float FadeEndDistance;
    float FadeCurve;
    float NormalSimilarityPower;      
    float NormalSimilarityThreshold;
} GTAOSettings;

__declspec(dllexport)
void CR_GTAOSetSettings(const GTAOSettings* settings);

#ifdef __cplusplus
}
#endif