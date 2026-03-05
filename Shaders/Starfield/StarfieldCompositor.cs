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
        private int _lastScreenWidth;
        private int _lastScreenHeight;
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

            // Create render buffer for Pass 2 (composite stars)
            _starfieldRenderBuffer = new CommandBuffer();
            _starfieldRenderBuffer.name = "Procedural Starfield Render";
            IntPtr renderEventFunc = StarfieldNative.CR_GetStarfieldRenderEventFunc();
            _starfieldRenderBuffer.IssuePluginEvent(renderEventFunc, 0);

            // Attach to ScaledSpace camera BEFORE it renders planets (so stars appear behind)
            _scaledSpaceCamera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, _starfieldRenderBuffer);

            _cachedFOV = _scaledSpaceCamera.fieldOfView;
            _cachedAspect = _scaledSpaceCamera.aspect;
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;

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

            // Pass whiteTexture to bootstrap D3D11 device acquisition in native code
            // (Texture2D.whiteTexture is a built-in 4x4 texture, no allocation/disposal needed)
            StarfieldNative.CR_StarfieldSetCameraMatrices(
                Texture2D.whiteTexture.GetNativeTexturePtr(),
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
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight ||
                _scaledSpaceCamera.pixelWidth != _lastScreenWidth ||
                _scaledSpaceCamera.pixelHeight != _lastScreenHeight)
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