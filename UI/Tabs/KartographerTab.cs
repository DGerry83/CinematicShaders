using CinematicShaders.Core;
using CinematicShaders.Native;
using UnityEngine;

namespace CinematicShaders.UI.Tabs
{
    public class KartographerTab
    {
        private bool _initialized = false;

        public KartographerTab()
        {
            // Settings loaded by StarfieldSettings on module startup
        }

        public void Draw()
        {
            if (!_initialized)
            {
                _initialized = true;
            }

            // Check native plugin loaded
            if (!StarfieldNative.IsLoaded)
            {
                GUILayout.Label("Native plugin failed to load. Check KSP.log for details.", 
                    CinematicShadersUIResources.Styles.Error());
                return;
            }

            DrawEnableToggle();
            
            // Future: Visual settings sliders (Phase 3)
            // - Grid Intensity
            // - Grid Thickness  
            // - Chromatic Aberration Strength
            // - Vignette Strength
            // - Rotation Yaw/Pitch

            DrawTooltip();
        }

        private void DrawEnableToggle()
        {
            GUIStyle toggleStyle = StarfieldSettings.EnableKartographer ?
                CinematicShadersUIResources.Styles.ToggleActive() : HighLogic.Skin.toggle;

            bool newEnable = GUILayout.Toggle(StarfieldSettings.EnableKartographer,
                " Enable Holographic Grid", toggleStyle);

            if (newEnable != StarfieldSettings.EnableKartographer)
            {
                StarfieldSettings.EnableKartographer = newEnable;
                // Call Starfield native to enable/disable the overlay
                StarfieldNative.CR_StarfieldSetKartographerEnabled(newEnable ? (byte)1 : (byte)0);
                StarfieldSettings.Save();
            }
        }

        private void DrawTooltip()
        {
            if (string.IsNullOrEmpty(GUI.tooltip))
                return;

            Vector2 mousePos = Event.current.mousePosition;
            GUIStyle tooltipStyle = HighLogic.Skin.box;
            float tooltipWidth = Mathf.Min(250f, tooltipStyle.CalcSize(new GUIContent(GUI.tooltip)).x + 20f);
            float tooltipHeight = tooltipStyle.CalcHeight(new GUIContent(GUI.tooltip), tooltipWidth) + 10f;

            float x = mousePos.x + 15f;
            float y = mousePos.y + 15f;
            Rect windowRect = CinematicShadersWindow.Instance.WindowRect;
            x = Mathf.Min(x, windowRect.width - tooltipWidth - 5f);
            y = Mathf.Min(y, windowRect.height - tooltipHeight - 5f);

            GUI.Box(new Rect(x, y, tooltipWidth, tooltipHeight), GUI.tooltip, tooltipStyle);
        }
    }
}
