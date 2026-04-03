using CinematicShaders.Core;
using CinematicShaders.Native;
using CinematicShaders.Shaders.Starfield;
using System;
using System.IO;
using UnityEngine;
using static CinematicShaders.Core.StarfieldSettings;

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
            if (_selector != null && forward.sqrMagnitude > 0.5f)
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
        
        // Debug label visualization (shows solid color instead of texture)
        private bool _labelDebugMode = false;
        
        // Star tracking
        private KartographerSelector _selector;
        
        // Grid label system is now managed by CinematicShadersAddon and shared with UI
        
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
            
            // Initialize mouse hover if it was previously enabled but selector doesn't exist
            // This happens when loading a game with mouse hover saved as enabled
            if (StarfieldSettings.EnableKartographer && 
                StarfieldSettings.KartographerMouseHoverSelect && 
                _selector == null)
            {
                CreateSelectorAndLoadJson();
                _selector.SetMouseHoverMode(true);
            }
            
            if (StarfieldSettings.EnableKartographer)
            {
                GUILayout.Space(10);
                DrawVisualSettings();
            }
            else
            {
                // Kartographer disabled - ensure tracking is stopped (prevents race condition)
                if (_selector != null)
                {
                    StopTracking();
                }
            }
            
            // Update grid labels independently of selector - runs when Kartographer is enabled
            UpdateGridLabels();

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
                // Call Starfield native to enable/disable the overlay (if loaded)
                if (StarfieldNative.IsLoaded)
                {
                    StarfieldNative.CR_StarfieldSetKartographerEnabled(newEnable ? (byte)1 : (byte)0);
                }
                StarfieldSettings.Save();
            }
        }

        private void DrawVisualSettings()
        {
            _showVisualSettings = GUILayout.Toggle(_showVisualSettings, " ▼ Visual Settings", HighLogic.Skin.button);
            
            if (!_showVisualSettings)
                return;

            GUILayout.BeginVertical(HighLogic.Skin.box);

            // Star catalog selection mode toggle
            bool newMouseHoverMode = GUILayout.Toggle(StarfieldSettings.KartographerMouseHoverSelect, 
                " Star Catalog", HighLogic.Skin.toggle);
            if (newMouseHoverMode != StarfieldSettings.KartographerMouseHoverSelect)
            {
                StarfieldSettings.KartographerMouseHoverSelect = newMouseHoverMode;
                StarfieldSettings.Save();
                
                // Ensure selector exists and is ready when mouse hover is enabled
                if (_selector == null && newMouseHoverMode)
                {
                    CreateSelectorAndLoadJson();
                }
                
                if (_selector != null)
                {
                    _selector.SetMouseHoverMode(newMouseHoverMode);
                }
            }
            
            // Vessel target tracking toggle
            bool newVesselTarget = GUILayout.Toggle(StarfieldSettings.KartographerVesselTargetSelect, 
                " Show Vessel Target", HighLogic.Skin.toggle);
            if (newVesselTarget != StarfieldSettings.KartographerVesselTargetSelect)
            {
                StarfieldSettings.KartographerVesselTargetSelect = newVesselTarget;
                StarfieldSettings.Save();
                // Note: Actual selector is managed by CinematicShadersAddon which checks the setting every frame
            }
            
            // Situation display toggle and rotation slider
            bool newSituationDisplay = GUILayout.Toggle(StarfieldSettings.KartographerSituationDisplay, 
                " Show Situation Display", HighLogic.Skin.toggle);
            if (newSituationDisplay != StarfieldSettings.KartographerSituationDisplay)
            {
                StarfieldSettings.KartographerSituationDisplay = newSituationDisplay;
                StarfieldSettings.Save();
            }
            
            // Situation label position adjustment (user-facing, not debug)
            if (StarfieldSettings.KartographerSituationDisplay)
            {
                int gridSize = Mathf.Clamp(StarfieldSettings.KartographerGridSize, 0, 3);
                int[] meridians = { 8, 12, 16, 24 };
                int numSteps = meridians[gridSize];
                
                // Rotation: discrete steps 0 to numMeridians-1
                int currentStep = StarfieldSettings.KartographerSituationRotationStep[gridSize];
                GUILayout.Label($"Rotation Step: {currentStep + 1} / {numSteps}");
                int newStep = Mathf.RoundToInt(GUILayout.HorizontalSlider(currentStep, 0, numSteps - 1));
                if (newStep != currentStep)
                {
                    StarfieldSettings.KartographerSituationRotationStep[gridSize] = newStep;
                    StarfieldSettings.Save();
                }
                
                // Row offset: -2 to +2 steps from base position
                // Left side = down (toward equator), Right side = up (toward pole)
                string[] rowLabels = { "-2 (Down)", "-1 (Down)", "0 (Default)", "+1 (Up)", "+2 (Up)" };
                int currentOffset = StarfieldSettings.KartographerSituationRowOffset[gridSize];
                int sliderIndex = currentOffset + 2;
                GUILayout.Label($"Display Height: {rowLabels[sliderIndex]}");
                int newSliderIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(sliderIndex, 0, 4));
                int newRowOffset = newSliderIndex - 2;
                if (newRowOffset != currentOffset)
                {
                    StarfieldSettings.KartographerSituationRowOffset[gridSize] = newRowOffset;
                    StarfieldSettings.Save();
                }
            }
            
            // Navball indicators toggle
            bool newNavballLabels = GUILayout.Toggle(StarfieldSettings.KartographerNavballLabels,
                " Show Navball Indicators", HighLogic.Skin.toggle);
            if (newNavballLabels != StarfieldSettings.KartographerNavballLabels)
            {
                StarfieldSettings.KartographerNavballLabels = newNavballLabels;
                StarfieldSettings.Save();
                
                // Enable/disable in NavballLabelManager
                if (CinematicShadersAddon.NavballManager != null)
                {
                    CinematicShadersAddon.NavballManager.SetEnabled(newNavballLabels);
                }
            }
            
            // Navball options (shown when enabled)
            if (StarfieldSettings.KartographerNavballLabels)
            {
                // Use navball colors toggle
                bool newUseColors = GUILayout.Toggle(StarfieldSettings.KartographerNavballUseColors,
                    " Use Navball Colors", HighLogic.Skin.toggle);
                if (newUseColors != StarfieldSettings.KartographerNavballUseColors)
                {
                    StarfieldSettings.KartographerNavballUseColors = newUseColors;
                    StarfieldSettings.Save();
                    
                    if (CinematicShadersAddon.NavballManager != null)
                    {
                        CinematicShadersAddon.NavballManager.SetUseNavballColors(newUseColors);
                    }
                }
                
                // Icon style selection - vertical layout to prevent overlapping
                GUILayout.Label("Icon Style:");
                string[] styleNames = { "KSP", "Retro" };
                int currentStyle = (int)StarfieldSettings.KartographerNavballIconStyle;
                int newStyle = GUILayout.SelectionGrid(currentStyle, styleNames, 1, HighLogic.Skin.toggle);
                if (newStyle != currentStyle)
                {
                    StarfieldSettings.KartographerNavballIconStyle = (NavballIconStyle)newStyle;
                    StarfieldSettings.Save();
                    
                    if (CinematicShadersAddon.NavballManager != null)
                    {
                        CinematicShadersAddon.NavballManager.SetIconStyle((NavballIconStyle)newStyle);
                    }
                }
                
                GUILayout.Space(4);
                
                // Icon thickness slider
                GUILayout.Label(new GUIContent($"Icon Thickness: {StarfieldSettings.KartographerNavballIconThickness:F2}", 
                    "Adjust SDF line thickness (0 = default, 1 = thickest)"));
                float newThickness = GUILayout.HorizontalSlider(StarfieldSettings.KartographerNavballIconThickness, 0f, 1f);
                if (!Mathf.Approximately(newThickness, StarfieldSettings.KartographerNavballIconThickness))
                {
                    StarfieldSettings.KartographerNavballIconThickness = newThickness;
                    StarfieldSettings.Save();
                }
                
            }
            
            GUILayout.Space(5);

            // Grid Color dropdown
            DrawColorDropdown();

            GUILayout.Space(5);

            // Grid Size: 0-3 (Jumbo, Large, Medium, Small), default 2 (Medium)
            // Note: Tiny (4) is available in code but disabled in UI - too dense for labels
            GUILayout.Label(new GUIContent($"Grid Size: {GetGridSizeLabel(StarfieldSettings.KartographerGridSize)}",
                "Density of the holographic grid lines"));
            int newGridSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(StarfieldSettings.KartographerGridSize, 0, 3));
            if (newGridSize != StarfieldSettings.KartographerGridSize)
            {
                StarfieldSettings.KartographerGridSize = newGridSize;
                PushKartographerParams();
                StarfieldSettings.Save();
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
                
                // Update HUCK label intensity to match grid intensity
                if (CinematicShadersAddon.SituationLabelSystem != null)
                {
                    CinematicShadersAddon.SituationLabelSystem.SetLabelIntensity("huck", StarfieldSettings.KartographerGridIntensity / 0.002f);
                }
                
                StarfieldSettings.Save();
            }

            // Grid Softness: display 0-10, internal 0-0.0009 (default display ~3.3)
            // Note: Higher value = softer/thicker lines, Lower = sharper/thinner
            float displayThickness = ThicknessToDisplay(StarfieldSettings.KartographerGridThickness);
            GUILayout.Label(new GUIContent($"Grid Softness: {displayThickness:F1}", 
                "Softness of the grid lines (higher = softer, lower = sharper)"));
            float newDisplayThickness = GUILayout.HorizontalSlider(displayThickness, 0f, 10f);
            if (!Mathf.Approximately(newDisplayThickness, displayThickness))
            {
                StarfieldSettings.KartographerGridThickness = DisplayToThickness(newDisplayThickness);
                PushKartographerParams();
                StarfieldSettings.Save();
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
                StarfieldSettings.Save();
            }

            // Vignette Start: 0.8 - 2.4, default 1.6
            GUILayout.Label(new GUIContent($"Vignette Start: {StarfieldSettings.KartographerVignetteStart:F2}", 
                "Distance from center where vignette begins"));
            float newVignetteStart = GUILayout.HorizontalSlider(StarfieldSettings.KartographerVignetteStart, 0.8f, 2.4f);
            if (!Mathf.Approximately(newVignetteStart, StarfieldSettings.KartographerVignetteStart))
            {
                StarfieldSettings.KartographerVignetteStart = newVignetteStart;
                PushKartographerParams();
                StarfieldSettings.Save();
            }

            // Vignette End: 1.1 - 3.3, default 2.2
            GUILayout.Label(new GUIContent($"Vignette End: {StarfieldSettings.KartographerVignetteEnd:F2}", 
                "Distance from center where vignette reaches full strength"));
            float newVignetteEnd = GUILayout.HorizontalSlider(StarfieldSettings.KartographerVignetteEnd, 1.1f, 3.3f);
            if (!Mathf.Approximately(newVignetteEnd, StarfieldSettings.KartographerVignetteEnd))
            {
                StarfieldSettings.KartographerVignetteEnd = newVignetteEnd;
                PushKartographerParams();
                StarfieldSettings.Save();
            }

            /* GRID ORIENTATION SLIDERS DISABLED - Code preserved for future use
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
                StarfieldSettings.Save();
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
                StarfieldSettings.Save();
            }
            */

            // Reset button
            GUILayout.Space(10);
            if (GUILayout.Button("Reset to Defaults"))
            {
                ResetToDefaults();
            }
            
            // Debug: Fixed padding tuning for situation labels
            if (CinematicShadersAddon.SituationLabelSystem != null)
            {
                var labelA = CinematicShadersAddon.SituationLabelSystem.GetLabel("situation_a");
                if (labelA != null && labelA.UseFixedPadding)
                {
                    GUILayout.Space(10);
                    GUILayout.Label("<b>Debug: Fixed Padding</b>", HighLogic.Skin.label);
                    
                    GUILayout.Label($"Bottom Pad: {labelA.FixedPaddingBottom:F3}");
                    float newPadB = GUILayout.HorizontalSlider(labelA.FixedPaddingBottom, 0f, 0.2f);
                    if (!Mathf.Approximately(newPadB, labelA.FixedPaddingBottom))
                    {
                        labelA.FixedPaddingBottom = newPadB;
                        labelA.PositionDirty = true;
                        
                        // Mirror to label B
                        var labelB = CinematicShadersAddon.SituationLabelSystem.GetLabel("situation_b");
                        if (labelB != null) labelB.FixedPaddingBottom = newPadB;
                    }
                    
                    GUILayout.Label($"Left Pad: {labelA.FixedPaddingLeft:F3}");
                    float newPadL = GUILayout.HorizontalSlider(labelA.FixedPaddingLeft, 0f, 0.2f);
                    if (!Mathf.Approximately(newPadL, labelA.FixedPaddingLeft))
                    {
                        labelA.FixedPaddingLeft = newPadL;
                        labelA.PositionDirty = true;
                        
                        // Mirror to label B
                        var labelB = CinematicShadersAddon.SituationLabelSystem.GetLabel("situation_b");
                        if (labelB != null) labelB.FixedPaddingLeft = newPadL;
                    }
                }
            }

            /* DEBUG UI DISABLED - Methods preserved for future use
            // Debug buttons
            GUILayout.Space(10);
            GUILayout.Label("<b>Debug</b>", HighLogic.Skin.label);
            
            if (GUILayout.Button("Export Grid Label Texture"))
            {
                Debug.Log("[KartographerTab] Export Grid Label Texture button clicked");
                ExportGridLabelDebug();
            }
            
            if (GUILayout.Button("Dump Orbit Info"))
            {
                DumpOrbitInfo();
            }
            
            // Situation info label debug tuning - uses Addon-managed label system
            // Wire up debug sliders to the shared situation label system
            if (CinematicShadersAddon.SituationLabelSystem != null)
            {
                // Sliders for situation_a (situation_b mirrors it)
                var labelA = CinematicShadersAddon.SituationLabelSystem.GetLabel("situation_a");
                if (labelA != null)
                {
                    GUILayout.Space(5);
                    GUILayout.Label("<b>Situation Display Debug (A)</b>", HighLogic.Skin.label);
                    
                    GUILayout.Label($"Rotation: {labelA.RotationDegrees:F1}°");
                    float newRot = GUILayout.HorizontalSlider(labelA.RotationDegrees, -10f, 10f);
                    if (!Mathf.Approximately(newRot, labelA.RotationDegrees))
                    {
                        labelA.RotationDegrees = newRot;
                        labelA.PositionDirty = true;
                    }
                    
                    GUILayout.Label($"Left Padding: {labelA.PaddingLeft:F2}");
                    float newPadL = GUILayout.HorizontalSlider(labelA.PaddingLeft, 0f, 0.7f);
                    if (!Mathf.Approximately(newPadL, labelA.PaddingLeft))
                    {
                        labelA.PaddingLeft = newPadL;
                        labelA.PositionDirty = true;
                    }
                    
                    GUILayout.Label($"Bottom Padding: {labelA.PaddingBottom:F2}");
                    float newPadB = GUILayout.HorizontalSlider(labelA.PaddingBottom, 0f, 0.7f);
                    if (!Mathf.Approximately(newPadB, labelA.PaddingBottom))
                    {
                        labelA.PaddingBottom = newPadB;
                        labelA.PositionDirty = true;
                    }
                    
                    GUILayout.Label($"Font Size: {labelA.FontSizePixels:F0}");
                    float newFont = GUILayout.HorizontalSlider(labelA.FontSizePixels, 8f, 48f);
                    if (!Mathf.Approximately(newFont, labelA.FontSizePixels))
                    {
                        labelA.FontSizePixels = newFont;
                        labelA.ForceTextureUpdate = true;
                    }
                    
                    GUILayout.Label($"Line Spacing: {labelA.LineSpacing:F1}");
                    float newSpacing = GUILayout.HorizontalSlider(labelA.LineSpacing, 0f, 20f);
                    if (!Mathf.Approximately(newSpacing, labelA.LineSpacing))
                    {
                        labelA.LineSpacing = newSpacing;
                        labelA.ForceTextureUpdate = true;
                    }
                    
                    // Mirror to label B
                    var labelB = CinematicShadersAddon.SituationLabelSystem.GetLabel("situation_b");
                    if (labelB != null)
                    {
                        labelB.RotationDegrees = labelA.RotationDegrees;
                        labelB.PaddingLeft = labelA.PaddingLeft;
                        labelB.PaddingBottom = labelA.PaddingBottom;
                        labelB.FontSizePixels = labelA.FontSizePixels;
                        labelB.LineSpacing = labelA.LineSpacing;
                    }
                }
            }
            */

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
                            StarfieldSettings.Save();
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
            PushKartographerParamsStatic();
        }
        
        /// <summary>
        /// Static method to push Kartographer params from settings.
        /// Can be called from CinematicShadersAddon to initialize grid without UI.
        /// </summary>
        public static void PushKartographerParamsStatic()
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
            kartParams.DebugShapesEnabled = 0; // Debug shapes not enabled by default
            kartParams.FocalLength = focalLength;
            StarfieldNative.LastKartographerParams = kartParams;
            StarfieldNative.CR_StarfieldSetKartographerParams(ref kartParams);
        }
        
        /// <summary>
        /// Initializes Kartographer from settings without requiring UI.
        /// Called from CinematicShadersAddon on scene load.
        /// NOTE: This may be called before native DLL is loaded - checks IsLoaded.
        /// </summary>
        public static void InitializeFromSettings()
        {
            // Early exit if native DLL not loaded yet
            if (!StarfieldNative.IsLoaded) 
            {
                Debug.Log("[KartographerTab] InitializeFromSettings() - Native not loaded yet, skipping");
                return;
            }
            
            // Enable/disable grid based on saved setting
            StarfieldNative.CR_StarfieldSetKartographerEnabled(
                StarfieldSettings.EnableKartographer ? (byte)1 : (byte)0);
            
            if (StarfieldSettings.EnableKartographer)
            {
                // Push all visual params
                PushKartographerParamsStatic();
                
                // Initialize label system and update labels
                var labelSystem = new GridLabelSystem();
                labelSystem.Initialize();
                
                // Ensure HUCK label is enabled (except for Tiny preset)
                var huckLabel = labelSystem.GetLabel("huck");
                if (huckLabel != null)
                {
                    int currentPreset = Mathf.Clamp(StarfieldSettings.KartographerGridSize, 0, 4);
                    if (currentPreset != 4 && !huckLabel.Enabled) // Not Tiny
                    {
                        labelSystem.SetLabelEnabled("huck", true);
                    }
                }
                
                // Update labels to push them to native
                labelSystem.Update();
                
                // Initialize star selector if mouse hover was previously enabled
                if (StarfieldSettings.KartographerMouseHoverSelect)
                {
                    CreateSelectorAndLoadJsonStatic();
                }
            }
        }
        
        /// <summary>
        /// Static version of CreateSelectorAndLoadJson for InitializeFromSettings.
        /// Creates selector and enables mouse hover mode.
        /// </summary>
        private static void CreateSelectorAndLoadJsonStatic()
        {
            var selector = new KartographerSelector();
            
            // Load JSON for current catalog
            string catalogPath = StarfieldSettings.ActiveCatalogPath;
            if (!string.IsNullOrEmpty(catalogPath))
            {
                string absolutePath = System.IO.Path.Combine(KSPUtil.ApplicationRootPath, catalogPath);
                selector.LoadJsonForCatalog(absolutePath);
            }
            
            // Enable mouse hover mode immediately
            selector.SetMouseHoverMode(true);
            
            // Register for camera updates
            StarfieldCompositor.KartographerSelectorCallback = (right, up, forward, aspect, vfov) =>
            {
                selector.CameraRight = right;
                selector.CameraUp = up;
                selector.CameraForward = forward;
                selector.AspectRatio = aspect;
                selector.VerticalFOV = vfov;
                selector.Update();
            };
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

        /// <summary>
        /// Create the selector and load JSON catalog data
        /// Called when mouse hover mode is enabled
        /// </summary>
        private void CreateSelectorAndLoadJson()
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
                Debug.Log("[KartographerTab] Selector created and JSON loaded for mouse hover");
            }
            else
            {
                Debug.LogWarning("[KartographerTab] No active catalog to load star data from");
            }
        }
        
        /// <summary>
        /// Stop tracking and clear selector
        /// Called when Kartographer is disabled
        /// </summary>
        private void StopTracking()
        {
            _selector?.StopTracking();
            _selector = null;  // Clear selector when disabled
            StarfieldSettings.KartographerTrackedStarHIP = 0;
            StarfieldSettings.EnablePolarisTracking = false;
            StarfieldSettings.Save();
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

        /// <summary>
        /// Update grid labels - runs independently of selector when Kartographer is enabled
        /// Grid label system is now managed entirely by CinematicShadersAddon.UpdateGridLabelSystem()
        /// This method just ensures native state is synchronized.
        /// </summary>
        private void UpdateGridLabels()
        {
            if (!StarfieldSettings.EnableKartographer)
                return;
            
            // Ensure native state matches settings (scene transition handling)
            if (StarfieldNative.IsLoaded)
            {
                PushKartographerParams();
                StarfieldNative.CR_StarfieldSetKartographerEnabled(1);
            }
        }

        /// <summary>
        /// Standalone grid label debug export - does not depend on selector
        /// </summary>
        private void ExportGridLabelDebug()
        {
            Debug.Log("[KartographerTab] Starting standalone grid label export...");
            
            try
            {
                // Create temporary selector just for this export
                var debugSelector = new KartographerSelector();
                
                // Load catalog JSON path
                string catalogPath = StarfieldSettings.ActiveCatalogPath;
                if (!string.IsNullOrEmpty(catalogPath))
                {
                    string absolutePath = Path.Combine(KSPUtil.ApplicationRootPath, catalogPath);
                    debugSelector.LoadJsonForCatalog(absolutePath);
                }
                
                debugSelector.ExportGridLabelTexture();
                
                // Cleanup
                debugSelector.Dispose();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[KartographerTab] Export failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Update vessel target selector - draws circle around current target
        /// </summary>
        /// <summary>
        /// Debug dump of orbit information for vessel
        /// </summary>
        private void DumpOrbitInfo()
        {
            Debug.Log("[ORBIT INFO DEBUG] ========== ORBIT INFO DUMP ==========");
            
            try
            {
                // SOI (Sphere of Influence)
                if (FlightGlobals.currentMainBody != null)
                {
                    Debug.Log($"[ORBIT INFO DEBUG] SOI: {FlightGlobals.currentMainBody.bodyName}");
                }
                else
                {
                    Debug.Log("[ORBIT INFO DEBUG] SOI: UNKNOWN");
                }
                
                // Situation
                if (FlightGlobals.ActiveVessel != null)
                {
                    string situation = FlightGlobals.ActiveVessel.situation.ToString();
                    Debug.Log($"[ORBIT INFO DEBUG] Situation: {situation}");
                    
                    // Altitude
                    double altitude = FlightGlobals.ActiveVessel.altitude;
                    Debug.Log($"[ORBIT INFO DEBUG] Altitude: {altitude:F1} m");
                    
                    // Orbit info
                    if (FlightGlobals.ActiveVessel.orbit != null)
                    {
                        double apoapsis = FlightGlobals.ActiveVessel.orbit.ApA;
                        double periapsis = FlightGlobals.ActiveVessel.orbit.PeA;
                        Debug.Log($"[ORBIT INFO DEBUG] Apoapsis: {apoapsis:F1} m");
                        Debug.Log($"[ORBIT INFO DEBUG] Periapsis: {periapsis:F1} m");
                    }
                    else
                    {
                        Debug.Log("[ORBIT INFO DEBUG] Orbit: NULL (landed/surface)");
                    }
                }
                else
                {
                    Debug.Log("[ORBIT INFO DEBUG] ActiveVessel: NULL");
                }
                
                Debug.Log("[ORBIT INFO DEBUG] ========== END DUMP ==========");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ORBIT INFO DEBUG] Exception during dump: {ex.Message}");
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
