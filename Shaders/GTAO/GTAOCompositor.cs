using System;
using CinematicShaders.Core;
using CinematicShaders.Native;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinematicShaders.Shaders.GTAO
{
    public class GTAOCompositor : MonoBehaviour
    {
        private Camera _camera;
        private RenderTexture _depthRT;
        private RenderTexture _normalRT;
        private CommandBuffer _depthCaptureBuffer;
        private CommandBuffer _normalCaptureBuffer;
        private CommandBuffer _gtaoPipelineBuffer;
        private bool _initialized = false;
        private static int _gtaoFrameIndex = 0;
        private bool _isRawAOMode = false;

        void OnEnable()
        {
            if (!GTAONative.IsLoaded)
            {
                Debug.LogError("[GTAOCompositor] Native DLL not loaded. Disabling GTAO.");
                enabled = false;
                return;
            }

            Initialize();
        }

        void OnDisable()
        {
            Cleanup();
        }

        void OnDestroy()
        {
            if (GTAOManager.Compositor == this)
                GTAOManager.ClearCompositorReference();

            Cleanup();
            try
            {
                if (GTAONative.IsLoaded)
                    GTAONative.CR_GTAOShutdown();
            }
            catch (Exception)
            {
                /* DLL unloaded, ignore */
            }
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

            _depthRT = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
            _depthRT.Create();

            _normalRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB2101010);
            _normalRT.Create();

            // Capture depth/normals after opaque geometry, before clouds/flares
            _depthCaptureBuffer = new CommandBuffer();
            _depthCaptureBuffer.name = "GTAO Capture Depth";
            _depthCaptureBuffer.Blit(BuiltinRenderTextureType.ResolvedDepth, _depthRT);
            _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _depthCaptureBuffer);

            _normalCaptureBuffer = new CommandBuffer();
            _normalCaptureBuffer.name = "GTAO Capture Normals";
            _normalCaptureBuffer.Blit(BuiltinRenderTextureType.GBuffer2, _normalRT);
            _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _normalCaptureBuffer);

            _gtaoPipelineBuffer = new CommandBuffer();
            _gtaoPipelineBuffer.name = "GTAO Compute and Composite";
            _gtaoPipelineBuffer.IssuePluginEvent(GTAONative.CR_GetGTAORenderEventFunc(), 0);

            _isRawAOMode = GTAOSettings.GTAORawAOOutput || GTAOSettings.DebugVisualizationMode > 0;
            if (_isRawAOMode)
                _camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, _gtaoPipelineBuffer);
            else
                _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _gtaoPipelineBuffer);

            _initialized = true;
        }

        private void Cleanup()
        {
            if (_camera != null)
            {
                if (_depthCaptureBuffer != null)
                    _camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _depthCaptureBuffer);
                if (_normalCaptureBuffer != null)
                    _camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _normalCaptureBuffer);
            }

            if (_gtaoPipelineBuffer != null)
            {
                if (_isRawAOMode)
                    _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _gtaoPipelineBuffer);
                else
                    _camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _gtaoPipelineBuffer);

                _gtaoPipelineBuffer.Release();
                _gtaoPipelineBuffer = null;
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

        void OnPreRender()
        {
            if (!_initialized || !GTAONative.IsLoaded)
                return;

            if (!GTAOSettings.EnableGTAO && GTAOSettings.DebugVisualizationMode == 0)
                return;

            Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, false);
            Matrix4x4 invProj = projMatrix.inverse;
            float tanHalfFOVX = invProj[0, 0];
            float tanHalfFOVY = invProj[1, 1];
            float[] fovArray = new float[2] { tanHalfFOVX, tanHalfFOVY };

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

            GTAONative.CR_GTAODebugSetInput(
                _depthRT.GetNativeTexturePtr(),
                _normalRT.GetNativeTexturePtr(),
                _depthRT.width,
                _depthRT.height,
                worldToViewArray,
                fovArray,
                _camera.nearClipPlane,
                _camera.farClipPlane,
                _gtaoFrameIndex);

            int outputMode = GTAOSettings.DebugVisualizationMode > 0
                ? GTAOSettings.DebugVisualizationMode
                : (GTAOSettings.GTAORawAOOutput ? 1 : 0);
            GTAONative.CR_GTAOSetOutputMode(outputMode);

            // Temporal index 0-7
            _gtaoFrameIndex = (_gtaoFrameIndex + 1) & 7;
        }

        void Update()
        {
            if (!_initialized) return;

            if (_camera.pixelWidth != _depthRT.width || _camera.pixelHeight != _depthRT.height)
            {
                Cleanup();
                Initialize();
                return;
            }

            bool wantRawAO = GTAOSettings.GTAORawAOOutput || GTAOSettings.DebugVisualizationMode > 0;
            if (wantRawAO != _isRawAOMode)
            {
                if (_gtaoPipelineBuffer != null && _camera != null)
                {
                    if (_isRawAOMode)
                        _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _gtaoPipelineBuffer);
                    else
                        _camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _gtaoPipelineBuffer);

                    if (wantRawAO)
                        _camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, _gtaoPipelineBuffer);
                    else
                        _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _gtaoPipelineBuffer);
                }
                _isRawAOMode = wantRawAO;
            }
        }
    }
}