#include "GalaxyCamCompositor.h"
#include <vector>
#include <algorithm>
#include <mutex>

static struct {
    std::vector<GalaxyCamLayer> layers;
    std::mutex mutex;
    int nextLayerId = 1;
    
    // D3D11 blend states for different blend modes
    ID3D11BlendState* blendStates[3] = { nullptr, nullptr, nullptr };
    bool blendStatesInitialized = false;
} g_Compositor;

static void EnsureBlendStates(ID3D11Device* device) {
    if (g_Compositor.blendStatesInitialized) return;
    if (!device) return;
    
    // Opaque: no blending
    D3D11_BLEND_DESC desc = {};
    desc.RenderTarget[0].BlendEnable = FALSE;
    desc.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
    device->CreateBlendState(&desc, &g_Compositor.blendStates[0]);
    
    // Additive: src + dst
    desc.RenderTarget[0].BlendEnable = TRUE;
    desc.RenderTarget[0].SrcBlend = D3D11_BLEND_ONE;
    desc.RenderTarget[0].DestBlend = D3D11_BLEND_ONE;
    desc.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
    desc.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
    desc.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ONE;
    desc.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
    device->CreateBlendState(&desc, &g_Compositor.blendStates[1]);
    
    // AlphaBlend: src.a * src + (1-src.a) * dst
    desc.RenderTarget[0].SrcBlend = D3D11_BLEND_SRC_ALPHA;
    desc.RenderTarget[0].DestBlend = D3D11_BLEND_INV_SRC_ALPHA;
    device->CreateBlendState(&desc, &g_Compositor.blendStates[2]);
    
    g_Compositor.blendStatesInitialized = true;
}

int GalaxyCamCompositor_RegisterLayer(
    const char* name,
    int priority,
    GalaxyCamBlendMode blendMode,
    GalaxyCamLayerCallback callback,
    void* userData)
{
    if (!callback) return -1;
    
    std::lock_guard<std::mutex> lock(g_Compositor.mutex);
    
    GalaxyCamLayer layer;
    layer.name = name;
    layer.priority = priority;
    layer.blendMode = blendMode;
    layer.callback = callback;
    layer.userData = userData;
    layer.layerId = g_Compositor.nextLayerId++;
    layer.enabled = true;
    
    g_Compositor.layers.push_back(layer);
    
    // Sort by priority (lower number = earlier)
    std::sort(g_Compositor.layers.begin(), g_Compositor.layers.end(),
        [](const GalaxyCamLayer& a, const GalaxyCamLayer& b) {
            return a.priority < b.priority;
        });
    
    return layer.layerId;
}

void GalaxyCamCompositor_UnregisterLayer(int layerId) {
    std::lock_guard<std::mutex> lock(g_Compositor.mutex);
    
    auto it = std::remove_if(g_Compositor.layers.begin(), g_Compositor.layers.end(),
        [layerId](const GalaxyCamLayer& layer) { return layer.layerId == layerId; });
    
    g_Compositor.layers.erase(it, g_Compositor.layers.end());
}

void GalaxyCamCompositor_SetLayerEnabled(int layerId, bool enabled) {
    std::lock_guard<std::mutex> lock(g_Compositor.mutex);
    
    for (auto& layer : g_Compositor.layers) {
        if (layer.layerId == layerId) {
            layer.enabled = enabled;
            break;
        }
    }
}

void GalaxyCamCompositor_RenderLayers(
    ID3D11DeviceContext* context,
    ID3D11RenderTargetView* renderTarget,
    int width, int height)
{
    extern void LogToFile(const char* fmt, ...);
    static bool firstCall = true;
    
    if (!context || !renderTarget) return;
    
    std::lock_guard<std::mutex> lock(g_Compositor.mutex);
    
    if (g_Compositor.layers.empty()) return;
    
    if (firstCall) {
        LogToFile("[Compositor] First render with %zu layers", g_Compositor.layers.size());
        firstCall = false;
    }
    
    // Save current blend state
    ID3D11BlendState* oldBlendState = nullptr;
    float oldBlendFactor[4];
    UINT oldSampleMask;
    context->OMGetBlendState(&oldBlendState, oldBlendFactor, &oldSampleMask);
    
    // Render each enabled layer in priority order
    for (const auto& layer : g_Compositor.layers) {
        if (!layer.enabled || !layer.callback) continue;
        
        // Set blend state for this layer
        int blendModeIdx = static_cast<int>(layer.blendMode);
        if (g_Compositor.blendStates[blendModeIdx]) {
            context->OMSetBlendState(g_Compositor.blendStates[blendModeIdx], nullptr, 0xFFFFFFFF);
        }
        
        // Call layer's render callback
        layer.callback(context, renderTarget, width, height, layer.userData);
    }
    
    // Restore original blend state
    context->OMSetBlendState(oldBlendState, oldBlendFactor, oldSampleMask);
    if (oldBlendState) oldBlendState->Release();
}

bool GalaxyCamCompositor_HasLayers() {
    std::lock_guard<std::mutex> lock(g_Compositor.mutex);
    return !g_Compositor.layers.empty();
}
