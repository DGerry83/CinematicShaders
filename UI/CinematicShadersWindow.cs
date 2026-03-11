using CinematicShaders.UI.Tabs;
using CinematicShaders.Core;
using UnityEngine;
using System;

namespace CinematicShaders.UI
{
    public class CinematicShadersWindow : MonoBehaviour
    {
        private Rect windowRect = new Rect(300, 60, 320, 500);
        private bool isVisible = false;
        private bool stylesInitialized = false;
        private GUIStyle windowStyle;
        private GUIStyle tabButtonStyle;
        private GUIStyle tabButtonActiveStyle;
        private string errorMessage = null;

        public enum ShaderTab { GTAO, Starfield }
        private ShaderTab currentTab = ShaderTab.GTAO;
        private GTAOTab _gtaoTab;
        private StarfieldTab _starfieldTab;

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
                _starfieldTab = new StarfieldTab();
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
                windowStyle,
                GUILayout.Width(320),
                GUILayout.Height(500)
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

                // Begin vertical layout with fixed width to prevent content from stretching window
                GUILayout.BeginVertical(GUILayout.Width(300));

                DrawTabs();
                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                switch (currentTab)
                {
                    case ShaderTab.GTAO:
                        _gtaoTab.Draw();
                        break;
                    case ShaderTab.Starfield:
                        if (_starfieldTab != null)
                            _starfieldTab.Draw();
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

            GUIStyle starfieldStyle = (currentTab == ShaderTab.Starfield) ? tabButtonActiveStyle : tabButtonStyle;
            if (GUILayout.Button(CinematicShadersUIStrings.Starfield.TabName, starfieldStyle,
                GUILayout.Height(tabHeight), GUILayout.Width(tabWidth)))
            {
                currentTab = ShaderTab.Starfield;
            }

            GUILayout.EndHorizontal();
        }

        public void Show() => isVisible = true;

        public void Hide()
        {
            isVisible = false;
            wasVisibleBeforeF2 = false;
            GTAOSettings.Save();
            StarfieldSettings.Save();
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
            {
                GTAOSettings.Save();
                StarfieldSettings.Save();
            }
        }
    }
}