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

            if (HighLogic.LoadedScene == GameScenes.MAINMENU)
            {
                Debug.Log("[StarfieldManager] Skipping enable - MainMenu has no camera");
                return;
            }

            if (_compositor != null && _compositor.enabled) return;

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                Debug.LogWarning("[StarfieldManager] No main camera found for starfield");
                return;
            }

            if (mainCam.actualRenderingPath != RenderingPath.DeferredShading)
            {
                Debug.LogWarning("[StarfieldManager] Starfield requires deferred rendering");
                return;
            }

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

            _compositor = mainCam.GetComponent<StarfieldCompositor>();
            if (_compositor == null)
            {
                _compositor = mainCam.gameObject.AddComponent<StarfieldCompositor>();
            }
            _compositor.enabled = true;

            Debug.Log("[StarfieldManager] Starfield enabled");
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
            if (_compositor == null) return false;
            return _compositor.gameObject == Camera.main?.gameObject;
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