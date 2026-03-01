using UnityEngine;
using CinematicShaders.UI.Tabs;

namespace CinematicShaders.UI
{
    public class CinematicShadersWindow : MonoBehaviour
    {
        private Rect windowRect = new Rect(300, 60, 320, 450);
        private bool isVisible = false;
        private bool stylesInitialized = false;
        private GUIStyle windowStyle;
        private GUIStyle tabButtonStyle;
        private GUIStyle tabButtonActiveStyle;
        private string errorMessage = null;

        public enum ShaderTab { GTAO }
        private ShaderTab currentTab = ShaderTab.GTAO;
        private GTAOTab _gtaoTab;

        public event System.Action OnClose;

        void Start()
        {
            InitStyles();

            // Safe initialization - don't let native plugin failures kill the UI
            try
            {
                _gtaoTab = new GTAOTab();
            }
            catch (System.Exception ex)
            {
                errorMessage = "Failed to initialize GTAO: " + ex.Message;
                UnityEngine.Debug.LogError($"[CinematicShaders] {errorMessage}\n{ex}");
            }
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;

            windowStyle = CinematicShadersUIResources.Styles.Window();
            tabButtonStyle = CinematicShadersUIResources.Styles.TabButton();
            tabButtonActiveStyle = CinematicShadersUIResources.Styles.TabButtonActive();

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            if (!isVisible) return;

            windowRect = GUILayout.Window(
                98765,
                windowRect,
                DrawWindow,
                CinematicShadersUIStrings.Common.WindowTitle,
                windowStyle
            );
        }

        private void DrawWindow(int id)
        {
            // Always allow dragging, even if content fails
            try
            {
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    GUILayout.BeginVertical();
                    GUILayout.Space(20);
                    GUIStyle errorStyle = new GUIStyle(HighLogic.Skin.label);
                    errorStyle.normal.textColor = Color.red;
                    errorStyle.wordWrap = true;
                    GUILayout.Label(errorMessage, errorStyle);
                    GUILayout.EndVertical();
                    GUI.DragWindow();
                    return;
                }

                if (_gtaoTab == null)
                {
                    GUILayout.BeginVertical();
                    GUILayout.Space(20);
                    GUILayout.Label("Initializing...", HighLogic.Skin.label);
                    GUILayout.EndVertical();
                    GUI.DragWindow();
                    return;
                }

                GUILayout.BeginVertical();

                DrawTabs();
                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                switch (currentTab)
                {
                    case ShaderTab.GTAO:
                        _gtaoTab.Draw();
                        break;
                }

                GUILayout.EndVertical();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[CinematicShaders] Error rendering window: {ex}");
                // Ensure layout stack is cleared if we had an exception mid-layout
                GUILayout.EndVertical();
            }

            GUI.DragWindow();
        }

        private void DrawTabs()
        {
            GUILayout.BeginHorizontal();

            float tabWidth = CinematicShadersUIResources.Layout.Tabs.BUTTON_WIDTH;
            float tabHeight = CinematicShadersUIResources.Layout.Tabs.BUTTON_HEIGHT;

            GUIStyle gtaoStyle = (currentTab == ShaderTab.GTAO) ? tabButtonActiveStyle : tabButtonStyle;
            if (GUILayout.Button(CinematicShadersUIStrings.GTAO.TabName, gtaoStyle,
                GUILayout.Height(tabHeight), GUILayout.Width(tabWidth)))
            {
                currentTab = ShaderTab.GTAO;
            }

            GUILayout.EndHorizontal();
        }

        public void Show() => isVisible = true;

        public void Hide()
        {
            isVisible = false;
            OnClose?.Invoke();
        }
    }
}