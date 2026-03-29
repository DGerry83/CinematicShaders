using CinematicShaders.Core;
using CinematicShaders.Native;
using CinematicShaders.Shaders.Starfield;
using System.IO;
using UnityEngine;

namespace CinematicShaders.UI.Tabs
{
    public class KartographerTab
    {
        private bool _initialized = false;
        private bool _showVisualSettings = true;
        private bool _showColorDropdown = false;
        private int _currentColorIndex = 0;

        private readonly string[] _colorNames = { "Seafoam", "Amber", "White", "Green" };
        private readonly Color[] _colorValues = {
            new Color(0.1f, 0.9f, 0.7f),
            new Color(1.0f, 0.65f, 0.0f),
            new Color(0.85f, 0.95f, 1.0f),
            new Color(0.25f, 1.0f, 0.0f)
        };
        private GUIStyle[] _colorButtonStyles = null;

        public KartographerTab()
        {
            // Settings loaded by StarfieldSettings on module startup
            _currentColorIndex = StarfieldSettings.KartographerGridColor;
            
            // Register for camera update callbacks from StarfieldCompositor
            StarfieldCompositor.KartographerSelectorCallback = OnCameraUpdate;
        }
        
        private void OnCameraUpdate(Vector3 right, Vector3 up, Vector3 forward, float aspect, float verticalFOV)
        {
            if (_selector != null && _trackPolaris)
            {
                _selector.CameraRight = right;
                _selector.CameraUp = up;
                _selector.CameraForward = forward;
                _selector.AspectRatio = aspect;
                _selector.VerticalFOV = verticalFOV;
                _selector.Update();
            }
        }

        // Debug shapes state (not persisted to settings file)
        private bool _debugShapesEnabled = false;
        
        // Star tracking state (Phase 2)
        private bool _trackPolaris = false;
        private KartographerSelector _selector;

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

            // Debug: Track Polaris button
            bool newTrackPolaris = GUILayout.Toggle(_trackPolaris,
                " Track Polaris", HighLogic.Skin.toggle);
            if (newTrackPolaris != _trackPolaris)
            {
                _trackPolaris = newTrackPolaris;
                if (_trackPolaris)
                {
                    StartTrackingPolaris();
                }
                else
                {
                    StopTracking();
                }
            }

            // Debug: Export font atlas
            if (GUILayout.Button("Export Font Atlas"))
            {
                ExportFontAtlas();
            }

            // Debug: Export glyph debug files
            if (GUILayout.Button("Export Glyph Debug"))
            {
                ExportGlyphDebug();
            }
            
            GUILayout.Space(5);

            // Grid Size: 0-4 (Jumbo, Large, Medium, Small, Tiny), default 2 (Medium)
            GUILayout.Label(new GUIContent($"Grid Size: {GetGridSizeLabel(StarfieldSettings.KartographerGridSize)}",
                "Density of the holographic grid lines"));
            int newGridSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(StarfieldSettings.KartographerGridSize, 0, 4));
            if (newGridSize != StarfieldSettings.KartographerGridSize)
            {
                StarfieldSettings.KartographerGridSize = newGridSize;
                PushKartographerParams();
            }

            // Grid Intensity: display 0-5, internal 0-0.006 (default display ~1.7)
            float displayIntensity = IntensityToDisplay(StarfieldSettings.KartographerGridIntensity);
            GUILayout.Label(new GUIContent($"Grid Intensity: {displayIntensity:F1}", 
                "Brightness of the holographic grid lines"));
            float newDisplayIntensity = GUILayout.HorizontalSlider(displayIntensity, 0f, 5f);
            if (!Mathf.Approximately(newDisplayIntensity, displayIntensity))
            {
                StarfieldSettings.KartographerGridIntensity = DisplayToIntensity(newDisplayIntensity);
                PushKartographerParams();
            }

            // Grid Thickness: display 0-10, internal 0-0.0009 (default display ~3.3)
            float displayThickness = ThicknessToDisplay(StarfieldSettings.KartographerGridThickness);
            GUILayout.Label(new GUIContent($"Grid Thickness: {displayThickness:F1}", 
                "Thickness of the grid lines (lower = sharper)"));
            float newDisplayThickness = GUILayout.HorizontalSlider(displayThickness, 0f, 10f);
            if (!Mathf.Approximately(newDisplayThickness, displayThickness))
            {
                StarfieldSettings.KartographerGridThickness = DisplayToThickness(newDisplayThickness);
                PushKartographerParams();
            }

            // Grid Color dropdown
            DrawColorDropdown();

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

        private void DrawColorDropdown()
        {
            EnsureColorStyles();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Grid Color", GUILayout.Width(CinematicShadersUIResources.Layout.Dropdowns.DEBUG_LABEL_WIDTH));
            GUIStyle currentStyle = _colorButtonStyles[_currentColorIndex];
            if (GUILayout.Button(_colorNames[_currentColorIndex], currentStyle, GUILayout.Width(CinematicShadersUIResources.Layout.Dropdowns.DEBUG_BUTTON_WIDTH)))
            {
                _showColorDropdown = !_showColorDropdown;
            }
            GUILayout.EndHorizontal();

            if (_showColorDropdown)
            {
                GUIStyle boxStyle = CinematicShadersUIResources.Styles.DropdownBox();
                GUILayout.BeginVertical(boxStyle);
                for (int i = 0; i < _colorNames.Length; i++)
                {
                    if (GUILayout.Button(_colorNames[i], _colorButtonStyles[i]))
                    {
                        if (_currentColorIndex != i)
                        {
                            _currentColorIndex = i;
                            StarfieldSettings.KartographerGridColor = i;
                            PushKartographerParams();
                        }
                        _showColorDropdown = false;
                    }
                }
                GUILayout.EndVertical();
            }
        }

        private void EnsureColorStyles()
        {
            if (_colorButtonStyles != null) return;

            _colorButtonStyles = new GUIStyle[_colorNames.Length];
            for (int i = 0; i < _colorNames.Length; i++)
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.button);
                Texture2D tex = MakeColorTexture(_colorValues[i]);
                style.normal.background = tex;
                style.normal.textColor = Color.black;
                style.hover.background = tex;
                style.hover.textColor = Color.black;
                style.active.background = tex;
                style.active.textColor = Color.black;
                style.focused.background = tex;
                style.focused.textColor = Color.black;
                style.onNormal.background = tex;
                style.onNormal.textColor = Color.black;
                style.onHover.background = tex;
                style.onHover.textColor = Color.black;
                style.onActive.background = tex;
                style.onActive.textColor = Color.black;
                style.onFocused.background = tex;
                style.onFocused.textColor = Color.black;
                _colorButtonStyles[i] = style;
            }
        }

        private static Texture2D MakeColorTexture(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void PushKartographerParams()
        {
            float focalLength = StarfieldCompositor.CachedVerticalFOV > 0.001f
                ? 1.0f / Mathf.Tan(StarfieldCompositor.CachedVerticalFOV * 0.5f)
                : 1.732f;

            // Merge with cached params so we don't stomp selection UI state
            var kartParams = StarfieldNative.LastKartographerParams;
            kartParams.GridIntensity = StarfieldSettings.KartographerGridIntensity;
            kartParams.GridThickness = StarfieldSettings.KartographerGridThickness;
            kartParams.ChromaticAberrationStrength = StarfieldSettings.KartographerCAStrength;
            kartParams.VignetteStrength = StarfieldSettings.KartographerVignetteStrength;
            kartParams.VignetteStart = StarfieldSettings.KartographerVignetteStart;
            kartParams.VignetteEnd = StarfieldSettings.KartographerVignetteEnd;
            kartParams.PreRotationYaw = StarfieldSettings.KartographerRotationYaw;
            kartParams.PreRotationPitch = StarfieldSettings.KartographerRotationPitch;
            kartParams.GridSizePreset = StarfieldSettings.KartographerGridSize;
            kartParams.GridColorIndex = StarfieldSettings.KartographerGridColor;
            kartParams.DebugShapesEnabled = _debugShapesEnabled ? 1 : 0;
            kartParams.FocalLength = focalLength;
            StarfieldNative.LastKartographerParams = kartParams;
            StarfieldNative.CR_StarfieldSetKartographerParams(ref kartParams);
        }

        private void ResetToDefaults()
        {
            StarfieldSettings.KartographerGridIntensity = 0.002f;
            StarfieldSettings.KartographerGridThickness = 0.0003f;
            StarfieldSettings.KartographerVignetteStrength = 0.7f;
            StarfieldSettings.KartographerVignetteStart = 1.6f;
            StarfieldSettings.KartographerVignetteEnd = 2.2f;
            StarfieldSettings.KartographerRotationYaw = 0.0f;
            StarfieldSettings.KartographerRotationPitch = 0.0f;
            StarfieldSettings.KartographerGridSize = 2;
            StarfieldSettings.KartographerGridColor = 0;
            _currentColorIndex = 0;
            
            PushKartographerParams();
            StarfieldSettings.Save();
        }

        // Debug: Export font atlas to file
        private void ExportFontAtlas()
        {
            if (_selector == null)
            {
                Debug.LogWarning("[KartographerTab] Cannot export atlas - selector not initialized");
                return;
            }
            _selector.ExportFontAtlas();
        }

        // Debug: Export glyph debug files
        private void ExportGlyphDebug()
        {
            if (_selector == null)
            {
                Debug.LogWarning("[KartographerTab] Cannot export glyph debug - selector not initialized");
                return;
            }
            _selector.ExportGlyphDebug();
        }

        // Debug: Track Polaris
        private void StartTrackingPolaris()
        {
            if (_selector == null)
            {
                _selector = new KartographerSelector();
            }
            
            // Load JSON for current catalog
            string catalogPath = StarfieldSettings.ActiveCatalogPath;
            if (!string.IsNullOrEmpty(catalogPath))
            {
                string absolutePath = Path.Combine(KSPUtil.ApplicationRootPath, catalogPath);
                _selector.LoadJsonForCatalog(absolutePath);
                
                // Start tracking Polaris (HIP 11767)
                _selector.TrackStarByHipId(11767);
            }
            else
            {
                Debug.LogWarning("[KartographerTab] No active catalog to load star data from");
            }
        }
        
        private void StopTracking()
        {
            _selector?.StopTracking();
        }
        
        /// <summary>
        /// Called by StarfieldCompositor to update selector with camera data
        /// </summary>
        public void UpdateSelector(Vector3 right, Vector3 up, Vector3 forward, float aspect, float verticalFOV)
        {
            if (_selector != null && _trackPolaris)
            {
                _selector.CameraRight = right;
                _selector.CameraUp = up;
                _selector.CameraForward = forward;
                _selector.AspectRatio = aspect;
                _selector.VerticalFOV = verticalFOV;
                _selector.Update();
            }
        }

        private float IntensityToDisplay(float internalVal) => internalVal / 0.006f * 5f;
        private float DisplayToIntensity(float displayVal) => displayVal / 5f * 0.006f;
        private float ThicknessToDisplay(float internalVal) => internalVal / 0.0009f * 10f;
        private float DisplayToThickness(float displayVal) => displayVal / 10f * 0.0009f;

        private string GetGridSizeLabel(int size)
        {
            switch (size)
            {
                case 0: return "Jumbo";
                case 1: return "Large";
                case 2: return "Medium";
                case 3: return "Small";
                case 4: return "Tiny";
                default: return "Medium";
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
