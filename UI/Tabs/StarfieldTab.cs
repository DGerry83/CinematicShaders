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
                GUIStyle helpStyle = CinematicShadersUIResources.Styles.Help();

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

                    DrawSlider(CinematicShadersUIStrings.Starfield.ExposureLabel, ref _exposure, -2.0f, 8.0f, "F1");
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.ExposureTooltip, helpStyle);
                    
                    // BlurPixels is angular sigma in radians; display as arcminutes (1' = 1/60° ≈ 0.00029 rad)
                    float blurArcminutes = _blurPixels * 3437.75f;  // rad to arcmin (180*60/π)
                    DrawSlider(CinematicShadersUIStrings.Starfield.BlurPixelsLabel, ref blurArcminutes, 1.0f, 2.0f, "F1");
                    _blurPixels = blurArcminutes / 3437.75f;
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.BlurPixelsTooltip, helpStyle);

                    // Bloom threshold slider 0-10 maps to actual 0-0.1 for finer control
                    float bloomThresholdDisplay = _bloomThreshold * 100.0f;
                    DrawSlider(CinematicShadersUIStrings.Starfield.BloomThresholdLabel, ref bloomThresholdDisplay, 0.0f, 10.0f, "F1");
                    _bloomThreshold = bloomThresholdDisplay / 100.0f;
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.BloomThresholdTooltip, helpStyle);
                    
                    // Bloom intensity 0-2 with logarithmic mapping
                    float bloomIntensityDisplay = Mathf.Sqrt(_bloomIntensity * 2.0f);
                    DrawSlider(CinematicShadersUIStrings.Starfield.BloomIntensityLabel, ref bloomIntensityDisplay, 0.0f, 2.0f, "F2");
                    _bloomIntensity = (bloomIntensityDisplay * bloomIntensityDisplay) * 0.5f;
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.BloomIntensityTooltip, helpStyle);
                    
                    // Color saturation slider: 0.5-4.0 range
                    DrawSlider(CinematicShadersUIStrings.Starfield.ColorSaturationLabel, ref _colorSaturation, 0.5f, 4.0f, "F2");
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.ColorSaturationTooltip, helpStyle);
                }

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                // === MAIN GENERATION SECTION ===
                _showMainGenerationSection = GUILayout.Toggle(_showMainGenerationSection, CinematicShadersUIStrings.Starfield.MainGenerationSection, HighLogic.Skin.label);
                if (_showMainGenerationSection)
                {
                    // Disable generation sliders if read-only
                    bool wasEnabled = GUI.enabled;
                    if (StarfieldSettings.IsReadOnly)
                    {
                        GUI.enabled = false;
                        GUILayout.Label(CinematicShadersUIStrings.Starfield.ReadOnlyLockMessage, CinematicShadersUIResources.Styles.Help());
                    }

                    DrawIntSlider(CinematicShadersUIStrings.Starfield.CatalogSeedLabel, ref _catalogSeed, 0, 99999);
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.CatalogSeedTooltip, helpStyle);

                    DrawIntSlider(CinematicShadersUIStrings.Starfield.CatalogSizeLabel, ref _catalogSize, 1000, 100000);
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.CatalogSizeTooltip, helpStyle);

                    DrawSlider(CinematicShadersUIStrings.Starfield.MinMagnitudeLabel, ref _minMagnitude, -2.0f, 3.0f, "F1");
                    DrawSlider(CinematicShadersUIStrings.Starfield.MaxMagnitudeLabel, ref _maxMagnitude, 5.0f, 12.0f, "F1");
                    
                    DrawIntSlider(CinematicShadersUIStrings.Starfield.HeroCountLabel, ref _heroCount, 16, 1024);
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.HeroCountTooltip, helpStyle);
                    
                    DrawSlider(CinematicShadersUIStrings.Starfield.MainSequenceLabel, ref _mainSequenceStrength, 0.0f, 1.0f, "F2");
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.MainSequenceTooltip, helpStyle);
                    
                    DrawSlider(CinematicShadersUIStrings.Starfield.RedGiantFrequencyLabel, ref _redGiantFrequency, 0.0f, 1.0f, "F2");
                    
                    GUI.enabled = wasEnabled;
                }

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                // === ADVANCED GENERATION SECTION (collapsed by default) ===
                _showAdvancedGenerationSection = GUILayout.Toggle(_showAdvancedGenerationSection, "▶ " + CinematicShadersUIStrings.Starfield.AdvancedGenerationSection, HighLogic.Skin.label);
                if (_showAdvancedGenerationSection)
                {
                    // Disable generation sliders if read-only
                    bool wasEnabled = GUI.enabled;
                    if (StarfieldSettings.IsReadOnly)
                    {
                        GUI.enabled = false;
                    }

                    DrawSlider(CinematicShadersUIStrings.Starfield.BrightnessDistributionLabel, ref _magnitudeBias, 0.02f, 0.5f, "F2");
                    DrawSlider(CinematicShadersUIStrings.Starfield.StellarPopulationLabel, ref _populationBias, -1.0f, 1.0f, "F2");
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.StellarPopulationTooltip, helpStyle);
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
                    
                    GUI.enabled = wasEnabled;
                }
            }
            finally
            {
                GUI.enabled = oldEnabled;
            }
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

        private void DrawSlider(string label, ref float value, float min, float max, string format, string suffix = "")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            float newValue = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.SLIDER_WIDTH));
            string displayText = value.ToString(format) + suffix;
            GUILayout.Label(displayText, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.VALUE_WIDTH));

            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(newValue, value))
            {
                value = newValue;
                StarfieldSettings.InvalidateCatalog();
                PushSettingsToNative();
            }
        }

        private void DrawIntSlider(string label, ref int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            float floatValue = value;
            float newValue = GUILayout.HorizontalSlider(floatValue, min, max, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.SLIDER_WIDTH));
            string displayText = value.ToString();
            GUILayout.Label(displayText, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.VALUE_WIDTH));

            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(newValue, floatValue))
            {
                value = Mathf.RoundToInt(newValue);
                // Trigger regeneration when catalog params change
                StarfieldSettings.CatalogSeed = _catalogSeed;
                StarfieldSettings.CatalogSize = _catalogSize;
                StarfieldSettings.InvalidateCatalog();
                PushSettingsToNative();
            }
        }

        private void DrawCatalogSection()
        {
            GUILayout.Label("Star Catalog", HighLogic.Skin.label);
            
            // Active catalog dropdown
            GUILayout.BeginHorizontal();
            GUILayout.Label("Active Catalog", GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));
            
            string activeName = "(None)";
            if (StarCatalogManager.ActiveCatalog != null)
                activeName = StarCatalogManager.ActiveCatalog.GetDropdownLabel();
            
            if (GUILayout.Button(activeName + " ▼", GUILayout.Width(200)))
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
            
            // Read-Only toggle with glowing style
            GUILayout.BeginHorizontal();
            GUIStyle toggleStyle = StarfieldSettings.IsReadOnly ? 
                CinematicShadersUIResources.Styles.ToggleActive() : HighLogic.Skin.toggle;
            
            bool newReadOnly = GUILayout.Toggle(StarfieldSettings.IsReadOnly, 
                StarfieldSettings.IsReadOnly ? "🔒 Read-Only Protection ON" : "Generation Active", 
                toggleStyle, GUILayout.Width(220));
            
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
                GUILayout.Label("⚠️ WARNING: Disabling Read-Only Protection", HighLogic.Skin.label);
                GUILayout.Label("You are about to unlock this catalog for editing. Any changes to generation parameters will PERMANENTLY modify this catalog. This cannot be undone.", CinematicShadersUIResources.Styles.Help());
                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Cancel", GUILayout.Width(100)))
                {
                    _showReadOnlyWarning = false;
                }
                if (GUILayout.Button("I Understand - Unlock", GUILayout.Width(150)))
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
            
            // Save button (disabled if read-only)
            GUI.enabled = !StarfieldSettings.IsReadOnly && StarCatalogManager.ActiveCatalog != null;
            if (GUILayout.Button("Save", GUILayout.Width(70)))
            {
                if (StarCatalogManager.ActiveCatalog != null)
                {
                    StarCatalogManager.SaveCatalog(StarCatalogManager.ActiveCatalog.FilePath,
                        StarCatalogManager.ActiveCatalog.GetDisplayName(), false);
                }
            }
            GUI.enabled = true;
            
            // New button (generate fresh catalog)
            if (GUILayout.Button("New", GUILayout.Width(60)))
            {
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
                _newFileName = "MyStarfield";
                _newCatalogName = "My Starfield";
            }
            
            // Save As button
            if (GUILayout.Button("Save As...", GUILayout.Width(80)))
            {
                _showSaveAsDialog = true;
                _newFileName = StarCatalogManager.ActiveCatalog?.GetDisplayName() ?? "MyStarfield";
                _newCatalogName = StarCatalogManager.ActiveCatalog?.GetDisplayName() ?? "My Starfield";
            }
            
            // Open Folder button
            if (GUILayout.Button("Open Folder", GUILayout.Width(90)))
            {
                StarCatalogManager.OpenCatalogFolder();
            }
            
            GUILayout.EndHorizontal();
            
            // Save As Dialog
            if (_showSaveAsDialog)
            {
                GUILayout.Space(10);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Save Catalog As:", HighLogic.Skin.label);
                
                GUILayout.Label("Filename:");
                _newFileName = GUILayout.TextField(_newFileName, GUILayout.Width(250));
                
                GUILayout.Label("Display Name:");
                _newCatalogName = GUILayout.TextField(_newCatalogName, GUILayout.Width(250));
                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Cancel", GUILayout.Width(70)))
                {
                    _showSaveAsDialog = false;
                }
                if (GUILayout.Button("Save", GUILayout.Width(70)))
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
            if (GUILayout.Button("Delete Catalog", GUILayout.Width(120)) && StarCatalogManager.ActiveCatalog != null)
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
                
                // Sync UI values from loaded catalog's generation params
                if (StarCatalogManager.ActiveCatalog != null)
                {
                    _catalogSeed = StarCatalogManager.ActiveCatalog.GenerationSeed;
                    StarfieldSettings.CatalogSeed = _catalogSeed;
                    // Reload other params if stored in catalog
                }
                
                StarfieldSettings.InvalidateCatalog();
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

            StarfieldSettings.PushSettingsToNative();
        }
    }
}