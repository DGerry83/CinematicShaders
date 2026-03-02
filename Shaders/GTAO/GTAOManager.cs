using CinematicShaders.Core;
using CinematicShaders.Native;
using UnityEngine;

namespace CinematicShaders.Shaders.GTAO
{
    /// <summary>
    /// Manages the GTAO compositor component on the main camera.
    /// Called when GTAOSettings.EnableGTAO changes.
    /// </summary>
    public static class GTAOManager
    {
        private static GTAOCompositor _compositor;
        public static bool IsActive => _compositor != null && _compositor.enabled;
        public static GTAOCompositor Compositor => _compositor;
        public static void ClearCompositorReference() => _compositor = null;

        /// <summary>
        /// Call this when the GTAO enable toggle changes.
        /// </summary>
        public static void OnToggleChanged()
        {
            bool shouldEnable = GTAOSettings.EnableGTAO;
            bool isEnabled = _compositor != null && _compositor.enabled;

            if (shouldEnable && !isEnabled)
            {
                EnableGTAO();
            }
            else if (!shouldEnable && isEnabled)
            {
                DisableGTAO();
            }
        }

        private static void EnableGTAO()
        {
            // Prevent re-initialization if already active
            if (_compositor != null && _compositor.enabled) return;

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                Debug.LogWarning("[GTAOManager] No main camera found for GTAO");
                return;
            }

            if (mainCam.actualRenderingPath != RenderingPath.DeferredShading)
            {
                Debug.LogWarning("[GTAOManager] GTAO requires deferred rendering");
                return;
            }

            // CRITICAL: Push settings to native BEFORE enabling the compositor
            if (GTAONative.IsLoaded)
            {
                GTAOSettings.PushSettingsToNative();
            }

            _compositor = mainCam.GetComponent<GTAOCompositor>();
            if (_compositor == null)
            {
                _compositor = mainCam.gameObject.AddComponent<GTAOCompositor>();
            }
            _compositor.enabled = true;

            Debug.Log("[GTAOManager] GTAO enabled");
        }

        public static void DisableGTAO()
        {
            if (_compositor != null)
            {
                _compositor.enabled = false;
                Debug.Log("[GTAOManager] GTAO disabled");
            }
        }

        public static bool IsCompositorOnCurrentCamera()
        {
            if (_compositor == null) return false;
            return _compositor.gameObject == Camera.main?.gameObject;
        }

        public static void EnableDebugMode()
        {
            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            _compositor = mainCam.GetComponent<GTAOCompositor>();
            if (_compositor == null)
            {
                _compositor = mainCam.gameObject.AddComponent<GTAOCompositor>();
            }
            _compositor.enabled = true;
        }

        /// <summary>
        /// Call at startup to sync with GTAOSettings.
        /// </summary>
        public static void Initialize()
        {
            if (GTAOSettings.EnableGTAO)
            {
                EnableGTAO();
            }
        }
    }
}