using CinematicShaders.Core;
using CinematicShaders.Native;
using CinematicShaders.Shaders.Starfield;
using System.Linq;
using UnityEngine;

namespace CinematicShaders.UI.Tabs
{
    public class StarfieldTab
    {
        // Rendering
        private float _exposure;
        private float _blurPixels;

        // Distribution
        private float _minMagnitude;
        private float _maxMagnitude;
        private float _magnitudeBias;
        private int _heroCount;
        private float _clustering;
        private float _populationBias;
        private float _mainSequenceStrength;
        private float _redGiantFrequency;

        // Galactic Structure
        private float _galacticFlatness;
        private float _galacticDiscFalloff;
        private float _bandCenterBoost;
        private float _bandCoreSharpness;
        private float _bulgeIntensity;
        private float _bulgeWidth;
        private float _bulgeHeight;
        private float _bulgeSoftness;
        private float _bulgeNoiseScale;
        private float _bulgeNoiseStrength;

        // Beauty
        private float _bloomThreshold;
        private float _bloomIntensity;
        private float _colorSaturation;
        private int _catalogSeed;
        private int _catalogSize;

        // Coordinate Rotation
        private float _rotationX;
        private float _rotationY;
        private float _rotationZ;

        // Catalog Management
        private bool _initialized = false;
        private bool _showReadOnlyWarning = false;
        private string _newCatalogName = "";
        private string _newFileName = "";
        private bool _showSaveAsDialog = false;
        private Vector2 _catalogDropdownScroll;
        private bool _catalogDropdownOpen = false;
        private string[] _catalogNames = new string[0];
        private string[] _catalogPaths = new string[0];
        
        // Section collapsible states
        private bool _showRenderingSection = true;
        private bool _showMainGenerationSection = true;
        private bool _showAdvancedGenerationSection = false;  // Collapsed by default

        public StarfieldTab()
        {
            // Initialize from settings
            _exposure = StarfieldSettings.Exposure;
            _blurPixels = StarfieldSettings.BlurPixels;
            _minMagnitude = StarfieldSettings.MinMagnitude;
            _maxMagnitude = StarfieldSettings.MaxMagnitude;
            _magnitudeBias = StarfieldSettings.MagnitudeBias;
            _heroCount = StarfieldSettings.HeroCount;
            _clustering = StarfieldSettings.Clustering;
            _populationBias = StarfieldSettings.PopulationBias;
            _mainSequenceStrength = StarfieldSettings.MainSequenceStrength;
            _redGiantFrequency = StarfieldSettings.RedGiantFrequency;
            _galacticFlatness = StarfieldSettings.GalacticFlatness;
            _galacticDiscFalloff = StarfieldSettings.GalacticDiscFalloff;
            _bandCenterBoost = StarfieldSettings.BandCenterBoost;
            _bandCoreSharpness = StarfieldSettings.BandCoreSharpness;
            _bulgeIntensity = StarfieldSettings.BulgeIntensity;
            _bulgeWidth = StarfieldSettings.BulgeWidth;
            _bulgeHeight = StarfieldSettings.BulgeHeight;
            _bulgeSoftness = StarfieldSettings.BulgeSoftness;
            _bulgeNoiseScale = StarfieldSettings.BulgeNoiseScale;
            _bulgeNoiseStrength = StarfieldSettings.BulgeNoiseStrength;
            _bloomThreshold = StarfieldSettings.BloomThreshold;
            _bloomIntensity = StarfieldSettings.BloomIntensity;
            _colorSaturation = StarfieldSettings.ColorSaturation;
            _catalogSeed = StarfieldSettings.CatalogSeed;
            _catalogSize = StarfieldSettings.CatalogSize;
            _rotationX = StarfieldSettings.RotationX;
            _rotationY = StarfieldSettings.RotationY;
            _rotationZ = StarfieldSettings.RotationZ;
        }

        public void Draw()
        {
            
            if (!StarfieldNative.IsLoaded)
            {
                GUILayout.Space(20);
                GUILayout.Label(CinematicShadersUIStrings.Starfield.NativeLoadError, CinematicShadersUIResources.Styles.Error());
                return;
            }

            if (!_initialized)
            {
                PushSettingsToNative();
                _initialized = true;
            }

            bool oldEnabled = GUI.enabled;

            try
            {
                // Catalog Management Section (always visible)
                DrawCatalogSection();
                
                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                // === RENDERING SECTION ===
                _showRenderingSection = GUILayout.Toggle(_showRenderingSection, CinematicShadersUIStrings.Starfield.RenderingSection, HighLogic.Skin.label);
                if (_showRenderingSection)
                {
                    DrawEnableToggle(oldEnabled);

                    if (!StarfieldSettings.EnableStarfield)
                        GUI.enabled = false;

                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.ExposureLabel, ref _exposure, -2.0f, 8.0f, "F1", 
                        CinematicShadersUIStrings.Starfield.ExposureTooltip);
                    
                    // BlurPixels is angular sigma in radians; display as arcminutes (1' = 1/60° ≈ 0.00029 rad)
                    float blurArcminutes = _blurPixels * 3437.75f;  // rad to arcmin (180*60/π)
                    float prevBlurArcminutes = blurArcminutes;
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.BlurPixelsLabel, ref blurArcminutes, 1.0f, 2.0f, "F1", 
                        CinematicShadersUIStrings.Starfield.BlurPixelsTooltip);
                    _blurPixels = blurArcminutes / 3437.75f;
                    // Push immediately if changed (since DrawRenderingSlider doesn't know about the conversion)
                    if (!Mathf.Approximately(blurArcminutes, prevBlurArcminutes))
                        PushSettingsToNative();

                    // Bloom threshold slider 0-10 maps to actual 0-0.1 for finer control
                    float bloomThresholdDisplay = _bloomThreshold * 100.0f;
                    float prevBloomThresholdDisplay = bloomThresholdDisplay;
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.BloomThresholdLabel, ref bloomThresholdDisplay, 0.0f, 10.0f, "F1", 
                        CinematicShadersUIStrings.Starfield.BloomThresholdTooltip);
                    _bloomThreshold = bloomThresholdDisplay / 100.0f;
                    if (!Mathf.Approximately(bloomThresholdDisplay, prevBloomThresholdDisplay))
                        PushSettingsToNative();
                    
                    // Bloom intensity 0-2 with logarithmic mapping
                    float bloomIntensityDisplay = Mathf.Sqrt(_bloomIntensity * 2.0f);
                    float prevBloomIntensityDisplay = bloomIntensityDisplay;
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.BloomIntensityLabel, ref bloomIntensityDisplay, 0.0f, 2.0f, "F2",
                        CinematicShadersUIStrings.Starfield.BloomIntensityTooltip);
                    _bloomIntensity = (bloomIntensityDisplay * bloomIntensityDisplay) * 0.5f;
                    if (!Mathf.Approximately(bloomIntensityDisplay, prevBloomIntensityDisplay))
                        PushSettingsToNative();

                    GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.TIGHT);

                    // Debug button for atmospheric data
                    if (GUILayout.Button(new GUIContent(CinematicShadersUIStrings.Starfield.DebugAtmosphereButton, 
                        CinematicShadersUIStrings.Starfield.DebugAtmosphereTooltip)))
                    {
                        AtmosphericScatteringData.LogDebugDump();
                    }
                }

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                // === MAIN GENERATION SECTION ===
                _showMainGenerationSection = GUILayout.Toggle(_showMainGenerationSection, CinematicShadersUIStrings.Starfield.MainGenerationSection, HighLogic.Skin.label);
                if (_showMainGenerationSection)
                {
                    // Disable generation sliders if read-only OR if catalog is intentional (real sky)
                    bool wasEnabled = GUI.enabled;
                    bool isIntentional = StarCatalogManager.ActiveCatalog != null && !StarCatalogManager.ActiveCatalog.IsProcedural;
                    
                    if (StarfieldSettings.IsReadOnly || isIntentional)
                    {
                        GUI.enabled = false;
                        if (isIntentional)
                        {
                            // Show red warning label for intentional catalogs
                            GUIStyle redLabelStyle = new GUIStyle(HighLogic.Skin.label);
                            redLabelStyle.normal.textColor = Color.red;
                            GUILayout.Label("Non-generated catalogs can only be rotated.", redLabelStyle);
                        }
                        else
                        {
                            GUILayout.Label(CinematicShadersUIStrings.Starfield.ReadOnlyLockMessage, CinematicShadersUIResources.Styles.Help());
                        }
                    }

                    DrawIntSlider(CinematicShadersUIStrings.Starfield.CatalogSeedLabel, ref _catalogSeed, 0, 99999,
                        CinematicShadersUIStrings.Starfield.CatalogSeedTooltip);

                    DrawIntSlider(CinematicShadersUIStrings.Starfield.CatalogSizeLabel, ref _catalogSize, 1000, 100000,
                        CinematicShadersUIStrings.Starfield.CatalogSizeTooltip);

                    DrawSlider(CinematicShadersUIStrings.Starfield.MinMagnitudeLabel, ref _minMagnitude, -2.0f, 3.0f, "F1");
                    DrawSlider(CinematicShadersUIStrings.Starfield.MaxMagnitudeLabel, ref _maxMagnitude, 5.0f, 12.0f, "F1");
                    
                    DrawIntSlider(CinematicShadersUIStrings.Starfield.HeroCountLabel, ref _heroCount, 16, 1024,
                        CinematicShadersUIStrings.Starfield.HeroCountTooltip);
                    
                    DrawSlider(CinematicShadersUIStrings.Starfield.MainSequenceLabel, ref _mainSequenceStrength, 0.0f, 1.0f, "F2",
                        CinematicShadersUIStrings.Starfield.MainSequenceTooltip);
                    
                    DrawSlider(CinematicShadersUIStrings.Starfield.RedGiantFrequencyLabel, ref _redGiantFrequency, 0.0f, 1.0f, "F2");
                    
                    // Color saturation affects generation (star colors), not post-processing
                    DrawSlider(CinematicShadersUIStrings.Starfield.ColorSaturationLabel, ref _colorSaturation, 0.5f, 4.0f, "F2",
                        CinematicShadersUIStrings.Starfield.ColorSaturationTooltip);
                    
                    GUI.enabled = wasEnabled;
                }

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                // === ADVANCED GENERATION SECTION (collapsed by default) ===
                _showAdvancedGenerationSection = GUILayout.Toggle(_showAdvancedGenerationSection, 
                    CinematicShadersUIStrings.Common.CollapsedPrefix + CinematicShadersUIStrings.Starfield.AdvancedGenerationSection, HighLogic.Skin.label);
                if (_showAdvancedGenerationSection)
                {
                    bool isIntentional = StarCatalogManager.ActiveCatalog != null && !StarCatalogManager.ActiveCatalog.IsProcedural;
                    
                    // Coordinate Rotation - available when NOT read-only
                    // (For intentional catalogs, rotation is the only editable parameter)
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.CoordinateRotationSection, HighLogic.Skin.label);
                    if (StarfieldSettings.IsReadOnly)
                        GUI.enabled = false;
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.RotationXLabel, ref _rotationX, 0.0f, 360.0f, "F1", 
                        CinematicShadersUIStrings.Starfield.RotationTooltip, "°");
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.RotationYLabel, ref _rotationY, 0.0f, 360.0f, "F1", 
                        CinematicShadersUIStrings.Starfield.RotationTooltip, "°");
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.RotationZLabel, ref _rotationZ, 0.0f, 360.0f, "F1", 
                        CinematicShadersUIStrings.Starfield.RotationTooltip, "°");
                    
                    // Generation params - disabled if read-only OR intentional (real sky)
                    if (StarfieldSettings.IsReadOnly || isIntentional)
                        GUI.enabled = false;
                    
                    GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.TIGHT);

                    DrawSlider(CinematicShadersUIStrings.Starfield.BrightnessDistributionLabel, ref _magnitudeBias, 0.02f, 0.5f, "F2");
                    DrawSlider(CinematicShadersUIStrings.Starfield.StellarPopulationLabel, ref _populationBias, -1.0f, 1.0f, "F2",
                        CinematicShadersUIStrings.Starfield.StellarPopulationTooltip);
                    DrawSlider(CinematicShadersUIStrings.Starfield.ClusteringLabel, ref _clustering, 0.0f, 1.0f, "F2");

                    GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.TIGHT);
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.GalacticStructureSection, HighLogic.Skin.label);
                    
                    DrawSlider(CinematicShadersUIStrings.Starfield.DiscFlatnessLabel, ref _galacticFlatness, 0.0f, 1.0f, "F2");
                    DrawSlider(CinematicShadersUIStrings.Starfield.DiscFalloffLabel, ref _galacticDiscFalloff, 0.5f, 10.0f, "F1");
                    DrawSlider(CinematicShadersUIStrings.Starfield.BandCenterBoostLabel, ref _bandCenterBoost, 0.0f, 10.0f, "F1");
                    DrawSlider(CinematicShadersUIStrings.Starfield.BandCoreSharpnessLabel, ref _bandCoreSharpness, 1.0f, 50.0f, "F0");
                    DrawSlider(CinematicShadersUIStrings.Starfield.BulgeIntensityLabel, ref _bulgeIntensity, 0.0f, 20.0f, "F1");
                    DrawSlider(CinematicShadersUIStrings.Starfield.BulgeWidthLabel, ref _bulgeWidth, 0.01f, 1.57f, "F2");
                    DrawSlider(CinematicShadersUIStrings.Starfield.BulgeHeightLabel, ref _bulgeHeight, 0.01f, 1.0f, "F2");
                    DrawSlider(CinematicShadersUIStrings.Starfield.BulgeSoftnessLabel, ref _bulgeSoftness, 0.0f, 1.0f, "F2");
                    DrawSlider(CinematicShadersUIStrings.Starfield.BulgeNoiseScaleLabel, ref _bulgeNoiseScale, 0.0f, 100.0f, "F1");
                    DrawSlider(CinematicShadersUIStrings.Starfield.BulgeNoiseStrengthLabel, ref _bulgeNoiseStrength, 0.0f, 1.0f, "F2");
                }
            }
            finally
            {
                GUI.enabled = oldEnabled;
            }
            
            // Draw tooltip at the very end (Unity's built-in GUI.tooltip system)
            DrawTooltip();
        }

        // Simple tooltip using Unity's built-in GUI.tooltip system
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
            
            // Clamp to window bounds
            Rect windowRect = CinematicShadersWindow.Instance.WindowRect;
            x = Mathf.Min(x, windowRect.width - tooltipWidth - 5f);
            y = Mathf.Min(y, windowRect.height - tooltipHeight - 5f);
            
            Rect tooltipRect = new Rect(x, y, tooltipWidth, tooltipHeight);
            GUI.Box(tooltipRect, GUI.tooltip, tooltipStyle);
        }

        private void DrawEnableToggle(bool parentEnabledState)
        {
            bool localEnabled = GUI.enabled;

            try
            {
                GUIStyle toggleStyle = StarfieldSettings.EnableStarfield ?
                    CinematicShadersUIResources.Styles.ToggleActive() : HighLogic.Skin.toggle;

                bool newEnable = GUILayout.Toggle(StarfieldSettings.EnableStarfield,
                    CinematicShadersUIStrings.Starfield.EnableToggle, toggleStyle);

                if (newEnable != StarfieldSettings.EnableStarfield)
                {
                    StarfieldSettings.EnableStarfield = newEnable;
                    if (newEnable)
                    {
                        StarfieldSettings.InvalidateCatalog();
                    }
                    StarfieldManager.OnToggleChanged();
                }
            }
            finally
            {
                GUI.enabled = localEnabled;
            }
        }

        private void DrawSlider(string label, ref float value, float min, float max, string format, string tooltip = null, string suffix = "")
        {
            GUILayout.BeginHorizontal();
            
            // CRITICAL: Use GUIContent to attach tooltip to label (Unity's built-in system)
            GUIContent labelContent = new GUIContent(label, tooltip);
            GUILayout.Label(labelContent, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            float newValue = GUILayout.HorizontalSlider(value, min, max, 
                GUILayout.Width(CinematicShadersUIResources.Layout.Labels.SLIDER_WIDTH));
            
            string displayText = newValue.ToString(format) + suffix;
            GUILayout.Label(displayText, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.VALUE_WIDTH));

            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(newValue, value))
            {
                value = newValue;
                StarfieldSettings.InvalidateCatalog();
                PushSettingsToNative();
            }
        }
        
        /// <summary>
        /// Draws a slider for rendering parameters that don't affect catalog generation.
        /// Only pushes settings to native without invalidating the catalog.
        /// </summary>
        private void DrawRenderingSlider(string label, ref float value, float min, float max, string format, string tooltip = null, string suffix = "")
        {
            GUILayout.BeginHorizontal();
            
            GUIContent labelContent = new GUIContent(label, tooltip);
            GUILayout.Label(labelContent, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            float newValue = GUILayout.HorizontalSlider(value, min, max, 
                GUILayout.Width(CinematicShadersUIResources.Layout.Labels.SLIDER_WIDTH));
            
            string displayText = newValue.ToString(format) + suffix;
            GUILayout.Label(displayText, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.VALUE_WIDTH));

            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(newValue, value))
            {
                value = newValue;
                // NOTE: Does NOT invalidate catalog - only pushes to native for runtime rendering
                PushSettingsToNative();
            }
        }

        private void DrawIntSlider(string label, ref int value, int min, int max, string tooltip = null)
        {
            GUILayout.BeginHorizontal();
            
            // CRITICAL: Use GUIContent to attach tooltip to label (Unity's built-in system)
            GUIContent labelContent = new GUIContent(label, tooltip);
            GUILayout.Label(labelContent, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            float floatValue = value;
            float newValue = GUILayout.HorizontalSlider(floatValue, min, max, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.SLIDER_WIDTH));
            string displayText = value.ToString();
            GUILayout.Label(displayText, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.VALUE_WIDTH));

            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(newValue, floatValue))
            {
                value = Mathf.RoundToInt(newValue);
                StarfieldSettings.CatalogSeed = _catalogSeed;
                StarfieldSettings.CatalogSize = _catalogSize;
                StarfieldSettings.InvalidateCatalog();
                PushSettingsToNative();
            }
        }

        private void DrawCatalogSection()
        {
            GUILayout.Label(CinematicShadersUIStrings.Starfield.StarCatalogSection, HighLogic.Skin.label);
            
            // Active catalog dropdown
            GUILayout.BeginHorizontal();
            GUILayout.Label(CinematicShadersUIStrings.Starfield.ActiveCatalogLabel, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));
            
            string activeName = CinematicShadersUIStrings.Starfield.ActiveCatalogNone;
            if (StarCatalogManager.ActiveCatalog != null)
                activeName = StarCatalogManager.ActiveCatalog.GetDropdownLabel();
            
            if (GUILayout.Button(activeName + CinematicShadersUIStrings.Common.DropdownArrow, GUILayout.Width(200)))
            {
                _catalogDropdownOpen = !_catalogDropdownOpen;
                if (_catalogDropdownOpen)
                {
                    RefreshCatalogList();
                }
            }
            GUILayout.EndHorizontal();
            
            // Dropdown menu
            if (_catalogDropdownOpen)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                _catalogDropdownScroll = GUILayout.BeginScrollView(_catalogDropdownScroll, GUILayout.Height(150));
                
                foreach (var catalog in StarCatalogManager.GetAvailableCatalogs())
                {
                    if (GUILayout.Button(catalog.GetDropdownLabel()))
                    {
                        LoadCatalog(catalog.FilePath);
                        _catalogDropdownOpen = false;
                    }
                }
                
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }
            
            GUILayout.Space(5);
            
            // Read-Only toggle with colored ON/OFF text
            GUILayout.BeginHorizontal();
            
            // Create toggle style with rich text support
            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            toggleStyle.richText = true;
            
            // Use rich text strings with colored ON/OFF
            string toggleLabel = StarfieldSettings.IsReadOnly ? 
                CinematicShadersUIStrings.Starfield.ReadOnlyToggleOn : 
                CinematicShadersUIStrings.Starfield.ReadOnlyToggleOff;
            
            bool newReadOnly = GUILayout.Toggle(StarfieldSettings.IsReadOnly, toggleLabel, toggleStyle, GUILayout.Width(220));
            
            if (newReadOnly != StarfieldSettings.IsReadOnly)
            {
                if (!newReadOnly && StarfieldSettings.IsReadOnly)
                {
                    // User is trying to DISABLE read-only - show warning
                    _showReadOnlyWarning = true;
                }
                else
                {
                    // Enabling read-only is always safe
                    StarfieldSettings.IsReadOnly = true;
                }
            }
            GUILayout.EndHorizontal();
            
            // Read-Only Warning Dialog
            if (_showReadOnlyWarning)
            {
                GUILayout.Space(10);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(CinematicShadersUIStrings.Starfield.ReadOnlyWarningTitle, HighLogic.Skin.label);
                GUILayout.Label(CinematicShadersUIStrings.Starfield.ReadOnlyWarningMessage, CinematicShadersUIResources.Styles.Help());
                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(CinematicShadersUIStrings.Starfield.CancelButton, GUILayout.Width(100)))
                {
                    _showReadOnlyWarning = false;
                }
                if (GUILayout.Button(CinematicShadersUIStrings.Starfield.UnlockButton, GUILayout.Width(150)))
                {
                    StarfieldSettings.IsReadOnly = false;
                    _showReadOnlyWarning = false;
                    // Force save to mark as modifiable
                    if (StarCatalogManager.ActiveCatalog != null)
                    {
                        StarCatalogManager.SaveCatalog(StarCatalogManager.ActiveCatalog.FilePath, 
                            StarCatalogManager.ActiveCatalog.GetDisplayName(), false);
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.Space(10);
            }
            
            // Action buttons
            GUILayout.BeginHorizontal();

            // Save button - allow saving to persist read-only state changes
            GUI.enabled = StarCatalogManager.ActiveCatalog != null;
            if (GUILayout.Button(CinematicShadersUIStrings.Starfield.SaveButton, GUILayout.Width(70)))
            {
                if (StarCatalogManager.ActiveCatalog != null)
                {
                    StarCatalogManager.SaveCatalog(StarCatalogManager.ActiveCatalog.FilePath,
                        StarCatalogManager.ActiveCatalog.GetDisplayName(), false);
                }
            }
            GUI.enabled = true;
            
            // New button (generate fresh catalog)
            if (GUILayout.Button(CinematicShadersUIStrings.Starfield.NewButton, GUILayout.Width(60)))
            {
                // Reset all UI fields to StarfieldSettings defaults
                ResetToDefaults();
                
                // Generate new random seed
                _catalogSeed = new System.Random().Next(0, 99999);
                StarfieldSettings.CatalogSeed = _catalogSeed;
                StarfieldSettings.IsReadOnly = false;  // New catalogs start modifiable
                StarfieldSettings.ActiveCatalogPath = "";  // Clear active catalog
                StarCatalogManager.ActiveCatalog = null;
                StarfieldSettings.InvalidateCatalog();
                PushSettingsToNative();
                
                // Open Save As dialog for naming
                _showSaveAsDialog = true;
                _newFileName = CinematicShadersUIStrings.Starfield.DefaultCatalogFileName;
                _newCatalogName = CinematicShadersUIStrings.Starfield.DefaultCatalogDisplayName;
            }
            
            // Save As button
            if (GUILayout.Button(CinematicShadersUIStrings.Starfield.SaveAsButton, GUILayout.Width(80)))
            {
                _showSaveAsDialog = true;
                _newFileName = StarCatalogManager.ActiveCatalog?.GetDisplayName() ?? CinematicShadersUIStrings.Starfield.DefaultCatalogFileName;
                _newCatalogName = StarCatalogManager.ActiveCatalog?.GetDisplayName() ?? CinematicShadersUIStrings.Starfield.DefaultCatalogDisplayName;
            }
            
            // Open Folder button
            if (GUILayout.Button(CinematicShadersUIStrings.Starfield.OpenFolderButton, GUILayout.Width(90)))
            {
                StarCatalogManager.OpenCatalogFolder();
            }
            
            GUILayout.EndHorizontal();
            
            // Save As Dialog
            if (_showSaveAsDialog)
            {
                GUILayout.Space(10);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(CinematicShadersUIStrings.Starfield.SaveCatalogAsTitle, HighLogic.Skin.label);
                
                GUILayout.Label(CinematicShadersUIStrings.Starfield.FilenameLabel);
                _newFileName = GUILayout.TextField(_newFileName, GUILayout.Width(250));
                
                GUILayout.Label(CinematicShadersUIStrings.Starfield.DisplayNameLabel);
                _newCatalogName = GUILayout.TextField(_newCatalogName, GUILayout.Width(250));
                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Cancel", GUILayout.Width(70)))
                {
                    _showSaveAsDialog = false;
                }
                if (GUILayout.Button(CinematicShadersUIStrings.Starfield.SaveButton, GUILayout.Width(70)))
                {
                    string path = StarCatalogManager.SaveCatalogAs(_newFileName, _newCatalogName, false);
                    if (path != null)
                    {
                        // Reload to switch to new catalog
                        StarCatalogManager.LoadCatalog(path);
                        StarfieldSettings.ActiveCatalogPath = path;
                        StarfieldSettings.IsReadOnly = false;
                    }
                    _showSaveAsDialog = false;
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            
            GUILayout.Space(5);
            
            // Delete button (red, right-aligned)
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.color = Color.red;
            if (GUILayout.Button(CinematicShadersUIStrings.Starfield.DeleteCatalogButton, GUILayout.Width(120)) && StarCatalogManager.ActiveCatalog != null)
            {
                if (StarCatalogManager.ActiveCatalog != null)
                {
                    StarCatalogManager.DeleteCatalog(StarCatalogManager.ActiveCatalog.FilePath);
                }
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }
        
        private void RefreshCatalogList()
        {
            var catalogs = StarCatalogManager.GetAvailableCatalogs();
            _catalogNames = catalogs.Select(c => c.GetDropdownLabel()).ToArray();
            _catalogPaths = catalogs.Select(c => c.FilePath).ToArray();
        }
        
        private void LoadCatalog(string filePath)
        {
            if (StarCatalogManager.LoadCatalog(filePath))
            {
                StarfieldSettings.ActiveCatalogPath = filePath;
                // Read-only status comes from the loaded catalog
                if (StarCatalogManager.ActiveCatalog != null)
                {
                    StarfieldSettings.IsReadOnly = StarCatalogManager.ActiveCatalog.IsReadOnly;
                }
                
                // Sync UI values from loaded catalog
                if (StarCatalogManager.ActiveCatalog != null)
                {
                    // Always sync rotation (available for both procedural and intentional)
                    _rotationX = StarCatalogManager.ActiveCatalog.RotationX;
                    _rotationY = StarCatalogManager.ActiveCatalog.RotationY;
                    _rotationZ = StarCatalogManager.ActiveCatalog.RotationZ;
                    StarfieldSettings.RotationX = _rotationX;
                    StarfieldSettings.RotationY = _rotationY;
                    StarfieldSettings.RotationZ = _rotationZ;
                    
                    // Only sync generation params for procedural catalogs
                    // Intentional catalogs (real sky) have meaningless generation params
                    if (StarCatalogManager.ActiveCatalog.IsProcedural)
                    {
                        _catalogSeed = StarCatalogManager.ActiveCatalog.GenerationSeed;
                        StarfieldSettings.CatalogSeed = _catalogSeed;
                        
                        _minMagnitude = StarCatalogManager.ActiveCatalog.MinMagnitude;
                        _maxMagnitude = StarCatalogManager.ActiveCatalog.MaxMagnitude;
                        _magnitudeBias = StarCatalogManager.ActiveCatalog.MagnitudeBias;
                        _clustering = StarCatalogManager.ActiveCatalog.Clustering;
                        _populationBias = StarCatalogManager.ActiveCatalog.PopulationBias;
                        _mainSequenceStrength = StarCatalogManager.ActiveCatalog.MainSequenceStrength;
                        _redGiantFrequency = StarCatalogManager.ActiveCatalog.RedGiantFrequency;
                        _galacticFlatness = StarCatalogManager.ActiveCatalog.GalacticFlatness;
                        StarfieldSettings.MinMagnitude = _minMagnitude;
                        StarfieldSettings.MaxMagnitude = _maxMagnitude;
                        StarfieldSettings.MagnitudeBias = _magnitudeBias;
                        StarfieldSettings.Clustering = _clustering;
                        StarfieldSettings.PopulationBias = _populationBias;
                        StarfieldSettings.MainSequenceStrength = _mainSequenceStrength;
                        StarfieldSettings.RedGiantFrequency = _redGiantFrequency;
                        StarfieldSettings.GalacticFlatness = _galacticFlatness;
                    }
                    
                    // Sync tracking vars for ALL catalogs (procedural and intentional)
                    // This clears the regeneration flag so scene changes don't regenerate
                    StarfieldSettings.SyncTrackingVars();
                }
                
                // Mark for reload so native plugin processes the catalog
                // This is needed to ensure the catalog is properly registered
                StarfieldSettings.InvalidateCatalogForReload();
                PushSettingsToNative();
            }
        }

        private void PushSettingsToNative()
        {
            StarfieldSettings.Exposure = _exposure;
            StarfieldSettings.BlurPixels = _blurPixels;
            StarfieldSettings.MinMagnitude = _minMagnitude;
            StarfieldSettings.MaxMagnitude = _maxMagnitude;
            StarfieldSettings.MagnitudeBias = _magnitudeBias;
            StarfieldSettings.HeroCount = _heroCount;
            StarfieldSettings.Clustering = _clustering;
            StarfieldSettings.PopulationBias = _populationBias;
            StarfieldSettings.MainSequenceStrength = _mainSequenceStrength;
            StarfieldSettings.RedGiantFrequency = _redGiantFrequency;
            StarfieldSettings.GalacticFlatness = _galacticFlatness;
            StarfieldSettings.GalacticDiscFalloff = _galacticDiscFalloff;
            StarfieldSettings.BandCenterBoost = _bandCenterBoost;
            StarfieldSettings.BandCoreSharpness = _bandCoreSharpness;
            StarfieldSettings.BulgeIntensity = _bulgeIntensity;
            StarfieldSettings.BulgeWidth = _bulgeWidth;
            StarfieldSettings.BulgeHeight = _bulgeHeight;
            StarfieldSettings.BulgeSoftness = _bulgeSoftness;
            StarfieldSettings.BulgeNoiseScale = _bulgeNoiseScale;
            StarfieldSettings.BulgeNoiseStrength = _bulgeNoiseStrength;
            StarfieldSettings.BloomThreshold = _bloomThreshold;
            StarfieldSettings.BloomIntensity = _bloomIntensity;
            StarfieldSettings.ColorSaturation = _colorSaturation;
            StarfieldSettings.RotationX = _rotationX;
            StarfieldSettings.RotationY = _rotationY;
            StarfieldSettings.RotationZ = _rotationZ;

            StarfieldSettings.PushSettingsToNative();
        }

        private void ResetToDefaults()
        {
            // Reset all UI fields to match StarfieldSettings default values
            _exposure = 3.0f;
            _blurPixels = 0.00029f;  // 1.0 arcminute (minimum)
            
            _minMagnitude = -1.0f;
            _maxMagnitude = 10.0f;
            _magnitudeBias = 0.25f;  // Closer to real HYG distribution
            _heroCount = 128;
            _clustering = 0.6f;
            _populationBias = 0.0f;
            _mainSequenceStrength = 0.8f;  // Default: mostly realistic
            _redGiantFrequency = 0.05f;    // Default: ~5% red giants
            
            _galacticFlatness = 0.85f;
            _galacticDiscFalloff = 3.0f;
            _bandCenterBoost = 0.0f;
            _bandCoreSharpness = 20.0f;
            _bulgeIntensity = 5.0f;
            _bulgeWidth = 0.5f;
            _bulgeHeight = 0.5f;
            _bulgeSoftness = 0.0f;
            _bulgeNoiseScale = 20.0f;
            _bulgeNoiseStrength = 0.0f;
            
            _bloomThreshold = 0.08f;  // Displays as 8.0
            _bloomIntensity = 0.5f;  // Displays as 1.0
            _colorSaturation = 1.0f;
            _catalogSize = 50000;  // Default: 50k stars
            
            // Reset rotation to 0 (no rotation for new catalogs)
            _rotationX = 0.0f;
            _rotationY = 0.0f;
            _rotationZ = 0.0f;
        }
    }
}