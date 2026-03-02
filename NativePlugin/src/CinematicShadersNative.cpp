#include <windows.h>
#include <d3d11.h>
#include <mutex>
#include <algorithm>
#include <cstring>
#include <ctime>
#include <cstdio>
#include <fstream>
#include <iomanip>
#include <cmath>

#ifndef BYTE
typedef unsigned char BYTE;
#endif

#include "EmbeddedResources.h"

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")

// Unity interface macros (in case headers don't define them)
#ifndef UNITY_INTERFACE_API
#define UNITY_INTERFACE_API __stdcall
#define UNITY_INTERFACE_EXPORT __declspec(dllexport)
typedef void (UNITY_INTERFACE_API * UnityRenderingEvent)(int eventId);
#endif

// ============================================================================
// Logging Infrastructure
// ============================================================================
static std::mutex g_logMutex;
static std::ofstream g_logFile;
static bool g_logInitialized = false;

static void InitLogFile()
{
    std::lock_guard<std::mutex> lock(g_logMutex);
    if (g_logInitialized) return;
    
    char dllPath[MAX_PATH] = {0};
    HMODULE hModule = NULL;
    
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | 
                           GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           (LPCSTR)&InitLogFile, &hModule))
    {
        GetModuleFileNameA(hModule, dllPath, MAX_PATH);
    }
    
    if (strlen(dllPath) > 0)
    {
        char* lastSlash = strrchr(dllPath, '\\');
        if (lastSlash)
        {
            *lastSlash = '\0';
            char* secondLastSlash = strrchr(dllPath, '\\');
            if (secondLastSlash)
            {
                *(secondLastSlash + 1) = '\0';
                strcat_s(dllPath, MAX_PATH, "CinematicShaders_Native.log");
                g_logFile.open(dllPath, std::ios::app);
            }
        }
    }
    
    if (!g_logFile.is_open())
    {
        g_logFile.open("CinematicShaders_Native.log", std::ios::app);
    }
    
    g_logInitialized = true;
    
    if (g_logFile.is_open())
    {
        auto now = std::time(nullptr);
        auto tm = *std::localtime(&now);
        g_logFile << "\n=== GTAO Session started at " 
                  << std::put_time(&tm, "%Y-%m-%d %H:%M:%S") 
                  << " ===\n" << std::flush;
    }
}

static void LogToFile(const char* fmt, ...)
{
    InitLogFile();
    
    std::lock_guard<std::mutex> lock(g_logMutex);
    if (!g_logFile.is_open()) return;
    
    auto now = std::time(nullptr);
    auto tm = *std::localtime(&now);
    g_logFile << "[" << std::put_time(&tm, "%H:%M:%S") << "] ";
    
    char buffer[1024];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buffer, sizeof(buffer), fmt, args);
    va_end(args);
    
    g_logFile << buffer << std::endl << std::flush;
}


// ============================================================================
// GTAO Debug Test - Full GTAO compute shader
// ============================================================================

#include "GTAO.h"  // Compiled compute shader bytecode
#include "GTAO_Filter.h"  // Compiled filter shader bytecode
#include "GTAO_Output_VS.h"  // Output blit vertex shader
#include "GTAO_Output_PS.h"  // Output blit pixel shader
#include "HiZ.h"

static struct {
    // GTAO State - renamed from g_GTAOState
    ID3D11Texture2D* depthTexture = nullptr;
    ID3D11Texture2D* normalTexture = nullptr;
    int width = 0;
    int height = 0;
    float worldToView[9] = {};     // World-to-view matrix (3x3 rotation)
    float nearPlane = 0.1f;        // Camera near plane
    float farPlane = 1000.0f;      // Camera far plane
    int frameIndex = 0;            // For temporal noise (0-7 cycle)
    ID3D11Texture2D* blueNoiseTexture = nullptr;  // Cached blue noise texture
    ID3D11ShaderResourceView* blueNoiseSRV = nullptr;  // Cached SRV
    
    // Filter Resources (created once, cached)
    ID3D11Texture2D* filteredAOTexture = nullptr;
    ID3D11UnorderedAccessView* filteredUAV = nullptr;
    ID3D11ComputeShader* filterShader = nullptr;
    ID3D11Buffer* filterCB = nullptr;
    
    // Filter intermediate resources (for ping-pong between passes)
    ID3D11Texture2D* filterIntermediateTexture = nullptr;
    ID3D11UnorderedAccessView* filterIntermediateUAV = nullptr;
    ID3D11UnorderedAccessView* filteredAOUAV = nullptr;
    
    // Output Blit Resources (created once, cached)
    ID3D11VertexShader* outputVS = nullptr;
    ID3D11PixelShader* outputPS = nullptr;
    ID3D11SamplerState* outputSampler = nullptr;
    ID3D11Buffer* outputCB = nullptr;      // For mode parameter
    
    // Intermediate texture for composite (avoids read-write hazard)
    ID3D11Texture2D* intermediateTexture = nullptr;
    ID3D11ShaderResourceView* intermediateSRV = nullptr;
    
    // State objects (cached)
    ID3D11DepthStencilState* dsState = nullptr;
    ID3D11BlendState* blendState = nullptr;
    ID3D11RasterizerState* rasterState = nullptr;
    
    // Cached compute resources (managed, not created per-frame)
    ID3D11Texture2D* aoTexture = nullptr;
    ID3D11UnorderedAccessView* aoUAV = nullptr;
    ID3D11Texture2D* hiZTexture = nullptr;
    ID3D11Buffer* gtaoCB = nullptr;
    ID3D11Buffer* hizCB = nullptr;
    ID3D11ComputeShader* hiZShaderCached = nullptr;
    ID3D11ComputeShader* gtaoShaderCached = nullptr;
    ID3D11SamplerState* pointSamplerCached = nullptr;
    
    // Cached SRVs for compute inputs
    ID3D11ShaderResourceView* depthSRVCached = nullptr;
    ID3D11ShaderResourceView* normalSRVCached = nullptr;
    
    // Current frame params (set by C#, read by render thread)
    int outputMode = 0; // 0=Composite, 1=Raw
    int debugMode = 0;  // ADD THIS: 0=AO, 1=WorldNorm, 2=ViewNorm, 3=NormAlpha
    bool paramsDirty = false;
    
    // Device pointer for render callback
    ID3D11Device* device = nullptr;
    
    int cachedWidth = 0;
    int cachedHeight = 0;
    
    // Explicit FOV parameters for view-space reconstruction
    float tanHalfFOVX = 0.0f;
    float tanHalfFOVY = 0.0f;
    
    // Thread safety for cross-thread access
    std::mutex stateMutex;
} g_GTAOState;

// User-tweakable settings for distance fade and sampling
struct GTAOUserSettings {
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
};

// Global settings storage (initialized to defaults)
static GTAOUserSettings g_UserSettings = {
    2.0f,   // EffectRadius
    0.8f,   // Intensity
    2,      // SliceCount
    4,      // StepsPerSlice
    2.0f,   // SampleDistributionPower
    32.0f,  // NormalPower
    0.5f,   // DepthSigma
    50.0f,  // MaxPixelRadius (was hardcoded)
    0.0f,   // FadeStartDistance
    500.0f, // FadeEndDistance
    1.0f    // FadeCurve
};

// Forward declarations for functions defined later in the file
static void InitializeBlueNoiseResources(ID3D11Device* device);
static void EnsureComputeResources(ID3D11Device* device, int width, int height);

// GTAO params constant buffer (matches GTAO.hlsl - XeGTAO style)
// Total: 80 bytes (5 float4s)
struct GTAOParams {
    // float4 #1 (offset 0)
    float ndcToViewMul[2];
    float ndcToViewAdd[2];
    
    // float4 #2 (offset 16)
    float depthUnpackConsts[2];
    float resolution[2];
    
    // float4 #3 (offset 32)
    float invResolution[2];
    float effectRadius;
    float maxPixelRadius;        // WAS: falloffRange - RENAMED TO MATCH HLSL
    
    // float4 #4 (offset 48)
    float intensity;
    float sampleDistributionPower;
    int sliceCount;
    int stepsPerSlice;
    
    // float4 #5 (offset 64)
    int FrameIndex;
    float depthMipSamplingOffset;
    float fadeStartDistance;     // NEW
    float fadeEndDistance;       // NEW
    
    // float4 #6 (offset 80)
    float fadeCurve;             // NEW
    int DebugMode;
    float __pad2;
    float __pad3;
    
    // float4 #7 (offset 96)
    float worldToViewRow0[4];
    
    // float4 #8 (offset 112)
    float worldToViewRow1[4];
    
    // float4 #9 (offset 128)
    float worldToViewRow2[4];
    // Total: 144 bytes
};

// Filter params constant buffer (matches GTAO_Filter.hlsl)
struct FilterParams {
    float invScreenSize[2];
    float normalPower;
    float depthSigma;
};

// Initialize filter resources (one-time, cached)
static void InitializeFilterResources(ID3D11Device* device, int width, int height)
{
    // Safer check: verify resources actually exist and match dimensions
    if (g_GTAOState.filteredAOTexture && g_GTAOState.filteredAOUAV && 
        g_GTAOState.cachedWidth == width && g_GTAOState.cachedHeight == height)
        return; // Already valid
    
    // Cleanup old if resizing (and nullify to prevent dangling pointers)
    if (g_GTAOState.filteredAOTexture) {
        g_GTAOState.filteredAOTexture->Release();
        g_GTAOState.filteredAOTexture = nullptr;
        
        if (g_GTAOState.filterIntermediateTexture) {
            g_GTAOState.filterIntermediateTexture->Release();
            g_GTAOState.filterIntermediateTexture = nullptr;
        }
        if (g_GTAOState.filterIntermediateUAV) {
            g_GTAOState.filterIntermediateUAV->Release();
            g_GTAOState.filterIntermediateUAV = nullptr;
        }
        if (g_GTAOState.filteredAOUAV) {
            g_GTAOState.filteredAOUAV->Release();
            g_GTAOState.filteredAOUAV = nullptr;
        }
        if (g_GTAOState.filterShader) {
            g_GTAOState.filterShader->Release();
            g_GTAOState.filterShader = nullptr;
        }
        if (g_GTAOState.filterCB) {
            g_GTAOState.filterCB->Release();
            g_GTAOState.filterCB = nullptr;
        }
    }
    
    // 1. Filtered AO Texture (R32_FLOAT - filter outputs single-channel AO only)
    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R32_FLOAT;  // Single channel AO (filter outputs float, not float2)
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_UNORDERED_ACCESS | D3D11_BIND_SHADER_RESOURCE;
    
    HRESULT hr = device->CreateTexture2D(&desc, nullptr, &g_GTAOState.filteredAOTexture);
    if (FAILED(hr) || !g_GTAOState.filteredAOTexture) {
        LogToFile("[GTAO] Failed to create filtered AO texture (0x%08X)", hr);
        return;
    }
    
    hr = device->CreateUnorderedAccessView(g_GTAOState.filteredAOTexture, nullptr, 
                                      &g_GTAOState.filteredAOUAV);
    if (FAILED(hr) || !g_GTAOState.filteredAOUAV) {
        LogToFile("[GTAO] Failed to create filtered AO UAV (0x%08X)", hr);
        g_GTAOState.filteredAOTexture->Release();
        g_GTAOState.filteredAOTexture = nullptr;
        return;
    }
    
    // 2. Intermediate texture for ping-pong (Pass 1 -> Pass 2)
    hr = device->CreateTexture2D(&desc, nullptr, &g_GTAOState.filterIntermediateTexture);
    if (FAILED(hr) || !g_GTAOState.filterIntermediateTexture) {
        LogToFile("[GTAO] Failed to create filter intermediate texture (0x%08X)", hr);
        // Cleanup and return
        g_GTAOState.filteredAOUAV->Release(); g_GTAOState.filteredAOUAV = nullptr;
        g_GTAOState.filteredAOTexture->Release(); g_GTAOState.filteredAOTexture = nullptr;
        return;
    }
    
    hr = device->CreateUnorderedAccessView(g_GTAOState.filterIntermediateTexture, nullptr,
                                      &g_GTAOState.filterIntermediateUAV);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create filter intermediate UAV (0x%08X)", hr);
    }
    
    // 3. Filter Compute Shader
    hr = device->CreateComputeShader(g_GTAOFilterCS, sizeof(g_GTAOFilterCS), nullptr, 
                                &g_GTAOState.filterShader);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create filter shader (0x%08X)", hr);
    }
    
    // 4. Constant Buffer (FilterParams: 16 bytes)
    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = sizeof(FilterParams);
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    hr = device->CreateBuffer(&cbDesc, nullptr, &g_GTAOState.filterCB);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create filter constant buffer (0x%08X)", hr);
    }
    
    g_GTAOState.cachedWidth = width;
    g_GTAOState.cachedHeight = height;
}

// Initialize blue noise texture (one-time, immutable)
static void InitializeBlueNoiseResources(ID3D11Device* device)
{
    if (g_GTAOState.blueNoiseTexture) return;  // Already initialized
    
    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = 256;
    desc.Height = 256;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_IMMUTABLE;
    desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    
    D3D11_SUBRESOURCE_DATA initData = {};
    initData.pSysMem = g_BlueNoise256x256R8;
    initData.SysMemPitch = 256;  // Bytes per row
    
    HRESULT hr = device->CreateTexture2D(&desc, &initData, &g_GTAOState.blueNoiseTexture);
    if (SUCCEEDED(hr) && g_GTAOState.blueNoiseTexture) {
        device->CreateShaderResourceView(g_GTAOState.blueNoiseTexture, nullptr, 
                                         &g_GTAOState.blueNoiseSRV);
    }
}

// Initialize cached compute resources (create only if null or size mismatch)
static void EnsureComputeResources(ID3D11Device* device, int width, int height)
{
    // Safer check: verify resources actually exist and match dimensions
    if (g_GTAOState.aoTexture && g_GTAOState.aoUAV && g_GTAOState.hiZTexture &&
        g_GTAOState.cachedWidth == width && g_GTAOState.cachedHeight == height)
        return;
    
    // Cleanup old resources if resizing (and nullify to prevent dangling pointers)
    if (g_GTAOState.aoTexture) {
        g_GTAOState.aoTexture->Release(); g_GTAOState.aoTexture = nullptr;
        g_GTAOState.aoUAV->Release(); g_GTAOState.aoUAV = nullptr;
        g_GTAOState.hiZTexture->Release(); g_GTAOState.hiZTexture = nullptr;
        g_GTAOState.gtaoCB->Release(); g_GTAOState.gtaoCB = nullptr;
        g_GTAOState.hizCB->Release(); g_GTAOState.hizCB = nullptr;
        if (g_GTAOState.hiZShaderCached) { g_GTAOState.hiZShaderCached->Release(); g_GTAOState.hiZShaderCached = nullptr; }
        if (g_GTAOState.gtaoShaderCached) { g_GTAOState.gtaoShaderCached->Release(); g_GTAOState.gtaoShaderCached = nullptr; }
        if (g_GTAOState.pointSamplerCached) { g_GTAOState.pointSamplerCached->Release(); g_GTAOState.pointSamplerCached = nullptr; }
        if (g_GTAOState.depthSRVCached) { g_GTAOState.depthSRVCached->Release(); g_GTAOState.depthSRVCached = nullptr; }
        if (g_GTAOState.normalSRVCached) { g_GTAOState.normalSRVCached->Release(); g_GTAOState.normalSRVCached = nullptr; }
    }
    
    // 1. AO Output Texture (RG32_FLOAT)
    D3D11_TEXTURE2D_DESC aoDesc = {};
    aoDesc.Width = width;
    aoDesc.Height = height;
    aoDesc.MipLevels = 1;
    aoDesc.ArraySize = 1;
    aoDesc.Format = DXGI_FORMAT_R32G32_FLOAT;
    aoDesc.SampleDesc.Count = 1;
    aoDesc.Usage = D3D11_USAGE_DEFAULT;
    aoDesc.BindFlags = D3D11_BIND_UNORDERED_ACCESS | D3D11_BIND_SHADER_RESOURCE;
    
    HRESULT hr = device->CreateTexture2D(&aoDesc, nullptr, &g_GTAOState.aoTexture);
    if (FAILED(hr) || !g_GTAOState.aoTexture) {
        LogToFile("[GTAO] Failed to create AO texture (0x%08X). Format: %d", hr, aoDesc.Format);
        if (hr == DXGI_ERROR_DEVICE_REMOVED) {
            LogToFile("[GTAO] Device removed! Reason: 0x%08X", device->GetDeviceRemovedReason());
        }
        return;
    }
    
    hr = device->CreateUnorderedAccessView(g_GTAOState.aoTexture, nullptr, &g_GTAOState.aoUAV);
    if (FAILED(hr) || !g_GTAOState.aoUAV) {
        LogToFile("[GTAO] Failed to create AO UAV (0x%08X)", hr);
        g_GTAOState.aoTexture->Release(); g_GTAOState.aoTexture = nullptr;
        return;
    }
    
    // 2. Hi-Z Texture with mip chain
    int hiZMipCount = (int)(log2((std::max)(width, height))) + 1;
    hiZMipCount = (std::min)(hiZMipCount, 12);
    
    D3D11_TEXTURE2D_DESC hiZDesc = {};
    hiZDesc.Width = width;
    hiZDesc.Height = height;
    hiZDesc.MipLevels = hiZMipCount;
    hiZDesc.ArraySize = 1;
    hiZDesc.Format = DXGI_FORMAT_R32_FLOAT;
    hiZDesc.SampleDesc.Count = 1;
    hiZDesc.Usage = D3D11_USAGE_DEFAULT;
    hiZDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS;
    
    hr = device->CreateTexture2D(&hiZDesc, nullptr, &g_GTAOState.hiZTexture);
    if (FAILED(hr) || !g_GTAOState.hiZTexture) {
        LogToFile("[GTAO] Failed to create Hi-Z texture (0x%08X)", hr);
        // Cleanup and return
        g_GTAOState.aoUAV->Release(); g_GTAOState.aoUAV = nullptr;
        g_GTAOState.aoTexture->Release(); g_GTAOState.aoTexture = nullptr;
        return;
    }
    
    // 3. Constant Buffers
    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = sizeof(GTAOParams);
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    
    hr = device->CreateBuffer(&cbDesc, nullptr, &g_GTAOState.gtaoCB);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create GTAO constant buffer (0x%08X)", hr);
    }
    
    cbDesc.ByteWidth = sizeof(int) * 4; // HiZParams
    hr = device->CreateBuffer(&cbDesc, nullptr, &g_GTAOState.hizCB);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create Hi-Z constant buffer (0x%08X)", hr);
    }
    
    // 4. Compute Shaders
    hr = device->CreateComputeShader(g_HiZCS, sizeof(g_HiZCS), nullptr, &g_GTAOState.hiZShaderCached);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create Hi-Z shader (0x%08X)", hr);
    }
    
    hr = device->CreateComputeShader(g_GTAOCS, sizeof(g_GTAOCS), nullptr, &g_GTAOState.gtaoShaderCached);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create GTAO shader (0x%08X)", hr);
    }
    
    // 5. Point Sampler for Hi-Z
    D3D11_SAMPLER_DESC sampDesc = {};
    sampDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
    sampDesc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampDesc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampDesc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    
    hr = device->CreateSamplerState(&sampDesc, &g_GTAOState.pointSamplerCached);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create point sampler (0x%08X)", hr);
    }
    
    // Cache dimensions
    g_GTAOState.cachedWidth = width;
    g_GTAOState.cachedHeight = height;
}

// Initialize output pipeline state objects (one-time)
static void InitializeOutputStates(ID3D11Device* device)
{
    if (g_GTAOState.dsState) return; // Already initialized
    
    // Depth-stencil: Disable depth test/write for full-screen quad
    D3D11_DEPTH_STENCIL_DESC dsDesc = {};
    dsDesc.DepthEnable = FALSE;
    dsDesc.DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ZERO;
    dsDesc.StencilEnable = FALSE;
    device->CreateDepthStencilState(&dsDesc, &g_GTAOState.dsState);
    
    // Blend: Standard alpha blend disabled for AO composite
    D3D11_BLEND_DESC blendDesc = {};
    blendDesc.RenderTarget[0].BlendEnable = FALSE;
    blendDesc.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
    device->CreateBlendState(&blendDesc, &g_GTAOState.blendState);
    
    // Rasterizer: No culling, fill mode
    D3D11_RASTERIZER_DESC rsDesc = {};
    rsDesc.FillMode = D3D11_FILL_SOLID;
    rsDesc.CullMode = D3D11_CULL_NONE;
    device->CreateRasterizerState(&rsDesc, &g_GTAOState.rasterState);
}

// Ensure intermediate texture exists for read-modify-write safety
static void EnsureIntermediateTexture(ID3D11Device* device, DXGI_FORMAT format, int width, int height)
{
    if (g_GTAOState.intermediateTexture && 
        g_GTAOState.cachedWidth == width && 
        g_GTAOState.cachedHeight == height)
        return;
    
    // Cleanup old
    if (g_GTAOState.intermediateTexture) {
        g_GTAOState.intermediateTexture->Release(); g_GTAOState.intermediateTexture = nullptr;
        g_GTAOState.intermediateSRV->Release(); g_GTAOState.intermediateSRV = nullptr;
    }
    
    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = format;  // Use source format (may be TYPELESS)
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    
    HRESULT hr = device->CreateTexture2D(&desc, nullptr, &g_GTAOState.intermediateTexture);
    if (FAILED(hr) || !g_GTAOState.intermediateTexture) {
        LogToFile("[GTAO] Failed to create intermediate texture (0x%08X)", hr);
        return;
    }
    
    // CRITICAL: Explicitly specify concrete format for SRV if texture is typeless
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Texture2D.MipLevels = 1;
    
    // Convert typeless to concrete format for SRV (HDR float buffers need FLOAT, not UNORM)
    switch (format) {
        case DXGI_FORMAT_R8G8B8A8_TYPELESS: 
            srvDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; 
            break;
        case DXGI_FORMAT_R10G10B10A2_TYPELESS: 
            srvDesc.Format = DXGI_FORMAT_R10G10B10A2_UNORM; 
            break;
        case DXGI_FORMAT_R16G16B16A16_TYPELESS: 
            srvDesc.Format = DXGI_FORMAT_R16G16B16A16_FLOAT;  // HDR - NOT UNORM!
            break;
        case DXGI_FORMAT_R32G32B32A32_TYPELESS: 
            srvDesc.Format = DXGI_FORMAT_R32G32B32A32_FLOAT; 
            break;
        case DXGI_FORMAT_R16G16_TYPELESS: 
            srvDesc.Format = DXGI_FORMAT_R16G16_FLOAT;  // HDR - NOT UNORM!
            break;
        case DXGI_FORMAT_R32G32_TYPELESS: 
            srvDesc.Format = DXGI_FORMAT_R32G32_FLOAT; 
            break;
        default: 
            srvDesc.Format = format; 
            break;
    }
    
    hr = device->CreateShaderResourceView(g_GTAOState.intermediateTexture, &srvDesc, &g_GTAOState.intermediateSRV);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create intermediate SRV (0x%08X), attempted format: %d", hr, srvDesc.Format);
        g_GTAOState.intermediateTexture->Release();
        g_GTAOState.intermediateTexture = nullptr;
    }
}

extern "C" __declspec(dllexport)
void CR_GTAODebugSetInput(ID3D11Texture2D* depthTex, ID3D11Texture2D* normalTex, int width, int height,
                          const float* worldToView, const float* fovParams,
                          float nearPlane, float farPlane,
                          int frameIndex)
{
    std::lock_guard<std::mutex> lock(g_GTAOState.stateMutex);
    
    g_GTAOState.depthTexture = depthTex;
    g_GTAOState.normalTexture = normalTex;
    g_GTAOState.width = width;
    g_GTAOState.height = height;
    g_GTAOState.nearPlane = nearPlane;
    g_GTAOState.farPlane = farPlane;
    g_GTAOState.frameIndex = frameIndex;
    g_GTAOState.paramsDirty = true;
    
    if (worldToView) {
        memcpy(g_GTAOState.worldToView, worldToView, sizeof(float) * 9);
    } else {
        memset(g_GTAOState.worldToView, 0, sizeof(g_GTAOState.worldToView));
        g_GTAOState.worldToView[0] = 1.0f;
        g_GTAOState.worldToView[4] = 1.0f;
        g_GTAOState.worldToView[8] = 1.0f;
    }
    
    // Validate and store explicit FOV parameters (mandatory)
    if (!fovParams || fovParams[0] <= 0.0f || fovParams[1] <= 0.0f)
    {
        LogToFile("[GTAO] ERROR: Invalid or missing FOV parameters");
        return;
    }
    g_GTAOState.tanHalfFOVX = fovParams[0];
    g_GTAOState.tanHalfFOVY = fovParams[1];
    
    // Initialize resources (one-time)
    if (depthTex) {
        ID3D11Device* device = nullptr;
        depthTex->GetDevice(&device);
        if (device) {
            // Store device pointer for render callback
            if (!g_GTAOState.device) {
                g_GTAOState.device = device;
                g_GTAOState.device->AddRef();
            }
            InitializeBlueNoiseResources(device);
            EnsureComputeResources(device, width, height);
            device->Release();
        }
    }
}

// Internal composite function that uses an existing RTV
static void ExecuteComposite(ID3D11DeviceContext* context, ID3D11RenderTargetView* rtv, 
                             ID3D11Texture2D* sourceSceneTexture, int width, int height, int outputMode)
{
    if (!rtv) {
        LogToFile("[GTAO] ExecuteComposite: RTV is null");
        return;
    }
    if (!g_GTAOState.filteredAOTexture) {
        LogToFile("[GTAO] ExecuteComposite: filteredAOTexture is null - filter resources failed to create");
        return;
    }
    
    // Get device from context
    ID3D11Device* device = nullptr;
    context->GetDevice(&device);
    if (!device) return;
    
    // Initialize cached resources if needed
    InitializeOutputStates(device);
    
    // Ensure intermediate texture for read-write hazard (use source format)
    if (sourceSceneTexture) {
        D3D11_TEXTURE2D_DESC srcDesc;
        sourceSceneTexture->GetDesc(&srcDesc);
        EnsureIntermediateTexture(device, srcDesc.Format, width, height);
    } else {
        EnsureIntermediateTexture(device, DXGI_FORMAT_R8G8B8A8_UNORM, width, height);
    }
    
    // Check if intermediate texture creation succeeded
    if (!g_GTAOState.intermediateTexture) {
        LogToFile("[GTAO] ExecuteComposite: intermediate texture creation failed");
        device->Release();
        return;
    }
    
    if (!g_GTAOState.outputVS) {
        device->CreateVertexShader(g_GTAOOutputVS, sizeof(g_GTAOOutputVS), 
                                   nullptr, &g_GTAOState.outputVS);
        device->CreatePixelShader(g_GTAOOutputPS, sizeof(g_GTAOOutputPS), 
                                  nullptr, &g_GTAOState.outputPS);
        
        D3D11_SAMPLER_DESC sdesc = {};
        sdesc.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sdesc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
        sdesc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
        device->CreateSamplerState(&sdesc, &g_GTAOState.outputSampler);
        
        D3D11_BUFFER_DESC cbDesc = {};
        cbDesc.ByteWidth = 16;
        cbDesc.Usage = D3D11_USAGE_DYNAMIC;
        cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        device->CreateBuffer(&cbDesc, nullptr, &g_GTAOState.outputCB);
    }
    
    // Create SRV for filtered AO
    ID3D11ShaderResourceView* aoSRV = nullptr;
    HRESULT hr = device->CreateShaderResourceView(g_GTAOState.filteredAOTexture, nullptr, &aoSRV);
    if (FAILED(hr) || !aoSRV) {
        LogToFile("[GTAO] Failed to create AO SRV");
        device->Release();
        return;
    }
    
    // Handle Composite Mode (Mode 0) with Read-Write Hazard
    ID3D11ShaderResourceView* sceneSRV = nullptr;
    ID3D11RenderTargetView* compositeRTV = rtv; // Default: draw directly to dest
    
    if (outputMode == 0 && sourceSceneTexture)
    {
        // For in-place composite, we need to copy to intermediate first
        // Check if we're reading from the same texture we're rendering to
        ID3D11Resource* rtvRes = nullptr;
        rtv->GetResource(&rtvRes);
        if (rtvRes == sourceSceneTexture)
        {
            // COPY source to intermediate first
            context->CopyResource(g_GTAOState.intermediateTexture, sourceSceneTexture);
            
            // Use intermediate as SRV for scene
            sceneSRV = g_GTAOState.intermediateSRV;
            sceneSRV->AddRef(); // AddRef because we'll release at end
        }
        else
        {
            // Different textures, safe to source directly
            D3D11_TEXTURE2D_DESC srcDesc;
            sourceSceneTexture->GetDesc(&srcDesc);
            D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
            srvDesc.Format = srcDesc.Format;
            srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
            srvDesc.Texture2D.MipLevels = 1;
            
            HRESULT hr = device->CreateShaderResourceView(sourceSceneTexture, &srvDesc, &sceneSRV);
            if (FAILED(hr)) {
                LogToFile("[GTAO] Failed to create scene SRV (0x%08X), format: %d", hr, srcDesc.Format);
                // Fallback: treat as same-texture case using intermediate
                context->CopyResource(g_GTAOState.intermediateTexture, sourceSceneTexture);
                sceneSRV = g_GTAOState.intermediateSRV;
                sceneSRV->AddRef();
            }
        }
        if (rtvRes) rtvRes->Release();
    }
    
    // Safety check for composite mode
    if (outputMode == 0 && !sceneSRV) {
        LogToFile("[GTAO] Cannot composite: sceneSRV is null");
        aoSRV->Release();
        device->Release();
        return;
    }
    
    // Update constant buffer with mode
    if (!g_GTAOState.outputCB) {
        LogToFile("[GTAO] ExecuteComposite: outputCB is null - failed to create earlier");
        if (aoSRV) aoSRV->Release();
        if (sceneSRV && sceneSRV != g_GTAOState.intermediateSRV) sceneSRV->Release();
        if (sceneSRV == g_GTAOState.intermediateSRV) sceneSRV->Release();
        device->Release();
        return;
    }
    
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(context->Map(g_GTAOState.outputCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        int* params = (int*)mapped.pData;
        params[0] = outputMode;
        params[1] = params[2] = params[3] = 0;
        context->Unmap(g_GTAOState.outputCB, 0);
    }
    
    // Setup pipeline state
    context->OMSetRenderTargets(1, &compositeRTV, nullptr);
    context->OMSetDepthStencilState(g_GTAOState.dsState, 0);
    context->OMSetBlendState(g_GTAOState.blendState, nullptr, 0xFFFFFFFF);
    context->RSSetState(g_GTAOState.rasterState);
    
    D3D11_VIEWPORT vp = { 0, 0, (float)width, (float)height, 0, 1 };
    context->RSSetViewports(1, &vp);
    
    // Bind shaders
    context->VSSetShader(g_GTAOState.outputVS, nullptr, 0);
    context->PSSetShader(g_GTAOState.outputPS, nullptr, 0);
    context->PSSetConstantBuffers(0, 1, &g_GTAOState.outputCB);
    context->PSSetSamplers(0, 1, &g_GTAOState.outputSampler);
    
    // Bind SRVs
    ID3D11ShaderResourceView* srvs[2] = { aoSRV, sceneSRV };
    context->PSSetShaderResources(0, 2, srvs);
    
    // Draw fullscreen triangle (3 vertices, no vertex buffer)
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->IASetInputLayout(nullptr);
    context->IASetVertexBuffers(0, 0, nullptr, nullptr, nullptr);
    context->IASetIndexBuffer(nullptr, DXGI_FORMAT_UNKNOWN, 0);
    
    context->Draw(3, 0);
    
    // CRITICAL: Complete unbind of all resources
    ID3D11RenderTargetView* nullRTV = nullptr;
    ID3D11ShaderResourceView* nullSRV[2] = { nullptr, nullptr };
    ID3D11Buffer* nullCB = nullptr;
    ID3D11VertexShader* nullVS = nullptr;
    ID3D11PixelShader* nullPS = nullptr;
    ID3D11SamplerState* nullSampler = nullptr;
    
    context->OMSetRenderTargets(1, &nullRTV, nullptr);
    context->PSSetShaderResources(0, 2, nullSRV);
    context->PSSetConstantBuffers(0, 1, &nullCB);
    context->VSSetShader(nullVS, nullptr, 0);
    context->PSSetShader(nullPS, nullptr, 0);
    context->PSSetSamplers(0, 1, &nullSampler);
    
    // Cleanup local resources (don't release rtv or context - caller owns them)
    if (aoSRV) aoSRV->Release();
    if (sceneSRV && sceneSRV != g_GTAOState.intermediateSRV) sceneSRV->Release();
    if (sceneSRV == g_GTAOState.intermediateSRV) sceneSRV->Release(); // Release our AddRef
    
    device->Release();
}

// Internal function that executes GTAO compute using cached resources
// Assumes EnsureComputeResources has been called and resources are valid
static void ExecuteGTAOCompute(ID3D11DeviceContext* context)
{
    if (!g_GTAOState.device || !g_GTAOState.aoTexture || !g_GTAOState.hiZTexture) {
        LogToFile("[GTAO] ExecuteGTAOCompute: Resources not initialized");
        return;
    }
    
    int width = g_GTAOState.width;
    int height = g_GTAOState.height;
    ID3D11Device* device = g_GTAOState.device;
    
    // ===== Hi-Z GENERATION =====
    int hiZMipCount = (int)(log2((std::max)(width, height))) + 1;
    hiZMipCount = (std::min)(hiZMipCount, 12);
    
    // Get raw depth texture format
    D3D11_TEXTURE2D_DESC rawDepthDesc;
    g_GTAOState.depthTexture->GetDesc(&rawDepthDesc);
    
    // Copy raw depth to mip 0 of Hi-Z texture
    context->CopySubresourceRegion(
        g_GTAOState.hiZTexture, 0, 0, 0, 0,
        g_GTAOState.depthTexture, 0, nullptr
    );
    
    // Generate remaining Hi-Z mips
    int currentWidth = width;
    int currentHeight = height;
    
    struct HiZParams {
        int sourceDim[2];
        int isFirstIteration;
        int __pad;
    };
    
    for (int mipLevel = 0; mipLevel < hiZMipCount - 1; mipLevel++) {
        // Create UAV for output mip
        D3D11_UNORDERED_ACCESS_VIEW_DESC hiZUavDesc = {};
        hiZUavDesc.Format = DXGI_FORMAT_R32_FLOAT;
        hiZUavDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2D;
        hiZUavDesc.Texture2D.MipSlice = mipLevel + 1;
        
        ID3D11UnorderedAccessView* hiZOutputUAV = nullptr;
        HRESULT hr = device->CreateUnorderedAccessView(g_GTAOState.hiZTexture, &hiZUavDesc, &hiZOutputUAV);
        if (FAILED(hr)) continue;
        
        // Fill constant buffer
        D3D11_MAPPED_SUBRESOURCE hiZMapped;
        if (SUCCEEDED(context->Map(g_GTAOState.hizCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &hiZMapped))) {
            HiZParams* params = (HiZParams*)hiZMapped.pData;
            params->sourceDim[0] = currentWidth;
            params->sourceDim[1] = currentHeight;
            params->isFirstIteration = (mipLevel == 0) ? 1 : 0;
            params->__pad = 0;
            context->Unmap(g_GTAOState.hizCB, 0);
        }
        
        // Bind source SRV
        ID3D11ShaderResourceView* hiZSourceSRV = nullptr;
        if (mipLevel == 0) {
            D3D11_SHADER_RESOURCE_VIEW_DESC srcSrvDesc = {};
            srcSrvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
            srcSrvDesc.Texture2D.MipLevels = 1;
            srcSrvDesc.Format = (rawDepthDesc.Format == 39) ? DXGI_FORMAT_R32_FLOAT : rawDepthDesc.Format;
            device->CreateShaderResourceView(g_GTAOState.depthTexture, &srcSrvDesc, &hiZSourceSRV);
        } else {
            D3D11_SHADER_RESOURCE_VIEW_DESC srcSrvDesc = {};
            srcSrvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
            srcSrvDesc.Texture2D.MostDetailedMip = mipLevel;
            srcSrvDesc.Texture2D.MipLevels = 1;
            srcSrvDesc.Format = DXGI_FORMAT_R32_FLOAT;
            device->CreateShaderResourceView(g_GTAOState.hiZTexture, &srcSrvDesc, &hiZSourceSRV);
        }
        
        // Dispatch
        context->CSSetShader(g_GTAOState.hiZShaderCached, nullptr, 0);
        context->CSSetConstantBuffers(0, 1, &g_GTAOState.hizCB);
        ID3D11ShaderResourceView* srvs[1] = { hiZSourceSRV };
        context->CSSetShaderResources(0, 1, srvs);
        ID3D11UnorderedAccessView* uavs[1] = { hiZOutputUAV };
        context->CSSetUnorderedAccessViews(0, 1, uavs, nullptr);
        
        UINT dispatchX = (currentWidth / 2 + 7) / 8;
        UINT dispatchY = (currentHeight / 2 + 7) / 8;
        context->Dispatch(dispatchX, dispatchY, 1);
        
        // Cleanup per-mip resources
        hiZOutputUAV->Release();
        if (hiZSourceSRV) hiZSourceSRV->Release();
        
        currentWidth = (std::max)(1, currentWidth / 2);
        currentHeight = (std::max)(1, currentHeight / 2);
    }
    
    // Unbind Hi-Z
    ID3D11UnorderedAccessView* nullHiZUAV[1] = { nullptr };
    ID3D11ShaderResourceView* nullHiZSRV[1] = { nullptr };
    context->CSSetUnorderedAccessViews(0, 1, nullHiZUAV, nullptr);
    context->CSSetShaderResources(0, 1, nullHiZSRV);
    context->CSSetShader(nullptr, nullptr, 0);
    
    // ===== GTAO COMPUTE =====
    // Create SRVs for input textures
    ID3D11ShaderResourceView* depthSRV = nullptr;
    ID3D11ShaderResourceView* normalSRV = nullptr;
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Texture2D.MipLevels = -1;
    srvDesc.Format = DXGI_FORMAT_R32_FLOAT;
    device->CreateShaderResourceView(g_GTAOState.depthTexture, &srvDesc, &depthSRV);
    srvDesc.Format = DXGI_FORMAT_R10G10B10A2_UNORM;
    device->CreateShaderResourceView(g_GTAOState.normalTexture, &srvDesc, &normalSRV);
    
    // Fill constant buffer
    D3D11_MAPPED_SUBRESOURCE mapped;
     if (SUCCEEDED(context->Map(g_GTAOState.gtaoCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        GTAOParams* params = (GTAOParams*)mapped.pData;
        
        // Use explicit FOV parameters for view-space reconstruction
        float tanHalfFOVX = g_GTAOState.tanHalfFOVX;
        float tanHalfFOVY = g_GTAOState.tanHalfFOVY;
        
        // float4 #1 (offset 0)
        params->ndcToViewMul[0] = tanHalfFOVX * 2.0f;
        params->ndcToViewAdd[0] = tanHalfFOVX * -1.0f;
        params->ndcToViewMul[1] = tanHalfFOVY * -2.0f;
        params->ndcToViewAdd[1] = tanHalfFOVY * 1.0f;
        
        // float4 #2 (offset 16)
        float n = g_GTAOState.nearPlane;
        float f = g_GTAOState.farPlane;
        params->depthUnpackConsts[0] = n;
        params->depthUnpackConsts[1] = f;
        params->resolution[0] = (float)width;
        params->resolution[1] = (float)height;
        
        // float4 #3 (offset 32)
        params->invResolution[0] = 1.0f / width;
        params->invResolution[1] = 1.0f / height;
        params->effectRadius = g_UserSettings.EffectRadius;
        params->maxPixelRadius = g_UserSettings.MaxPixelRadius;
        
        // float4 #4 (offset 48)
        params->intensity = g_UserSettings.Intensity;
        params->sampleDistributionPower = g_UserSettings.SampleDistributionPower;
        params->sliceCount = g_UserSettings.SliceCount;
        params->stepsPerSlice = g_UserSettings.StepsPerSlice;
        
        // float4 #5 (offset 64) - CRITICAL: These were missing/wrong before
        params->FrameIndex = g_GTAOState.frameIndex;
        params->depthMipSamplingOffset = 100.0f;
        params->fadeStartDistance = g_UserSettings.FadeStartDistance;
        params->fadeEndDistance = g_UserSettings.FadeEndDistance;
        
        // float4 #6 (offset 80)
        params->fadeCurve = g_UserSettings.FadeCurve;
        params->DebugMode = g_GTAOState.debugMode;
        params->__pad2 = 0.0f;
        params->__pad3 = 0.0f;
        
        // float4 #7 (offset 96)
        params->worldToViewRow0[0] = g_GTAOState.worldToView[0];
        params->worldToViewRow0[1] = g_GTAOState.worldToView[1];
        params->worldToViewRow0[2] = g_GTAOState.worldToView[2];
        params->worldToViewRow0[3] = 0.0f;
        
        // float4 #8 (offset 112)
        params->worldToViewRow1[0] = g_GTAOState.worldToView[3];
        params->worldToViewRow1[1] = g_GTAOState.worldToView[4];
        params->worldToViewRow1[2] = g_GTAOState.worldToView[5];
        params->worldToViewRow1[3] = 0.0f;
        
        // float4 #9 (offset 128)
        params->worldToViewRow2[0] = g_GTAOState.worldToView[6];
        params->worldToViewRow2[1] = g_GTAOState.worldToView[7];
        params->worldToViewRow2[2] = g_GTAOState.worldToView[8];
        params->worldToViewRow2[3] = 0.0f;

        context->Unmap(g_GTAOState.gtaoCB, 0);
    }
    
    // Validate blue noise before binding
    if (!g_GTAOState.blueNoiseSRV) {
        LogToFile("[GTAO] Blue noise not initialized");
        if (depthSRV) depthSRV->Release();
        if (normalSRV) normalSRV->Release();
        return;
    }
    
    // Bind and dispatch GTAO
    context->CSSetShader(g_GTAOState.gtaoShaderCached, nullptr, 0);
    context->CSSetConstantBuffers(0, 1, &g_GTAOState.gtaoCB);
    ID3D11ShaderResourceView* srvs[3] = { depthSRV, normalSRV, g_GTAOState.blueNoiseSRV };
    context->CSSetShaderResources(0, 3, srvs);
    context->CSSetSamplers(0, 1, &g_GTAOState.pointSamplerCached);
    ID3D11UnorderedAccessView* uavs[1] = { g_GTAOState.aoUAV };
    context->CSSetUnorderedAccessViews(0, 1, uavs, nullptr);
    
    UINT dispatchX = (width + 7) / 8;
    UINT dispatchY = (height + 7) / 8;
    context->Dispatch(dispatchX, dispatchY, 1);
    
    // Unbind
    ID3D11UnorderedAccessView* nullUAV[1] = { nullptr };
    ID3D11ShaderResourceView* nullSRV[3] = { nullptr, nullptr, nullptr };
    ID3D11SamplerState* nullSampler[1] = { nullptr };
    context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
    context->CSSetShaderResources(0, 3, nullSRV);
    context->CSSetSamplers(0, 1, nullSampler);
    context->CSSetShader(nullptr, nullptr, 0);
    
    // Cleanup temp SRVs
    if (depthSRV) depthSRV->Release();
    if (normalSRV) normalSRV->Release();
    
    // ===== NORMAL-AWARE FILTER PASS =====
    InitializeFilterResources(device, width, height);
    if (!g_GTAOState.filteredAOTexture || !g_GTAOState.filteredAOUAV) {
        LogToFile("[GTAO] Filter resources not available, skipping filter pass");
        if (depthSRV) depthSRV->Release();
        if (normalSRV) normalSRV->Release();
        return;
    }
    
    // Create SRV for raw AO
    D3D11_SHADER_RESOURCE_VIEW_DESC rawAoSrvDesc = {};
    rawAoSrvDesc.Format = DXGI_FORMAT_R32G32_FLOAT;
    rawAoSrvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    rawAoSrvDesc.Texture2D.MipLevels = 1;
    ID3D11ShaderResourceView* rawAOSRV = nullptr;
    HRESULT hr = device->CreateShaderResourceView(g_GTAOState.aoTexture, &rawAoSrvDesc, &rawAOSRV);
    if (FAILED(hr) || !rawAOSRV) {
        LogToFile("[GTAO] Failed to create raw AO SRV in ExecuteGTAOCompute");
        return;
    }
    
    // Recreate normal SRV for filter (filter samples from normalTexture)
    ID3D11ShaderResourceView* filterNormalSRV = nullptr;
    D3D11_SHADER_RESOURCE_VIEW_DESC filterNormalDesc = {};
    filterNormalDesc.Format = DXGI_FORMAT_R10G10B10A2_UNORM;
    filterNormalDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    filterNormalDesc.Texture2D.MipLevels = 1;
    hr = device->CreateShaderResourceView(g_GTAOState.normalTexture, &filterNormalDesc, &filterNormalSRV);
    if (FAILED(hr) || !filterNormalSRV) {
        LogToFile("[GTAO] Failed to create filter normal SRV");
        rawAOSRV->Release();
        return;
    }
    
    // Update filter constant buffer (single pass)
    D3D11_MAPPED_SUBRESOURCE filterMapped;
    if (SUCCEEDED(context->Map(g_GTAOState.filterCB, 0, D3D11_MAP_WRITE_DISCARD, 0, &filterMapped))) {
        FilterParams* fparams = (FilterParams*)filterMapped.pData;
        fparams->invScreenSize[0] = 1.0f / width;
        fparams->invScreenSize[1] = 1.0f / height;
        fparams->normalPower = g_UserSettings.NormalPower;
        fparams->depthSigma = g_UserSettings.DepthSigma;
        context->Unmap(g_GTAOState.filterCB, 0);
    }
    
    // Filter Pass: Raw AO -> Filtered AO
    context->CSSetShader(g_GTAOState.filterShader, nullptr, 0);
    context->CSSetConstantBuffers(0, 1, &g_GTAOState.filterCB);
    ID3D11ShaderResourceView* filterSRVs[2] = { rawAOSRV, filterNormalSRV };
    context->CSSetShaderResources(0, 2, filterSRVs);
    ID3D11UnorderedAccessView* filterUAVs[1] = { g_GTAOState.filteredAOUAV };
    context->CSSetUnorderedAccessViews(0, 1, filterUAVs, nullptr);
    
    UINT filterDispatchX = (width + 7) / 8;
    UINT filterDispatchY = (height + 7) / 8;
    context->Dispatch(filterDispatchX, filterDispatchY, 1);
    
    // Cleanup
    context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
    context->CSSetShaderResources(0, 2, nullSRV);
    context->CSSetShader(nullptr, nullptr, 0);
    
    rawAOSRV->Release();
    filterNormalSRV->Release();
}

// Unity render event callback - runs on render thread
static void UNITY_INTERFACE_API OnGTAORenderEvent(int eventID)
{
    if (!g_GTAOState.device || !g_GTAOState.depthTexture || !g_GTAOState.normalTexture) return;
    
    std::lock_guard<std::mutex> lock(g_GTAOState.stateMutex);
    
    ID3D11DeviceContext* context = nullptr;
    g_GTAOState.device->GetImmediateContext(&context);
    if (!context) return;
    
    // 1. Execute compute (populates filteredAOTexture)
    ExecuteGTAOCompute(context);
    
    // 2. Get current render target
    ID3D11RenderTargetView* currentRTV = nullptr;
    context->OMGetRenderTargets(1, &currentRTV, nullptr);
    if (!currentRTV) {
        context->Release();
        return;
    }
    
    // 3. Get backing texture
    ID3D11Resource* res = nullptr;
    currentRTV->GetResource(&res);
    ID3D11Texture2D* currentTex = static_cast<ID3D11Texture2D*>(res);
    
    // 4. Composite AO onto current target using existing RTV
    ExecuteComposite(context, currentRTV, currentTex, g_GTAOState.width, g_GTAOState.height, g_GTAOState.outputMode);
    
    // 5. Cleanup
    currentTex->Release();
    currentRTV->Release();
    context->Release();
}

// Export for Unity's IssuePluginEvent
extern "C" __declspec(dllexport)
UnityRenderingEvent CR_GetGTAORenderEventFunc()
{
    return OnGTAORenderEvent;
}

// Set output mode (0=Composite, 1=Raw, 2=WorldNorm, 3=ViewNorm, 4=NormAlpha)
extern "C" __declspec(dllexport)
void CR_GTAOSetOutputMode(int mode)
{
    // Mode mapping from C# UI to internal state:
    // 0=None (Composite AO), 1=Raw AO, 2=World Normals, 3=View Normals, 4=Normal Alpha
    if (mode == 0)
    {
        g_GTAOState.outputMode = 0; // Composite AO over scene
        g_GTAOState.debugMode = 0;  // Normal AO computation
    }
    else if (mode == 1)
    {
        g_GTAOState.outputMode = 1; // Raw AO output (grayscale)
        g_GTAOState.debugMode = 0;  // Normal AO computation
    }
    else
    {
        // Debug visualizations (modes 2-4): force raw output, set debug mode for shader
        // 2->World Normals (debugMode=1), 3->View Normals (debugMode=2), 4->Normal Alpha (debugMode=3)
        g_GTAOState.outputMode = 1;
        g_GTAOState.debugMode = mode - 1;
    }
}

extern "C" __declspec(dllexport)
void CR_GTAOSetSettings(const GTAOUserSettings* settings)
{
    if (!settings) return;
    
    std::lock_guard<std::mutex> lock(g_GTAOState.stateMutex);
    g_UserSettings = *settings;
}

extern "C" __declspec(dllexport)
void CR_GTAOShutdown()
{
    // Lock to prevent concurrent access from render thread
    std::lock_guard<std::mutex> lock(g_GTAOState.stateMutex);
    
    // Release intermediate texture
    if (g_GTAOState.intermediateSRV) { g_GTAOState.intermediateSRV->Release(); g_GTAOState.intermediateSRV = nullptr; }
    if (g_GTAOState.intermediateTexture) { g_GTAOState.intermediateTexture->Release(); g_GTAOState.intermediateTexture = nullptr; }
    
    // Release state objects
    if (g_GTAOState.dsState) { g_GTAOState.dsState->Release(); g_GTAOState.dsState = nullptr; }
    if (g_GTAOState.blendState) { g_GTAOState.blendState->Release(); g_GTAOState.blendState = nullptr; }
    if (g_GTAOState.rasterState) { g_GTAOState.rasterState->Release(); g_GTAOState.rasterState = nullptr; }
    
    // Release shaders
    if (g_GTAOState.outputVS) { g_GTAOState.outputVS->Release(); g_GTAOState.outputVS = nullptr; }
    if (g_GTAOState.outputPS) { g_GTAOState.outputPS->Release(); g_GTAOState.outputPS = nullptr; }
    if (g_GTAOState.outputSampler) { g_GTAOState.outputSampler->Release(); g_GTAOState.outputSampler = nullptr; }
    if (g_GTAOState.outputCB) { g_GTAOState.outputCB->Release(); g_GTAOState.outputCB = nullptr; }
    
    // Release filter resources
    if (g_GTAOState.filteredAOTexture) { g_GTAOState.filteredAOTexture->Release(); g_GTAOState.filteredAOTexture = nullptr; }
    if (g_GTAOState.filteredUAV) { g_GTAOState.filteredUAV->Release(); g_GTAOState.filteredUAV = nullptr; }
    if (g_GTAOState.filteredAOUAV) { g_GTAOState.filteredAOUAV->Release(); g_GTAOState.filteredAOUAV = nullptr; }
    if (g_GTAOState.filterIntermediateTexture) { g_GTAOState.filterIntermediateTexture->Release(); g_GTAOState.filterIntermediateTexture = nullptr; }
    if (g_GTAOState.filterIntermediateUAV) { g_GTAOState.filterIntermediateUAV->Release(); g_GTAOState.filterIntermediateUAV = nullptr; }
    if (g_GTAOState.filterShader) { g_GTAOState.filterShader->Release(); g_GTAOState.filterShader = nullptr; }
    if (g_GTAOState.filterCB) { g_GTAOState.filterCB->Release(); g_GTAOState.filterCB = nullptr; }
    
    // Release blue noise
    if (g_GTAOState.blueNoiseSRV) { g_GTAOState.blueNoiseSRV->Release(); g_GTAOState.blueNoiseSRV = nullptr; }
    if (g_GTAOState.blueNoiseTexture) { g_GTAOState.blueNoiseTexture->Release(); g_GTAOState.blueNoiseTexture = nullptr; }
    
    // Release cached compute resources
    if (g_GTAOState.aoUAV) { g_GTAOState.aoUAV->Release(); g_GTAOState.aoUAV = nullptr; }
    if (g_GTAOState.aoTexture) { g_GTAOState.aoTexture->Release(); g_GTAOState.aoTexture = nullptr; }
    if (g_GTAOState.hiZTexture) { g_GTAOState.hiZTexture->Release(); g_GTAOState.hiZTexture = nullptr; }
    if (g_GTAOState.gtaoCB) { g_GTAOState.gtaoCB->Release(); g_GTAOState.gtaoCB = nullptr; }
    if (g_GTAOState.hizCB) { g_GTAOState.hizCB->Release(); g_GTAOState.hizCB = nullptr; }
    if (g_GTAOState.hiZShaderCached) { g_GTAOState.hiZShaderCached->Release(); g_GTAOState.hiZShaderCached = nullptr; }
    if (g_GTAOState.gtaoShaderCached) { g_GTAOState.gtaoShaderCached->Release(); g_GTAOState.gtaoShaderCached = nullptr; }
    if (g_GTAOState.pointSamplerCached) { g_GTAOState.pointSamplerCached->Release(); g_GTAOState.pointSamplerCached = nullptr; }
    if (g_GTAOState.device) { g_GTAOState.device->Release(); g_GTAOState.device = nullptr; }
    
    g_GTAOState.cachedWidth = 0;
    g_GTAOState.cachedHeight = 0;
}
