#pragma once

#include <Windows.h>
#include <d3d11.h>

// Unity native plugin interface types
#define UNITY_INTERFACE_API __stdcall
#define UNITY_INTERFACE_EXPORT __declspec(dllexport)

typedef void (UNITY_INTERFACE_API * UnityRenderingEvent)(int eventId);

#ifdef __cplusplus
extern "C" {
#endif

// Set input textures and matrices for GTAO computation
// Must be called before the render event each frame
__declspec(dllexport)
void CR_GTAODebugSetInput(
    ID3D11Texture2D* depthTex, 
    ID3D11Texture2D* normalTex, 
    int width, 
    int height,
    const float* invProj, 
    const float* worldToView, 
    float nearPlane, 
    float farPlane,
    int frameIndex
);

// Get the Unity render event callback function
// Use with IssuePluginEvent in Unity CommandBuffer
__declspec(dllexport)
UnityRenderingEvent CR_GetGTAORenderEventFunc();

// Set output mode: 0 = Composite AO over scene, 1 = Raw AO only (debug)
__declspec(dllexport)
void CR_GTAOSetOutputMode(int mode);

// Cleanup all GTAO resources (call when mod unloads or scene changes)
__declspec(dllexport)
void CR_GTAOShutdown();

// Settings struct for Phase 1 UI control
typedef struct {
    float EffectRadius;
    float Intensity;
    int SliceCount;
    int StepsPerSlice;
    float SampleDistributionPower;
    float NormalPower;
    float DepthSigma;
} GTAOSettings;

__declspec(dllexport)
void CR_GTAOSetSettings(const GTAOSettings* settings);

#ifdef __cplusplus
}
#endif