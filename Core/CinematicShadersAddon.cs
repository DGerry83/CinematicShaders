using CinematicShaders.Native;
using CinematicShaders.Shaders.GTAO;
using CinematicShaders.Shaders.Starfield;
using CinematicShaders.UI;
using KSP.UI.Screens;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CinematicShaders.Core
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class CinematicShadersAddon : MonoBehaviour
    {
        public static CinematicShadersAddon Instance { get; private set; }

        private static ApplicationLauncherButton _toolbarButton;
        private static Texture2D _toolbarIcon;

        private CinematicShadersWindow _mainWindow;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            GTAOSettings.Load();
            StarfieldSettings.Load();
            StarCatalogManager.Initialize();  // Ensure catalog folder exists

            // Only auto-enable if in a playable scene (not LOADING, MAINMENU, or EDITOR)
            if (IsPlayableScene() && (GTAOSettings.EnableGTAO || StarfieldSettings.EnableStarfield))
            {
                Invoke(nameof(DelayedInit), 0.5f);
            }

            GameEvents.onGUIApplicationLauncherReady.Add(OnGUIApplicationLauncherReady);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelWasLoadedGUIReady);
            
            // Listen for game load/save events for per-save settings
            GameEvents.onGameStateLoad.Add(OnGameStateLoad);
            GameEvents.onGameStateSave.Add(OnGameStateSave);

            if (_toolbarIcon == null)
            {
                _toolbarIcon = GameDatabase.Instance.GetTexture("CinematicShaders/Icons/ToolbarIcon", false);
                if (_toolbarIcon == null)
                {
                    _toolbarIcon = new Texture2D(38, 38, TextureFormat.RGBA32, false);
                    Color[] pixels = new Color[38 * 38];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(1f, 0.5f, 0f);
                    _toolbarIcon.SetPixels(pixels);
                    _toolbarIcon.Apply();
                }
            }
        }

        private void DelayedInit()
        {
            if (GTAOSettings.EnableGTAO)
                GTAOManager.Initialize();
            if (StarfieldSettings.EnableStarfield)
                StarfieldManager.Initialize();
        }

        /// <summary>
        /// Check if current scene is a playable scene (not LOADING, MAINMENU, or EDITOR)
        /// Starfield needs a sky camera which only exists in these scenes
        /// </summary>
        private bool IsPlayableScene()
        {
            return HighLogic.LoadedScene == GameScenes.SPACECENTER ||
                   HighLogic.LoadedScene == GameScenes.FLIGHT ||
                   HighLogic.LoadedScene == GameScenes.TRACKSTATION;
        }

        private void OnLevelWasLoadedGUIReady(GameScenes scene)
        {
            if (scene == GameScenes.MAINMENU) return;

            // If coming from MAINMENU to a playable scene, reset the starfield compositor
            // It may be in a bad state from failed initialization during game startup
            if (StarfieldManager.IsActive)
            {
                Debug.Log("[CinematicShaders] Scene change from menu - resetting starfield compositor...");
                StarfieldManager.DisableStarfield();
            }

            // Mark catalog for reload on scene change (device may have reset)
            StarfieldSettings.InvalidateCatalogForReload();

            if (GTAOSettings.EnableGTAO)
            {
                if (scene == GameScenes.EDITOR && GTAOManager.IsActive)
                {
                    // Check if compositor is on the wrong (destroyed) camera
                    if (!GTAOManager.IsCompositorOnCurrentCamera())
                    {
                        Debug.Log("[CinematicShaders] Detected stale compositor in Editor, resetting...");
                        GTAOManager.DisableGTAO();
                    }
                }

                GTAOManager.Initialize();

                if (!GTAOManager.IsActive && scene == GameScenes.EDITOR)
                {
                    CancelInvoke(nameof(RetryInit));
                    Invoke(nameof(RetryInit), 0.5f);
                    Invoke(nameof(RetryInit), 1.5f);
                    Invoke(nameof(RetryInit), 3.0f);
                }
                if (StarfieldSettings.EnableStarfield && IsPlayableScene())
                {
                    StarfieldManager.Initialize();
                }
            }
        }

        private void RetryInit()
        {
            if (GTAOSettings.EnableGTAO && !GTAOManager.IsActive)
            {
                Debug.Log("[CinematicShaders] Retrying GTAO initialization...");
                GTAOManager.Initialize();
            }
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            CancelInvoke(nameof(RetryInit));

            GameEvents.onGUIApplicationLauncherReady.Remove(OnGUIApplicationLauncherReady);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelWasLoadedGUIReady);
            GameEvents.onGameStateLoad.Remove(OnGameStateLoad);
            GameEvents.onGameStateSave.Remove(OnGameStateSave);

            if (_toolbarButton != null && ApplicationLauncher.Instance != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(_toolbarButton);
                _toolbarButton = null;
            }

            if (_mainWindow != null && _mainWindow.gameObject != null)
                Destroy(_mainWindow.gameObject);

            // Shutdown GTAO
            try
            {
                if (GTAONative.IsLoaded)
                    GTAONative.CR_GTAOShutdown();
            }
            catch (System.Exception)
            {
                /* DLL already unloaded, ignore */
            }

            // Shutdown Starfield
            try
            {
                if (StarfieldNative.IsLoaded)
                    StarfieldNative.CR_StarfieldShutdown();
            }
            catch (System.Exception)
            {
                /* DLL already unloaded, ignore */
            }

            Instance = null;
        }

        private void OnGameStateLoad(ConfigNode node)
        {
            Debug.Log("[CinematicShaders] Game state loaded - applying per-save settings");
            
            // Apply per-save settings from ScenarioModule if available
            if (StarfieldPerSaveSettings.Instance != null)
            {
                Debug.Log("[CinematicShaders] Found StarfieldPerSaveSettings, applying...");
                StarfieldPerSaveSettings.Instance.ApplyToSettings();
            }
            else
            {
                Debug.LogWarning("[CinematicShaders] StarfieldPerSaveSettings.Instance is null!");
            }
            
            Debug.Log($"[CinematicShaders] After per-save settings: EnableStarfield={StarfieldSettings.EnableStarfield}, Catalog={StarfieldSettings.ActiveCatalogPath}");
            
            // Initialize starfield if enabled and we're in a playable scene
            if (StarfieldSettings.EnableStarfield && IsPlayableScene())
            {
                Debug.Log("[CinematicShaders] Initializing Starfield...");
                StarfieldManager.Initialize();
            }
        }

        private void OnGameStateSave(ConfigNode node)
        {
            Debug.Log("[CinematicShaders] Game state saving - capturing per-save settings");
            // Per-save settings are automatically saved by KSP from StarfieldPerSaveSettings.Instance
        }



        private void OnGUIApplicationLauncherReady()
        {
            if (_toolbarButton != null || Instance != this) return;

            if (ApplicationLauncher.Instance != null)
            {
                _toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                    OnToolbarButtonOn,
                    OnToolbarButtonOff,
                    null, null, null, null,
                    ApplicationLauncher.AppScenes.ALWAYS,
                    _toolbarIcon
                );
            }
        }

        private void OnToolbarButtonOn()
        {
            if (_mainWindow == null)
            {
                GameObject go = new GameObject("CinematicShadersWindow");
                DontDestroyOnLoad(go);
                _mainWindow = go.AddComponent<CinematicShadersWindow>();
                _mainWindow.OnClose += () =>
                {
                    if (_toolbarButton != null)
                        _toolbarButton.SetFalse(false);
                };
            }
            _mainWindow.Show();
        }

        private void OnToolbarButtonOff()
        {
            if (_mainWindow != null)
            {
                _mainWindow.Hide();
            }
        }
    }
}