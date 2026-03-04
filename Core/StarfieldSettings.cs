using CinematicShaders.Native;
using UnityEngine;

namespace CinematicShaders.Core
{
    public static class StarfieldSettings
    {
        public static bool EnableStarfield { get; set; } = false;

        // Rendering
        public static float Exposure { get; set; } = 3.0f;
        public static float BlurPixels { get; set; } = 1.0f;

        // Star Distribution
        public static float StarDensity { get; set; } = 200.0f;
        public static float MinMagnitude { get; set; } = -1.0f;
        public static float MaxMagnitude { get; set; } = 10.0f;
        public static float MagnitudeBias { get; set; } = 0.08f;
        public static float HeroRarity { get; set; } = 0.02f;
        public static float Clustering { get; set; } = 0.6f;
        public static float StaggerAmount { get; set; } = 0.5f;
        public static float PopulationBias { get; set; } = 0.0f;
        public static float MainSequenceStrength { get; set; } = 0.6f;
        public static float RedGiantRarity { get; set; } = 0.02f;

        // Galactic Structure
        public static float GalacticFlatness { get; set; } = 0.85f;
        public static float GalacticDiscFalloff { get; set; } = 3.0f;
        public static float BandCenterBoost { get; set; } = 0.0f;
        public static float BandCoreSharpness { get; set; } = 20.0f;
        public static float BulgeIntensity { get; set; } = 5.0f;
        public static float BulgeWidth { get; set; } = 0.5f;
        public static float BulgeHeight { get; set; } = 0.5f;
        public static float BulgeSoftness { get; set; } = 0.0f;
        public static float BulgeNoiseScale { get; set; } = 20.0f;
        public static float BulgeNoiseStrength { get; set; } = 0.0f;

        private static readonly string SettingsPath = System.IO.Path.Combine(
            KSPUtil.ApplicationRootPath, "GameData", "CinematicShaders", "PluginData", "Settings.cfg");

        public static void Load()
        {
            if (!System.IO.File.Exists(SettingsPath)) return;

            try
            {
                ConfigNode node = ConfigNode.Load(SettingsPath);
                if (node == null) return;

                ConfigNode settingsNode = node.GetNode("StarfieldSettings");
                if (settingsNode == null) return;

                EnableStarfield = bool.Parse(settingsNode.GetValue("EnableStarfield") ?? "false");
                Exposure = float.Parse(settingsNode.GetValue("Exposure") ?? "3.0");
                BlurPixels = float.Parse(settingsNode.GetValue("BlurPixels") ?? "1.0");
                StarDensity = float.Parse(settingsNode.GetValue("StarDensity") ?? "200.0");
                MinMagnitude = float.Parse(settingsNode.GetValue("MinMagnitude") ?? "-1.0");
                MaxMagnitude = float.Parse(settingsNode.GetValue("MaxMagnitude") ?? "10.0");
                MagnitudeBias = float.Parse(settingsNode.GetValue("MagnitudeBias") ?? "0.08");
                HeroRarity = float.Parse(settingsNode.GetValue("HeroRarity") ?? "0.02");
                Clustering = float.Parse(settingsNode.GetValue("Clustering") ?? "0.6");
                StaggerAmount = float.Parse(settingsNode.GetValue("StaggerAmount") ?? "0.5");
                PopulationBias = float.Parse(settingsNode.GetValue("PopulationBias") ?? "0.0");
                MainSequenceStrength = float.Parse(settingsNode.GetValue("MainSequenceStrength") ?? "0.6");
                RedGiantRarity = float.Parse(settingsNode.GetValue("RedGiantRarity") ?? "0.02");
                GalacticFlatness = float.Parse(settingsNode.GetValue("GalacticFlatness") ?? "0.85");
                GalacticDiscFalloff = float.Parse(settingsNode.GetValue("GalacticDiscFalloff") ?? "3.0");
                BandCenterBoost = float.Parse(settingsNode.GetValue("BandCenterBoost") ?? "0.0");
                BandCoreSharpness = float.Parse(settingsNode.GetValue("BandCoreSharpness") ?? "20.0");
                BulgeIntensity = float.Parse(settingsNode.GetValue("BulgeIntensity") ?? "5.0");
                BulgeWidth = float.Parse(settingsNode.GetValue("BulgeWidth") ?? "0.5");
                BulgeHeight = float.Parse(settingsNode.GetValue("BulgeHeight") ?? "0.5");
                BulgeSoftness = float.Parse(settingsNode.GetValue("BulgeSoftness") ?? "0.0");
                BulgeNoiseScale = float.Parse(settingsNode.GetValue("BulgeNoiseScale") ?? "20.0");
                BulgeNoiseStrength = float.Parse(settingsNode.GetValue("BulgeNoiseStrength") ?? "0.0");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to load starfield settings: {ex}");
            }
        }

        public static void PushSettingsToNative()
        {
            if (!StarfieldNative.IsLoaded)
                return;

            var nativeSettings = new StarfieldNative.StarfieldSettingsNative
            {
                Exposure = Exposure,
                BlurPixels = BlurPixels,
                StarDensity = StarDensity,
                MinMagnitude = MinMagnitude,
                MaxMagnitude = MaxMagnitude,
                MagnitudeBias = MagnitudeBias,
                HeroRarity = HeroRarity,
                Clustering = Clustering,
                StaggerAmount = StaggerAmount,
                PopulationBias = PopulationBias,
                MainSequenceStrength = MainSequenceStrength,
                RedGiantRarity = RedGiantRarity,
                GalacticFlatness = GalacticFlatness,
                GalacticDiscFalloff = GalacticDiscFalloff,
                BandCenterBoost = BandCenterBoost,
                BandCoreSharpness = BandCoreSharpness,
                BulgeIntensity = BulgeIntensity,
                BulgeWidth = BulgeWidth,
                BulgeHeight = BulgeHeight,
                BulgeSoftness = BulgeSoftness,
                BulgeNoiseScale = BulgeNoiseScale,
                BulgeNoiseStrength = BulgeNoiseStrength
            };

            StarfieldNative.CR_StarfieldSetSettings(ref nativeSettings);
        }

        public static void Save()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(SettingsPath);
                if (!System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                ConfigNode node = new ConfigNode();
                if (System.IO.File.Exists(SettingsPath))
                {
                    node = ConfigNode.Load(SettingsPath) ?? node;
                }

                ConfigNode settingsNode = node.GetNode("StarfieldSettings") ?? node.AddNode("StarfieldSettings");

                settingsNode.AddValue("EnableStarfield", EnableStarfield);
                settingsNode.AddValue("Exposure", Exposure);
                settingsNode.AddValue("BlurPixels", BlurPixels);
                settingsNode.AddValue("StarDensity", StarDensity);
                settingsNode.AddValue("MinMagnitude", MinMagnitude);
                settingsNode.AddValue("MaxMagnitude", MaxMagnitude);
                settingsNode.AddValue("MagnitudeBias", MagnitudeBias);
                settingsNode.AddValue("HeroRarity", HeroRarity);
                settingsNode.AddValue("Clustering", Clustering);
                settingsNode.AddValue("StaggerAmount", StaggerAmount);
                settingsNode.AddValue("PopulationBias", PopulationBias);
                settingsNode.AddValue("MainSequenceStrength", MainSequenceStrength);
                settingsNode.AddValue("RedGiantRarity", RedGiantRarity);
                settingsNode.AddValue("GalacticFlatness", GalacticFlatness);
                settingsNode.AddValue("GalacticDiscFalloff", GalacticDiscFalloff);
                settingsNode.AddValue("BandCenterBoost", BandCenterBoost);
                settingsNode.AddValue("BandCoreSharpness", BandCoreSharpness);
                settingsNode.AddValue("BulgeIntensity", BulgeIntensity);
                settingsNode.AddValue("BulgeWidth", BulgeWidth);
                settingsNode.AddValue("BulgeHeight", BulgeHeight);
                settingsNode.AddValue("BulgeSoftness", BulgeSoftness);
                settingsNode.AddValue("BulgeNoiseScale", BulgeNoiseScale);
                settingsNode.AddValue("BulgeNoiseStrength", BulgeNoiseStrength);

                node.Save(SettingsPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to save starfield settings: {ex}");
            }
        }
    }
}