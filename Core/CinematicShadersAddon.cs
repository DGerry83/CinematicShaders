using CinematicShaders.Native;
using CinematicShaders.Shaders.GTAO;
using CinematicShaders.UI;
using KSP.UI.Screens;
using UnityEngine;

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

            if (HighLogic.LoadedScene != GameScenes.MAINMENU && GTAOSettings.EnableGTAO)
            {
                Invoke(nameof(DelayedInit), 0.5f);
            }

            GameEvents.onGUIApplicationLauncherReady.Add(OnGUIApplicationLauncherReady);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelWasLoadedGUIReady);

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
        }

        private void OnLevelWasLoadedGUIReady(GameScenes scene)
        {
            if (scene == GameScenes.MAINMENU) return;

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

            if (_toolbarButton != null && ApplicationLauncher.Instance != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(_toolbarButton);
                _toolbarButton = null;
            }

            if (_mainWindow != null && _mainWindow.gameObject != null)
                Destroy(_mainWindow.gameObject);

            try
            {
                if (GTAONative.IsLoaded)
                    GTAONative.CR_GTAOShutdown();
            }
            catch (System.Exception)
            {
                /* DLL already unloaded, ignore */
            }

            Instance = null;
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