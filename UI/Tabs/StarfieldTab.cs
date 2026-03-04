using CinematicShaders.Core;
using CinematicShaders.Native;
using CinematicShaders.Shaders.Starfield;
using UnityEngine;

namespace CinematicShaders.UI.Tabs
{
    public class StarfieldTab
    {
        // Rendering
        private float _exposure;
        private float _blurPixels;

        // Distribution
        private float _starDensity;
        private float _minMagnitude;
        private float _maxMagnitude;
        private float _magnitudeBias;
        private float _heroRarity;
        private float _clustering;
        private float _staggerAmount;
        private float _populationBias;
        private float _mainSequenceStrength;
        private float _redGiantRarity;

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
        private float _spikeIntensity;

        private bool _initialized = false;

        public StarfieldTab()
        {
            // Initialize from settings
            _exposure = StarfieldSettings.Exposure;
            _blurPixels = StarfieldSettings.BlurPixels;
            _starDensity = StarfieldSettings.StarDensity;
            _minMagnitude = StarfieldSettings.MinMagnitude;
            _maxMagnitude = StarfieldSettings.MaxMagnitude;
            _magnitudeBias = StarfieldSettings.MagnitudeBias;
            _heroRarity = StarfieldSettings.HeroRarity;
            _clustering = StarfieldSettings.Clustering;
            _staggerAmount = StarfieldSettings.StaggerAmount;
            _populationBias = StarfieldSettings.PopulationBias;
            _mainSequenceStrength = StarfieldSettings.MainSequenceStrength;
            _redGiantRarity = StarfieldSettings.RedGiantRarity;
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
            _spikeIntensity = StarfieldSettings.SpikeIntensity;
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

                GUILayout.Label(CinematicShadersUIStrings.Starfield.RenderingSection, HighLogic.Skin.label);

                DrawEnableToggle(oldEnabled);

                if (!StarfieldSettings.EnableStarfield)
                    GUI.enabled = false;

                DrawSlider(CinematicShadersUIStrings.Starfield.ExposureLabel, ref _exposure, -2.0f, 8.0f, "F1");
                GUILayout.Label(CinematicShadersUIStrings.Starfield.ExposureTooltip, helpStyle);
                DrawSlider(CinematicShadersUIStrings.Starfield.BlurPixelsLabel, ref _blurPixels, 1.0f, 3.0f, "F1");

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                // Beauty
                GUILayout.Label(CinematicShadersUIStrings.Starfield.BeautySection, HighLogic.Skin.label);
                DrawSlider(CinematicShadersUIStrings.Starfield.BloomThresholdLabel, ref _bloomThreshold, 0.0f, 0.5f, "F3");
                GUILayout.Label(CinematicShadersUIStrings.Starfield.BloomThresholdTooltip, helpStyle);
                DrawSlider(CinematicShadersUIStrings.Starfield.BloomIntensityLabel, ref _bloomIntensity, 0.0f, 5.0f, "F2");
                GUILayout.Label(CinematicShadersUIStrings.Starfield.BloomIntensityTooltip, helpStyle);
                DrawSlider(CinematicShadersUIStrings.Starfield.SpikeIntensityLabel, ref _spikeIntensity, 0.0f, 1.0f, "F2");
                GUILayout.Label(CinematicShadersUIStrings.Starfield.SpikeIntensityTooltip, helpStyle);

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);
                // Distribution
                GUILayout.Label(CinematicShadersUIStrings.Starfield.DistributionSection, HighLogic.Skin.label);
                DrawSlider(CinematicShadersUIStrings.Starfield.StarDensityLabel, ref _starDensity, 50.0f, 400.0f, "F0");
                DrawSlider(CinematicShadersUIStrings.Starfield.MinMagnitudeLabel, ref _minMagnitude, -2.0f, 3.0f, "F1");
                DrawSlider(CinematicShadersUIStrings.Starfield.MaxMagnitudeLabel, ref _maxMagnitude, 5.0f, 12.0f, "F1");
                DrawSlider(CinematicShadersUIStrings.Starfield.MagnitudeBiasLabel, ref _magnitudeBias, 0.02f, 0.5f, "F2");
                DrawSlider(CinematicShadersUIStrings.Starfield.HeroRarityLabel, ref _heroRarity, 0.001f, 0.5f, "F3");
                DrawSlider(CinematicShadersUIStrings.Starfield.ClusteringLabel, ref _clustering, 0.0f, 1.0f, "F2");
                DrawSlider(CinematicShadersUIStrings.Starfield.StaggerAmountLabel, ref _staggerAmount, 0.0f, 5.0f, "F1");
                DrawSlider(CinematicShadersUIStrings.Starfield.PopulationBiasLabel, ref _populationBias, -1.0f, 1.0f, "F2");
                GUILayout.Label(CinematicShadersUIStrings.Starfield.PopulationBiasTooltip, helpStyle);
                DrawSlider(CinematicShadersUIStrings.Starfield.MainSequenceLabel, ref _mainSequenceStrength, 0.0f, 1.0f, "F2");
                DrawSlider(CinematicShadersUIStrings.Starfield.RedGiantRarityLabel, ref _redGiantRarity, 0.0f, 0.5f, "F2");

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);
                // Structure
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
                PushSettingsToNative();
            }
        }

        private void PushSettingsToNative()
        {
            StarfieldSettings.Exposure = _exposure;
            StarfieldSettings.BlurPixels = _blurPixels;
            StarfieldSettings.StarDensity = _starDensity;
            StarfieldSettings.MinMagnitude = _minMagnitude;
            StarfieldSettings.MaxMagnitude = _maxMagnitude;
            StarfieldSettings.MagnitudeBias = _magnitudeBias;
            StarfieldSettings.HeroRarity = _heroRarity;
            StarfieldSettings.Clustering = _clustering;
            StarfieldSettings.StaggerAmount = _staggerAmount;
            StarfieldSettings.PopulationBias = _populationBias;
            StarfieldSettings.MainSequenceStrength = _mainSequenceStrength;
            StarfieldSettings.RedGiantRarity = _redGiantRarity;
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
            StarfieldSettings.SpikeIntensity = _spikeIntensity;

            StarfieldSettings.PushSettingsToNative();
        }
    }
}