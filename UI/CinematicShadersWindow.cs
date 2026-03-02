using CinematicShaders.UI.Tabs;
using CinematicShaders.Core;
using UnityEngine;
using System;

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

        public event Action OnClose;
        private bool wasVisibleBeforeF2 = false;

        void Start()
        {
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);

            InitStyles();

            try
            {
                _gtaoTab = new GTAOTab();
            }
            catch (Exception ex)
            {
                errorMessage = "Failed to initialize GTAO: " + ex.Message;
                Debug.LogError($"[CinematicShaders] {errorMessage}\n{ex}");
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
            try
            {
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    GUILayout.BeginVertical();
                    GUILayout.Space(20);
                    GUILayout.Label(errorMessage, CinematicShadersUIResources.Styles.Error());
                    GUILayout.EndVertical();
                    GUI.DragWindow();
                    return;
                }

                if (_gtaoTab == null)
                {
                    GUILayout.BeginVertical();
                    GUILayout.Space(20);
                    GUILayout.Label(CinematicShadersUIStrings.Common.Initializing, HighLogic.Skin.label);
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
            catch (Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Error rendering window: {ex}");
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
            wasVisibleBeforeF2 = false;
            GTAOSettings.Save();
            OnClose?.Invoke();
        }

        private void OnHideUI()
        {
            if (isVisible)
            {
                wasVisibleBeforeF2 = true;
                isVisible = false;
            }
        }

        private void OnShowUI()
        {
            if (wasVisibleBeforeF2)
            {
                isVisible = true;
                wasVisibleBeforeF2 = false;
            }
        }

        void OnDestroy()
        {
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);

            if (isVisible || wasVisibleBeforeF2)
                GTAOSettings.Save();
        }
    }
}