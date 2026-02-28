using System;
using System.IO;
using System.Runtime.InteropServices;
using CinematicShaders.Core;
using CinematicShaders.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinematicShaders.Shaders.GTAO
{
    /// <summary>
    /// Attaches to the main camera and manages GTAO rendering via native compute shaders.
    /// </summary>
    public class GTAOCompositor : MonoBehaviour
    {
        #region Native Imports & DLL Loading
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        static GTAOCompositor()
        {
            try
            {
                string assemblyPath = Path.GetDirectoryName(typeof(GTAOCompositor).Assembly.Location);
                if (assemblyPath != null)
                {
                    string pluginDataPath = Path.GetFullPath(Path.Combine(assemblyPath, "..", "PluginData"));
                    string dllPath = Path.Combine(pluginDataPath, "CinematicShadersNative.dll");

                    if (!File.Exists(dllPath))
                    {
                        Debug.LogError($"[GTAOCompositor] Native DLL not found: {dllPath}");
                        return;
                    }

                    IntPtr hModule = LoadLibrary(dllPath);
                    if (hModule == IntPtr.Zero)
                    {
                        Debug.LogError($"[GTAOCompositor] LoadLibrary failed: {Marshal.GetLastWin32Error()}");
                        return;
                    }

                    Debug.Log("[GTAOCompositor] Native DLL loaded successfully");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GTAOCompositor] Static init error: {ex}");
            }
        }
        #endregion

        private Camera _camera;
        private RenderTexture _depthRT;
        private RenderTexture _normalRT;
        private CommandBuffer _depthCaptureBuffer;
        private CommandBuffer _normalCaptureBuffer;
        private CommandBuffer _gtaoPipelineBuffer;
        private bool _initialized = false;
        private static int _gtaoFrameIndex = 0;

        void OnEnable()
        {
            Initialize();
        }

        void OnDisable()
        {
            Cleanup();
        }

        void OnDestroy()
        {
            Cleanup();
            GTAONative.CR_GTAOShutdown();
        }

        private void Initialize()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null)
            {
                Debug.LogError("[GTAOCompositor] No camera found!");
                enabled = false;
                return;
            }

            int width = _camera.pixelWidth;
            int height = _camera.pixelHeight;

            // Create render textures for depth/normal capture
            _depthRT = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
            _depthRT.Create();

            _normalRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB2101010);
            _normalRT.Create();

            // 1. Depth/Normal capture at BeforeImageEffectsOpaque (fills textures)
            _depthCaptureBuffer = new CommandBuffer();
            _depthCaptureBuffer.name = "GTAO Capture Depth";
            _depthCaptureBuffer.Blit(BuiltinRenderTextureType.ResolvedDepth, _depthRT);
            _camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, _depthCaptureBuffer);

            _normalCaptureBuffer = new CommandBuffer();
            _normalCaptureBuffer.name = "GTAO Capture Normals";
            _normalCaptureBuffer.Blit(BuiltinRenderTextureType.GBuffer2, _normalRT);
            _camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, _normalCaptureBuffer);

            // 2. GTAO Pipeline at BeforeImageEffects
            // This ensures GTAO runs BEFORE image effects and capture
            _gtaoPipelineBuffer = new CommandBuffer();
            _gtaoPipelineBuffer.name = "GTAO Compute and Composite";
            _gtaoPipelineBuffer.IssuePluginEvent(GTAONative.CR_GetGTAORenderEventFunc(), 0);
            _camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, _gtaoPipelineBuffer);

            _initialized = true;
        }

        private void Cleanup()
        {
            if (_camera != null)
            {
                if (_depthCaptureBuffer != null)
                    _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, _depthCaptureBuffer);
                if (_normalCaptureBuffer != null)
                    _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, _normalCaptureBuffer);
                if (_gtaoPipelineBuffer != null)
                    _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _gtaoPipelineBuffer);
            }

            if (_depthCaptureBuffer != null)
            {
                _depthCaptureBuffer.Release();
                _depthCaptureBuffer = null;
            }
            if (_normalCaptureBuffer != null)
            {
                _normalCaptureBuffer.Release();
                _normalCaptureBuffer = null;
            }
            if (_gtaoPipelineBuffer != null)
            {
                _gtaoPipelineBuffer.Release();
                _gtaoPipelineBuffer = null;
            }
            if (_depthRT != null)
            {
                _depthRT.Release();
                Destroy(_depthRT);
                _depthRT = null;
            }
            if (_normalRT != null)
            {
                _normalRT.Release();
                Destroy(_normalRT);
                _normalRT = null;
            }

            _initialized = false;
        }

        // CRITICAL: OnPreRender runs BEFORE the CommandBuffer executes
        // This allows us to upload frame data that the render callback will use
        void OnPreRender()
        {
            if (!_initialized || !GTAOSettings.EnableGTAO)
                return;

            // Get camera matrices for the upcoming frame
            Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, false);
            Matrix4x4 invProjMatrix = projMatrix.inverse;
            float[] invProjArray = new float[16];
            for (int i = 0; i < 16; i++)
                invProjArray[i] = invProjMatrix[i];

            Matrix4x4 worldToCamera = _camera.worldToCameraMatrix;
            float[] worldToViewArray = new float[9];
            worldToViewArray[0] = worldToCamera[0, 0];
            worldToViewArray[1] = worldToCamera[0, 1];
            worldToViewArray[2] = worldToCamera[0, 2];
            worldToViewArray[3] = worldToCamera[1, 0];
            worldToViewArray[4] = worldToCamera[1, 1];
            worldToViewArray[5] = worldToCamera[1, 2];
            worldToViewArray[6] = worldToCamera[2, 0];
            worldToViewArray[7] = worldToCamera[2, 1];
            worldToViewArray[8] = worldToCamera[2, 2];

            // Pass pointers to native state
            // These will be read by OnGTAORenderEvent on the render thread
            GTAONative.CR_GTAODebugSetInput(
                _depthRT.GetNativeTexturePtr(),
                _normalRT.GetNativeTexturePtr(),
                _depthRT.width,
                _depthRT.height,
                invProjArray,
                worldToViewArray,
                _camera.nearClipPlane,
                _camera.farClipPlane,
                _gtaoFrameIndex);

            // Set output mode (0=Composite, 1=Raw AO)
            GTAONative.CR_GTAOSetOutputMode(GTAOSettings.GTAORawAOOutput ? 1 : 0);

            // Advance temporal index (0-7)
            _gtaoFrameIndex = (_gtaoFrameIndex + 1) & 7;
        }

        // Handle resolution changes (window resize)
        void Update()
        {
            if (!_initialized) return;

            if (_camera.pixelWidth != _depthRT.width || _camera.pixelHeight != _depthRT.height)
            {
                Cleanup();
                Initialize();
            }
        }
    }
}