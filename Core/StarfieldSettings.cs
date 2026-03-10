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

        // Beauty
        public static float BloomThreshold { get; set; } = 0.8f;
        public static float BloomIntensity { get; set; } = 2.0f;
        public static float SpikeIntensity { get; set; } = 0.4f;

        // Catalog Generation
        public static int CatalogSeed { get; set; } = 12345;
        public static int CatalogSize { get; set; } = 20000;

        // Track last pushed values to detect changes requiring regeneration
        private static int _lastCatalogSeed = 12345;
        private static int _lastCatalogSize = 20000;
        private static float _lastStarDensity = 200.0f;
        private static float _lastMinMagnitude = -1.0f;
        private static float _lastMaxMagnitude = 10.0f;
        private static float _lastMagnitudeBias = 0.08f;
        private static float _lastHeroRarity = 0.02f;
        private static float _lastClustering = 0.6f;
        private static float _lastStaggerAmount = 0.5f;
        private static float _lastPopulationBias = 0.0f;
        private static float _lastMainSequenceStrength = 0.6f;
        private static float _lastRedGiantRarity = 0.02f;
        private static float _lastGalacticFlatness = 0.85f;
        private static float _lastGalacticDiscFalloff = 3.0f;
        private static float _lastBandCenterBoost = 0.0f;
        private static float _lastBandCoreSharpness = 20.0f;
        private static float _lastBulgeIntensity = 5.0f;
        private static float _lastBulgeWidth = 0.5f;
        private static float _lastBulgeHeight = 0.5f;
        private static float _lastBulgeSoftness = 0.0f;
        private static float _lastBulgeNoiseScale = 20.0f;
        private static float _lastBulgeNoiseStrength = 0.0f;

        private static bool _catalogNeedsRegeneration = true;
        private static float _lastCatalogGenerationTime = -1f;
        private const float CATALOG_GENERATION_DEBOUNCE = 0.3f; // 300ms

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
                CatalogSeed = int.Parse(settingsNode.GetValue("CatalogSeed") ?? "12345");
                CatalogSize = int.Parse(settingsNode.GetValue("CatalogSize") ?? "20000");
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
                BloomThreshold = float.Parse(settingsNode.GetValue("BloomThreshold") ?? "0.8");
                BloomIntensity = float.Parse(settingsNode.GetValue("BloomIntensity") ?? "2.0");
                SpikeIntensity = float.Parse(settingsNode.GetValue("SpikeIntensity") ?? "0.4");

                // Force regeneration on next push since we loaded new values
                _catalogNeedsRegeneration = true;
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

            // Safety: Ensure catalog size is valid
            if (CatalogSize < 100) CatalogSize = 100;
            if (CatalogSize > 500000) CatalogSize = 500000; // Upper sanity limit

            // Check if any catalog generation parameters changed
            bool catalogParamsChanged = _catalogNeedsRegeneration ||
                (CatalogSeed != _lastCatalogSeed) ||
                (CatalogSize != _lastCatalogSize) ||
                !Mathf.Approximately(StarDensity, _lastStarDensity) ||
                !Mathf.Approximately(MinMagnitude, _lastMinMagnitude) ||
                !Mathf.Approximately(MaxMagnitude, _lastMaxMagnitude) ||
                !Mathf.Approximately(MagnitudeBias, _lastMagnitudeBias) ||
                !Mathf.Approximately(HeroRarity, _lastHeroRarity) ||
                !Mathf.Approximately(Clustering, _lastClustering) ||
                !Mathf.Approximately(StaggerAmount, _lastStaggerAmount) ||
                !Mathf.Approximately(PopulationBias, _lastPopulationBias) ||
                !Mathf.Approximately(MainSequenceStrength, _lastMainSequenceStrength) ||
                !Mathf.Approximately(RedGiantRarity, _lastRedGiantRarity) ||
                !Mathf.Approximately(GalacticFlatness, _lastGalacticFlatness) ||
                !Mathf.Approximately(GalacticDiscFalloff, _lastGalacticDiscFalloff) ||
                !Mathf.Approximately(BandCenterBoost, _lastBandCenterBoost) ||
                !Mathf.Approximately(BandCoreSharpness, _lastBandCoreSharpness) ||
                !Mathf.Approximately(BulgeIntensity, _lastBulgeIntensity) ||
                !Mathf.Approximately(BulgeWidth, _lastBulgeWidth) ||
                !Mathf.Approximately(BulgeHeight, _lastBulgeHeight) ||
                !Mathf.Approximately(BulgeSoftness, _lastBulgeSoftness) ||
                !Mathf.Approximately(BulgeNoiseScale, _lastBulgeNoiseScale) ||
                !Mathf.Approximately(BulgeNoiseStrength, _lastBulgeNoiseStrength);

            // Update native settings (exposure, etc.) - always done
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
                BulgeNoiseStrength = BulgeNoiseStrength,
                BloomThreshold = BloomThreshold,
                BloomIntensity = BloomIntensity,
                SpikeIntensity = SpikeIntensity
            };

            StarfieldNative.CR_StarfieldSetSettings(ref nativeSettings);

            // Regenerate catalog if needed (with debounce)
            float currentTime = Time.time;
            if (catalogParamsChanged && EnableStarfield &&
                (currentTime - _lastCatalogGenerationTime > CATALOG_GENERATION_DEBOUNCE || _lastCatalogGenerationTime < 0))
            {
                try
                {
                    StarfieldNative.CR_StarfieldGenerateCatalog(CatalogSeed, CatalogSize);
                    _lastCatalogGenerationTime = currentTime;
                    _lastCatalogSeed = CatalogSeed;
                    _lastCatalogSize = CatalogSize;
                    _lastStarDensity = StarDensity;
                    _lastMinMagnitude = MinMagnitude;
                    _lastMaxMagnitude = MaxMagnitude;
                    _lastMagnitudeBias = MagnitudeBias;
                    _lastHeroRarity = HeroRarity;
                    _lastClustering = Clustering;
                    _lastStaggerAmount = StaggerAmount;
                    _lastPopulationBias = PopulationBias;
                    _lastMainSequenceStrength = MainSequenceStrength;
                    _lastRedGiantRarity = RedGiantRarity;
                    _lastGalacticFlatness = GalacticFlatness;
                    _lastGalacticDiscFalloff = GalacticDiscFalloff;
                    _lastBandCenterBoost = BandCenterBoost;
                    _lastBandCoreSharpness = BandCoreSharpness;
                    _lastBulgeIntensity = BulgeIntensity;
                    _lastBulgeWidth = BulgeWidth;
                    _lastBulgeHeight = BulgeHeight;
                    _lastBulgeSoftness = BulgeSoftness;
                    _lastBulgeNoiseScale = BulgeNoiseScale;
                    _lastBulgeNoiseStrength = BulgeNoiseStrength;
                    _catalogNeedsRegeneration = false;
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogError($"[StarfieldSettings] Failed to generate catalog: {ex}");
                }
            }
        }

        public static void InvalidateCatalog()
        {
            _catalogNeedsRegeneration = true;
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
                settingsNode.AddValue("BloomThreshold", BloomThreshold);
                settingsNode.AddValue("BloomIntensity", BloomIntensity);
                settingsNode.AddValue("SpikeIntensity", SpikeIntensity);
                settingsNode.AddValue("CatalogSeed", CatalogSeed);
                settingsNode.AddValue("CatalogSize", CatalogSize);

                node.Save(SettingsPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to save starfield settings: {ex}");
            }
        }
    }
}