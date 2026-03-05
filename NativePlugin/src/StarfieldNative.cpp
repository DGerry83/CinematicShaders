#include "StarfieldNative.h"
#include "../include/StarfieldPass1.h"
#include "../include/StarfieldPass2.h"
#include "../include/StarfieldVS.h"
#include <mutex>
#include <algorithm>
#include <cmath>

// External declarations from main module
extern void LogToFile(const char* fmt, ...);

static struct {
    ID3D11Device* device = nullptr;
    ID3D11Texture2D* hdrTexture = nullptr;
    ID3D11UnorderedAccessView* hdrUAV = nullptr;
    ID3D11ShaderResourceView* hdrSRV = nullptr;
    
    // Shaders
    ID3D11ComputeShader* pass1CS = nullptr;
    ID3D11VertexShader* pass2VS = nullptr;
    ID3D11PixelShader* pass2PS = nullptr;
    
    // States
    ID3D11SamplerState* linearSampler = nullptr;
    ID3D11DepthStencilState* depthState = nullptr;  // Depth test: draw if depth < epsilon (sky)
    ID3D11BlendState* blendState = nullptr;
    ID3D11RasterizerState* rasterState = nullptr;
    
    // Constant buffers
    ID3D11Buffer* pass1CB = nullptr;
    ID3D11Buffer* pass2CB = nullptr;
    
    // Cached dimensions
    int width = 0;
    int height = 0;
    bool initialized = false;
    
    // Current frame params
    float verticalFOV = 1.0f;  // Radians
    float aspectRatio = 16.0f/9.0f;
    float3 cameraRight;
    float _pad0;               // 16-byte alignment for constant buffer matching
    float3 cameraUp;
    float _pad1;               // 16-byte alignment
    float3 cameraForward;
    float _pad2;               // 16-byte alignment
    float exposure = 3.0f;
    float starDensity = 200.0f;
    float minMagnitude = -1.0f;
    float maxMagnitude = 10.0f;
    float magnitudeBias = 0.08f;
    float heroRarity = 0.02f;
    float clustering = 0.6f;
    float staggerAmount = 0.5f;
    float populationBias = 0.0f;
    float mainSequenceStrength = 0.6f;
    float redGiantRarity = 0.02f;
    float galacticFlatness = 0.85f;
    float galacticDiscFalloff = 3.0f;
    float bandCenterBoost = 0.0f;
    float bandCoreSharpness = 20.0f;
    float3 galacticPlaneNormal = float3(0.0f, 1.0f, 0.0f);
    float bulgeIntensity = 5.0f;
    float3 bulgeCenterDirection = float3(1.0f, 0.0f, 0.0f);
    float bulgeWidth = 0.5f;
    float bulgeHeight = 0.5f;
    float bulgeSoftness = 0.0f;
    float bulgeNoiseScale = 20.0f;
    float bulgeNoiseStrength = 0.0f;
    float bloomThreshold = 0.8f;
    float bloomIntensity = 2.0f;
    float spikeIntensity = 0.4f;
    float blurPixels = 1.0f;
    int frameIndex = 0;
    
    std::mutex stateMutex;
} g_StarfieldState;

// Constant buffer layouts (must match HLSL exactly, 16-byte aligned)
struct StarfieldPass1Params {
    float VerticalFOV;
    float AspectRatio;
    float _padCamera0[2];
    
    float CameraRight[3];
    float _padCamera1;
    
    float CameraUp[3];
    float _padCamera2;
    
    float CameraForward[3];
    float _padCamera3;
    
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
    float Exposure;
    float BlurPixels;
    
    float GalacticFlatness;
    float GalacticDiscFalloff;
    float BandCenterBoost;
    float BandCoreSharpness;
    
    // float3 + float = 16 bytes
    float GalacticPlaneNormalX;
    float GalacticPlaneNormalY;
    float GalacticPlaneNormalZ;
    float BulgeIntensity;
    
    // float3 + float = 16 bytes  
    float BulgeCenterDirectionX;
    float BulgeCenterDirectionY;
    float BulgeCenterDirectionZ;
    float BulgeWidth;
    
    float BulgeHeight;
    float BulgeSoftness;
    float BulgeNoiseScale;
    float BulgeNoiseStrength;
    
    float ScreenSizeX;
    float ScreenSizeY;
    float InvScreenSizeX;
    float InvScreenSizeY;
    
    int FrameIndex;
    int Pad1[3];  // Pad to 16 bytes
};

struct StarfieldPass2Params {
    float ScreenSizeX;
    float ScreenSizeY;
    float InvScreenSizeX;
    float InvScreenSizeY;
    float BloomThreshold;
    float BloomIntensity;
    float DepthThreshold;
    float ExposureEV;
    int EnableTonemapping;
    int Pad[3];  // Pad to 16 bytes
    float SpikeIntensity;
    float SpikePad[3]; // Pad to 16 bytes
};

// Starfield Internal Functions
static void EnsureStarfieldResources(ID3D11Device* device, int width, int height)
{
    if (g_StarfieldState.initialized && g_StarfieldState.width == width && g_StarfieldState.height == height)
        return;
    
    // Cleanup old resources
    if (g_StarfieldState.hdrTexture) {
        g_StarfieldState.hdrTexture->Release();
        g_StarfieldState.hdrUAV->Release();
        g_StarfieldState.hdrSRV->Release();
        g_StarfieldState.hdrTexture = nullptr;
        g_StarfieldState.hdrUAV = nullptr;
        g_StarfieldState.hdrSRV = nullptr;
    }
    
    // HDR Texture for Pass 1 output (R11G11B10_FLOAT)
    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R11G11B10_FLOAT;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_UNORDERED_ACCESS | D3D11_BIND_SHADER_RESOURCE;
    
    HRESULT hr = device->CreateTexture2D(&desc, nullptr, &g_StarfieldState.hdrTexture);
    if (FAILED(hr)) {
        LogToFile("[Starfield] Failed to create HDR texture (0x%08X)", hr);
        return;
    }
    
    hr = device->CreateUnorderedAccessView(g_StarfieldState.hdrTexture, nullptr, &g_StarfieldState.hdrUAV);
    if (FAILED(hr)) {
        LogToFile("[Starfield] Failed to create HDR UAV (0x%08X)", hr);
        g_StarfieldState.hdrTexture->Release();
        return;
    }
    
    hr = device->CreateShaderResourceView(g_StarfieldState.hdrTexture, nullptr, &g_StarfieldState.hdrSRV);
    if (FAILED(hr)) {
        LogToFile("[Starfield] Failed to create HDR SRV (0x%08X)", hr);
        g_StarfieldState.hdrUAV->Release();
        g_StarfieldState.hdrTexture->Release();
        return;
    }
    
    // Shaders
    if (!g_StarfieldState.pass1CS) {
        hr = device->CreateComputeShader(g_StarfieldPass1CS, sizeof(g_StarfieldPass1CS), nullptr, &g_StarfieldState.pass1CS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create Pass 1 CS (0x%08X)", hr);
    }
    
    if (!g_StarfieldState.pass2PS) {
        hr = device->CreatePixelShader(g_StarfieldPass2PS, sizeof(g_StarfieldPass2PS), nullptr, &g_StarfieldState.pass2PS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create Pass 2 PS (0x%08X)", hr);
    }
    
    if (!g_StarfieldState.pass2VS) {
        hr = device->CreateVertexShader(g_StarfieldVS, sizeof(g_StarfieldVS), nullptr, &g_StarfieldState.pass2VS);
        if (FAILED(hr)) LogToFile("[Starfield] Failed to create Pass 2 VS (0x%08X)", hr);
    }
    
    // Samplers
    if (!g_StarfieldState.linearSampler) {
        D3D11_SAMPLER_DESC sampDesc = {};
        sampDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        sampDesc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampDesc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
        sampDesc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        device->CreateSamplerState(&sampDesc, &g_StarfieldState.linearSampler);
    }
    
    // Depth stencil state: Disabled - we handle masking in pixel shader via normal alpha
    if (!g_StarfieldState.depthState) {
        D3D11_DEPTH_STENCIL_DESC dsDesc = {};
        dsDesc.DepthEnable = FALSE;
        dsDesc.DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ZERO;
        dsDesc.StencilEnable = FALSE;
        device->CreateDepthStencilState(&dsDesc, &g_StarfieldState.depthState);
    }
    
// Blend state: Alpha blend - SrcAlpha/InvSrcAlpha 
// Sky (alpha=1.0): draw stars, Geometry (alpha=0.0): preserve existing pixel
if (!g_StarfieldState.blendState) {
    D3D11_BLEND_DESC blendDesc = {};
    blendDesc.RenderTarget[0].BlendEnable = TRUE;
    blendDesc.RenderTarget[0].SrcBlend = D3D11_BLEND_SRC_ALPHA;
    blendDesc.RenderTarget[0].DestBlend = D3D11_BLEND_INV_SRC_ALPHA;
    blendDesc.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD;
    blendDesc.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE;
    blendDesc.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ZERO;
    blendDesc.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
    blendDesc.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
    device->CreateBlendState(&blendDesc, &g_StarfieldState.blendState);
}
    
    // Rasterizer state
    if (!g_StarfieldState.rasterState) {
        D3D11_RASTERIZER_DESC rsDesc = {};
        rsDesc.FillMode = D3D11_FILL_SOLID;
        rsDesc.CullMode = D3D11_CULL_NONE;
        device->CreateRasterizerState(&rsDesc, &g_StarfieldState.rasterState);
    }
    
    // Constant buffers
    if (!g_StarfieldState.pass1CB) {
        D3D11_BUFFER_DESC cbDesc = {};
        cbDesc.ByteWidth = sizeof(StarfieldPass1Params);
        cbDesc.Usage = D3D11_USAGE_DYNAMIC;
        cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        device->CreateBuffer(&cbDesc, nullptr, &g_StarfieldState.pass1CB);
    }
    
    if (!g_StarfieldState.pass2CB) {
        D3D11_BUFFER_DESC cbDesc = {};
        cbDesc.ByteWidth = sizeof(StarfieldPass2Params);
        cbDesc.Usage = D3D11_USAGE_DYNAMIC;
        cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        device->CreateBuffer(&cbDesc, nullptr, &g_StarfieldState.pass2CB);
    }
    
    if (!g_StarfieldState.device) {
        g_StarfieldState.device = device;
        g_StarfieldState.device->AddRef();
    }
    
    g_StarfieldState.width = width;
    g_StarfieldState.height = height;
    g_StarfieldState.initialized = true;
    
    LogToFile("[Starfield] Resources initialized: %dx%d", width, height);
}

static void ExecuteStarfieldRender(ID3D11DeviceContext* context)
{
    if (!context) return;
    
    // Get device from context (safe even if g_StarfieldState.device is null)
    ID3D11Device* device = nullptr;
    context->GetDevice(&device);
    if (!device) return;
    
    // Lazy initialization: ensure resources match current dimensions
    if (!g_StarfieldState.initialized || g_StarfieldState.width != g_StarfieldState.width || g_StarfieldState.height != g_StarfieldState.height) {
        // Note: You'll need to pass width/height to this function or retrieve from context
        // For now, using cached values from g_StarfieldState
        EnsureStarfieldResources(device, g_StarfieldState.width, g_StarfieldState.height);
    }
    
    if (!g_StarfieldState.initialized) {
        LogToFile("[Starfield] ExecuteStarfieldRender skipped: resource initialization failed");
        device->Release();
        return;
    }
    
    // Get current render target (to use as destination for Pass 2)
    ID3D11RenderTargetView* currentRTV = nullptr;
    ID3D11DepthStencilView* currentDSV = nullptr;
    context->OMGetRenderTargets(1, &currentRTV, &currentDSV);
    
    if (!currentRTV) {
        LogToFile("[Starfield] No current RTV");
        return;
    }
    
    // ===== PASS 1: Compute Star Generation =====
    // Update Pass 1 constant buffer
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(context->Map(g_StarfieldState.pass1CB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        StarfieldPass1Params* params = (StarfieldPass1Params*)mapped.pData;
        
        params->VerticalFOV = g_StarfieldState.verticalFOV;
        params->AspectRatio = g_StarfieldState.aspectRatio;
        params->_padCamera0[0] = 0.0f;
        params->_padCamera0[1] = 0.0f;
        
        params->CameraRight[0] = g_StarfieldState.cameraRight.x;
        params->CameraRight[1] = g_StarfieldState.cameraRight.y;
        params->CameraRight[2] = g_StarfieldState.cameraRight.z;
        params->_padCamera1 = 0.0f;
        
        params->CameraUp[0] = g_StarfieldState.cameraUp.x;
        params->CameraUp[1] = g_StarfieldState.cameraUp.y;
        params->CameraUp[2] = g_StarfieldState.cameraUp.z;
        params->_padCamera2 = 0.0f;
        
        params->CameraForward[0] = g_StarfieldState.cameraForward.x;
        params->CameraForward[1] = g_StarfieldState.cameraForward.y;
        params->CameraForward[2] = g_StarfieldState.cameraForward.z;
        params->_padCamera3 = 0.0f;
        
        params->StarDensity = g_StarfieldState.starDensity;
        params->MinMagnitude = g_StarfieldState.minMagnitude;
        params->MaxMagnitude = g_StarfieldState.maxMagnitude;
        params->MagnitudeBias = g_StarfieldState.magnitudeBias;
        
        params->HeroRarity = g_StarfieldState.heroRarity;
        params->Clustering = g_StarfieldState.clustering;
        params->StaggerAmount = g_StarfieldState.staggerAmount;
        params->PopulationBias = g_StarfieldState.populationBias;
        
        params->MainSequenceStrength = g_StarfieldState.mainSequenceStrength;
        params->RedGiantRarity = g_StarfieldState.redGiantRarity;
        params->Exposure = g_StarfieldState.exposure;
        params->BlurPixels = g_StarfieldState.blurPixels;
        
        params->GalacticFlatness = g_StarfieldState.galacticFlatness;
        params->GalacticDiscFalloff = g_StarfieldState.galacticDiscFalloff;
        params->BandCenterBoost = g_StarfieldState.bandCenterBoost;
        params->BandCoreSharpness = g_StarfieldState.bandCoreSharpness;
        
        params->GalacticPlaneNormalX = g_StarfieldState.galacticPlaneNormal.x;
        params->GalacticPlaneNormalY = g_StarfieldState.galacticPlaneNormal.y;
        params->GalacticPlaneNormalZ = g_StarfieldState.galacticPlaneNormal.z;
        params->BulgeIntensity = g_StarfieldState.bulgeIntensity;
        
        params->BulgeCenterDirectionX = g_StarfieldState.bulgeCenterDirection.x;
        params->BulgeCenterDirectionY = g_StarfieldState.bulgeCenterDirection.y;
        params->BulgeCenterDirectionZ = g_StarfieldState.bulgeCenterDirection.z;
        params->BulgeWidth = g_StarfieldState.bulgeWidth;
        
        params->BulgeHeight = g_StarfieldState.bulgeHeight;
        params->BulgeSoftness = g_StarfieldState.bulgeSoftness;
        params->BulgeNoiseScale = g_StarfieldState.bulgeNoiseScale;
        params->BulgeNoiseStrength = g_StarfieldState.bulgeNoiseStrength;
        
        params->ScreenSizeX = (float)g_StarfieldState.width;
        params->ScreenSizeY = (float)g_StarfieldState.height;
        params->InvScreenSizeX = 1.0f / g_StarfieldState.width;
        params->InvScreenSizeY = 1.0f / g_StarfieldState.height;
        params->FrameIndex = g_StarfieldState.frameIndex;
        params->Pad1[0] = params->Pad1[1] = params->Pad1[2] = 0;
        
        context->Unmap(g_StarfieldState.pass1CB, 0);
    }
    
    context->CSSetShader(g_StarfieldState.pass1CS, nullptr, 0);
    context->CSSetConstantBuffers(0, 1, &g_StarfieldState.pass1CB);
    ID3D11UnorderedAccessView* uavs[1] = {g_StarfieldState.hdrUAV};
    context->CSSetUnorderedAccessViews(0, 1, uavs, nullptr);
    
    // Dispatch: 8x8 threads per group
    UINT dispatchX = (g_StarfieldState.width + 7) / 8;
    UINT dispatchY = (g_StarfieldState.height + 7) / 8;
    context->Dispatch(dispatchX, dispatchY, 1);
    
    // Unbind compute UAV to avoid conflict with PS SRV
    ID3D11UnorderedAccessView* nullUAV[1] = {nullptr};
    context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
    context->CSSetShader(nullptr, nullptr, 0);
    
    // ===== PASS 2: Composite with Normal Alpha Mask =====
    // Update Pass 2 constant buffer
    if (SUCCEEDED(context->Map(g_StarfieldState.pass2CB, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        StarfieldPass2Params* params = (StarfieldPass2Params*)mapped.pData;
        params->ScreenSizeX = (float)g_StarfieldState.width;
        params->ScreenSizeY = (float)g_StarfieldState.height;
        params->InvScreenSizeX = 1.0f / g_StarfieldState.width;
        params->InvScreenSizeY = 1.0f / g_StarfieldState.height;
        params->BloomThreshold = g_StarfieldState.bloomThreshold;
        params->BloomIntensity = g_StarfieldState.bloomIntensity;
        params->SpikeIntensity = g_StarfieldState.spikeIntensity;
        params->DepthThreshold = 0.5f;  // Alpha threshold: < 0.5 = sky (0), >= 0.5 = geometry (1)
        params->ExposureEV = g_StarfieldState.exposure;
        params->EnableTonemapping = 1;
        params->Pad[0] = params->Pad[1] = params->Pad[2] = 0;
        context->Unmap(g_StarfieldState.pass2CB, 0);
    }
    
    // Setup output merger
    context->OMSetRenderTargets(1, &currentRTV, nullptr);  // No depth testing
    context->OMSetDepthStencilState(g_StarfieldState.depthState, 0);
    context->OMSetBlendState(g_StarfieldState.blendState, nullptr, 0xFFFFFFFF);
    context->RSSetState(g_StarfieldState.rasterState);
    
    D3D11_VIEWPORT vp = {0, 0, (float)g_StarfieldState.width, (float)g_StarfieldState.height, 0, 1};
    context->RSSetViewports(1, &vp);
    
    // Bind shaders
    context->VSSetShader(g_StarfieldState.pass2VS, nullptr, 0);
    context->PSSetShader(g_StarfieldState.pass2PS, nullptr, 0);
    context->PSSetConstantBuffers(0, 1, &g_StarfieldState.pass2CB);
    context->PSSetSamplers(0, 1, &g_StarfieldState.linearSampler);
    
    ID3D11ShaderResourceView* srvs[1] = {g_StarfieldState.hdrSRV};
    context->PSSetShaderResources(0, 1, srvs);
    
    // Draw fullscreen triangle
    context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context->IASetInputLayout(nullptr);
    context->IASetVertexBuffers(0, 0, nullptr, nullptr, nullptr);
    context->IASetIndexBuffer(nullptr, DXGI_FORMAT_UNKNOWN, 0);
    
    if (!g_StarfieldState.pass2VS || !g_StarfieldState.pass2PS) {
        LogToFile("[Starfield] CRITICAL: VS=%p PS=%p", g_StarfieldState.pass2VS, g_StarfieldState.pass2PS);
    }

    context->Draw(3, 0);
    
    // Cleanup bindings
    ID3D11ShaderResourceView* nullSRV[1] = {nullptr};
    ID3D11RenderTargetView* nullRTV = nullptr;
    ID3D11DepthStencilState* nullDS = nullptr;
    ID3D11BlendState* nullBlend = nullptr;
    
    context->PSSetShaderResources(0, 1, nullSRV);
    context->OMSetRenderTargets(1, &nullRTV, nullptr);
    context->OMSetDepthStencilState(nullDS, 0);
    context->OMSetBlendState(nullBlend, nullptr, 0xFFFFFFFF);
    
    currentRTV->Release();
    if (currentDSV) currentDSV->Release();
    device->Release();  // Release the reference we obtained at start
}

static void UNITY_INTERFACE_API OnStarfieldRenderEvent(int eventId)
{
    if (!g_StarfieldState.device) return;
    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    ID3D11DeviceContext* context = nullptr;
    g_StarfieldState.device->GetImmediateContext(&context);
    if (!context) return;
    
    ExecuteStarfieldRender(context);
    
    context->Release();
    
    // Increment temporal frame index
    g_StarfieldState.frameIndex = (g_StarfieldState.frameIndex + 1) & 7;
}

// Starfield Exports
extern "C" __declspec(dllexport)
void CR_StarfieldSetCameraMatrices(ID3D11Texture2D* deviceSourceTexture, int width, int height,
                                   float verticalFOV, float aspectRatio, float3 cameraRight, float3 cameraUp, float3 cameraForward)
{    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    g_StarfieldState.width = width;
    g_StarfieldState.height = height;
    g_StarfieldState.verticalFOV = verticalFOV;
    g_StarfieldState.aspectRatio = aspectRatio;
    g_StarfieldState.cameraRight = cameraRight;
    g_StarfieldState.cameraUp = cameraUp;
    g_StarfieldState.cameraForward = cameraForward;
    
    // Acquire device from any valid texture (we use whiteTexture from C#)
    if (deviceSourceTexture && !g_StarfieldState.device) {
        ID3D11Device* device = nullptr;
        deviceSourceTexture->GetDevice(&device);
        if (device) {
            g_StarfieldState.device = device;
            g_StarfieldState.device->AddRef();
            EnsureStarfieldResources(device, width, height);
        }
    }
}

extern "C" __declspec(dllexport)
void CR_StarfieldSetSettings(const StarfieldSettingsNative* settings)
{
    if (!settings) return;
    
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    g_StarfieldState.exposure = settings->Exposure;
    g_StarfieldState.blurPixels = settings->BlurPixels;
    g_StarfieldState.starDensity = settings->StarDensity;
    g_StarfieldState.minMagnitude = settings->MinMagnitude;
    g_StarfieldState.maxMagnitude = settings->MaxMagnitude;
    g_StarfieldState.magnitudeBias = settings->MagnitudeBias;
    g_StarfieldState.heroRarity = settings->HeroRarity;
    g_StarfieldState.clustering = settings->Clustering;
    g_StarfieldState.staggerAmount = settings->StaggerAmount;
    g_StarfieldState.populationBias = settings->PopulationBias;
    g_StarfieldState.mainSequenceStrength = settings->MainSequenceStrength;
    g_StarfieldState.redGiantRarity = settings->RedGiantRarity;
    g_StarfieldState.galacticFlatness = settings->GalacticFlatness;
    g_StarfieldState.galacticDiscFalloff = settings->GalacticDiscFalloff;
    g_StarfieldState.bandCenterBoost = settings->BandCenterBoost;
    g_StarfieldState.bandCoreSharpness = settings->BandCoreSharpness;
    g_StarfieldState.bulgeIntensity = settings->BulgeIntensity;
    g_StarfieldState.bulgeWidth = settings->BulgeWidth;
    g_StarfieldState.bulgeHeight = settings->BulgeHeight;
    g_StarfieldState.bulgeSoftness = settings->BulgeSoftness;
    g_StarfieldState.bulgeNoiseScale = settings->BulgeNoiseScale;
    g_StarfieldState.bulgeNoiseStrength = settings->BulgeNoiseStrength;
    g_StarfieldState.bloomThreshold = settings->BloomThreshold;
    g_StarfieldState.bloomIntensity = settings->BloomIntensity;
    g_StarfieldState.spikeIntensity = settings->SpikeIntensity;
}

extern "C" __declspec(dllexport)
UnityRenderingEvent CR_GetStarfieldRenderEventFunc()
{
    return OnStarfieldRenderEvent;
}

extern "C" __declspec(dllexport)
void CR_StarfieldShutdown()
{
    std::lock_guard<std::mutex> lock(g_StarfieldState.stateMutex);
    
    if (g_StarfieldState.hdrSRV) { g_StarfieldState.hdrSRV->Release(); g_StarfieldState.hdrSRV = nullptr; }
    if (g_StarfieldState.hdrUAV) { g_StarfieldState.hdrUAV->Release(); g_StarfieldState.hdrUAV = nullptr; }
    if (g_StarfieldState.hdrTexture) { g_StarfieldState.hdrTexture->Release(); g_StarfieldState.hdrTexture = nullptr; }
    if (g_StarfieldState.pass1CS) { g_StarfieldState.pass1CS->Release(); g_StarfieldState.pass1CS = nullptr; }
    if (g_StarfieldState.pass2VS) { g_StarfieldState.pass2VS->Release(); g_StarfieldState.pass2VS = nullptr; }
    if (g_StarfieldState.pass2PS) { g_StarfieldState.pass2PS->Release(); g_StarfieldState.pass2PS = nullptr; }
    if (g_StarfieldState.linearSampler) { g_StarfieldState.linearSampler->Release(); g_StarfieldState.linearSampler = nullptr; }
    if (g_StarfieldState.depthState) { g_StarfieldState.depthState->Release(); g_StarfieldState.depthState = nullptr; }
    if (g_StarfieldState.blendState) { g_StarfieldState.blendState->Release(); g_StarfieldState.blendState = nullptr; }
    if (g_StarfieldState.rasterState) { g_StarfieldState.rasterState->Release(); g_StarfieldState.rasterState = nullptr; }
    if (g_StarfieldState.pass1CB) { g_StarfieldState.pass1CB->Release(); g_StarfieldState.pass1CB = nullptr; }
    if (g_StarfieldState.pass2CB) { g_StarfieldState.pass2CB->Release(); g_StarfieldState.pass2CB = nullptr; }
    if (g_StarfieldState.device) { g_StarfieldState.device->Release(); g_StarfieldState.device = nullptr; }
    
    g_StarfieldState.initialized = false;
}