#pragma once
#include <d3d11.h>

// Unity rendering event typedef (same as in CinematicShadersNative.h)
#define UNITY_INTERFACE_API __stdcall
typedef void (UNITY_INTERFACE_API * UnityRenderingEvent)(int eventId);

// Kartographer Native Plugin - Holographic grid visualizer
// Exports for C# interop

#ifdef __cplusplus
extern "C" {
#endif

// Set camera matrices and parameters for rendering
// Must be called before each frame render
__declspec(dllexport) void CR_KartographerSetCameraMatrices(
    ID3D11Texture2D* deviceSourceTexture,  // Any D3D11 texture to query device from
    int width,
    int height,
    float verticalFOV,
    float aspectRatio,
    float cameraRightX, float cameraRightY, float cameraRightZ,
    float cameraUpX, float cameraUpY, float cameraUpZ,
    float cameraForwardX, float cameraForwardY, float cameraForwardZ
);

// Get the render event function for CommandBuffer.IssuePluginEvent
__declspec(dllexport) UnityRenderingEvent CR_GetKartographerRenderEventFunc();

// Check if device is ready for rendering
__declspec(dllexport) unsigned char CR_KartographerIsDeviceReady();

// Cleanup resources
__declspec(dllexport) void CR_KartographerShutdown();

#ifdef __cplusplus
}
#endif
