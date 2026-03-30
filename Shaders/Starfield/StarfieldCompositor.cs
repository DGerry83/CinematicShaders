using CinematicShaders.Core;
using CinematicShaders.Native;
using CinematicShaders.UI;
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
        private CommandBuffer _starfieldRenderBuffer;
        private bool _initialized = false;
        private int _frameIndex = 0;

        // Cached camera params to detect FOV changes
        private float _cachedFOV;
        private float _cachedAspect;
        
        // Cached camera basis for KartographerSelector (Phase 2)
        private Vector3 _cachedCameraRight;
        private Vector3 _cachedCameraUp;
        private Vector3 _cachedCameraForward;
        private float _cachedVerticalFOV;

        public static float CachedVerticalFOV { get; private set; } = 60f * Mathf.Deg2Rad;

        void OnEnable()
        {
            Initialize();
            if (_initialized)
            {
                Camera.onPreRender += OnCameraPreRender;
            }
        }

        void OnDisable()
        {
            _initialized = false;
            Cleanup();
            Camera.onPreRender -= OnCameraPreRender;
        }

        void OnDestroy()
        {
            // Prevent any further rendering immediately
            _initialized = false;

            if (StarfieldManager.Compositor == this)
                StarfieldManager.ClearCompositorReference();

            Cleanup();

            // Invalidate native resources so they get recreated on next init
            // This preserves the catalog while ensuring fresh GPU resources
            StarfieldNative.InvalidateResources();
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
                Debug.Log("[StarfieldCompositor] Galaxy Camera not found - will retry next frame");
                // Don't disable immediately - let Update() retry
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

            // Ensure catalog is loaded/generated if settings indicate we should have one
            if (StarfieldSettings.EnableStarfield && StarfieldNative.IsLoaded)
            {
                // Check if native plugin has a catalog loaded
                int nativeCatalogSize = StarfieldNative.GetCatalogSize();
                if (nativeCatalogSize == 0)
                {
                    // Native plugin has no catalog - force reload
                    Debug.Log("[StarfieldCompositor] No catalog in native plugin, forcing reload...");
                    StarfieldSettings.InvalidateCatalogForReload();
                }
                // Push settings (this will load/generate catalog if needed)
                StarfieldSettings.PushSettingsToNative();
            }
            
            Debug.Log("[StarfieldCompositor] Initialization complete");
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

        // Cached reflection info for GalaxyCubeControl.glareFade (private field)
        private static System.Reflection.FieldInfo _glareFadeField = null;
        private static bool _glareFadeReflectionAttempted = false;

        /// <summary>
        /// Calculate dimming factor from sun glare (GalaxyCubeControl.glareFade).
        /// Returns 1.0 (full brightness) when sun not in view, down to 0.2f minimum when sun centered.
        /// Note: glareFade is a private field, accessed via cached reflection.
        /// </summary>
        private float GetSunGlareDimming()
        {
            if (GalaxyCubeControl.Instance == null)
                return 1.0f;

            if (!_glareFadeReflectionAttempted)
            {
                try
                {
                    _glareFadeField = typeof(GalaxyCubeControl).GetField("glareFade",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                }
                catch { _glareFadeField = null; }
                _glareFadeReflectionAttempted = true;
            }

            if (_glareFadeField == null) return 1.0f;

            try
            {
                float glare = (float)_glareFadeField.GetValue(GalaxyCubeControl.Instance);
                if (float.IsNaN(glare) || float.IsInfinity(glare)) return 1.0f;

                // Aggressive drop-off, then floor at 0.1
                // glareFade 0.0 → 1.0 (full stars)
                // glareFade 0.6 → ~0.15 (aggressive dimming)  
                // glareFade 1.0 → 0.1 (floor)
                float curvedDimming = 1.0f - Mathf.Pow(glare, 0.3f);

                const float MIN_BRIGHTNESS = 0.1f;
                return Mathf.Max(curvedDimming, MIN_BRIGHTNESS);
            }
            catch { return 1.0f; }
        }

        /// <summary>
        /// Calculate planetary dimming - FLIGHT ONLY with distance early-out.
        /// Skips bodies > 1 billion meters (1 million km) away.
        /// </summary>
        private float CalculatePlanetaryDimming(Camera cam)
        {
            // Only calculate in Flight view - coordinates are consistent here
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
                return 1.0f;

            if (cam == null || FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0)
                return 1.0f;

            Vector3d camPos = cam.transform.position;
            Vector3d camForward = cam.transform.forward;
            double camFov = cam.fieldOfView;

            double minDimming = 1.0;
            const double MIN_BRIGHTNESS = 0.25;  // Planets never dim below 25%
            const double MIN_ANGULAR_SIZE = 0.5;  // degrees
            const double REFERENCE_BODY_SIZE = 2.0;  // degrees
            const double MIN_TARGET_REL_ANGLE = 90.0;  // degrees
            const double MAX_DISTANCE_SQR = 1e18; // (1,000,000,000 meters)^2

            CelestialBody sun = FlightGlobals.Bodies[0];

            for (int i = 1; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body == null || body == sun) continue;

                // EARLY OUT: Skip bodies > 1 billion meters away (1 million km)
                Vector3d offset = body.position - camPos;
                if (offset.sqrMagnitude > MAX_DISTANCE_SQR)
                    continue;

                // Now do the math only for nearby bodies
                double dist = body.GetAltitude(camPos) + body.Radius;
                double radius = body.Radius;

                double bodyAngularSize = Math.Atan2(radius, dist) * (180.0 / Math.PI);

                if (bodyAngularSize < MIN_ANGULAR_SIZE)
                    continue;

                // Phase calculation: angle between body->sun and body->camera
                Vector3d bodyPos = body.position;
                Vector3d bodyToSun = sun.position - bodyPos;
                Vector3d bodyToCam = camPos - bodyPos;

                double phaseAngle = Vector3d.Angle(bodyToSun, bodyToCam);
                phaseAngle = Math.Max(phaseAngle, bodyAngularSize);
                phaseAngle = Math.Min(phaseAngle, MIN_TARGET_REL_ANGLE);
                double phaseFactor = 1.0 - ((phaseAngle - bodyAngularSize) / (MIN_TARGET_REL_ANGLE - bodyAngularSize));

                // View alignment: how centered in camera?
                double viewAngle = Math.Max(0.0, Vector3.Angle((bodyPos - camPos).normalized, camForward) - bodyAngularSize);
                double viewAlignment = 1.0 - Math.Min(1.0, Math.Max(0.0, (viewAngle - (camFov / 2.0)) - 5.0) / (camFov / 4.0));

                // Size weight
                double sizeWeight = Math.Sqrt(Math.Min(bodyAngularSize, REFERENCE_BODY_SIZE) / REFERENCE_BODY_SIZE);

                // Calculate dimming for this body
                double bodyDimming = 1.0 - (phaseFactor * sizeWeight * viewAlignment);

                if (bodyDimming < minDimming)
                {
                    minDimming = bodyDimming;
                    if (minDimming <= MIN_BRIGHTNESS)
                    {
                        minDimming = MIN_BRIGHTNESS;
                        break;
                    }
                }
            }

            return (float)Math.Max(minDimming, MIN_BRIGHTNESS);
        }

        void OnCameraPreRender(Camera cam)
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

            // Capture atmospheric extinction for this frame
            // Guard against null refs during scene transitions when atmosphere data isn't ready yet
            // I hate using try-catch to hide exceptions but I think it's appropriate here
            AtmosphericScatteringData.RawData atmoRaw;
            AtmosphericScatteringData.CalculatedData atmoCalc;
            try
            {
                atmoRaw = AtmosphericScatteringData.CaptureRawData();
                atmoCalc = AtmosphericScatteringData.Calculate(atmoRaw);
            }
            catch (Exception)
            {
                // Scene transition - atmosphere not ready, use defaults (no extinction)
                atmoRaw = new AtmosphericScatteringData.RawData { UpVector = Vector3.up };
                atmoCalc = new AtmosphericScatteringData.CalculatedData
                {
                    ExtinctionZenith = 1.0f,
                    ExtinctionHorizon = 1.0f
                };
            }

            // Pass whiteTexture to bootstrap D3D11 device acquisition in native code
            // (Texture2D.whiteTexture is a built-in 4x4 texture, no allocation/disposal needed)
            // IntPtr.Zero for explicitRenderTarget = use current render target from context
            StarfieldNative.CR_StarfieldSetCameraMatrices(
                Texture2D.whiteTexture.GetNativeTexturePtr(),
                _scaledSpaceCamera.pixelWidth,
                _scaledSpaceCamera.pixelHeight,
                verticalFOV,
                _scaledSpaceCamera.aspect,
                right,
                up,
                forward,
                atmoCalc.ExtinctionZenith,
                atmoCalc.ExtinctionHorizon,
                atmoRaw.UpVector,
                IntPtr.Zero  // explicitRenderTarget - use context's current RT
            );

            // Calculate and push global scene dimming (sun glare + planetary occlusion)
            float sunGlareDimming = GetSunGlareDimming();
            float planetaryDimming = CalculatePlanetaryDimming(_scaledSpaceCamera);
            StarfieldNative.CR_StarfieldSetDimming(sunGlareDimming, planetaryDimming);

            _frameIndex = (_frameIndex + 1) & 7; // Temporal index 0-7
            
            // Cache camera basis for KartographerSelector (Phase 2)
            _cachedCameraRight = right;
            _cachedCameraUp = up;
            _cachedCameraForward = forward;
            _cachedVerticalFOV = verticalFOV;
            CachedVerticalFOV = verticalFOV;
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
            
            // Check if native plugin needs catalog reload (device acquired late)
            if (StarfieldNative.CatalogNeedsReload())
            {
                Debug.Log("[StarfieldCompositor] Native plugin signaled catalog reload needed");
                StarfieldSettings.InvalidateCatalogForReload();
                StarfieldSettings.PushSettingsToNative();
            }
            
            // Update KartographerSelector with current camera basis (Phase 2)
            // Always call if callback is registered - the selector needs updates even when UI is closed
            if (KartographerSelectorCallback != null)
            {
                UpdateKartographerSelector();
            }
        }
        
        private void UpdateKartographerSelector()
        {
            // This will be called to update the selector - the actual implementation
            // needs access to the KartographerTab which is managed by the window
            // For now, we'll use a callback pattern
            KartographerSelectorCallback?.Invoke(_cachedCameraRight, _cachedCameraUp, _cachedCameraForward, _cachedAspect, _cachedVerticalFOV);
        }
        
        // Callback for KartographerTab to receive camera updates
        public static System.Action<Vector3, Vector3, Vector3, float, float> KartographerSelectorCallback;

        // Called by manager when settings change
        public void InvalidateResources()
        {
            Cleanup();
            Initialize();
        }
    }
}