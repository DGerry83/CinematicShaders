#pragma once
#include <d3d11.h>

// GalaxyCam Compositor - Simple render layer system
// Layers are rendered in priority order (lowest first)
// This header is for internal native use - no C# interop needed

enum class GalaxyCamBlendMode {
    Opaque = 0,      // Replace destination
    Additive = 1,    // Add to destination (src + dst)
    AlphaBlend = 2   // Standard alpha blend
};

// Layer render callback signature
// context: D3D11 device context
// renderTarget: The current render target view
// width, height: Render target dimensions
// userData: User data passed at registration
using GalaxyCamLayerCallback = void(*)(
    ID3D11DeviceContext* context,
    ID3D11RenderTargetView* renderTarget,
    int width, int height,
    void* userData);

struct GalaxyCamLayer {
    const char* name;
    int priority;                       // Lower = earlier
    GalaxyCamBlendMode blendMode;
    GalaxyCamLayerCallback callback;
    void* userData;
    int layerId;
    bool enabled;
};

// Internal functions for native modules to use
// These are NOT exported to C# - they're for inter-native communication

// Register a render layer. Returns layer ID (>=0) or -1 on failure.
// Must be called AFTER the starfield device is initialized.
int GalaxyCamCompositor_RegisterLayer(
    const char* name,
    int priority,
    GalaxyCamBlendMode blendMode,
    GalaxyCamLayerCallback callback,
    void* userData);

// Unregister a layer
void GalaxyCamCompositor_UnregisterLayer(int layerId);

// Enable/disable a layer
void GalaxyCamCompositor_SetLayerEnabled(int layerId, bool enabled);

// Render all registered layers - called from StarfieldNative's render callback
// This is the main entry point that executes all layers in priority order
void GalaxyCamCompositor_RenderLayers(
    ID3D11DeviceContext* context,
    ID3D11RenderTargetView* renderTarget,
    int width, int height);

// Check if compositor has any registered layers
bool GalaxyCamCompositor_HasLayers();
