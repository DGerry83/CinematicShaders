using UnityEngine;
using CinematicShaders.UI.Tabs;

namespace CinematicShaders.UI
{
    public class CinematicShadersWindow : MonoBehaviour
    {
        private Rect windowRect = new Rect(300, 60, 320, 250);
        private bool isVisible = false;
        private bool stylesInitialized = false;
        private GUIStyle windowStyle;
        private GUIStyle tabButtonStyle;
        private GUIStyle tabButtonActiveStyle;

        public enum ShaderTab { GTAO }
        private ShaderTab currentTab = ShaderTab.GTAO;
        private GTAOTab _gtaoTab;

        public event System.Action OnClose;

        void Start()
        {
            InitStyles();
            _gtaoTab = new GTAOTab();
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            windowStyle = new GUIStyle(HighLogic.Skin.window);

            // Standard button for inactive tabs
            tabButtonStyle = new GUIStyle(HighLogic.Skin.button);

            // Active tab: green bold text (exactly like AdvancedSettingsWindow)
            tabButtonActiveStyle = new GUIStyle(HighLogic.Skin.button);
            tabButtonActiveStyle.normal.textColor = new Color(0.2f, 0.9f, 0.2f);
            tabButtonActiveStyle.fontStyle = FontStyle.Bold;

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (!isVisible) return;

            windowRect = GUILayout.Window(
                98765,
                windowRect,
                DrawWindow,
                "Cinematic Shaders",
                windowStyle
            );
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            // TAB ROW - Uses GUILayout.Button NOT GUILayout.Toggle
            GUILayout.BeginHorizontal();

            float tabWidth = 280f; // Full width for single tab, split when adding more

            GUIStyle gtaoStyle = (currentTab == ShaderTab.GTAO) ? tabButtonActiveStyle : tabButtonStyle;

            // CRITICAL: This must be GUILayout.Button, not GUILayout.Toggle
            if (GUILayout.Button("GTAO", gtaoStyle, GUILayout.Height(28), GUILayout.Width(tabWidth)))
            {
                currentTab = ShaderTab.GTAO;
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Content area
            if (currentTab == ShaderTab.GTAO)
            {
                _gtaoTab.Draw();
            }

            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        public void Show() => isVisible = true;
        public void Hide()
        {
            isVisible = false;
            OnClose?.Invoke();
        }
    }
}