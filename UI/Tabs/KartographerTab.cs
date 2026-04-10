using CinematicShaders.Core;
using CinematicShaders.Native;
using CinematicShaders.Shaders.Starfield;
using CinematicShaders.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static CinematicShaders.Core.StarfieldSettings;

namespace CinematicShaders.UI.Tabs
{
    public class KartographerTab
    {
        private bool _initialized = false;
        private bool _showDisplayOptions = true;
        private bool _showNavballOptions = false;
        private bool _showSituationOptions = false;
        private bool _showColorDropdown = false;
        private int _currentColorIndex = 0;

        private GUIStyle[] _colorButtonStyles = null;

        #region Holographic Display Integration

        // Display mode enum
        public enum StarConsoleMode
        {
            Legacy,     // Original IMGUI window
            Small,      // Holographic 450x525
            Medium,     // Holographic 600x700 (default)
            Large       // Holographic 800x933
        }

        // State
        private StarConsoleMode _consoleMode = StarConsoleMode.Medium;
        private StarCatalogHolographicDisplay _holographicDisplay = null;

        #endregion

        public KartographerTab()
        {
            // Settings loaded by StarfieldSettings on module startup
            _currentColorIndex = StarfieldSettings.KartographerGridColor;
            
            // Register for camera update callbacks from StarfieldCompositor
            StarfieldCompositor.KartographerSelectorCallback = OnCameraUpdate;
            
            // Subscribe to StarCatalogStateManager for catalog change notifications
            StarCatalogStateManager.OnCatalogChanged += HandleStateManagerCatalogChanged;
            StarCatalogStateManager.OnJsonStateChanged += HandleJsonStateChanged;
        }
        
        /// <summary>
        /// Called when active catalog changes - reloads JSON for new catalog
        /// Called via StarCatalogStateManager.OnCatalogChanged event
        /// </summary>
        private void HandleStateManagerCatalogChanged(CatalogChangedEventArgs args)
        {
            Debug.Log($"[KartographerTab] State manager catalog changed: {Path.GetFileName(args.NewCatalogPath)}");
            // Selector and HolographicDisplay handle their own updates via events
        }

        private void HandleJsonStateChanged(JsonStateChangedEventArgs args)
        {
            Debug.Log($"[KartographerTab] JSON state changed: {args.OldAvailability} -> {args.NewAvailability}");
            
            // Update holographic display star list if JSON became available
            if (args.NewAvailability != JsonAvailability.None && 
                args.OldAvailability == JsonAvailability.None &&
                _holographicDisplay != null && _holographicDisplay.IsVisible)
            {
                var stars = GetNamedStarsFromSelector();
                _holographicDisplay.SetStarList(stars);
            }
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

        // Star tracking
        
        // Star tracking
        private KartographerSelector _selector;
        
        /// <summary>
        /// Public accessor for the selector (used by StarCatalogEditorWindow)
        /// </summary>
        public KartographerSelector Selector => _selector;
        
        // Star Catalog Editor
        private StarCatalogEditorWindow _starEditorWindow;
        
        // Grid label system is now managed by CinematicShadersAddon and shared with UI
        
        public void Draw()
        {
            if (!_initialized)
            {
                _initialized = true;
            }

            if (!StarfieldNative.IsLoaded)
            {
                GUILayout.Label(CinematicShadersUIStrings.Kartographer.NativeLoadError, 
                    CinematicShadersUIResources.Styles.Error());
                return;
            }

            DrawEnableToggle();
            
            // Initialize mouse hover if previously enabled (loading game state)
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
                
                // Add Star Console display mode selector when enabled
                if (StarfieldSettings.KartographerMouseHoverSelect || 
                    (_holographicDisplay != null && _holographicDisplay.IsVisible) ||
                    (_starEditorWindow != null && _starEditorWindow.IsVisible))
                {
                    GUILayout.Space(10);
                    GUILayout.Label(CinematicShadersUIStrings.Kartographer.DisplayModeLabel, HighLogic.Skin.label);
                    DrawStarConsoleSelector();
                }
            }
            else
            {
                // Kartographer disabled - ensure tracking is stopped (prevents race condition)
                if (_selector != null)
                {
                    StopTracking();
                }
                
                // Hide editor window when Kartographer disabled
                if (_starEditorWindow != null && _starEditorWindow.IsVisible)
                {
                    _starEditorWindow.Hide();
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
                CinematicShadersUIStrings.Kartographer.EnableToggleLabel, toggleStyle);

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
            GUILayout.BeginVertical(HighLogic.Skin.box);

            bool newMouseHoverMode = GUILayout.Toggle(StarfieldSettings.KartographerMouseHoverSelect, 
                CinematicShadersUIStrings.Kartographer.StarCatalogToggle, HighLogic.Skin.toggle);
            
            // Star Console box toggle - matches Situation/Navball section style
            GUILayout.Space(5);
            bool displayVisible = (_consoleMode == StarConsoleMode.Legacy && 
                                   _starEditorWindow != null && 
                                   _starEditorWindow.IsVisible) ||
                                  (_consoleMode != StarConsoleMode.Legacy &&
                                   _holographicDisplay != null &&
                                   _holographicDisplay.IsVisible);

            bool newDisplayVisible = GUILayout.Toggle(displayVisible,
                CinematicShadersUIStrings.Kartographer.StarConsoleToggle, 
                HighLogic.Skin.button);

            if (newDisplayVisible != displayVisible)
            {
                if (newDisplayVisible)
                {
                    ShowCurrentDisplay();
                }
                else
                {
                    HideCurrentDisplay();
                }
            }
            
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
            
            bool newVesselTarget = GUILayout.Toggle(StarfieldSettings.KartographerVesselTargetSelect, 
                CinematicShadersUIStrings.Kartographer.VesselTargetToggle, HighLogic.Skin.toggle);
            if (newVesselTarget != StarfieldSettings.KartographerVesselTargetSelect)
            {
                StarfieldSettings.KartographerVesselTargetSelect = newVesselTarget;
                StarfieldSettings.Save();
                // Note: Actual selector is managed by CinematicShadersAddon which checks the setting every frame
            }
            
            GUILayout.BeginHorizontal();
            bool newSituationDisplay = GUILayout.Toggle(StarfieldSettings.KartographerSituationDisplay, "", HighLogic.Skin.toggle, GUILayout.Width(20));
            GUILayout.Space(20);
            _showSituationOptions = GUILayout.Toggle(_showSituationOptions, CinematicShadersUIStrings.Kartographer.SituationDisplaySection, HighLogic.Skin.button);
            GUILayout.EndHorizontal();
            
            if (newSituationDisplay != StarfieldSettings.KartographerSituationDisplay)
            {
                StarfieldSettings.KartographerSituationDisplay = newSituationDisplay;
                StarfieldSettings.Save();
            }
            
            // Situation label position adjustment (user-facing, not debug)
            if (_showSituationOptions)
            {
                int gridSize = Mathf.Clamp(StarfieldSettings.KartographerGridSize, 0, 3);
                int[] meridians = { 8, 12, 16, 24 };
                int numSteps = meridians[gridSize];
                
                // Rotation: discrete steps 0 to numMeridians-1
                int currentStep = StarfieldSettings.KartographerSituationRotationStep[gridSize];
                GUILayout.Label(string.Format(CinematicShadersUIStrings.Kartographer.RotationStepFormat, currentStep + 1, numSteps));
                int newStep = Mathf.RoundToInt(GUILayout.HorizontalSlider(currentStep, 0, numSteps - 1));
                if (newStep != currentStep)
                {
                    StarfieldSettings.KartographerSituationRotationStep[gridSize] = newStep;
                    StarfieldSettings.Save();
                }
                
                // Row offset: -2 to +2 steps from base position
                // Left side = down (toward equator), Right side = up (toward pole)
                int currentOffset = StarfieldSettings.KartographerSituationRowOffset[gridSize];
                int sliderIndex = currentOffset + 2;
                GUILayout.Label(string.Format(CinematicShadersUIStrings.Kartographer.DisplayHeightFormat, 
                    CinematicShadersUIStrings.Kartographer.RowOffsetLabels[sliderIndex]));
                int newSliderIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(sliderIndex, 0, 4));
                int newRowOffset = newSliderIndex - 2;
                if (newRowOffset != currentOffset)
                {
                    StarfieldSettings.KartographerSituationRowOffset[gridSize] = newRowOffset;
                    StarfieldSettings.Save();
                }
            }
            
            GUILayout.BeginHorizontal();
            bool newNavballLabels = GUILayout.Toggle(StarfieldSettings.KartographerNavballLabels, "", HighLogic.Skin.toggle, GUILayout.Width(20));
            GUILayout.Space(20);
            _showNavballOptions = GUILayout.Toggle(_showNavballOptions, CinematicShadersUIStrings.Kartographer.NavballIndicatorsSection, HighLogic.Skin.button);
            GUILayout.EndHorizontal();
            
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
            
            if (_showNavballOptions)
            {
                bool newUseColors = GUILayout.Toggle(StarfieldSettings.KartographerNavballUseColors,
                    CinematicShadersUIStrings.Kartographer.NavballColorsToggle, HighLogic.Skin.toggle);
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
                GUILayout.Label(CinematicShadersUIStrings.Kartographer.IconStyleLabel);
                int currentStyle = (int)StarfieldSettings.KartographerNavballIconStyle;
                int newStyle = GUILayout.SelectionGrid(currentStyle, CinematicShadersUIStrings.Kartographer.IconStyleNames, 1, HighLogic.Skin.toggle);
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
                
                // Icon thickness slider (display 1-5, maps to 0.1-0.49)
                float navballDisplayThickness = StarfieldSettings.KartographerNavballIconThickness * 10f;
                GUILayout.Label(string.Format(CinematicShadersUIStrings.Kartographer.IconThicknessFormat, navballDisplayThickness));
                float newNavballDisplayThickness = GUILayout.HorizontalSlider(navballDisplayThickness, 1f, 5f);
                float newThickness = Mathf.Min(newNavballDisplayThickness / 10f, 0.49f);
                if (!Mathf.Approximately(newThickness, StarfieldSettings.KartographerNavballIconThickness))
                {
                    StarfieldSettings.KartographerNavballIconThickness = newThickness;
                    StarfieldSettings.Save();
                }
                
                GUILayout.Space(4);
                
                // Icon size slider (display 1-5, maps to 0.05-0.15)
                float navballDisplaySize = (StarfieldSettings.KartographerNavballIconSize - 0.05f) * 40f + 1f;
                GUILayout.Label(string.Format(CinematicShadersUIStrings.Kartographer.IconSizeFormat, navballDisplaySize));
                float newNavballDisplaySize = GUILayout.HorizontalSlider(navballDisplaySize, 1f, 5f);
                float newSize = 0.05f + (newNavballDisplaySize - 1f) * 0.025f;
                if (!Mathf.Approximately(newSize, StarfieldSettings.KartographerNavballIconSize))
                {
                    StarfieldSettings.KartographerNavballIconSize = newSize;
                    StarfieldSettings.Save();
                }
                
                GUILayout.Space(4);
                
                float pointingDisplaySize = (StarfieldSettings.KartographerPointingIconSize - 0.05f) * 40f + 1f;
                GUILayout.Label(string.Format(CinematicShadersUIStrings.Kartographer.HeadingIndicatorFormat, pointingDisplaySize));
                float newPointingDisplaySize = GUILayout.HorizontalSlider(pointingDisplaySize, 1f, 5f);
                float newPointingSize = 0.05f + (newPointingDisplaySize - 1f) * 0.025f;
                if (!Mathf.Approximately(newPointingSize, StarfieldSettings.KartographerPointingIconSize))
                {
                    StarfieldSettings.KartographerPointingIconSize = newPointingSize;
                    StarfieldSettings.Save();
                }
                
                GUILayout.Space(4);
                
                GUILayout.Label(string.Format(CinematicShadersUIStrings.Kartographer.ManeuverOffsetFormat, 
                    StarfieldSettings.KartographerManeuverTextOffset));
                float newManeuverOffset = GUILayout.HorizontalSlider(StarfieldSettings.KartographerManeuverTextOffset, 0.02f, 0.15f);
                if (!Mathf.Approximately(newManeuverOffset, StarfieldSettings.KartographerManeuverTextOffset))
                {
                    StarfieldSettings.KartographerManeuverTextOffset = newManeuverOffset;
                    StarfieldSettings.Save();
                }
                
                GUILayout.Space(4);
                
                GUILayout.Label(string.Format(CinematicShadersUIStrings.Kartographer.ManeuverScaleFormat, 
                    StarfieldSettings.KartographerManeuverTextScale));
                float newManeuverScale = GUILayout.HorizontalSlider(StarfieldSettings.KartographerManeuverTextScale, 0.5f, 2.0f);
                if (!Mathf.Approximately(newManeuverScale, StarfieldSettings.KartographerManeuverTextScale))
                {
                    StarfieldSettings.KartographerManeuverTextScale = newManeuverScale;
                    StarfieldSettings.Save();
                }
                
            }
            
            GUILayout.Space(5);

            _showDisplayOptions = GUILayout.Toggle(_showDisplayOptions, CinematicShadersUIStrings.Kartographer.DisplayOptionsSection, HighLogic.Skin.button);

            if (_showDisplayOptions)
            {
                // Display Color dropdown
                DrawColorDropdown();

                GUILayout.Space(5);

                // Grid Size: 0-3 (Jumbo, Large, Medium, Small), default 2 (Medium)
                // Note: Tiny (4) is available in code but disabled in UI - too dense for labels
                GUILayout.Label(new GUIContent(
                    string.Format(CinematicShadersUIStrings.Kartographer.GridSizeFormat, 
                        CinematicShadersUIStrings.Kartographer.GridSizeLabels[StarfieldSettings.KartographerGridSize]),
                    CinematicShadersUIStrings.Kartographer.GridSizeTooltip));
                int newGridSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(StarfieldSettings.KartographerGridSize, 0, 3));
                if (newGridSize != StarfieldSettings.KartographerGridSize)
                {
                    StarfieldSettings.KartographerGridSize = newGridSize;
                    PushKartographerParams();
                    StarfieldSettings.Save();
                }

                // Grid Intensity: display 0-5, internal 0-0.006 (default display ~1.7)
                float displayIntensity = IntensityToDisplay(StarfieldSettings.KartographerGridIntensity);
                GUILayout.Label(new GUIContent(
                    string.Format(CinematicShadersUIStrings.Kartographer.GridIntensityFormat, displayIntensity), 
                    CinematicShadersUIStrings.Kartographer.GridIntensityTooltip));
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
                GUILayout.Label(new GUIContent(
                    string.Format(CinematicShadersUIStrings.Kartographer.GridSoftnessFormat, displayThickness), 
                    CinematicShadersUIStrings.Kartographer.GridSoftnessTooltip));
                float newDisplayThickness = GUILayout.HorizontalSlider(displayThickness, 0f, 10f);
                if (!Mathf.Approximately(newDisplayThickness, displayThickness))
                {
                    StarfieldSettings.KartographerGridThickness = DisplayToThickness(newDisplayThickness);
                    PushKartographerParams();
                    StarfieldSettings.Save();
                }

                GUILayout.Label(new GUIContent(
                    string.Format(CinematicShadersUIStrings.Kartographer.VignetteStrengthFormat, 
                        StarfieldSettings.KartographerVignetteStrength), 
                    CinematicShadersUIStrings.Kartographer.VignetteStrengthTooltip));
                float newVignetteStr = GUILayout.HorizontalSlider(StarfieldSettings.KartographerVignetteStrength, 0.35f, 1.0f);
                if (!Mathf.Approximately(newVignetteStr, StarfieldSettings.KartographerVignetteStrength))
                {
                    StarfieldSettings.KartographerVignetteStrength = newVignetteStr;
                    PushKartographerParams();
                    StarfieldSettings.Save();
                }

                GUILayout.Label(new GUIContent(
                    string.Format(CinematicShadersUIStrings.Kartographer.VignetteStartFormat, 
                        StarfieldSettings.KartographerVignetteStart), 
                    CinematicShadersUIStrings.Kartographer.VignetteStartTooltip));
                float newVignetteStart = GUILayout.HorizontalSlider(StarfieldSettings.KartographerVignetteStart, 0.8f, 2.4f);
                if (!Mathf.Approximately(newVignetteStart, StarfieldSettings.KartographerVignetteStart))
                {
                    StarfieldSettings.KartographerVignetteStart = newVignetteStart;
                    PushKartographerParams();
                    StarfieldSettings.Save();
                }

                GUILayout.Label(new GUIContent(
                    string.Format(CinematicShadersUIStrings.Kartographer.VignetteEndFormat, 
                        StarfieldSettings.KartographerVignetteEnd), 
                    CinematicShadersUIStrings.Kartographer.VignetteEndTooltip));
                float newVignetteEnd = GUILayout.HorizontalSlider(StarfieldSettings.KartographerVignetteEnd, 1.1f, 3.3f);
                if (!Mathf.Approximately(newVignetteEnd, StarfieldSettings.KartographerVignetteEnd))
                {
                    StarfieldSettings.KartographerVignetteEnd = newVignetteEnd;
                    PushKartographerParams();
                    StarfieldSettings.Save();
                }
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
            if (GUILayout.Button(CinematicShadersUIStrings.Kartographer.ResetButton))
            {
                ResetToDefaults();
            }
            
            /* DEBUG: Fixed padding tuning for situation labels - DISABLED
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
            */

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
            GUILayout.Label(CinematicShadersUIStrings.Kartographer.DisplayColorLabel, 
                GUILayout.Width(CinematicShadersUIResources.Layout.Dropdowns.DEBUG_LABEL_WIDTH));
            GUIStyle currentStyle = _colorButtonStyles[_currentColorIndex];
            if (GUILayout.Button(CinematicShadersUIStrings.Kartographer.ColorNames[_currentColorIndex], 
                currentStyle, GUILayout.Width(CinematicShadersUIResources.Layout.Dropdowns.DEBUG_BUTTON_WIDTH)))
            {
                _showColorDropdown = !_showColorDropdown;
            }
            GUILayout.EndHorizontal();

            if (_showColorDropdown)
            {
                GUIStyle boxStyle = CinematicShadersUIResources.Styles.DropdownBox();
                GUILayout.BeginVertical(boxStyle);
                for (int i = 0; i < CinematicShadersUIStrings.Kartographer.ColorNames.Length; i++)
                {
                    if (GUILayout.Button(CinematicShadersUIStrings.Kartographer.ColorNames[i], _colorButtonStyles[i]))
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

            _colorButtonStyles = new GUIStyle[CinematicShadersUIStrings.Kartographer.ColorNames.Length];
            for (int i = 0; i < CinematicShadersUIStrings.Kartographer.ColorNames.Length; i++)
            {
                _colorButtonStyles[i] = CinematicShadersUIResources.Styles.ColorButton(
                    CinematicShadersUIResources.Colors.GridColors.All[i]);
            }
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
            
            // JSON loading is handled by StarCatalogStateManager
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
            // Display Options
            StarfieldSettings.KartographerGridIntensity = 0.0012f;
            StarfieldSettings.KartographerGridThickness = 0.00036f;
            StarfieldSettings.KartographerVignetteStrength = 0.7f;
            StarfieldSettings.KartographerVignetteStart = 1.0f;
            StarfieldSettings.KartographerVignetteEnd = 2.2f;
            StarfieldSettings.KartographerRotationYaw = 0.0f;
            StarfieldSettings.KartographerRotationPitch = 0.0f;
            StarfieldSettings.KartographerGridSize = 2;
            StarfieldSettings.KartographerGridColor = 0;
            _currentColorIndex = 0;

            // Situation Display
            StarfieldSettings.KartographerSituationRotationStep = new int[4] { 0, 0, 0, 0 };
            StarfieldSettings.KartographerSituationRowOffset = new int[4] { 0, 0, 0, 0 };

            // Navball Indicators
            StarfieldSettings.KartographerNavballUseColors = false;
            StarfieldSettings.KartographerNavballIconStyle = NavballIconStyle.Retro;
            StarfieldSettings.KartographerNavballIconThickness = 0.2f;
            StarfieldSettings.KartographerNavballIconSize = 0.125f;
            StarfieldSettings.KartographerPointingIconSize = 0.125f;
            StarfieldSettings.KartographerManeuverTextOffset = 0.1f;
            StarfieldSettings.KartographerManeuverTextScale = 1.0f;
            
            // Notify managers of style reset
            if (CinematicShadersAddon.NavballManager != null)
            {
                CinematicShadersAddon.NavballManager.SetUseNavballColors(StarfieldSettings.KartographerNavballUseColors);
                CinematicShadersAddon.NavballManager.SetIconStyle(StarfieldSettings.KartographerNavballIconStyle);
            }
            
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
                Debug.Log("[KartographerTab] KartographerSelector created");
            }
            
            // JSON loading is now handled automatically by StarCatalogStateManager
            // when the catalog is set. The selector subscribes to events.
            // Just ensure the catalog is initialized if not already.
            string catalogPath = StarfieldSettings.ActiveCatalogPath;
            if (!string.IsNullOrEmpty(catalogPath))
            {
                string absolutePath = Path.Combine(KSPUtil.ApplicationRootPath, catalogPath);
                
                // This will initialize the state manager if needed
                _selector.LoadJsonForCatalog(absolutePath);
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
        
        /// <summary>
        /// Toggle the legacy Star Catalog Editor window
        /// </summary>
        private void ToggleStarEditorLegacy()
        {
            if (_starEditorWindow == null)
            {
                CreateEditorWindow();
            }
            
            if (_starEditorWindow.IsVisible)
            {
                _starEditorWindow.Hide();
            }
            else
            {
                // Refresh star list from selector
                if (_selector != null)
                {
                    var stars = GetNamedStarsFromSelector();
                    NamedStar preselectedStar = _selector.GetLockedStar();
                    _starEditorWindow.Initialize(stars, _selector, preselectedStar);
                }
                _starEditorWindow.Show();
            }
        }

        /// <summary>
        /// Draw the Star Console mode selector
        /// </summary>
        private void DrawStarConsoleSelector()
        {
            GUILayout.Space(5);
            
            // Mode selection buttons
            GUILayout.BeginHorizontal();
            
            GUIStyle buttonStyle = HighLogic.Skin.button;
            
            // Legacy button
            GUIStyle legacyStyle = (_consoleMode == StarConsoleMode.Legacy) ? 
                CinematicShadersUIResources.Styles.ButtonActive() : buttonStyle;
            if (GUILayout.Button(CinematicShadersUIStrings.Kartographer.DisplayModeLegacy, legacyStyle, GUILayout.Width(60)))
            {
                SetConsoleMode(StarConsoleMode.Legacy);
            }
            
            // Small button
            GUIStyle smallStyle = (_consoleMode == StarConsoleMode.Small) ? 
                CinematicShadersUIResources.Styles.ButtonActive() : buttonStyle;
            if (GUILayout.Button(CinematicShadersUIStrings.Kartographer.DisplayModeSmall, smallStyle, GUILayout.Width(60)))
            {
                SetConsoleMode(StarConsoleMode.Small);
            }
            
            // Medium button
            GUIStyle mediumStyle = (_consoleMode == StarConsoleMode.Medium) ? 
                CinematicShadersUIResources.Styles.ButtonActive() : buttonStyle;
            if (GUILayout.Button(CinematicShadersUIStrings.Kartographer.DisplayModeMedium, mediumStyle, GUILayout.Width(60)))
            {
                SetConsoleMode(StarConsoleMode.Medium);
            }
            
            // Large button
            GUIStyle largeStyle = (_consoleMode == StarConsoleMode.Large) ? 
                CinematicShadersUIResources.Styles.ButtonActive() : buttonStyle;
            if (GUILayout.Button(CinematicShadersUIStrings.Kartographer.DisplayModeLarge, largeStyle, GUILayout.Width(60)))
            {
                SetConsoleMode(StarConsoleMode.Large);
            }
            
            GUILayout.EndHorizontal();
            
            // Debug: Export textures button
            GUILayout.Space(5);
            if (GUILayout.Button("Export Textures (Debug)", HighLogic.Skin.button))
            {
                ExportHolographicTextures();
            }
        }

        /// <summary>
        /// Set the console display mode
        /// </summary>
        private void SetConsoleMode(StarConsoleMode mode)
        {
            if (_consoleMode == mode) return;
            
            // Hide current display
            HideCurrentDisplay();
            
            _consoleMode = mode;
            
            // Show new display
            ShowCurrentDisplay();
            
            Debug.Log($"[KartographerTab] Star Console mode changed to: {mode}");
        }

        /// <summary>
        /// Hide the current display (legacy or holographic)
        /// </summary>
        private void HideCurrentDisplay()
        {
            if (_consoleMode == StarConsoleMode.Legacy)
            {
                if (_starEditorWindow != null && _starEditorWindow.IsVisible)
                {
                    _starEditorWindow.Hide();
                }
            }
            else
            {
                if (_holographicDisplay != null && _holographicDisplay.IsVisible)
                {
                    _holographicDisplay.Hide();
                }
            }
        }

        /// <summary>
        /// Show the current display based on mode
        /// </summary>
        private void ShowCurrentDisplay()
        {
            if (_consoleMode == StarConsoleMode.Legacy)
            {
                ToggleStarEditorLegacy();
            }
            else
            {
                ToggleHolographicDisplay();
            }
        }

        /// <summary>
        /// Map StarConsoleMode to HolographicDisplaySize
        /// </summary>
        private HolographicDisplaySize MapConsoleModeToSize(StarConsoleMode mode)
        {
            switch (mode)
            {
                case StarConsoleMode.Small:
                    return HolographicDisplaySize.Small;
                case StarConsoleMode.Large:
                    return HolographicDisplaySize.Large;
                case StarConsoleMode.Medium:
                default:
                    return HolographicDisplaySize.Medium;
            }
        }

        /// <summary>
        /// Toggle the holographic display
        /// </summary>
        private void ToggleHolographicDisplay()
        {
            if (_holographicDisplay == null)
            {
                CreateHolographicDisplay();
            }
            else
            {
                // Update size if mode changed
                HolographicDisplaySize expectedSize = MapConsoleModeToSize(_consoleMode);
                _holographicDisplay.SetDisplaySize(expectedSize);
            }
            
            if (_holographicDisplay.IsVisible)
            {
                _holographicDisplay.Hide();
            }
            else
            {
                // Initialize with data
                InitializeHolographicDisplay();
                _holographicDisplay.Show();
            }
        }

        /// <summary>
        /// Create the holographic display component
        /// </summary>
        private void CreateHolographicDisplay()
        {
            var addon = CinematicShadersAddon.Instance;
            if (addon != null)
            {
                _holographicDisplay = addon.gameObject.AddComponent<StarCatalogHolographicDisplay>();
                
                // Calculate position (docked to main window)
                float x = 0, y = 0;
                if (CinematicShadersWindow.Instance != null)
                {
                    Rect mainRect = CinematicShadersWindow.Instance.WindowRect;
                    x = mainRect.x + mainRect.width + 5f;
                    y = mainRect.y;
                }
                
                // Get text system from selector (or initialize new one)
                IntPtr textSystem = _selector?.GetTextSystem() ?? IntPtr.Zero;
                
                // Get JSON paths from selector
                string customPath = _selector?.CustomJsonPath ?? "";
                string defaultPath = _selector?.DefaultJsonPath ?? "";
                
                // Get catalog path for state manager initialization (required for scan-to-main transition)
                string catalogPath = StarfieldSettings.ActiveCatalogPath ?? "";
                
                // Map StarConsoleMode to HolographicDisplaySize
                HolographicDisplaySize size = HolographicDisplaySize.Medium;
                switch (_consoleMode)
                {
                    case StarConsoleMode.Small:
                        size = HolographicDisplaySize.Small;
                        break;
                    case StarConsoleMode.Large:
                        size = HolographicDisplaySize.Large;
                        break;
                    case StarConsoleMode.Medium:
                    default:
                        size = HolographicDisplaySize.Medium;
                        break;
                }
                
                // Initialize with selected size
                _holographicDisplay.Initialize(textSystem, x, y, size, customPath, defaultPath, catalogPath);
                
                // Set selector for bidirectional sync
                _holographicDisplay.SetSelector(_selector);
                
                // Subscribe to events
                _holographicDisplay.OnRescanConfirmed += OnHolographicRescan;
                _holographicDisplay.OnWindowClosed += OnHolographicWindowClosed;
            }
        }

        /// <summary>
        /// Initialize holographic display with star data
        /// </summary>
        private void InitializeHolographicDisplay()
        {
            if (_holographicDisplay == null) return;
            if (_selector == null) return;
            
            // Get star list from selector
            var stars = GetNamedStarsFromSelector();
            _holographicDisplay.SetStarList(stars);
            
            // Set pre-selected star if any
            var lockedStar = _selector.GetLockedStar();
            if (lockedStar != null)
            {
                _holographicDisplay.SelectStar(lockedStar);
            }
        }

        /// <summary>
        /// Event handlers for holographic display
        /// </summary>
        private void OnHolographicRescan()
        {
            ScanCatalog();
        }
        
        private void OnHolographicWindowClosed()
        {
            Debug.Log("[KartographerTab] Holographic display closed via X button");
            // Window closed itself, just clean up reference if needed
            // The component will be destroyed by the GameObject cleanup
        }

        /// <summary>
        /// Export all holographic display textures to PNG for debugging/layout
        /// Files are saved to PluginData/TextureExports/
        /// </summary>
        private void ExportHolographicTextures()
        {
            if (_holographicDisplay == null)
            {
                Debug.Log("[KartographerTab] No holographic display to export");
                return;
            }
            
            _holographicDisplay.ExportAllTexturesToPng();
        }

        /// <summary>
        /// Scan the current catalog and generate JSON
        /// </summary>
        private void ScanCatalog()
        {
            try
            {
                string catalogPath = StarfieldSettings.ActiveCatalogPath;
                if (string.IsNullOrEmpty(catalogPath))
                {
                    Debug.LogError("[KartographerTab] No active catalog to scan");
                    return;
                }
                
                string binPath = Path.Combine(KSPUtil.ApplicationRootPath, catalogPath);
                if (!File.Exists(binPath))
                {
                    Debug.LogError($"[KartographerTab] Catalog file not found: {binPath}");
                    return;
                }
                
                // Generate JSON
                if (StarCatalogManager.GenerateJsonForProceduralCatalog(binPath))
                {
                    Debug.Log($"[KartographerTab] Successfully scanned catalog: {binPath}");
                    
                    // Notify StarCatalogStateManager that JSON is now available
                    // This triggers OnJsonStateChanged event which causes Scan->Main transition
                    StarCatalogStateManager.RefreshJsonState();
                    
                    // Force reload JSON from disk
                    if (_selector != null)
                    {
                        _selector.ForceReloadJson();
                    }
                    
                    // Refresh holographic display if active
                    if (_holographicDisplay != null && _holographicDisplay.IsVisible)
                    {
                        var stars = GetNamedStarsFromSelector();
                        _holographicDisplay.SetStarList(stars);
                    }
                }
                else
                {
                    Debug.LogWarning($"[KartographerTab] Failed to scan catalog (may not be procedural): {binPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KartographerTab] Error scanning catalog: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Create the StarCatalogEditorWindow component
        /// </summary>
        private void CreateEditorWindow()
        {
            // Add component to same GameObject as the addon
            var addon = CinematicShadersAddon.Instance;
            if (addon != null)
            {
                _starEditorWindow = addon.gameObject.AddComponent<StarCatalogEditorWindow>();
            }
        }
        
        /// <summary>
        /// Get the list of named stars from the selector
        /// </summary>
        private List<NamedStar> GetNamedStarsFromSelector()
        {
            if (_selector == null) return new List<NamedStar>();
            
            // Use reflection to access private _namedStars field
            var field = typeof(KartographerSelector).GetField("_namedStars", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                var namedStars = field.GetValue(_selector) as Dictionary<int, NamedStar>;
                if (namedStars != null)
                {
                    return namedStars.Values.ToList();
                }
            }
            return new List<NamedStar>();
        }
        
        private float IntensityToDisplay(float internalVal) => internalVal / 0.006f * 5f;
        private float DisplayToIntensity(float displayVal) => displayVal / 5f * 0.006f;
        private float ThicknessToDisplay(float internalVal) => internalVal / 0.0009f * 10f;
        private float DisplayToThickness(float displayVal) => displayVal / 10f * 0.0009f;

        private string GetGridSizeLabel(int size)
        {
            if (size >= 0 && size < CinematicShadersUIStrings.Kartographer.GridSizeLabels.Length)
            {
                return CinematicShadersUIStrings.Kartographer.GridSizeLabels[size];
            }
            return CinematicShadersUIStrings.Kartographer.GridSizeMedium;
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
