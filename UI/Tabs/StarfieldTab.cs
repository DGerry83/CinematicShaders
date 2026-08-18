using CinematicShaders.Core;
using CinematicShaders.Native;
using CinematicShaders.Shaders.Starfield;
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CinematicShaders.UI.Tabs
{
    public class StarfieldTab
    {
        // Generation slider throttling: prevents expensive catalog regeneration spam during dragging
        // while ensuring deterministic final updates (no "lost" updates on rapid release)
        private const float GENERATION_THROTTLE_SECONDS = 0.05f;
        private float _lastGenerationPushTime = -999f;
        private bool _generationPushPending = false;

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
        private float _extinctionFactor;
        private float _dimmingFactor;
        private bool _useSoftBloom;
        private bool _restoreOriginalSkyboxOnDisable;
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
        private bool _showAdvancedGenerationSection = false;

        public StarfieldTab()
        {
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
            _extinctionFactor = StarfieldSettings.ExtinctionFactor;
            _dimmingFactor = StarfieldSettings.DimmingFactor;
            _useSoftBloom = StarfieldSettings.UseSoftBloom;
            _restoreOriginalSkyboxOnDisable = StarfieldSettings.RestoreOriginalSkyboxOnDisable;
            _catalogSeed = StarfieldSettings.CatalogSeed;
            _catalogSize = StarfieldSettings.CatalogSize;
            _rotationX = StarfieldSettings.RotationX;
            _rotationY = StarfieldSettings.RotationY;
            _rotationZ = StarfieldSettings.RotationZ;
        }

        public void Draw()
        {
            // Check for pending generation updates (catches final values after rapid drag-and-release)
            if (_generationPushPending && Time.time - _lastGenerationPushTime >= GENERATION_THROTTLE_SECONDS)
            {
                PushSettingsToNative();
                _lastGenerationPushTime = Time.time;
                _generationPushPending = false;
            }

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
                DrawCatalogSection();
                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                _showRenderingSection = GUILayout.Toggle(_showRenderingSection, CinematicShadersUIStrings.Starfield.RenderingSection, HighLogic.Skin.label);
                if (_showRenderingSection)
                {
                    DrawEnableToggle(oldEnabled);

                    bool newRestore = GUILayout.Toggle(_restoreOriginalSkyboxOnDisable,
                        new GUIContent(CinematicShadersUIStrings.Starfield.RestoreOriginalSkyboxOnDisableToggle,
                            CinematicShadersUIStrings.Starfield.RestoreOriginalSkyboxOnDisableTooltip),
                        HighLogic.Skin.toggle);
                    if (newRestore != _restoreOriginalSkyboxOnDisable)
                    {
                        _restoreOriginalSkyboxOnDisable = newRestore;
                        StarfieldSettings.RestoreOriginalSkyboxOnDisable = newRestore;
                    }

                    if (!StarfieldSettings.EnableStarfield)
                        GUI.enabled = false;

                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.ExposureLabel, ref _exposure, -2.0f, 8.0f, "F1",
                        CinematicShadersUIStrings.Starfield.ExposureTooltip);

                    float blurArcminutes = _blurPixels * 3437.75f;
                    blurArcminutes = Mathf.Clamp(blurArcminutes, 1.0f, 2.0f);
                    float prevBlurArcminutes = blurArcminutes;
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.BlurPixelsLabel, ref blurArcminutes, 1.0f, 2.0f, "F1",
                        CinematicShadersUIStrings.Starfield.BlurPixelsTooltip);
                    _blurPixels = blurArcminutes / 3437.75f;
                    if (!Mathf.Approximately(blurArcminutes, prevBlurArcminutes))
                        PushSettingsToNative();

                    float bloomThresholdDisplay = _bloomThreshold * 100.0f;
                    float prevBloomThresholdDisplay = bloomThresholdDisplay;
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.BloomThresholdLabel, ref bloomThresholdDisplay, 0.0f, 10.0f, "F1",
                        CinematicShadersUIStrings.Starfield.BloomThresholdTooltip);
                    _bloomThreshold = bloomThresholdDisplay / 100.0f;
                    if (!Mathf.Approximately(bloomThresholdDisplay, prevBloomThresholdDisplay))
                        PushSettingsToNative();

                    float bloomIntensityDisplay = Mathf.Sqrt(_bloomIntensity * 2.0f);
                    float prevBloomIntensityDisplay = bloomIntensityDisplay;
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.BloomIntensityLabel, ref bloomIntensityDisplay, 0.0f, 2.0f, "F2",
                        CinematicShadersUIStrings.Starfield.BloomIntensityTooltip);
                    _bloomIntensity = (bloomIntensityDisplay * bloomIntensityDisplay) * 0.5f;
                    if (!Mathf.Approximately(bloomIntensityDisplay, prevBloomIntensityDisplay))
                        PushSettingsToNative();

                    GUILayout.BeginHorizontal();
                    GUIContent labelContent = new GUIContent(CinematicShadersUIStrings.Starfield.BloomIsotropyLabel, CinematicShadersUIStrings.Starfield.BloomIsotropyTooltip);
                    GUILayout.Label(labelContent, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

                    bool useClassic = !_useSoftBloom;
                    bool useSoft = _useSoftBloom;

                    // Detect which button was actually clicked by comparing return value with passed value
                    bool newClassic = GUILayout.Toggle(useClassic, CinematicShadersUIStrings.Starfield.BloomModeClassic, "button");
                    bool newSoft = GUILayout.Toggle(useSoft, CinematicShadersUIStrings.Starfield.BloomModeSoft, "button");

                    // If Classic was clicked (changed from inactive to active)
                    if (newClassic && !useClassic)
                    {
                        _useSoftBloom = false;
                        PushSettingsToNative();
                    }
                    // If Soft was clicked (changed from inactive to active)
                    else if (newSoft && !useSoft)
                    {
                        _useSoftBloom = true;
                        PushSettingsToNative();
                    }
                    GUILayout.EndHorizontal();

                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.ExtinctionFactorLabel, ref _extinctionFactor, 0.0f, 2.0f, "F2",
                        CinematicShadersUIStrings.Starfield.ExtinctionFactorTooltip);
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.DimmingFactorLabel, ref _dimmingFactor, 0.0f, 2.0f, "F2",
                        CinematicShadersUIStrings.Starfield.DimmingFactorTooltip);

                    GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.TIGHT);
                }

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                _showMainGenerationSection = GUILayout.Toggle(_showMainGenerationSection, CinematicShadersUIStrings.Starfield.MainGenerationSection, HighLogic.Skin.label);
                if (_showMainGenerationSection)
                {
                    bool wasEnabled = GUI.enabled;
                    bool isIntentional = StarCatalogManager.ActiveCatalog != null && !StarCatalogManager.ActiveCatalog.IsProcedural;

                    if (StarfieldSettings.IsReadOnly || isIntentional)
                    {
                        GUI.enabled = false;
                        if (isIntentional)
                        {
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
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.MinMagnitudeLabel, ref _minMagnitude, -2.0f, 3.0f, "F1");
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.MaxMagnitudeLabel, ref _maxMagnitude, 5.0f, 12.0f, "F1");
                    DrawIntSlider(CinematicShadersUIStrings.Starfield.HeroCountLabel, ref _heroCount, 16, 1024,
                        CinematicShadersUIStrings.Starfield.HeroCountTooltip);
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.MainSequenceLabel, ref _mainSequenceStrength, 0.0f, 1.0f, "F2",
                        CinematicShadersUIStrings.Starfield.MainSequenceTooltip);
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.RedGiantFrequencyLabel, ref _redGiantFrequency, 0.0f, 1.0f, "F2");
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.ColorSaturationLabel, ref _colorSaturation, 0.5f, 4.0f, "F2",
                        CinematicShadersUIStrings.Starfield.ColorSaturationTooltip);

                    GUI.enabled = wasEnabled;
                }

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                _showAdvancedGenerationSection = GUILayout.Toggle(_showAdvancedGenerationSection,
                    CinematicShadersUIStrings.Common.CollapsedPrefix + CinematicShadersUIStrings.Starfield.AdvancedGenerationSection, HighLogic.Skin.label);
                if (_showAdvancedGenerationSection)
                {
                    bool isIntentional = StarCatalogManager.ActiveCatalog != null && !StarCatalogManager.ActiveCatalog.IsProcedural;

                    GUILayout.Label(CinematicShadersUIStrings.Starfield.CoordinateRotationSection, HighLogic.Skin.label);
                    if (StarfieldSettings.IsReadOnly)
                        GUI.enabled = false;
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.RotationXLabel, ref _rotationX, 0.0f, 360.0f, "F1",
                        CinematicShadersUIStrings.Starfield.RotationTooltip, "°");
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.RotationYLabel, ref _rotationY, 0.0f, 360.0f, "F1",
                        CinematicShadersUIStrings.Starfield.RotationTooltip, "°");
                    DrawRenderingSlider(CinematicShadersUIStrings.Starfield.RotationZLabel, ref _rotationZ, 0.0f, 360.0f, "F1",
                        CinematicShadersUIStrings.Starfield.RotationTooltip, "°");

                    if (StarfieldSettings.IsReadOnly || isIntentional)
                        GUI.enabled = false;

                    GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.TIGHT);
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.BrightnessDistributionLabel, ref _magnitudeBias, 0.02f, 0.5f, "F2");
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.StellarPopulationLabel, ref _populationBias, -1.0f, 1.0f, "F2",
                        CinematicShadersUIStrings.Starfield.StellarPopulationTooltip);
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.ClusteringLabel, ref _clustering, 0.0f, 1.0f, "F2");

                    GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.TIGHT);
                    GUILayout.Label(CinematicShadersUIStrings.Starfield.GalacticStructureSection, HighLogic.Skin.label);
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.DiscFlatnessLabel, ref _galacticFlatness, 0.0f, 1.0f, "F2");
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.DiscFalloffLabel, ref _galacticDiscFalloff, 0.5f, 10.0f, "F1");
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.BandCenterBoostLabel, ref _bandCenterBoost, 0.0f, 10.0f, "F1");
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.BandCoreSharpnessLabel, ref _bandCoreSharpness, 1.0f, 50.0f, "F0");
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.BulgeIntensityLabel, ref _bulgeIntensity, 0.0f, 20.0f, "F1");
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.BulgeWidthLabel, ref _bulgeWidth, 0.01f, 1.57f, "F2");
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.BulgeHeightLabel, ref _bulgeHeight, 0.01f, 1.0f, "F2");
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.BulgeSoftnessLabel, ref _bulgeSoftness, 0.0f, 1.0f, "F2");
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.BulgeNoiseScaleLabel, ref _bulgeNoiseScale, 0.0f, 100.0f, "F1");
                    DrawGenerationSlider(CinematicShadersUIStrings.Starfield.BulgeNoiseStrengthLabel, ref _bulgeNoiseStrength, 0.0f, 1.0f, "F2");
                }
            }
            finally
            {
                GUI.enabled = oldEnabled;
            }
            DrawTooltip();
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
                        StarfieldSettings.InvalidateCatalog();
                    StarfieldManager.OnToggleChanged();
                }
            }
            finally
            {
                GUI.enabled = localEnabled;
            }
        }

        /// <summary>
        /// Generation slider: throttled to ~6-7 updates/second max. Updates immediately if throttle window 
        /// is open, otherwise queues for next frame. Guarantees final value is pushed via pending check in Draw().
        /// </summary>
        private void DrawGenerationSlider(string label, ref float value, float min, float max, string format, string tooltip = null, string suffix = "")
        {
            GUILayout.BeginHorizontal();
            GUIContent labelContent = new GUIContent(label, tooltip);
            GUILayout.Label(labelContent, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            float newValue = GUILayout.HorizontalSlider(value, min, max,
                GUILayout.Width(CinematicShadersUIResources.Layout.Labels.SLIDER_WIDTH));

            GUILayout.Label(newValue.ToString(format) + suffix, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.VALUE_WIDTH));
            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(newValue, value))
            {
                value = newValue;
                StarfieldSettings.InvalidateCatalog();
                _generationPushPending = true;

                // Immediate push if throttle window open, else defer to frame check
                if (Time.time - _lastGenerationPushTime >= GENERATION_THROTTLE_SECONDS)
                {
                    PushSettingsToNative();
                    _lastGenerationPushTime = Time.time;
                    _generationPushPending = false;
                }
            }
        }

        /// <summary>
        /// Rendering slider: immediate updates (cheap, no catalog invalidation).
        /// </summary>
        private void DrawRenderingSlider(string label, ref float value, float min, float max, string format, string tooltip = null, string suffix = "")
        {
            GUILayout.BeginHorizontal();
            GUIContent labelContent = new GUIContent(label, tooltip);
            GUILayout.Label(labelContent, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            float newValue = GUILayout.HorizontalSlider(value, min, max,
                GUILayout.Width(CinematicShadersUIResources.Layout.Labels.SLIDER_WIDTH));

            GUILayout.Label(newValue.ToString(format) + suffix, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.VALUE_WIDTH));
            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(newValue, value))
            {
                value = newValue;
                PushSettingsToNative();
            }
        }

        /// <summary>
        /// Integer generation slider: throttled like DrawGenerationSlider.
        /// </summary>
        private void DrawIntSlider(string label, ref int value, int min, int max, string tooltip = null)
        {
            GUILayout.BeginHorizontal();
            GUIContent labelContent = new GUIContent(label, tooltip);
            GUILayout.Label(labelContent, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            float floatValue = value;
            float newValue = GUILayout.HorizontalSlider(floatValue, min, max, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.SLIDER_WIDTH));
            int newIntValue = Mathf.RoundToInt(newValue);

            GUILayout.Label(value.ToString(), GUILayout.Width(CinematicShadersUIResources.Layout.Labels.VALUE_WIDTH));
            GUILayout.EndHorizontal();

            if (newIntValue != value)
            {
                value = newIntValue;

                if (label == CinematicShadersUIStrings.Starfield.CatalogSeedLabel)
                    StarfieldSettings.CatalogSeed = value;
                else if (label == CinematicShadersUIStrings.Starfield.CatalogSizeLabel)
                    StarfieldSettings.CatalogSize = value;
                else if (label == CinematicShadersUIStrings.Starfield.HeroCountLabel)
                    StarfieldSettings.HeroCount = value;

                StarfieldSettings.InvalidateCatalog();
                _generationPushPending = true;

                if (Time.time - _lastGenerationPushTime >= GENERATION_THROTTLE_SECONDS)
                {
                    PushSettingsToNative();
                    _lastGenerationPushTime = Time.time;
                    _generationPushPending = false;
                }
            }
        }

        private void DrawCatalogSection()
        {
            GUILayout.Label(CinematicShadersUIStrings.Starfield.StarCatalogSection, HighLogic.Skin.label);

            GUILayout.BeginHorizontal();
            GUILayout.Label(CinematicShadersUIStrings.Starfield.ActiveCatalogLabel, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            string activeName = StarCatalogManager.ActiveCatalog != null ?
                StarCatalogManager.ActiveCatalog.GetDropdownLabel() :
                CinematicShadersUIStrings.Starfield.ActiveCatalogNone;

            if (GUILayout.Button(activeName + CinematicShadersUIStrings.Common.DropdownArrow, GUILayout.Width(200)))
            {
                _catalogDropdownOpen = !_catalogDropdownOpen;
                if (_catalogDropdownOpen)
                    RefreshCatalogList();
            }
            GUILayout.EndHorizontal();

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

            GUILayout.BeginHorizontal();
            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            toggleStyle.richText = true;

            string toggleLabel = StarfieldSettings.IsReadOnly ?
                CinematicShadersUIStrings.Starfield.ReadOnlyToggleOn :
                CinematicShadersUIStrings.Starfield.ReadOnlyToggleOff;

            bool newReadOnly = GUILayout.Toggle(StarfieldSettings.IsReadOnly, toggleLabel, toggleStyle, GUILayout.Width(220));

            if (newReadOnly != StarfieldSettings.IsReadOnly)
            {
                if (!newReadOnly && StarfieldSettings.IsReadOnly)
                    _showReadOnlyWarning = true;
                else
                    StarfieldSettings.IsReadOnly = true;
            }
            GUILayout.EndHorizontal();

            if (_showReadOnlyWarning)
            {
                GUILayout.Space(10);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(CinematicShadersUIStrings.Starfield.ReadOnlyWarningTitle, HighLogic.Skin.label);
                GUILayout.Label(CinematicShadersUIStrings.Starfield.ReadOnlyWarningMessage, CinematicShadersUIResources.Styles.Help());

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(CinematicShadersUIStrings.Starfield.CancelButton, GUILayout.Width(100)))
                    _showReadOnlyWarning = false;

                if (GUILayout.Button(CinematicShadersUIStrings.Starfield.UnlockButton, GUILayout.Width(150)))
                {
                    StarfieldSettings.IsReadOnly = false;
                    _showReadOnlyWarning = false;
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

            GUILayout.BeginHorizontal();

            GUI.enabled = StarCatalogManager.ActiveCatalog != null;
            if (GUILayout.Button(CinematicShadersUIStrings.Starfield.SaveButton, GUILayout.Width(70)))
            {
                if (StarCatalogManager.ActiveCatalog != null)
                {
                    StarCatalogManager.SaveCatalog(StarCatalogManager.ActiveCatalog.FilePath,
                        StarCatalogManager.ActiveCatalog.GetDisplayName(), StarfieldSettings.IsReadOnly);
                }
            }
            GUI.enabled = true;

            if (GUILayout.Button(CinematicShadersUIStrings.Starfield.NewButton, GUILayout.Width(60)))
            {
                ResetToDefaults();
                _catalogSeed = new System.Random().Next(0, 99999);
                StarfieldSettings.CatalogSeed = _catalogSeed;
                StarfieldSettings.IsReadOnly = false;
                StarfieldSettings.ActiveCatalogPath = "";
                StarCatalogManager.ActiveCatalog = null;
                StarfieldSettings.InvalidateCatalog();
                PushSettingsToNative();
                
                // CRITICAL: Clear StarCatalogStateManager to remove stale JSON data
                // Pass empty string to indicate no catalog / no JSON available
                StarCatalogStateManager.SetCatalog("");

                _showSaveAsDialog = true;
                _newFileName = CinematicShadersUIStrings.Starfield.DefaultCatalogFileName;
                _newCatalogName = CinematicShadersUIStrings.Starfield.DefaultCatalogDisplayName;
            }

            if (GUILayout.Button(CinematicShadersUIStrings.Starfield.SaveAsButton, GUILayout.Width(80)))
            {
                _showSaveAsDialog = true;
                _newFileName = StarCatalogManager.ActiveCatalog?.GetDisplayName() ?? CinematicShadersUIStrings.Starfield.DefaultCatalogFileName;
                _newCatalogName = StarCatalogManager.ActiveCatalog?.GetDisplayName() ?? CinematicShadersUIStrings.Starfield.DefaultCatalogDisplayName;
            }

            if (GUILayout.Button(CinematicShadersUIStrings.Starfield.OpenFolderButton, GUILayout.Width(90)))
                StarCatalogManager.OpenCatalogFolder();

            GUILayout.EndHorizontal();

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
                    _showSaveAsDialog = false;

                if (GUILayout.Button(CinematicShadersUIStrings.Starfield.SaveButton, GUILayout.Width(70)))
                {
                    string path = StarCatalogManager.SaveCatalogAs(_newFileName, _newCatalogName, false);
                    if (path != null)
                    {
                        StarCatalogManager.LoadCatalog(path);
                        StarfieldSettings.ActiveCatalogPath = path;
                        
                        // CRITICAL: Notify StarCatalogStateManager of new catalog
                        StarCatalogStateManager.SetCatalog(path);
                        
                        StarfieldSettings.IsReadOnly = false;
                    }
                    _showSaveAsDialog = false;
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.color = Color.red;
            if (GUILayout.Button(CinematicShadersUIStrings.Starfield.DeleteCatalogButton, GUILayout.Width(120)) && StarCatalogManager.ActiveCatalog != null)
            {
                StarCatalogManager.DeleteCatalog(StarCatalogManager.ActiveCatalog.FilePath);
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

        // No longer used - kept for compatibility
        public static void ResetThrottleTimer() { }

        private void LoadCatalog(string filePath)
        {
            if (!StarCatalogManager.LoadCatalog(filePath))
                return;

            StarfieldSettings.ActiveCatalogPath = filePath;
            
            // CRITICAL: Notify StarCatalogStateManager of catalog change
            StarCatalogStateManager.SetCatalog(filePath);
            
            if (StarCatalogManager.ActiveCatalog != null)
                StarfieldSettings.IsReadOnly = StarCatalogManager.ActiveCatalog.IsReadOnly;

            if (StarCatalogManager.ActiveCatalog != null)
            {
                _rotationX = StarCatalogManager.ActiveCatalog.RotationX;
                _rotationY = StarCatalogManager.ActiveCatalog.RotationY;
                _rotationZ = StarCatalogManager.ActiveCatalog.RotationZ;
                StarfieldSettings.RotationX = _rotationX;
                StarfieldSettings.RotationY = _rotationY;
                StarfieldSettings.RotationZ = _rotationZ;

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

                StarfieldSettings.SyncTrackingVars();
            }

            StarfieldSettings.InvalidateCatalogForReload();
            PushSettingsToNative();
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
            StarfieldSettings.ExtinctionFactor = _extinctionFactor;
            StarfieldSettings.DimmingFactor = _dimmingFactor;
            StarfieldSettings.UseSoftBloom = _useSoftBloom;
            StarfieldSettings.RestoreOriginalSkyboxOnDisable = _restoreOriginalSkyboxOnDisable;
            StarfieldSettings.RotationX = _rotationX;
            StarfieldSettings.RotationY = _rotationY;
            StarfieldSettings.RotationZ = _rotationZ;
            StarfieldSettings.PushSettingsToNative();
        }

        private void ResetToDefaults()
        {
            _exposure = 3.0f;
            _blurPixels = 0.00029f;
            _minMagnitude = -1.0f;
            _maxMagnitude = 10.0f;
            _magnitudeBias = 0.25f;
            _heroCount = 128;
            _clustering = 0.6f;
            _populationBias = 0.0f;
            _mainSequenceStrength = 0.8f;
            _redGiantFrequency = 0.05f;
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
            _bloomThreshold = 0.08f;
            _bloomIntensity = 0.5f;
            _colorSaturation = 1.0f;
            _useSoftBloom = false;
            _restoreOriginalSkyboxOnDisable = true;
            _catalogSize = 50000;
            _rotationX = 0.0f;
            _rotationY = 0.0f;
            _rotationZ = 0.0f;
        }
    }
}