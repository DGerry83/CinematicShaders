using CinematicShaders.Core;
using CinematicShaders.Native;
using UnityEngine;
using System.Collections;

namespace CinematicShaders.Shaders.Starfield
{
    public static class StarfieldManager
    {
        private static StarfieldCompositor _compositor;
        private static MonoBehaviour _coroutineHost;
        public static bool IsActive => _compositor != null && _compositor.enabled;
        public static StarfieldCompositor Compositor => _compositor;
        public static void ClearCompositorReference() => _compositor = null;

        public static void OnToggleChanged()
        {
            bool shouldEnable = StarfieldSettings.EnableStarfield;
            bool isEnabled = _compositor != null && _compositor.enabled;

            if (shouldEnable && !isEnabled)
            {
                // User just toggled ON - try to enable immediately
                // If camera isn't ready, the coroutine will handle it
                TryEnableWithCameraWait();
            }
            else if (!shouldEnable && isEnabled)
            {
                DisableStarfield();
            }
        }

        /// <summary>
        /// Try to enable starfield, waiting for camera if necessary
        /// </summary>
        private static void TryEnableWithCameraWait()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                Debug.Log("[StarfieldManager] Skipping - Editor scene has no sky");
                return;
            }

            if (_compositor != null && _compositor.enabled) 
            {
                Debug.Log("[StarfieldManager] Already enabled");
                return;
            }

            // Check if camera is ready right now
            if (IsCameraReady())
            {
                EnableStarfieldNow();
            }
            else
            {
                // Start coroutine to wait for camera
                Debug.Log("[StarfieldManager] Camera not ready, starting wait coroutine...");
                StartCameraWaitCoroutine();
            }
        }

        /// <summary>
        /// Check if the galaxy camera is available
        /// </summary>
        private static bool IsCameraReady()
        {
            // Try to find GalaxyCamera directly first
            GameObject camObj = GameObject.Find("GalaxyCamera");
            if (camObj != null)
            {
                Camera galaxyCam = camObj.GetComponent<Camera>();
                if (galaxyCam != null && galaxyCam.enabled)
                    return true;
            }

            // Fallback to ScaledCamera API if available
            try
            {
                var scaledCamera = ScaledCamera.Instance;
                if (scaledCamera != null && scaledCamera.galaxyCamera != null)
                    return true;
            }
            catch
            {
                // ScaledCamera might not exist yet
            }

            return false;
        }

        /// <summary>
        /// Start a coroutine that waits for the galaxy camera then enables starfield
        /// </summary>
        private static void StartCameraWaitCoroutine()
        {
            // Need a MonoBehaviour to run coroutines - use the compositor host or create one
            if (_coroutineHost == null)
            {
                GameObject host = GameObject.Find("StarfieldCoroutineHost");
                if (host == null)
                {
                    host = new GameObject("StarfieldCoroutineHost");
                    UnityEngine.Object.DontDestroyOnLoad(host);
                }
                _coroutineHost = host.GetComponent<MonoBehaviour>();
                if (_coroutineHost == null)
                {
                    _coroutineHost = host.AddComponent<StarfieldCoroutineHost>();
                }
            }

            _coroutineHost.StopAllCoroutines();
            _coroutineHost.StartCoroutine(WaitForGalaxyCameraCoroutine());
        }

        private static IEnumerator WaitForGalaxyCameraCoroutine()
        {
            Debug.Log("[StarfieldManager] Waiting for galaxy camera...");
            
            int maxWaitFrames = 300; // 5 seconds at 60fps
            int waitedFrames = 0;
            
            while (!IsCameraReady() && waitedFrames < maxWaitFrames)
            {
                waitedFrames++;
                yield return null; // Wait one frame
            }

            if (IsCameraReady())
            {
                Debug.Log($"[StarfieldManager] Galaxy camera ready after {waitedFrames} frames");
                if (StarfieldSettings.EnableStarfield)
                {
                    EnableStarfieldNow();
                }
            }
            else
            {
                Debug.LogWarning("[StarfieldManager] Timed out waiting for galaxy camera");
            }
        }

        /// <summary>
        /// Enable starfield immediately (camera is confirmed ready)
        /// </summary>
        private static void EnableStarfieldNow()
        {
            Debug.Log("[StarfieldManager] Enabling starfield now...");

            // Skip if native DLL not loaded
            if (!StarfieldNative.IsLoaded)
            {
                Debug.LogWarning("[StarfieldManager] Cannot enable - native DLL not loaded");
                return;
            }
            
            // Mark catalog for reload - this triggers loading ActiveCatalogPath
            StarfieldSettings.InvalidateCatalogForReload();
            
            // Push settings (this will load the catalog)
            StarfieldSettings.PushSettingsToNative();

            // Create persistent GameObject to host the compositor
            GameObject compositorHost = GameObject.Find("StarfieldCompositorHost");
            if (compositorHost == null)
            {
                compositorHost = new GameObject("StarfieldCompositorHost");
                UnityEngine.Object.DontDestroyOnLoad(compositorHost);
            }

            _compositor = compositorHost.GetComponent<StarfieldCompositor>();
            if (_compositor == null)
            {
                _compositor = compositorHost.AddComponent<StarfieldCompositor>();
            }
            _compositor.enabled = true;

            Debug.Log("[StarfieldManager] Starfield enabled successfully");
        }

        public static void DisableStarfield()
        {
            if (_compositor != null)
            {
                _compositor.enabled = false;
                Debug.Log("[StarfieldManager] Starfield disabled");
            }
        }

        public static bool IsCompositorOnCurrentCamera()
        {
            return _compositor != null && _compositor.enabled && _compositor.gameObject != null;
        }

        /// <summary>
        /// Called on scene changes - start the camera wait process if starfield should be enabled
        /// </summary>
        public static void Initialize()
        {
            Debug.Log($"[StarfieldManager] Initialize called: EnableStarfield={StarfieldSettings.EnableStarfield}, IsActive={IsActive}");
            
            if (!StarfieldSettings.EnableStarfield) 
            {
                Debug.Log("[StarfieldManager] Skipping - starfield disabled in settings");
                return;
            }
            
            // Don't try to init immediately - wait for camera to be ready
            // This prevents the race conditions with device/camera initialization
            TryEnableWithCameraWait();
        }

        /// <summary>
        /// Simple MonoBehaviour host for running coroutines
        /// </summary>
        private class StarfieldCoroutineHost : MonoBehaviour { }
    }
}
