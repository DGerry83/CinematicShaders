using CinematicShaders.Core;
using CinematicShaders.Native;
using UnityEngine;

namespace CinematicShaders.Shaders.Starfield
{
    public static class StarfieldManager
    {
        private static StarfieldCompositor _compositor;
        public static bool IsActive => _compositor != null && _compositor.enabled;
        public static StarfieldCompositor Compositor => _compositor;
        public static void ClearCompositorReference() => _compositor = null;

        public static void OnToggleChanged()
        {
            bool shouldEnable = StarfieldSettings.EnableStarfield;
            bool isEnabled = _compositor != null && _compositor.enabled;

            if (shouldEnable && !isEnabled)
            {
                EnableStarfield();
            }
            else if (!shouldEnable && isEnabled)
            {
                DisableStarfield();
            }
        }

        private static void EnableStarfield()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                Debug.Log("[StarfieldManager] Skipping enable - Editor scene has no sky");
                return;
            }

            //if (HighLogic.LoadedScene == GameScenes.MAINMENU)
            //{
            //    Debug.Log("[StarfieldManager] Skipping enable - MainMenu has no camera");
            //    return;
            //}

            if (_compositor != null && _compositor.enabled) return;

            // Push settings to native BEFORE enabling the compositor
            if (StarfieldNative.IsLoaded)
            {
                StarfieldSettings.PushSettingsToNative();
            }
            else
            {
                Debug.LogWarning("[StarfieldManager] Native DLL not loaded");
                return;
            }

            // Create persistent GameObject to host the compositor
            // (ScaledSpace camera gets destroyed during scene transitions, so we need our own host)
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

            Debug.Log("[StarfieldManager] Starfield enabled on persistent host");
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
            // With ScaledSpace rendering, we just check if compositor exists and is enabled
            // The compositor handles its own camera discovery internally
            return _compositor != null && _compositor.enabled && _compositor.gameObject != null;
        }

        public static void Initialize()
        {
            if (StarfieldSettings.EnableStarfield)
            {
                EnableStarfield();
            }
        }
    }
}