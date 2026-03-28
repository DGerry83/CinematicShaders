using CinematicShaders.Core;
using CinematicShaders.Native;
using UnityEngine;

namespace CinematicShaders.UI.Tabs
{
    public class KartographerTab
    {
        private bool _initialized = false;
        private bool _showVisualSettings = true;

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
            
            if (StarfieldSettings.EnableKartographer)
            {
                GUILayout.Space(10);
                DrawVisualSettings();
            }

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

        private void DrawVisualSettings()
        {
            _showVisualSettings = GUILayout.Toggle(_showVisualSettings, " ▼ Visual Settings", HighLogic.Skin.button);
            
            if (!_showVisualSettings)
                return;

            GUILayout.BeginVertical(HighLogic.Skin.box);

            // Grid Intensity: 0.001 - 0.003, default 0.002
            GUILayout.Label(new GUIContent($"Grid Intensity: {StarfieldSettings.KartographerGridIntensity:F4}", 
                "Brightness of the holographic grid lines"));
            float newIntensity = GUILayout.HorizontalSlider(StarfieldSettings.KartographerGridIntensity, 0.001f, 0.003f);
            if (!Mathf.Approximately(newIntensity, StarfieldSettings.KartographerGridIntensity))
            {
                StarfieldSettings.KartographerGridIntensity = newIntensity;
                PushKartographerParams();
            }

            // Grid Thickness: 0.00015 - 0.00045, default 0.0003
            GUILayout.Label(new GUIContent($"Grid Thickness: {StarfieldSettings.KartographerGridThickness:F5}", 
                "Thickness of the grid lines (lower = sharper)"));
            float newThickness = GUILayout.HorizontalSlider(StarfieldSettings.KartographerGridThickness, 0.00015f, 0.00045f);
            if (!Mathf.Approximately(newThickness, StarfieldSettings.KartographerGridThickness))
            {
                StarfieldSettings.KartographerGridThickness = newThickness;
                PushKartographerParams();
            }

            // Chromatic Aberration: 0.002 - 0.006, default 0.004
            GUILayout.Label(new GUIContent($"Chromatic Aberration: {StarfieldSettings.KartographerCAStrength:F4}", 
                "RGB color separation at screen edges (holographic effect)"));
            float newCA = GUILayout.HorizontalSlider(StarfieldSettings.KartographerCAStrength, 0.002f, 0.006f);
            if (!Mathf.Approximately(newCA, StarfieldSettings.KartographerCAStrength))
            {
                StarfieldSettings.KartographerCAStrength = newCA;
                PushKartographerParams();
            }

            GUILayout.Space(5);
            GUILayout.Label("<b>Vignette Settings</b>", HighLogic.Skin.label);

            // Vignette Strength: 0.35 - 1.0, default 0.7
            GUILayout.Label(new GUIContent($"Vignette Strength: {StarfieldSettings.KartographerVignetteStrength:F2}", 
                "Darkening at screen corners (0 = no vignette, 1 = black corners)"));
            float newVignetteStr = GUILayout.HorizontalSlider(StarfieldSettings.KartographerVignetteStrength, 0.35f, 1.0f);
            if (!Mathf.Approximately(newVignetteStr, StarfieldSettings.KartographerVignetteStrength))
            {
                StarfieldSettings.KartographerVignetteStrength = newVignetteStr;
                PushKartographerParams();
            }

            // Vignette Start: 0.8 - 2.4, default 1.6
            GUILayout.Label(new GUIContent($"Vignette Start: {StarfieldSettings.KartographerVignetteStart:F2}", 
                "Distance from center where vignette begins"));
            float newVignetteStart = GUILayout.HorizontalSlider(StarfieldSettings.KartographerVignetteStart, 0.8f, 2.4f);
            if (!Mathf.Approximately(newVignetteStart, StarfieldSettings.KartographerVignetteStart))
            {
                StarfieldSettings.KartographerVignetteStart = newVignetteStart;
                PushKartographerParams();
            }

            // Vignette End: 1.1 - 3.3, default 2.2
            GUILayout.Label(new GUIContent($"Vignette End: {StarfieldSettings.KartographerVignetteEnd:F2}", 
                "Distance from center where vignette reaches full strength"));
            float newVignetteEnd = GUILayout.HorizontalSlider(StarfieldSettings.KartographerVignetteEnd, 1.1f, 3.3f);
            if (!Mathf.Approximately(newVignetteEnd, StarfieldSettings.KartographerVignetteEnd))
            {
                StarfieldSettings.KartographerVignetteEnd = newVignetteEnd;
                PushKartographerParams();
            }

            GUILayout.Space(5);
            GUILayout.Label("<b>Grid Orientation</b>", HighLogic.Skin.label);

            // Rotation Yaw: -180 to 180 degrees, stored as radians
            float yawDegrees = StarfieldSettings.KartographerRotationYaw * Mathf.Rad2Deg;
            GUILayout.Label(new GUIContent($"Yaw: {yawDegrees:F0}°", 
                "Rotate the grid left/right around the vertical axis"));
            float newYaw = GUILayout.HorizontalSlider(yawDegrees, -180f, 180f);
            if (!Mathf.Approximately(newYaw, yawDegrees))
            {
                StarfieldSettings.KartographerRotationYaw = newYaw * Mathf.Deg2Rad;
                PushKartographerParams();
            }

            // Rotation Pitch: -90 to 90 degrees, stored as radians
            float pitchDegrees = StarfieldSettings.KartographerRotationPitch * Mathf.Rad2Deg;
            GUILayout.Label(new GUIContent($"Pitch: {pitchDegrees:F0}°", 
                "Rotate the grid up/down around the horizontal axis"));
            float newPitch = GUILayout.HorizontalSlider(pitchDegrees, -90f, 90f);
            if (!Mathf.Approximately(newPitch, pitchDegrees))
            {
                StarfieldSettings.KartographerRotationPitch = newPitch * Mathf.Deg2Rad;
                PushKartographerParams();
            }

            // Reset button
            GUILayout.Space(10);
            if (GUILayout.Button("Reset to Defaults"))
            {
                ResetToDefaults();
            }

            GUILayout.EndVertical();
        }

        private void PushKartographerParams()
        {
            var kartParams = new StarfieldNative.KartographerParamsNative
            {
                GridIntensity = StarfieldSettings.KartographerGridIntensity,
                GridThickness = StarfieldSettings.KartographerGridThickness,
                ChromaticAberrationStrength = StarfieldSettings.KartographerCAStrength,
                VignetteStrength = StarfieldSettings.KartographerVignetteStrength,
                VignetteStart = StarfieldSettings.KartographerVignetteStart,
                VignetteEnd = StarfieldSettings.KartographerVignetteEnd,
                PreRotationYaw = StarfieldSettings.KartographerRotationYaw,
                PreRotationPitch = StarfieldSettings.KartographerRotationPitch
            };
            StarfieldNative.CR_StarfieldSetKartographerParams(ref kartParams);
        }

        private void ResetToDefaults()
        {
            StarfieldSettings.KartographerGridIntensity = 0.002f;
            StarfieldSettings.KartographerGridThickness = 0.0003f;
            StarfieldSettings.KartographerCAStrength = 0.004f;
            StarfieldSettings.KartographerVignetteStrength = 0.7f;
            StarfieldSettings.KartographerVignetteStart = 1.6f;
            StarfieldSettings.KartographerVignetteEnd = 2.2f;
            StarfieldSettings.KartographerRotationYaw = 0.0f;
            StarfieldSettings.KartographerRotationPitch = 0.0f;
            
            PushKartographerParams();
            StarfieldSettings.Save();
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
