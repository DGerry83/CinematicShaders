using CinematicShaders.Native;
using CinematicShaders.Shaders.GTAO;
using CinematicShaders.UI;
using KSP.UI.Screens;
using UnityEngine;

namespace CinematicShaders.Core
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CinematicShadersAddon : MonoBehaviour
    {
        public static CinematicShadersAddon Instance { get; private set; }

        private ApplicationLauncherButton toolbarButton;
        private CinematicShadersWindow mainWindow;
        private Texture2D toolbarIcon;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            // Initialize GTAO if enabled
            GTAOManager.Initialize();

            // Hook into ApplicationLauncher (toolbar)
            GameEvents.onGUIApplicationLauncherReady.Add(OnGUIApplicationLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnGUIApplicationLauncherDestroyed);

            // Load icon (create a simple colored texture if file not found, or use your own)
            toolbarIcon = GameDatabase.Instance.GetTexture("CinematicShaders/Icons/ToolbarIcon", false);
            if (toolbarIcon == null)
            {
                // Create a simple placeholder icon (orange square)
                toolbarIcon = new Texture2D(38, 38, TextureFormat.RGBA32, false);
                Color[] pixels = new Color[38 * 38];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(1f, 0.5f, 0f);
                toolbarIcon.SetPixels(pixels);
                toolbarIcon.Apply();
            }
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnGUIApplicationLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnGUIApplicationLauncherDestroyed);

            if (toolbarButton != null)
                ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);

            if (mainWindow != null && mainWindow.gameObject != null)
                Destroy(mainWindow.gameObject);

            // Cleanup GTAO native resources
            GTAONative.CR_GTAOShutdown();
        }

        private void OnGUIApplicationLauncherReady()
        {
            if (toolbarButton == null)
            {
                toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                    OnToolbarButtonOn,
                    OnToolbarButtonOff,
                    null, null, null, null,
                    ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
                    toolbarIcon
                );
            }
        }

        private void OnGUIApplicationLauncherDestroyed()
        {
            if (toolbarButton != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
                toolbarButton = null;
            }
        }

        private void OnToolbarButtonOn()
        {
            if (mainWindow == null)
            {
                GameObject go = new GameObject("CinematicShadersWindow");
                DontDestroyOnLoad(go);
                mainWindow = go.AddComponent<CinematicShadersWindow>();
                mainWindow.OnClose += () =>
                {
                    if (toolbarButton != null)
                        toolbarButton.SetFalse(false);
                };
            }
            mainWindow.Show();
        }

        private void OnToolbarButtonOff()
        {
            if (mainWindow != null)
            {
                mainWindow.Hide();
            }
        }
    }
}