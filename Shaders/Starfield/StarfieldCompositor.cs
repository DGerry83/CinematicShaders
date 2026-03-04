using CinematicShaders.Core;
using CinematicShaders.Native;
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinematicShaders.Shaders.Starfield
{
    public class StarfieldCompositor : MonoBehaviour
    {
        private Camera _scaledSpaceCamera;
        private RenderTexture _dummyDepthRT;
        private RenderTexture _dummyNormalRT;
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
            if (_initialized)
            {
                Camera.onPreRender += OnPreRender;
            }
        }

        void OnDisable()
        {
            _initialized = false;
            Cleanup();
            Camera.onPreRender -= OnPreRender;
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
            // Find Galaxy Camera (renders first in all scenes with sky, handles all scene types)
            _scaledSpaceCamera = null;
            GameObject camObj = GameObject.Find("GalaxyCamera");
            if (camObj != null)
            {
                _scaledSpaceCamera = camObj.GetComponent<Camera>();
            }

            if (_scaledSpaceCamera == null)
            {
                Debug.Log("[StarfieldCompositor] Galaxy Camera not found - no sky to draw");
                enabled = false;
                return;
            }

            // Create dummy 1x1 textures to satisfy native API (alpha=0 means sky everywhere)
            _dummyDepthRT = new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat);
            _dummyDepthRT.Create();

            _dummyNormalRT = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGB2101010);
            _dummyNormalRT.Create();

            // Create render buffer for Pass 2 (composite stars)
            _starfieldRenderBuffer = new CommandBuffer();
            _starfieldRenderBuffer.name = "Procedural Starfield Render";
            IntPtr renderEventFunc = StarfieldNative.CR_GetStarfieldRenderEventFunc();
            _starfieldRenderBuffer.IssuePluginEvent(renderEventFunc, 0);

            // Attach to ScaledSpace camera BEFORE it renders planets (so stars appear behind)
            _scaledSpaceCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, _starfieldRenderBuffer);

            _cachedFOV = _scaledSpaceCamera.fieldOfView;
            _cachedAspect = _scaledSpaceCamera.aspect;
            _initialized = true;
        }

        private void Cleanup()
        {
            _initialized = false;

            // Remove command buffer from whichever camera it was attached to
            if (_scaledSpaceCamera != null && _starfieldRenderBuffer != null)
            {
                _scaledSpaceCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, _starfieldRenderBuffer);
            }

            if (_starfieldRenderBuffer != null)
            {
                _starfieldRenderBuffer.Release();
                _starfieldRenderBuffer = null;
            }

            if (_dummyDepthRT != null)
            {
                _dummyDepthRT.Release();
                Destroy(_dummyDepthRT);
                _dummyDepthRT = null;
            }
            if (_dummyNormalRT != null)
            {
                _dummyNormalRT.Release();
                Destroy(_dummyNormalRT);
                _dummyNormalRT = null;
            }
        }

        void OnPreRender(Camera cam)
        {
            // Only process for our target galaxy camera
            if (cam != _scaledSpaceCamera) return;

            if (!_initialized || !StarfieldNative.IsLoaded || _scaledSpaceCamera == null)
                return;

            float verticalFOV = _scaledSpaceCamera.fieldOfView * Mathf.Deg2Rad;

            // Extract basis vectors in Surface Frame (rotating with planet)
            Vector3 surfaceRight = _scaledSpaceCamera.transform.right;
            Vector3 surfaceUp = _scaledSpaceCamera.transform.up;
            Vector3 surfaceForward = _scaledSpaceCamera.transform.forward;

            // Transform to Inertial Frame (fixed celestial frame) to counteract planetary rotation
            QuaternionD inverseRotation = QuaternionD.Inverse(Planetarium.Rotation);

            Vector3 right = (Vector3)(inverseRotation * (Vector3d)surfaceRight);
            Vector3 up = (Vector3)(inverseRotation * (Vector3d)surfaceUp);
            Vector3 forward = (Vector3)(inverseRotation * (Vector3d)surfaceForward);

            // Use dummy textures (1x1 black) to satisfy native API - actual occlusion via painter's algorithm
            StarfieldNative.CR_StarfieldSetCameraMatrices(
                _dummyDepthRT.GetNativeTexturePtr(),
                _dummyNormalRT.GetNativeTexturePtr(),
                _scaledSpaceCamera.pixelWidth,
                _scaledSpaceCamera.pixelHeight,
                verticalFOV,
                _scaledSpaceCamera.aspect,
                right,
                up,
                forward
            );

            _frameIndex = (_frameIndex + 1) & 7; // Temporal index 0-7
        }

        void Update()
        {
            if (!_initialized) return;

            // Handle ScaledSpace camera destruction (scene transitions)
            if (_scaledSpaceCamera == null)
            {
                Debug.Log("[StarfieldCompositor] ScaledSpace camera lost, cleaning up...");
                Cleanup();
                return;
            }

            // Handle screen resize or camera change
            if (_scaledSpaceCamera.pixelWidth != _dummyDepthRT.width + 1 || // Dummy is 1x1, actual camera changed
                _scaledSpaceCamera.pixelHeight != _dummyDepthRT.height + 1)
            {
                // Reinitialize to catch new camera dimensions
                Cleanup();
                Initialize();
                return;
            }

            // Handle FOV changes (Update camera matrices for shader)
            if (!Mathf.Approximately(_scaledSpaceCamera.fieldOfView, _cachedFOV) ||
                !Mathf.Approximately(_scaledSpaceCamera.aspect, _cachedAspect))
            {
                _cachedFOV = _scaledSpaceCamera.fieldOfView;
                _cachedAspect = _scaledSpaceCamera.aspect;
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