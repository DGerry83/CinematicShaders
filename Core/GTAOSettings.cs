using System.Collections.Generic;
using CinematicShaders.Native;
using UnityEngine;

namespace CinematicShaders.Core
{
    public static class GTAOSettings
    {
        /// <summary>
        /// One scene's worth of GTAO settings. Defaults match the historical global defaults.
        /// </summary>
        public class GTAOProfile
        {
            public bool EnableGTAO = false;
            public int QualityPreset = 1;
            public float EffectRadius = 2.0f;
            public float Intensity = 0.8f;
            public float MaxPixelRadius = 50.0f;
            public float FadeStartDistance = 0.0f;
            public float FadeEndDistance = 25000.0f;
            public float FadeCurve = 1.0f;
        }

        /// <summary>Scenes that get their own GTAO profile.</summary>
        public static readonly GameScenes[] ProfileScenes =
        {
            GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION, GameScenes.EDITOR
        };

        private static readonly Dictionary<GameScenes, GTAOProfile> _profiles = new Dictionary<GameScenes, GTAOProfile>
        {
            { GameScenes.FLIGHT, new GTAOProfile() },
            { GameScenes.SPACECENTER, new GTAOProfile() },
            { GameScenes.TRACKSTATION, new GTAOProfile() },
            { GameScenes.EDITOR, new GTAOProfile() }
        };

        // Active runtime values - the profile of the current scene, applied via ApplySceneProfile()
        public static bool EnableGTAO { get; set; } = false;
        public static int QualityPreset { get; set; } = 1;
        public static float EffectRadius { get; set; } = 2.0f;
        public static float Intensity { get; set; } = 0.8f;
        public static float MaxPixelRadius { get; set; } = 50.0f;
        public static float FadeStartDistance { get; set; } = 0.0f;
        public static float FadeEndDistance { get; set; } = 25000.0f;
        public static float FadeCurve { get; set; } = 1.0f;

        // Scene whose profile the active statics currently represent (set by ApplySceneProfile).
        // Save() must capture into THIS scene, not HighLogic.LoadedScene: a save firing after
        // a scene flip (window OnDestroy during teardown) would otherwise bleed the old
        // scene's values into the new scene's profile (#035).
        private static GameScenes? _staticsOwner;

        // Global settings - developer tools, not per-scene
        public static int DebugVisualizationMode { get; set; } = 0;
        public static bool GTAORawAOOutput { get; set; } = false;

        private static readonly string SettingsPath = System.IO.Path.Combine(
            KSPUtil.ApplicationRootPath, "GameData", "CinematicShaders", "PluginData", "Settings.cfg");

        /// <summary>Returns the profile for a scene, or null if the scene has none.</summary>
        public static GTAOProfile GetProfile(GameScenes scene)
        {
            GTAOProfile profile;
            return _profiles.TryGetValue(scene, out profile) ? profile : null;
        }

        /// <summary>Copies a scene's profile into the active runtime values.</summary>
        public static void ApplySceneProfile(GameScenes scene)
        {
            GTAOProfile profile = GetProfile(scene);
            if (profile == null) return;

            _staticsOwner = scene;

            EnableGTAO = profile.EnableGTAO;
            QualityPreset = profile.QualityPreset;
            EffectRadius = profile.EffectRadius;
            Intensity = profile.Intensity;
            MaxPixelRadius = profile.MaxPixelRadius;
            FadeStartDistance = profile.FadeStartDistance;
            FadeEndDistance = profile.FadeEndDistance;
            FadeCurve = profile.FadeCurve;
        }

        /// <summary>Copies the active runtime values back into a scene's profile.</summary>
        public static void CaptureSceneProfile(GameScenes scene)
        {
            GTAOProfile profile = GetProfile(scene);
            if (profile == null) return;

            profile.EnableGTAO = EnableGTAO;
            profile.QualityPreset = QualityPreset;
            profile.EffectRadius = EffectRadius;
            profile.Intensity = Intensity;
            profile.MaxPixelRadius = MaxPixelRadius;
            profile.FadeStartDistance = FadeStartDistance;
            profile.FadeEndDistance = FadeEndDistance;
            profile.FadeCurve = FadeCurve;
        }

        public static void Load()
        {
            if (!System.IO.File.Exists(SettingsPath)) return;

            try
            {
                ConfigNode node = ConfigNode.Load(SettingsPath);
                if (node == null) return;

                ConfigNode settingsNode = node.GetNode("CinematicShadersSettings");
                if (settingsNode == null) return;

                GTAORawAOOutput = bool.Parse(settingsNode.GetValue("GTAORawAOOutput") ?? "false");
                DebugVisualizationMode = int.Parse(settingsNode.GetValue("DebugVisualizationMode") ?? "0");

                foreach (GameScenes scene in ProfileScenes)
                {
                    GTAOProfile profile = _profiles[scene];
                    ConfigNode sceneNode = settingsNode.GetNode("GTAO_" + scene);
                    if (sceneNode != null)
                    {
                        LoadProfile(profile, sceneNode);
                    }
                    else
                    {
                        // Migration: pre-per-scene files have flat keys at this level;
                        // seed every scene from them so current behavior carries over
                        LoadProfile(profile, settingsNode);
                    }
                }

                ApplySceneProfile(HighLogic.LoadedScene);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to load settings: {ex}");
            }
        }

        private static void LoadProfile(GTAOProfile profile, ConfigNode node)
        {
            profile.EnableGTAO = bool.Parse(node.GetValue("EnableGTAO") ?? profile.EnableGTAO.ToString());
            profile.QualityPreset = int.Parse(node.GetValue("QualityPreset") ?? profile.QualityPreset.ToString());
            profile.EffectRadius = float.Parse(node.GetValue("EffectRadius") ?? profile.EffectRadius.ToString());
            profile.Intensity = float.Parse(node.GetValue("Intensity") ?? profile.Intensity.ToString());
            profile.MaxPixelRadius = float.Parse(node.GetValue("MaxPixelRadius") ?? profile.MaxPixelRadius.ToString());
            profile.FadeStartDistance = float.Parse(node.GetValue("FadeStartDistance") ?? profile.FadeStartDistance.ToString());
            profile.FadeEndDistance = float.Parse(node.GetValue("FadeEndDistance") ?? profile.FadeEndDistance.ToString());
            profile.FadeCurve = float.Parse(node.GetValue("FadeCurve") ?? profile.FadeCurve.ToString());
        }

        public static void PushSettingsToNative()
        {
            if (!GTAONative.IsLoaded)
                return;

            int[] kSlicePresets = { 2, 3, 4, 6 };
            int[] kStepPresets = { 4, 8, 12, 16 };
            int q = Mathf.Clamp(QualityPreset, 0, 3);

            var settings = new GTAONative.GTAOSettings
            {
                EffectRadius = EffectRadius,
                Intensity = Intensity,
                SliceCount = kSlicePresets[q],
                StepsPerSlice = kStepPresets[q],
                SampleDistributionPower = 2.0f,
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
                // Persist any live edits into the profile that owns the active statics.
                // Fallback to the current scene if ownership has not been set yet.
                CaptureSceneProfile(_staticsOwner ?? HighLogic.LoadedScene);

                string dir = System.IO.Path.GetDirectoryName(SettingsPath);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                // Preserve the rest of the file; rewrite only our node. The old node is
                // removed first because ConfigNode.AddValue appends and GetValue reads the
                // first match - re-adding onto the old node would duplicate keys and the
                // stale values would win on next load.
                ConfigNode node = new ConfigNode();
                if (System.IO.File.Exists(SettingsPath))
                {
                    node = ConfigNode.Load(SettingsPath) ?? node;
                    node.RemoveNode("CinematicShadersSettings");
                }

                ConfigNode settingsNode = node.AddNode("CinematicShadersSettings");
                settingsNode.AddValue("GTAORawAOOutput", GTAORawAOOutput);
                settingsNode.AddValue("DebugVisualizationMode", DebugVisualizationMode);

                foreach (GameScenes scene in ProfileScenes)
                {
                    GTAOProfile profile = _profiles[scene];
                    ConfigNode sceneNode = settingsNode.AddNode("GTAO_" + scene);
                    sceneNode.AddValue("EnableGTAO", profile.EnableGTAO);
                    sceneNode.AddValue("QualityPreset", profile.QualityPreset);
                    sceneNode.AddValue("EffectRadius", profile.EffectRadius);
                    sceneNode.AddValue("Intensity", profile.Intensity);
                    sceneNode.AddValue("MaxPixelRadius", profile.MaxPixelRadius);
                    sceneNode.AddValue("FadeStartDistance", profile.FadeStartDistance);
                    sceneNode.AddValue("FadeEndDistance", profile.FadeEndDistance);
                    sceneNode.AddValue("FadeCurve", profile.FadeCurve);
                }

                node.Save(SettingsPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to save settings: {ex}");
            }
        }
    }
}
