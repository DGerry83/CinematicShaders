using CinematicShaders.Core;
using CinematicShaders.Native;
using UnityEngine;

namespace CinematicShaders.Core
{
    public static class StarfieldSettings
    {
        public static bool EnableStarfield { get; set; } = false;

        // Rendering
        public static float Exposure { get; set; } = 3.0f;
        // BlurPixels is now ANGULAR SIGMA in radians (not screen pixels)
        // Default 0.00029 rad ≈ 1.0 arcminute (minimum for sharp stars)
        public static float BlurPixels { get; set; } = 0.00029f;

        // Star Distribution
        public static float MinMagnitude { get; set; } = -1.0f;
        public static float MaxMagnitude { get; set; } = 10.0f;
        public static float MagnitudeBias { get; set; } = 0.25f;  // 0.25 closer to real HYG distribution (was 0.08)
        public static int HeroCount { get; set; } = 128;  // 16-1024 hero stars
        public static float Clustering { get; set; } = 0.6f;
        public static float PopulationBias { get; set; } = 0.0f;
        public static float MainSequenceStrength { get; set; } = 0.8f;  // Mostly realistic
        public static float RedGiantFrequency { get; set; } = 0.05f;

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
        public static float BloomThreshold { get; set; } = 0.08f;  // Display 0-10, default 8.0
        public static float BloomIntensity { get; set; } = 0.5f;  // sqrt(0.5*2)=1.0 display
        
        // Color
        public static float ColorSaturation { get; set; } = 1.0f;  // 0.5=realistic, 1.0=natural, 2.0=vivid

        // Catalog Generation
        public static int CatalogSeed { get; set; } = 12345;
        public static int CatalogSize { get; set; } = 50000;  // 50k stars - good balance
        
        // Active Catalog
        public static string ActiveCatalogPath { get; set; } = "";
        public static bool IsReadOnly { get; set; } = false;  // New catalogs start as Generation Active (read-only = false)

        // Track last pushed values to detect changes requiring regeneration
        private static int _lastCatalogSeed = 12345;
        private static int _lastCatalogSize = 20000;
        private static float _lastMinMagnitude = -1.0f;
        private static float _lastMaxMagnitude = 10.0f;
        private static float _lastMagnitudeBias = 0.25f;
        private static int _lastHeroCount = 128;
        private static float _lastClustering = 0.6f;
        private static float _lastPopulationBias = 0.0f;
        private static float _lastMainSequenceStrength = 0.6f;
        private static float _lastRedGiantFrequency = 0.05f;
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
        private static bool _catalogNeedsReload = false;  // Set when device reinitializes or scene changes
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
                BlurPixels = float.Parse(settingsNode.GetValue("BlurPixels") ?? "0.001");
                MinMagnitude = float.Parse(settingsNode.GetValue("MinMagnitude") ?? "-1.0");
                MaxMagnitude = float.Parse(settingsNode.GetValue("MaxMagnitude") ?? "10.0");
                MagnitudeBias = float.Parse(settingsNode.GetValue("MagnitudeBias") ?? "0.25");
                HeroCount = int.Parse(settingsNode.GetValue("HeroCount") ?? "128");
                Clustering = float.Parse(settingsNode.GetValue("Clustering") ?? "0.6");
                PopulationBias = float.Parse(settingsNode.GetValue("PopulationBias") ?? "0.0");
                MainSequenceStrength = float.Parse(settingsNode.GetValue("MainSequenceStrength") ?? "0.6");
                // Note: Changed from RedGiantRarity (legacy) to RedGiantFrequency
                RedGiantFrequency = float.Parse(settingsNode.GetValue("RedGiantFrequency") ?? "0.05");
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
                BloomThreshold = float.Parse(settingsNode.GetValue("BloomThreshold") ?? "0.08");
                BloomIntensity = float.Parse(settingsNode.GetValue("BloomIntensity") ?? "0.5");
                ColorSaturation = float.Parse(settingsNode.GetValue("ColorSaturation") ?? "1.0");
                ActiveCatalogPath = settingsNode.GetValue("ActiveCatalogPath") ?? "";
                IsReadOnly = bool.Parse(settingsNode.GetValue("IsReadOnly") ?? "false");

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
                !Mathf.Approximately(MinMagnitude, _lastMinMagnitude) ||
                !Mathf.Approximately(MaxMagnitude, _lastMaxMagnitude) ||
                !Mathf.Approximately(MagnitudeBias, _lastMagnitudeBias) ||
                (HeroCount != _lastHeroCount) ||
                !Mathf.Approximately(Clustering, _lastClustering) ||
                !Mathf.Approximately(PopulationBias, _lastPopulationBias) ||
                !Mathf.Approximately(MainSequenceStrength, _lastMainSequenceStrength) ||
                !Mathf.Approximately(RedGiantFrequency, _lastRedGiantFrequency) ||
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
                MinMagnitude = MinMagnitude,
                MaxMagnitude = MaxMagnitude,
                MagnitudeBias = MagnitudeBias,
                HeroCount = HeroCount,
                Clustering = Clustering,
                PopulationBias = PopulationBias,
                MainSequenceStrength = MainSequenceStrength,
                RedGiantFrequency = RedGiantFrequency,
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
                ColorSaturation = ColorSaturation
            };

            StarfieldNative.CR_StarfieldSetSettings(ref nativeSettings);

            // Check if we need to load an existing catalog
            bool catalogPathExists = !string.IsNullOrEmpty(ActiveCatalogPath) && System.IO.File.Exists(ActiveCatalogPath);
            bool shouldLoadCatalog = _catalogNeedsReload && EnableStarfield && catalogPathExists;
            
            UnityEngine.Debug.Log($"[StarfieldSettings] Catalog check: needsReload={_catalogNeedsReload}, enabled={EnableStarfield}, pathExists={catalogPathExists}, path={ActiveCatalogPath}");

            // Check if we need to generate a new catalog
            float currentTime = Time.time;
            bool shouldGenerateCatalog = catalogParamsChanged && EnableStarfield && !IsReadOnly &&
                (currentTime - _lastCatalogGenerationTime > CATALOG_GENERATION_DEBOUNCE || _lastCatalogGenerationTime < 0);

            if (shouldLoadCatalog)
            {
                // Load existing catalog instead of generating
                try
                {
                    UnityEngine.Debug.Log($"[StarfieldSettings] Loading catalog: {ActiveCatalogPath}");
                    StarCatalogManager.LoadCatalog(ActiveCatalogPath);
                    _catalogNeedsReload = false;
                    
                    // Update tracking vars to match loaded catalog
                    _lastCatalogSeed = CatalogSeed;
                    _lastCatalogSize = CatalogSize;
                    _lastMinMagnitude = MinMagnitude;
                    _lastMaxMagnitude = MaxMagnitude;
                    _lastMagnitudeBias = MagnitudeBias;
                    _lastHeroCount = HeroCount;
                    _lastClustering = Clustering;
                    _lastPopulationBias = PopulationBias;
                    _lastMainSequenceStrength = MainSequenceStrength;
                    _lastRedGiantFrequency = RedGiantFrequency;
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
                    UnityEngine.Debug.LogError($"[StarfieldSettings] Failed to load catalog: {ex}");
                    // Fall through to generation if loading fails
                    shouldGenerateCatalog = true;
                }
            }

            if (shouldGenerateCatalog)
            {
                try
                {
                    UnityEngine.Debug.Log($"[StarfieldSettings] Generating new catalog: seed={CatalogSeed}, size={CatalogSize}");
                    StarfieldNative.CR_StarfieldGenerateCatalog(CatalogSeed, CatalogSize);
                    _lastCatalogGenerationTime = currentTime;
                    _lastCatalogSeed = CatalogSeed;
                    _lastCatalogSize = CatalogSize;
                    _lastMinMagnitude = MinMagnitude;
                    _lastMaxMagnitude = MaxMagnitude;
                    _lastMagnitudeBias = MagnitudeBias;
                    _lastHeroCount = HeroCount;
                    _lastClustering = Clustering;
                    _lastPopulationBias = PopulationBias;
                    _lastMainSequenceStrength = MainSequenceStrength;
                    _lastRedGiantFrequency = RedGiantFrequency;
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
                    _catalogNeedsReload = false;
                    
                    // Auto-save to current catalog file if one is active
                    if (!string.IsNullOrEmpty(ActiveCatalogPath) && !IsReadOnly)
                    {
                        StarCatalogManager.IsDirty = true;
                    }
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
        
        public static void InvalidateCatalogForReload()
        {
            // Call this when device reinitializes or scene changes - triggers reload, not regeneration
            _catalogNeedsReload = true;
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
                settingsNode.AddValue("MinMagnitude", MinMagnitude);
                settingsNode.AddValue("MaxMagnitude", MaxMagnitude);
                settingsNode.AddValue("MagnitudeBias", MagnitudeBias);
                settingsNode.AddValue("HeroCount", HeroCount);
                settingsNode.AddValue("Clustering", Clustering);
                settingsNode.AddValue("PopulationBias", PopulationBias);
                settingsNode.AddValue("MainSequenceStrength", MainSequenceStrength);
                settingsNode.AddValue("RedGiantFrequency", RedGiantFrequency);
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
                settingsNode.AddValue("ColorSaturation", ColorSaturation);
                settingsNode.AddValue("ActiveCatalogPath", ActiveCatalogPath);
                settingsNode.AddValue("IsReadOnly", IsReadOnly);
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