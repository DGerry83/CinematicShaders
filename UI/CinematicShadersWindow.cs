using CinematicShaders.UI.Tabs;
using CinematicShaders.Core;
using CinematicShaders.Shaders.Starfield;
using UnityEngine;
using System;

namespace CinematicShaders.UI
{
    public class CinematicShadersWindow : MonoBehaviour
    {
        private Rect windowRect = new Rect(300, 60, 320, 500);
        public Rect WindowRect { get { return windowRect; } }
        public static CinematicShadersWindow Instance { get; private set; }
        
        private bool isVisible = false;
        private bool stylesInitialized = false;
        private GUIStyle windowStyle;
        private GUIStyle tabButtonStyle;
        private GUIStyle tabButtonActiveStyle;
        private string errorMessage = null;

        public enum ShaderTab { GTAO, Starfield, Kartographer }
        private ShaderTab currentTab = ShaderTab.GTAO;
        private GTAOTab _gtaoTab;
        private StarfieldTab _starfieldTab;
        private KartographerTab _kartographerTab;

        public event Action OnClose;
        
        /// <summary>
        /// Public accessor for KartographerTab (used by StarCatalogEditorWindow)
        /// </summary>
        public KartographerTab KartographerTab => _kartographerTab;

        void Start()
        {
            Instance = this;
            InitStyles();

            try
            {
                _gtaoTab = new GTAOTab();
                _starfieldTab = new StarfieldTab();
                _kartographerTab = new KartographerTab();
            }
            catch (Exception ex)
            {
                errorMessage = string.Format(CinematicShadersUIStrings.Common.InitErrorFormat, ex.Message);
                Debug.LogError($"[CinematicShaders] {errorMessage}\n{ex}");
            }
        }

        /// <summary>
        /// Draws the IMGUI tooltip box near the mouse, clamped to the window rect.
        /// Shared by all tabs (was a verbatim duplicate in StarfieldTab and KartographerTab).
        /// </summary>
        internal void DrawTooltip()
        {
            if (string.IsNullOrEmpty(GUI.tooltip))
                return;

            Vector2 mousePos = Event.current.mousePosition;
            GUIStyle tooltipStyle = CinematicShadersUIResources.Styles.Tooltip();
            float tooltipWidth = Mathf.Min(CinematicShadersUIResources.Layout.Tooltip.MAX_WIDTH, tooltipStyle.CalcSize(new GUIContent(GUI.tooltip)).x + CinematicShadersUIResources.Layout.Tooltip.PADDING);
            float tooltipHeight = tooltipStyle.CalcHeight(new GUIContent(GUI.tooltip), tooltipWidth) + CinematicShadersUIResources.Layout.Tooltip.HEIGHT_PADDING;

            float x = mousePos.x + CinematicShadersUIResources.Layout.Tooltip.OFFSET_X;
            float y = mousePos.y + CinematicShadersUIResources.Layout.Tooltip.OFFSET_Y;
            Rect windowRect = WindowRect;
            x = Mathf.Min(x, windowRect.width - tooltipWidth - CinematicShadersUIResources.Layout.Tooltip.CLAMP_MARGIN);
            y = Mathf.Min(y, windowRect.height - tooltipHeight - CinematicShadersUIResources.Layout.Tooltip.CLAMP_MARGIN);

            GUI.Box(new Rect(x, y, tooltipWidth, tooltipHeight), GUI.tooltip, tooltipStyle);
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
                    windowRect.x = Mathf.Clamp(windowRect.x, 0, Screen.width - windowRect.width);
                    windowRect.y = Mathf.Clamp(windowRect.y, 0, Screen.height - windowRect.height);
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
                    case ShaderTab.Kartographer:
                        if (_kartographerTab != null)
                            _kartographerTab.Draw();
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

            GUIStyle kartographerStyle = (currentTab == ShaderTab.Kartographer) ? tabButtonActiveStyle : tabButtonStyle;
            if (GUILayout.Button(CinematicShadersUIStrings.Kartographer.TabName, kartographerStyle,
                GUILayout.Height(tabHeight), GUILayout.Width(tabWidth)))
            {
                currentTab = ShaderTab.Kartographer;
            }

            GUILayout.EndHorizontal();
        }

        public void Show() => isVisible = true;

        public void Hide()
        {
            isVisible = false;
            GTAOSettings.Save();
            StarfieldSettings.Save();
            OnClose?.Invoke();
        }

        void OnDestroy()
        {
            if (isVisible)
            {
                GTAOSettings.Save();
                StarfieldSettings.Save();
                CubemapGenerationScheduler.OnUIClose();
            }

            if (_kartographerTab != null && _kartographerTab.Selector != null)
            {
                _kartographerTab.Selector.Dispose();
            }
            StarfieldCompositor.KartographerSelectorCallback = null;
        }
    }
}