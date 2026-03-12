namespace CinematicShaders.UI
{
    public static class CinematicShadersUIStrings
    {
        // ============================================================================
        // COMMON - Shared symbols and UI elements
        // ============================================================================
        public static class Common
        {
            public const string WindowTitle = "Cinematic Shaders";
            public const string Initializing = "Initializing...";
            
            // Symbols
            public const string CollapsedPrefix = "▶ ";
            public const string DropdownArrow = " ▼";
        }

        // ============================================================================
        // GTAO
        // ============================================================================
        public static class GTAO
        {
            public const string TabName = "GTAO";
            public const string SamplingSection = "SAMPLING";
            public const string ShadowStrengthSection = "SHADOW STRENGTH";
            public const string FilteringSection = "FILTERING";
            public const string DistanceFadeSection = "DISTANCE FADE";
            public const string AdvancedSection = "ADVANCED";
            public const string QualityLabel = "Quality";
            public const string RadiusLabel = "Radius";
            public const string RadiusTooltip = "How far to search for occluders (larger = more distant shadows)";
            public const string DetailRangeLabel = "Shadow Spread";
            public const string IntensityLabel = "Intensity";
            public const string EdgeSharpnessLabel = "Edge Sharpness";
            public const string DepthToleranceLabel = "Depth Tolerance";
            public const string StartFadeLabel = "Start Fade";
            public const string EndFadeLabel = "End Fade";
            public const string EdgeHardnessLabel = "Edge Hardness";
            public const string EdgeHardnessTooltip = "0.5=Soft, 1.0=Linear, 3.0=Sharp";
            public const string DistributionLabel = "Distribution";
            public const string EnableToggle = " Enable Ground-Truth AO";
            public const string RawAOOutputToggle = " Show Raw AO Output";
            public const string DeferredWarning = "GTAO requires deferred rendering.";
            public const string DistributionLinear = "Linear";
            public const string DistributionQuadratic = "Quadratic";
            public const string DistributionCubic = "Cubic";
            public const string QualityLow = "Low";
            public const string QualityMedium = "Medium";
            public const string QualityHigh = "High";
            public const string QualityUltra = "Ultra";
            public const string DebugVisualizationHeader = "Debug Visualization";
            public const string DebugViewLabel = "View";
            public const string DebugModeNone = "None";
            public const string DebugModeRawAO = "Raw AO";
            public const string DebugModeWorldNormals = "World Normals";
            public const string DebugModeViewNormals = "View Normals";
            public const string DebugModeNormalAlpha = "Normal Alpha";
            public const string NativeLoadError = "Native plugin failed to load. Check KSP.log for details.";
        }

        // ============================================================================
        // STARFIELDTAB - Organized by UI layout (top to bottom)
        // ============================================================================
        public static class Starfield
        {
            // ------------------------------------------------------------------------
            // GENERAL
            // ------------------------------------------------------------------------
            public const string TabName = "Starfield";
            public const string EnableToggle = " Enable Procedural Starfield";
            public const string NativeLoadError = "Native plugin failed to load. Check KSP.log for details.";
            public const string Initializing = "Initializing starfield...";

            // ------------------------------------------------------------------------
            // SECTION HEADERS (in order of appearance)
            // ------------------------------------------------------------------------
            public const string RenderingSection = "RENDERING";
            public const string MainGenerationSection = "MAIN GENERATION";
            public const string AdvancedGenerationSection = "ADVANCED GENERATION";
            public const string GalacticStructureSection = "GALACTIC STRUCTURE";
            public const string StarCatalogSection = "Star Catalog";

            // ------------------------------------------------------------------------
            // CATALOG MANAGEMENT UI (in order of appearance)
            // ------------------------------------------------------------------------
            public const string ActiveCatalogLabel = "Active Catalog";
            public const string ActiveCatalogNone = "(None)";
            public const string SaveCatalogAsTitle = "Save Catalog As:";
            public const string FilenameLabel = "Filename:";
            public const string DisplayNameLabel = "Display Name:";
            public const string DefaultCatalogFileName = "MyStarfield";
            public const string DefaultCatalogDisplayName = "My Starfield";
            
            // Buttons
            public const string CancelButton = "Cancel";
            public const string UnlockButton = "I Understand - Unlock";
            public const string SaveButton = "Save";
            public const string NewButton = "New";
            public const string SaveAsButton = "Save As...";
            public const string OpenFolderButton = "Open Folder";
            public const string DeleteCatalogButton = "Delete Catalog";

            // ------------------------------------------------------------------------
            // READ-ONLY PROTECTION UI
            // ------------------------------------------------------------------------
            public const string ReadOnlyLockMessage = "Generation parameters locked (Read-Only mode)";
            public const string ReadOnlyToggleOn = "Read-Only Protection <color=#33FF33>ON</color>";
            public const string ReadOnlyToggleOff = "Read-Only Protection <color=#FF3333>OFF</color>";
            public const string ReadOnlyWarningTitle = "WARNING: Disabling Read-Only Protection";
            public const string ReadOnlyWarningMessage = "You are about to unlock this catalog for editing. Any changes to generation parameters will PERMANENTLY modify this catalog. This cannot be undone.";

            // ------------------------------------------------------------------------
            // SLIDER LABELS (in order of appearance in UI)
            // ------------------------------------------------------------------------
            // Rendering Section
            public const string ExposureLabel = "Exposure";
            public const string BlurPixelsLabel = "Star Softness";
            public const string BloomThresholdLabel = "Bloom Threshold";
            public const string BloomIntensityLabel = "Bloom Intensity";
            
            // Main Generation Section
            public const string CatalogSeedLabel = "Catalog Seed";
            public const string CatalogSizeLabel = "Catalog Size";
            public const string MinMagnitudeLabel = "Min Magnitude";
            public const string MaxMagnitudeLabel = "Max Magnitude";
            public const string HeroCountLabel = "Hero Count";
            public const string MainSequenceLabel = "Main Sequence Strength";
            public const string RedGiantFrequencyLabel = "Red Giant Frequency";
            public const string ColorSaturationLabel = "Color Saturation";
            
            // Advanced Generation Section
            public const string BrightnessDistributionLabel = "Brightness Distribution";
            public const string StellarPopulationLabel = "Stellar Population";
            public const string ClusteringLabel = "Star Clustering";
            
            // Galactic Structure Section
            public const string DiscFlatnessLabel = "Disc Flatness";
            public const string DiscFalloffLabel = "Disc Falloff";
            public const string BandCenterBoostLabel = "Band Boost";
            public const string BandCoreSharpnessLabel = "Band Sharpness";
            public const string BulgeIntensityLabel = "Bulge Intensity";
            public const string BulgeWidthLabel = "Bulge Width";
            public const string BulgeHeightLabel = "Bulge Height";
            public const string BulgeSoftnessLabel = "Bulge Softness";
            public const string BulgeNoiseScaleLabel = "Bulge Noise Scale";
            public const string BulgeNoiseStrengthLabel = "Bulge Noise Strength";

            // ------------------------------------------------------------------------
            // TOOLTIPS (grouped at bottom, in order of corresponding UI elements)
            // ------------------------------------------------------------------------
            // Rendering Section Tooltips
            public const string ExposureTooltip = "EV Stops";
            public const string BlurPixelsTooltip = "Angular size of star blur";
            public const string BloomThresholdTooltip = "HDR values above this trigger bloom";
            public const string BloomIntensityTooltip = "Bloom strength";
            
            // Main Generation Section Tooltips
            public const string CatalogSeedTooltip = "Random seed for star placement";
            public const string CatalogSizeTooltip = "Number of stars to generate";
            public const string HeroCountTooltip = "Number of bright hero stars (named/important stars)";
            public const string MainSequenceTooltip = "0.0=Wild West (any star type), 1.0=Strict (bright stars must be hot)";
            public const string ColorSaturationTooltip = "0.5=Realistic, 1.0=Slight Boost, 2.0=Vivid, 4.0=Hyper-saturated";
            
            // Advanced Generation Section Tooltips
            public const string StellarPopulationTooltip = "Star age bias: shift toward old/red or young/blue stars";
        }
    }
}
