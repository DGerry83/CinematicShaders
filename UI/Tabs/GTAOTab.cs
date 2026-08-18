using CinematicShaders.Core;
using CinematicShaders.Native;
using CinematicShaders.Shaders.GTAO;
using UnityEngine;

namespace CinematicShaders.UI.Tabs
{
    public class GTAOTab
    {
        private readonly int[] kSlicePresets = { 2, 3, 4, 6 };
        private readonly int[] kStepPresets = { 4, 8, 12, 16 };
        private readonly string[] kQualityNames = {
            CinematicShadersUIStrings.GTAO.QualityLow,
            CinematicShadersUIStrings.GTAO.QualityMedium,
            CinematicShadersUIStrings.GTAO.QualityHigh,
            CinematicShadersUIStrings.GTAO.QualityUltra
        };

        private GameScenes _selectedScene;
        private int _qualityPresetIndex;
        private float _radius;
        private float _intensity;
        private float _maxPixelRadius;
        private float _fadeStartDistance;
        private float _fadeEndDistance;
        private float _fadeCurve;
        private bool _initialized = false;
        private bool _showQualityDropdown = false;
        private bool _showDebugDropdown = false;
        private int _currentDebugMode = 0;

        public GTAOTab()
        {
            // Edit the live scene's profile by default (fall back to Flight from scenes
            // without a profile, e.g. the main menu)
            _selectedScene = GTAOSettings.GetProfile(HighLogic.LoadedScene) != null
                ? HighLogic.LoadedScene
                : GameScenes.FLIGHT;
            ReadProfileIntoLocals(_selectedScene);
            _currentDebugMode = GTAOSettings.DebugVisualizationMode;
        }

        private void ReadProfileIntoLocals(GameScenes scene)
        {
            GTAOSettings.GTAOProfile profile = GTAOSettings.GetProfile(scene);
            _qualityPresetIndex = profile.QualityPreset;
            _radius = profile.EffectRadius;
            _intensity = profile.Intensity;
            _maxPixelRadius = profile.MaxPixelRadius;
            _fadeStartDistance = profile.FadeStartDistance;
            _fadeEndDistance = profile.FadeEndDistance;
            _fadeCurve = profile.FadeCurve;
        }

        private void WriteLocalsToProfile(GameScenes scene)
        {
            GTAOSettings.GTAOProfile profile = GTAOSettings.GetProfile(scene);
            profile.QualityPreset = _qualityPresetIndex;
            profile.EffectRadius = _radius;
            profile.Intensity = _intensity;
            profile.MaxPixelRadius = _maxPixelRadius;
            profile.FadeStartDistance = _fadeStartDistance;
            profile.FadeEndDistance = _fadeEndDistance;
            profile.FadeCurve = _fadeCurve;
        }

        private static string SceneLabel(GameScenes scene)
        {
            switch (scene)
            {
                case GameScenes.FLIGHT: return CinematicShadersUIStrings.GTAO.SceneFlight;
                case GameScenes.SPACECENTER: return CinematicShadersUIStrings.GTAO.SceneSpaceCenter;
                case GameScenes.TRACKSTATION: return CinematicShadersUIStrings.GTAO.SceneTrackingStation;
                case GameScenes.EDITOR: return CinematicShadersUIStrings.GTAO.SceneEditor;
                default: return scene.ToString();
            }
        }

        private void SetDebugMode(int mode)
        {
            _currentDebugMode = mode;
            GTAOSettings.DebugVisualizationMode = mode;
            GTAONative.CR_GTAOSetOutputMode(mode);

            if (mode > 0 && !GTAOSettings.EnableGTAO)
            {
                GTAOManager.EnableDebugMode();
            }
        }

        public void Draw()
        {
            if (!GTAONative.IsLoaded)
            {
                GUILayout.Space(20);
                GUILayout.Label(CinematicShadersUIStrings.GTAO.NativeLoadError, CinematicShadersUIResources.Styles.Error());
                return;
            }

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

                GUIStyle helpStyle = CinematicShadersUIResources.Styles.Help();

                DrawSceneSelector();

                DrawDebugSection();

                GUILayout.Label(CinematicShadersUIStrings.GTAO.SamplingSection, HighLogic.Skin.label);

                DrawQualityDropdown();
                DrawSlider(CinematicShadersUIStrings.GTAO.RadiusLabel, ref _radius, 0.5f, 10.0f, "F1");
                GUILayout.Label(CinematicShadersUIStrings.GTAO.RadiusTooltip, helpStyle);
                DrawSlider(CinematicShadersUIStrings.GTAO.DetailRangeLabel, ref _maxPixelRadius, 20f, 300f, "F0", "px");

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                GUILayout.Label(CinematicShadersUIStrings.GTAO.ShadowStrengthSection, HighLogic.Skin.label);
                DrawSlider(CinematicShadersUIStrings.GTAO.IntensityLabel, ref _intensity, 0.0f, 2.0f, "F2");

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                GUILayout.Label(CinematicShadersUIStrings.GTAO.DistanceFadeSection, HighLogic.Skin.label);
                DrawSliderExponential(CinematicShadersUIStrings.GTAO.StartFadeLabel, ref _fadeStartDistance, 2000f, 25000f, 2.5f, "F0", "m");
                DrawSliderExponential(CinematicShadersUIStrings.GTAO.EndFadeLabel, ref _fadeEndDistance, 25000f, 200000f, 2.0f, "F0", "m");
                DrawSlider(CinematicShadersUIStrings.GTAO.EdgeHardnessLabel, ref _fadeCurve, 0.5f, 3.0f, "F1");

                GUILayout.Label(CinematicShadersUIStrings.GTAO.EdgeHardnessTooltip, helpStyle);

                GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);

                DrawEnableToggle(oldEnabled, isDeferred);

                if (!isDeferred)
                {
                    GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.TIGHT);
                    GUILayout.Label(CinematicShadersUIStrings.GTAO.DeferredWarning, helpStyle);
                }
            }
            finally
            {
                GUI.enabled = oldEnabled;
            }
        }

        private void DrawSceneSelector()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(CinematicShadersUIStrings.GTAO.SceneSelectorLabel, HighLogic.Skin.label);

            foreach (GameScenes scene in GTAOSettings.ProfileScenes)
            {
                string label = SceneLabel(scene);
                if (scene == HighLogic.LoadedScene)
                    label += CinematicShadersUIStrings.GTAO.SceneLiveSuffix;

                GUIStyle style = scene == _selectedScene
                    ? CinematicShadersUIResources.Styles.ToggleActive()
                    : HighLogic.Skin.button;

                if (GUILayout.Button(label, style))
                {
                    SelectScene(scene);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);
        }

        private void SelectScene(GameScenes scene)
        {
            if (scene == _selectedScene) return;

            WriteLocalsToProfile(_selectedScene);
            _selectedScene = scene;
            ReadProfileIntoLocals(scene);
            _showQualityDropdown = false;
        }

        private void DrawDebugSection()
        {
            GUILayout.Label(CinematicShadersUIStrings.GTAO.DebugVisualizationHeader, HighLogic.Skin.label);
            string[] debugOptions = { CinematicShadersUIStrings.GTAO.DebugModeNone, CinematicShadersUIStrings.GTAO.DebugModeRawAO, CinematicShadersUIStrings.GTAO.DebugModeWorldNormals, CinematicShadersUIStrings.GTAO.DebugModeViewNormals, CinematicShadersUIStrings.GTAO.DebugModeNormalAlpha };
            int currentDebugMode = _currentDebugMode;
            string currentLabel = debugOptions[currentDebugMode];

            GUILayout.BeginHorizontal();
            GUILayout.Label(CinematicShadersUIStrings.GTAO.DebugViewLabel, GUILayout.Width(CinematicShadersUIResources.Layout.Dropdowns.DEBUG_LABEL_WIDTH));
            if (GUILayout.Button(currentLabel, HighLogic.Skin.button, GUILayout.Width(CinematicShadersUIResources.Layout.Dropdowns.DEBUG_BUTTON_WIDTH)))
            {
                _showDebugDropdown = !_showDebugDropdown;
                _showQualityDropdown = false;
            }
            GUILayout.EndHorizontal();

            if (_showDebugDropdown)
            {
                GUIStyle boxStyle = CinematicShadersUIResources.Styles.DropdownBox();
                GUILayout.BeginVertical(boxStyle);
                for (int i = 0; i < debugOptions.Length; i++)
                {
                    if (GUILayout.Button(debugOptions[i], HighLogic.Skin.button))
                    {
                        if (currentDebugMode != i)
                        {
                            SetDebugMode(i);
                        }
                        _showDebugDropdown = false;
                    }
                }
                GUILayout.EndVertical();
            }

            GUILayout.Space(CinematicShadersUIResources.Layout.Spacing.NORMAL);
        }

        private void DrawQualityDropdown()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(CinematicShadersUIStrings.GTAO.QualityLabel, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            if (GUILayout.Button(kQualityNames[_qualityPresetIndex], HighLogic.Skin.button, GUILayout.Width(CinematicShadersUIResources.Layout.Dropdowns.QUALITY_BUTTON_WIDTH)))
            {
                _showQualityDropdown = !_showQualityDropdown;
                _showDebugDropdown = false;
            }
            GUILayout.EndHorizontal();

            if (_showQualityDropdown)
            {
                GUIStyle boxStyle = CinematicShadersUIResources.Styles.DropdownBox();
                GUILayout.BeginVertical(boxStyle);
                for (int i = 0; i < kQualityNames.Length; i++)
                {
                    if (GUILayout.Button(kQualityNames[i], HighLogic.Skin.button))
                    {
                        if (_qualityPresetIndex != i)
                        {
                            _qualityPresetIndex = i;
                            PushSettingsToNative();
                        }
                        _showQualityDropdown = false;
                    }
                }
                GUILayout.EndVertical();
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

        private void DrawSliderExponential(string label, ref float value, float min, float max, float exponent, string format, string suffix = "")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.DEFAULT_WIDTH));

            float normalized = Mathf.InverseLerp(min, max, value);
            float sliderT = Mathf.Pow(normalized, 1.0f / exponent);

            float newSliderT = GUILayout.HorizontalSlider(sliderT, 0f, 1f, GUILayout.Width(CinematicShadersUIResources.Layout.Labels.SLIDER_WIDTH));

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
                GTAOSettings.GTAOProfile profile = GTAOSettings.GetProfile(_selectedScene);

                GUIStyle toggleStyle = profile.EnableGTAO ?
                    CinematicShadersUIResources.Styles.ToggleActive() : HighLogic.Skin.toggle;

                bool newEnable = GUILayout.Toggle(profile.EnableGTAO, CinematicShadersUIStrings.GTAO.EnableToggle, toggleStyle);
                if (newEnable != profile.EnableGTAO)
                {
                    profile.EnableGTAO = newEnable;

                    // Only the live scene's toggle affects the renderer right now
                    if (_selectedScene == HighLogic.LoadedScene)
                    {
                        GTAOSettings.EnableGTAO = newEnable;
                        GTAOManager.OnToggleChanged();
                    }
                }

                if (!profile.EnableGTAO)
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
            WriteLocalsToProfile(_selectedScene);

            // Only the live scene's profile drives the renderer
            if (_selectedScene == HighLogic.LoadedScene)
            {
                GTAOSettings.ApplySceneProfile(_selectedScene);
                GTAOSettings.PushSettingsToNative();
            }
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
