using CinematicShaders.Native;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Persistent settings for GTAO shader. 
    /// Replaces SessionState from CinematicRecorder.
    /// </summary>
    public static class GTAOSettings
    {
        /// <summary>
        /// Enable Ground-Truth Ambient Occlusion.
        /// </summary>
        public static bool EnableGTAO { get; set; } = false;

        public static int DebugVisualizationMode { get; set; } = 0;
        /// <summary>
        /// Show raw AO output (grayscale debug view) instead of composited scene.
        /// </summary>
        public static bool GTAORawAOOutput { get; set; } = false;
        // Persistent settings fields - these mirror the UI state
        public static int QualityPreset { get; set; } = 1; // 0=Low, 1=Med, 2=High, 3=Ultra
        public static float EffectRadius { get; set; } = 2.0f;
        public static float Intensity { get; set; } = 0.8f;
        public static float MaxPixelRadius { get; set; } = 50.0f;
        public static float FadeStartDistance { get; set; } = 0.0f;
        public static float FadeEndDistance { get; set; } = 25000.0f;
        public static float FadeCurve { get; set; } = 1.0f;

        private static readonly string SettingsPath = System.IO.Path.Combine(
            KSPUtil.ApplicationRootPath, "GameData", "CinematicShaders", "PluginData", "Settings.cfg");

        public static void Load()
        {
            if (!System.IO.File.Exists(SettingsPath)) return;

            try
            {
                ConfigNode node = ConfigNode.Load(SettingsPath);
                if (node == null) return;

                ConfigNode settingsNode = node.GetNode("CinematicShadersSettings");
                if (settingsNode == null) return;

                EnableGTAO = bool.Parse(settingsNode.GetValue("EnableGTAO") ?? "false");
                GTAORawAOOutput = bool.Parse(settingsNode.GetValue("GTAORawAOOutput") ?? "false");
                DebugVisualizationMode = int.Parse(settingsNode.GetValue("DebugVisualizationMode") ?? "0");
                QualityPreset = int.Parse(settingsNode.GetValue("QualityPreset") ?? "1");
                EffectRadius = float.Parse(settingsNode.GetValue("EffectRadius") ?? "2.0");
                Intensity = float.Parse(settingsNode.GetValue("Intensity") ?? "0.8");
                MaxPixelRadius = float.Parse(settingsNode.GetValue("MaxPixelRadius") ?? "50.0");
                FadeStartDistance = float.Parse(settingsNode.GetValue("FadeStartDistance") ?? "0.0");
                FadeEndDistance = float.Parse(settingsNode.GetValue("FadeEndDistance") ?? "25000.0");
                FadeCurve = float.Parse(settingsNode.GetValue("FadeCurve") ?? "1.0");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[CinematicShaders] Failed to load settings: {ex}");
            }
        }

        public static void PushSettingsToNative()
        {
            if (!GTAONative.IsLoaded)
                return;

            // Quality Presets (Must match your UI/DLL expectations)
            int[] kSlicePresets = { 2, 3, 4, 6 };
            int[] kStepPresets = { 4, 8, 12, 16 };
            int q = Mathf.Clamp(QualityPreset, 0, 3);

            var settings = new GTAONative.GTAOSettings
            {
                EffectRadius = EffectRadius,
                Intensity = Intensity,
                SliceCount = kSlicePresets[q],
                StepsPerSlice = kStepPresets[q],
                SampleDistributionPower = 2.0f,  // Hardcoded Quadratic
                NormalPower = 32.0f,
                DepthSigma = 2.0f,
                MaxPixelRadius = MaxPixelRadius,
                FadeStartDistance = FadeStartDistance,
                FadeEndDistance = FadeEndDistance,
                FadeCurve = FadeCurve
            };

            GTAONative.CR_GTAOSetSettings(ref settings);
        }

        public static void Save()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(SettingsPath);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                ConfigNode node = new ConfigNode();
                ConfigNode settingsNode = node.AddNode("CinematicShadersSettings");

                settingsNode.AddValue("EnableGTAO", EnableGTAO);
                settingsNode.AddValue("GTAORawAOOutput", GTAORawAOOutput);
                settingsNode.AddValue("DebugVisualizationMode", DebugVisualizationMode);
                settingsNode.AddValue("QualityPreset", QualityPreset);
                settingsNode.AddValue("EffectRadius", EffectRadius);
                settingsNode.AddValue("Intensity", Intensity);
                settingsNode.AddValue("MaxPixelRadius", MaxPixelRadius);
                settingsNode.AddValue("FadeStartDistance", FadeStartDistance);
                settingsNode.AddValue("FadeEndDistance", FadeEndDistance);
                settingsNode.AddValue("FadeCurve", FadeCurve);

                node.Save(SettingsPath);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[CinematicShaders] Failed to save settings: {ex}");
            }
        }
    }
}