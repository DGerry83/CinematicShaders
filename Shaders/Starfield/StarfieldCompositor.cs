using CinematicShaders.Core;
using CinematicShaders.Native;
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinematicShaders.Shaders.Starfield
{
    public class StarfieldCompositor : MonoBehaviour
    {
        private Camera _camera;
        private RenderTexture _depthRT;  // Depth texture (keep for now, or remove if unused)
        private RenderTexture _normalRT; // Normal texture for sky masking via alpha
        private CommandBuffer _depthCaptureBuffer;
        private CommandBuffer _normalCaptureBuffer;
        private CommandBuffer _starfieldRenderBuffer;
        private bool _initialized = false;
        private int _frameIndex = 0;

        // Cached camera params to detect FOV changes
        private float _cachedFOV;
        private float _cachedAspect;

        void OnEnable()
        {
            Initialize();
        }

        void OnDisable()
        {
            _initialized = false;
            Cleanup();
        }

        void OnDestroy()
        {
            // Prevent any further rendering immediately
            _initialized = false;

            if (StarfieldManager.Compositor == this)
                StarfieldManager.ClearCompositorReference();

            Cleanup();

            try
            {
                if (StarfieldNative.IsLoaded)
                    StarfieldNative.CR_StarfieldShutdown();
            }
            catch (System.Exception)
            {
                /* DLL already unloaded, ignore */
            }
        }

        private void Initialize()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null)
            {
                Debug.LogError("[StarfieldCompositor] No camera found!");
                enabled = false;
                return;
            }

            int width = _camera.pixelWidth;
            int height = _camera.pixelHeight;

            // Depth texture (keep for native API compatibility)
            _depthRT = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
            _depthRT.Create();

            // Normal texture for sky masking (ARGB2101010, alpha = 0 for sky, 1 for geometry)
            _normalRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB2101010);
            _normalRT.Create();

            // Create three separate command buffers to ensure render target is restored between blits and plugin event
            _depthCaptureBuffer = new CommandBuffer();
            _depthCaptureBuffer.name = "Starfield Capture Depth";
            _depthCaptureBuffer.Blit(BuiltinRenderTextureType.ResolvedDepth, _depthRT);
            _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _depthCaptureBuffer);

            _normalCaptureBuffer = new CommandBuffer();
            _normalCaptureBuffer.name = "Starfield Capture Normals";
            _normalCaptureBuffer.Blit(BuiltinRenderTextureType.GBuffer2, _normalRT);
            _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _normalCaptureBuffer);

            _starfieldRenderBuffer = new CommandBuffer();
            _starfieldRenderBuffer.name = "Procedural Starfield Render";
            IntPtr renderEventFunc = StarfieldNative.CR_GetStarfieldRenderEventFunc();
            _starfieldRenderBuffer.IssuePluginEvent(renderEventFunc, 0);
            _camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, _starfieldRenderBuffer);

            _cachedFOV = _camera.fieldOfView;
            _cachedAspect = _camera.aspect;
            _initialized = true;

            Debug.Log("[StarfieldCompositor] Initialized");
        }

        private void Cleanup()
        {
            _initialized = false;

            if (_camera != null)
            {
                if (_depthCaptureBuffer != null)
                    _camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _depthCaptureBuffer);
                if (_normalCaptureBuffer != null)
                    _camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _normalCaptureBuffer);
                if (_starfieldRenderBuffer != null)
                    _camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, _starfieldRenderBuffer);
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
            if (_starfieldRenderBuffer != null)
            {
                _starfieldRenderBuffer.Release();
                _starfieldRenderBuffer = null;
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
        }

        void OnPreRender()
        {
            if (!_initialized || !StarfieldNative.IsLoaded)
                return;

            Debug.Log($"[StarfieldCompositor] OnPreRender: Camera={_camera.name} FOV={_camera.fieldOfView} DepthRT ptr={_depthRT.GetNativeTexturePtr()}");

            float verticalFOV = _camera.fieldOfView * Mathf.Deg2Rad;

            // Extract basis vectors in Surface Frame (rotating with planet)
            Vector3 surfaceRight = _camera.transform.right;
            Vector3 surfaceUp = _camera.transform.up;
            Vector3 surfaceForward = _camera.transform.forward;

            // Transform to Inertial Frame (fixed celestial frame) to counteract planetary rotation
            // Planetarium.Rotation converts Inertial->Surface, so we use Inverse to go Surface->Inertial
            QuaternionD inverseRotation = QuaternionD.Inverse(Planetarium.Rotation);

            Vector3 right = (Vector3)(inverseRotation * (Vector3d)surfaceRight);
            Vector3 up = (Vector3)(inverseRotation * (Vector3d)surfaceUp);
            Vector3 forward = (Vector3)(inverseRotation * (Vector3d)surfaceForward);

            StarfieldNative.CR_StarfieldSetCameraMatrices(
                _depthRT.GetNativeTexturePtr(),
                _normalRT.GetNativeTexturePtr(),
                _depthRT.width,
                _depthRT.height,
                verticalFOV,
                _camera.aspect,
                right,
                up,
                forward
            );

            _frameIndex = (_frameIndex + 1) & 7; // Temporal index 0-7
        }

        void Update()
        {
            if (!_initialized) return;

            // Handle screen resize
            if (_camera.pixelWidth != _depthRT.width ||
                _camera.pixelHeight != _depthRT.height)
            {
                Cleanup();
                Initialize();
                return;
            }

            // Handle FOV changes (Update camera matrices for shader)
            if (!Mathf.Approximately(_camera.fieldOfView, _cachedFOV) ||
                !Mathf.Approximately(_camera.aspect, _cachedAspect))
            {
                _cachedFOV = _camera.fieldOfView;
                _cachedAspect = _camera.aspect;
                // Matrices will be updated in next OnPreRender
            }
        }

        // Called by manager when settings change
        public void InvalidateResources()
        {
            Cleanup();
            Initialize();
        }
    }
}