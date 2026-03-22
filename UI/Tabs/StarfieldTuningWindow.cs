using CinematicShaders.Core;
using CinematicShaders.Native;
using UnityEngine;

namespace CinematicShaders.UI.Tabs
{
    public class StarfieldTuningWindow : MonoBehaviour
    {
        private Rect _windowRect = new Rect(600, 100, 400, 600);
        private int _windowId = "StarfieldTuning".GetHashCode();
        private StarfieldTab _parentTab;
        private bool _initialized = false;

        // Tuning parameters with defaults matching current hardcoded values
        private StarfieldNative.StarfieldTuningParams _tuning = new StarfieldNative.StarfieldTuningParams
        {
            CorePlatformWidth = 1.8f,
            CorePlatformAmp = 0.25f,
            CoreNormalization = 1.0f,
            MoffatBeta = 2.0f,
            HaloSigmaMin = 3.0f,
            HaloSigmaMax = 8.0f,
            HaloWeightMax = 0.5f,
            BrightnessDivisor = 6.0f,
            JitterAmplitudeMin = 0.1f,
            JitterAmplitudeMax = 1.8f,
            JitterStrength = 0.6f,
            JitterEdgeStart = 1.0f,
            SharpSinPower = 0.2f,
            BrightnessCurvePower = 0.6f,
            EdgeFadeStart = 0.85f,
            EdgeFadeEnd = 1.0f
        };

        // Last sent values to avoid spamming native plugin
        private StarfieldNative.StarfieldTuningParams _lastSentTuning;

        public void Initialize(Rect rect, StarfieldTab parent)
        {
            _windowRect = rect;
            _parentTab = parent;
            _lastSentTuning = _tuning; // Mark as sent
        }

        void OnGUI()
        {
            if (!_initialized)
            {
                _windowRect = GUILayout.Window(_windowId, _windowRect, DrawWindowContents, "Star Shape Tuning", HighLogic.Skin.window);
            }
            else
            {
                _windowRect = GUILayout.Window(_windowId, _windowRect, DrawWindowContents, "Star Shape Tuning", HighLogic.Skin.window);
            }
        }

        void DrawWindowContents(int id)
        {
            GUILayout.BeginVertical();

            // Allow window dragging
            GUI.DragWindow(new Rect(0, 0, 10000, 20));

            GUILayout.Label("Live adjustment - changes apply immediately", HighLogic.Skin.label);

            // CORE SECTION
            GUI.color = Color.yellow;
            GUILayout.Label("CORE (Neon Tube Body)", HighLogic.Skin.label);
            GUI.color = Color.white;

            DrawTuningSlider("Platform Width", ref _tuning.CorePlatformWidth, 1.0f, 4.0f);
            DrawTuningSlider("Platform Amp", ref _tuning.CorePlatformAmp, 0.0f, 1.0f);
            DrawTuningSlider("Core Norm", ref _tuning.CoreNormalization, 0.5f, 1.5f);
            DrawTuningSlider("Moffat Beta", ref _tuning.MoffatBeta, 1.5f, 4.0f, "lower=bigger");

            GUILayout.Space(10);

            // SPIKE SIZING SECTION
            GUI.color = Color.yellow;
            GUILayout.Label("SPIKE SIZE", HighLogic.Skin.label);
            GUI.color = Color.white;

            DrawTuningSlider("Halo Min Mult", ref _tuning.HaloSigmaMin, 1.0f, 5.0f);
            DrawTuningSlider("Halo Max Mult", ref _tuning.HaloSigmaMax, 3.0f, 15.0f);
            DrawTuningSlider("Max Halo Weight", ref _tuning.HaloWeightMax, 0.0f, 1.0f);
            DrawTuningSlider("Bright Divisor", ref _tuning.BrightnessDivisor, 2.0f, 12.0f);
            DrawTuningSlider("Bright Power", ref _tuning.BrightnessCurvePower, 0.1f, 2.0f);

            GUILayout.Space(10);

            // JITTER SECTION
            GUI.color = Color.yellow;
            GUILayout.Label("JITTER (Spike Variation)", HighLogic.Skin.label);
            GUI.color = Color.white;

            DrawTuningSlider("Amp Min", ref _tuning.JitterAmplitudeMin, 0.0f, 0.5f);
            DrawTuningSlider("Amp Max", ref _tuning.JitterAmplitudeMax, 0.5f, 3.0f);
            DrawTuningSlider("Strength", ref _tuning.JitterStrength, 0.0f, 1.0f);
            DrawTuningSlider("Edge Start (σ)", ref _tuning.JitterEdgeStart, 0.0f, 2.0f);
            DrawTuningSlider("Sharp Power", ref _tuning.SharpSinPower, 0.05f, 1.0f, "lower=sharper");

            GUILayout.Space(10);

            // EDGE FADE SECTION
            GUI.color = Color.yellow;
            GUILayout.Label("EDGE FADE", HighLogic.Skin.label);
            GUI.color = Color.white;

            DrawTuningSlider("Fade Start", ref _tuning.EdgeFadeStart, 0.5f, 0.95f);
            DrawTuningSlider("Fade End", ref _tuning.EdgeFadeEnd, 0.8f, 1.5f);

            GUILayout.Space(20);

            // BUTTONS
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Defaults", GUILayout.Width(120)))
            {
                ResetToDefaults();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Close", GUILayout.Width(80)))
            {
                CloseWindow();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            // Send to native if changed (every frame is fine, it's just 64 bytes)
            if (!TuningEquals(_tuning, _lastSentTuning))
            {
                if (StarfieldNative.IsLoaded)
                {
                    StarfieldNative.CR_StarfieldSetTuningParams(ref _tuning);
                    _lastSentTuning = _tuning;
                }
            }
        }

        private void DrawTuningSlider(string label, ref float value, float min, float max, string hint = "")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100));
            float newValue = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(180));
            GUILayout.Label(newValue.ToString("F2"), GUILayout.Width(50));
            if (!string.IsNullOrEmpty(hint))
            {
                GUIStyle smallStyle = new GUIStyle(HighLogic.Skin.label);
                smallStyle.fontSize = 10;
                smallStyle.normal.textColor = Color.gray;
                GUILayout.Label(hint, smallStyle, GUILayout.Width(60));
            }
            GUILayout.EndHorizontal();

            if (newValue != value)
            {
                value = newValue;
            }
        }

        private bool TuningEquals(StarfieldNative.StarfieldTuningParams a, StarfieldNative.StarfieldTuningParams b)
        {
            return a.CorePlatformWidth == b.CorePlatformWidth &&
                   a.CorePlatformAmp == b.CorePlatformAmp &&
                   a.CoreNormalization == b.CoreNormalization &&
                   a.MoffatBeta == b.MoffatBeta &&
                   a.HaloSigmaMin == b.HaloSigmaMin &&
                   a.HaloSigmaMax == b.HaloSigmaMax &&
                   a.HaloWeightMax == b.HaloWeightMax &&
                   a.BrightnessDivisor == b.BrightnessDivisor &&
                   a.JitterAmplitudeMin == b.JitterAmplitudeMin &&
                   a.JitterAmplitudeMax == b.JitterAmplitudeMax &&
                   a.JitterStrength == b.JitterStrength &&
                   a.JitterEdgeStart == b.JitterEdgeStart &&
                   a.SharpSinPower == b.SharpSinPower &&
                   a.BrightnessCurvePower == b.BrightnessCurvePower &&
                   a.EdgeFadeStart == b.EdgeFadeStart &&
                   a.EdgeFadeEnd == b.EdgeFadeEnd;
        }

        private void ResetToDefaults()
        {
            _tuning.CorePlatformWidth = 1.8f;
            _tuning.CorePlatformAmp = 0.25f;
            _tuning.CoreNormalization = 1.0f;
            _tuning.MoffatBeta = 2.0f;
            _tuning.HaloSigmaMin = 3.0f;
            _tuning.HaloSigmaMax = 8.0f;
            _tuning.HaloWeightMax = 0.5f;
            _tuning.BrightnessDivisor = 6.0f;
            _tuning.JitterAmplitudeMin = 0.1f;
            _tuning.JitterAmplitudeMax = 1.8f;
            _tuning.JitterStrength = 0.6f;
            _tuning.JitterEdgeStart = 1.0f;
            _tuning.SharpSinPower = 0.2f;
            _tuning.BrightnessCurvePower = 0.6f;
            _tuning.EdgeFadeStart = 0.85f;
            _tuning.EdgeFadeEnd = 1.0f;

            // Send immediately
            if (StarfieldNative.IsLoaded)
            {
                StarfieldNative.CR_StarfieldSetTuningParams(ref _tuning);
                _lastSentTuning = _tuning;
            }
        }

        private void CloseWindow()
        {
            if (_parentTab != null)
            {
                _parentTab.OnTuningWindowClosed();
            }
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            // Ensure parent knows we're gone
            if (_parentTab != null)
            {
                _parentTab.OnTuningWindowClosed();
            }
        }
    }
}