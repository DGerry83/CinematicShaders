using CinematicShaders.Core;
using CinematicShaders.Native;
using CinematicShaders.Shaders.GTAO;
using UnityEngine;

namespace CinematicShaders.UI.Tabs
{
    public class GTAOTab
    {
        // Quality presets
        private readonly int[] kSlicePresets = { 2, 3, 4, 6 };
        private readonly int[] kStepPresets = { 4, 8, 12, 16 };
        private readonly string[] kQualityNames = {
            CinematicShadersUIStrings.GTAO.QualityLow,
            CinematicShadersUIStrings.GTAO.QualityMedium,
            CinematicShadersUIStrings.GTAO.QualityHigh,
            CinematicShadersUIStrings.GTAO.QualityUltra
        };

        // State
        private int _qualityPresetIndex = 1;
        private float _radius = 2.0f;
        private float _intensity = 0.8f;
        private int _distributionCurve = 1;
        private float _edgeSharpness = 32.0f;
        private float _depthTolerance = 0.5f;
        private float _maxPixelRadius = 50.0f;
        private float _fadeStartDistance = 0.0f;
        private float _fadeEndDistance = 500.0f;
        private float _fadeCurve = 1.0f;
        private bool _initialized = false;

        private int _currentDebugMode = 0; // 0=None, 1=RawAO, 2=WorldNorm, 3=ViewNorm, 4=NormAlpha

        private int GetCurrentDebugMode()
        {
            return _currentDebugMode;
        }

        private void SetDebugMode(int mode)
        {
            _currentDebugMode = mode;
            GTAOSettings.DebugVisualizationMode = mode;
            // Mode mapping: 0=Composite, 1=Raw AO, 2=World Normals, 3=View Normals, 4=Normal Alpha
            GTAONative.CR_GTAOSetOutputMode(mode);

            // If enabling debug view, ensure GTAO is effectively "enabled" so render runs
            if (mode > 0 && !GTAOSettings.EnableGTAO)
            {
                GTAOManager.EnableDebugMode();
            }
        }

        public void Draw()
        {
            // Check if native DLL loaded
            if (!GTAONative.IsLoaded)
            {
                GUILayout.Space(20);
                GUIStyle errorStyle = new GUIStyle(HighLogic.Skin.label);
                errorStyle.normal.textColor = Color.red;
                errorStyle.wordWrap = true;
                GUILayout.Label("Native plugin failed to load. Check KSP.log for details.", errorStyle);
                return;
            }

            // Initialize on first draw when we're sure native plugin is ready
            if (!_initialized)
            {
                PushSettingsToNative();
                _initialized = true;
            }

            bool isDeferred = IsDeferredRenderingActive();
            bool oldEnabled = GUI.enabled;

            try
            {
                if (!isDeferred)
                    GUI.enabled = false;

                GUIStyle smallHelp = CinematicShadersUIResources.Styles.SmallHelp();

                // DEBUG VISUALIZATION SECTION
                // Comment out the line below to disable debug visualization UI in release builds
                DrawDebugSection();

                // SAMPLING SECTION
                GUILayout.Label(CinematicShadersUIStrings.GTAO.SamplingSection, HighLogic.Skin.label);

                DrawQualityDropdown();
                DrawSlider(CinematicShadersUIStrings.GTAO.RadiusLabel, ref _radius, 0.5f, 10.0f, "F1");
                GUILayout.Label(CinematicShadersUIStrings.GTAO.RadiusTooltip, smallHelp);
                DrawSlider(CinematicShadersUIStrings.GTAO.DetailRangeLabel, ref _maxPixelRadius, 20f, 300f, "F0", "px");

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                // SHADOW STRENGTH SECTION
                GUILayout.Label(CinematicShadersUIStrings.GTAO.ShadowStrengthSection, HighLogic.Skin.label);
                DrawSlider(CinematicShadersUIStrings.GTAO.IntensityLabel, ref _intensity, 0.0f, 2.0f, "F2");

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                // FILTERING SECTION
                //GUILayout.Label(CinematicShadersUIStrings.GTAO.FilteringSection, HighLogic.Skin.label);
                //DrawSlider(CinematicShadersUIStrings.GTAO.EdgeSharpnessLabel, ref _edgeSharpness, 1.0f, 64.0f, "F0");
                //DrawSlider(CinematicShadersUIStrings.GTAO.DepthToleranceLabel, ref _depthTolerance, 0.01f, 2.0f, "F2");
                //GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                // DISTANCE FADE SECTION
                GUILayout.Label(CinematicShadersUIStrings.GTAO.DistanceFadeSection, HighLogic.Skin.label);
                DrawSliderExponential(CinematicShadersUIStrings.GTAO.StartFadeLabel, ref _fadeStartDistance, 2000f, 25000f, 2.5f, "F0", "m");
                DrawSliderExponential(CinematicShadersUIStrings.GTAO.EndFadeLabel, ref _fadeEndDistance, 25000f, 200000f, 2.0f, "F0", "m");
                DrawSlider(CinematicShadersUIStrings.GTAO.EdgeHardnessLabel, ref _fadeCurve, 0.5f, 3.0f, "F1");

                GUILayout.Label(CinematicShadersUIStrings.GTAO.EdgeHardnessTooltip, smallHelp);

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                // ADVANCED SECTION
                //GUILayout.Label(CinematicShadersUIStrings.GTAO.AdvancedSection, HighLogic.Skin.label);
                //DrawDistributionDropdown();
                //GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.LARGE);

                // TOGGLES
                DrawEnableToggle(oldEnabled, isDeferred);

                if (!isDeferred)
                {
                    GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.TIGHT);
                    GUIStyle helpStyle = CinematicShadersUIResources.Styles.Help();
                    GUILayout.Label(CinematicShadersUIStrings.GTAO.DeferredWarning, helpStyle);
                }
            }
            finally
            {
                GUI.enabled = oldEnabled;
            }
        }

        private void DrawDebugSection()
        {
            GUILayout.Label("Debug Visualization", HighLogic.Skin.label);
            string[] debugOptions = { "None", "Raw AO", "World Normals", "View Normals", "Normal Alpha" };
            int currentDebugMode = GetCurrentDebugMode();

            GUILayout.BeginHorizontal();
            GUILayout.Label("View", GUILayout.Width(60));
            int newDebugMode = GUILayout.SelectionGrid(currentDebugMode, debugOptions, 2, HighLogic.Skin.button);
            GUILayout.EndHorizontal();

            if (newDebugMode != currentDebugMode)
            {
                SetDebugMode(newDebugMode);
            }

            GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);
        }

        private void DrawQualityDropdown()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(CinematicShadersUIStrings.GTAO.QualityLabel, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            if (GUILayout.Button(kQualityNames[_qualityPresetIndex], HighLogic.Skin.button, GUILayout.Width(100)))
            {
                _qualityPresetIndex = (_qualityPresetIndex + 1) % kQualityNames.Length;
                PushSettingsToNative();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawDistributionDropdown()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(CinematicShadersUIStrings.GTAO.DistributionLabel, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            string[] curveNames = {
                CinematicShadersUIStrings.GTAO.DistributionLinear,
                CinematicShadersUIStrings.GTAO.DistributionQuadratic,
                CinematicShadersUIStrings.GTAO.DistributionCubic
            };

            if (GUILayout.Button(curveNames[_distributionCurve], HighLogic.Skin.button, GUILayout.Width(100)))
            {
                _distributionCurve = (_distributionCurve + 1) % 3;
                PushSettingsToNative();
            }
            GUILayout.EndHorizontal();
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

        /// <summary>
        /// Draws a slider with exponential mapping for better precision at the low end.
        /// Higher 'exponent' = more precision at low values, less at high values.
        /// </summary>
        private void DrawSliderExponential(string label, ref float value, float min, float max, float exponent, string format, string suffix = "")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            // Convert value to normalized slider position (0-1) using inverse exponential
            float normalized = Mathf.InverseLerp(min, max, value);
            // Apply inverse power to get linear slider position
            float sliderT = Mathf.Pow(normalized, 1.0f / exponent);

            float newSliderT = GUILayout.HorizontalSlider(sliderT, 0f, 1f, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.SLIDER_WIDTH));

            // Convert back to value with exponential curve
            float newNormalized = Mathf.Pow(newSliderT, exponent);
            float newValue = Mathf.Lerp(min, max, newNormalized);

            string displayText = newValue.ToString(format) + suffix;
            GUILayout.Label(displayText, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.VALUE_WIDTH));

            GUILayout.EndHorizontal();

            if (!Mathf.Approximately(newValue, value))
            {
                value = newValue;
                PushSettingsToNative();
            }
        }

        private void DrawEnableToggle(bool parentEnabledState, bool isDeferred)
        {
            bool localEnabled = GUI.enabled;

            try
            {
                GUIStyle toggleStyle = GTAOSettings.EnableGTAO ?
                    CinematicShadersUIResources.Styles.ToggleActive() : HighLogic.Skin.toggle;

                bool newEnable = GUILayout.Toggle(GTAOSettings.EnableGTAO, CinematicShadersUIStrings.GTAO.EnableToggle, toggleStyle);
                if (newEnable != GTAOSettings.EnableGTAO)
                {
                    GTAOSettings.EnableGTAO = newEnable;
                    GTAOManager.OnToggleChanged();
                }

                if (!GTAOSettings.EnableGTAO)
                    GUI.enabled = false;

                GUIStyle rawToggleStyle = GTAOSettings.GTAORawAOOutput ?
                    CinematicShadersUIResources.Styles.ToggleActive() : HighLogic.Skin.toggle;

                bool newRaw = GUILayout.Toggle(GTAOSettings.GTAORawAOOutput, CinematicShadersUIStrings.GTAO.RawAOOutputToggle, rawToggleStyle);
                if (newRaw != GTAOSettings.GTAORawAOOutput)
                {
                    GTAOSettings.GTAORawAOOutput = newRaw;
                }
            }
            finally
            {
                GUI.enabled = localEnabled;
            }
        }

        private void PushSettingsToNative()
        {
            if (!GTAONative.IsLoaded)
                return;

            var settings = new GTAONative.GTAOSettings
            {
                EffectRadius = _radius,
                Intensity = _intensity,
                SliceCount = kSlicePresets[_qualityPresetIndex],
                StepsPerSlice = kStepPresets[_qualityPresetIndex],
                SampleDistributionPower = _distributionCurve + 1.0f,
                NormalPower = _edgeSharpness,
                DepthSigma = _depthTolerance,
                MaxPixelRadius = _maxPixelRadius,
                FadeStartDistance = _fadeStartDistance,
                FadeEndDistance = _fadeEndDistance,
                FadeCurve = _fadeCurve
            };

            GTAONative.CR_GTAOSetSettings(ref settings);
        }

        private bool IsDeferredRenderingActive()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
                mainCamera = Object.FindObjectOfType<Camera>();

            if (mainCamera == null)
                return false;

            return mainCamera.actualRenderingPath == RenderingPath.DeferredShading;
        }
    }
}