namespace CinematicShaders.UI
{
    public static class CinematicShadersUIStrings
    {
        public static class Common
        {
            public const string WindowTitle = "Cinematic Shaders";
            public const string Initializing = "Initializing...";
        }

        public static class GTAO
        {
            public const string SamplingSection = "SAMPLING";
            public const string ShadowStrengthSection = "SHADOW STRENGTH";
            public const string FilteringSection = "FILTERING";
            public const string DistanceFadeSection = "DISTANCE FADE";
            public const string AdvancedSection = "ADVANCED";
            public const string TabName = "GTAO";
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

        public static class Starfield
        {
            public const string TabName = "Starfield";
            public const string EnableToggle = " Enable Procedural Starfield";

            public const string RenderingSection = "RENDERING";
            public const string ExposureLabel = "Exposure";
            public const string ExposureTooltip = "Logarithmic exposure in EV stops (pow(2.0, exposure))";
            public const string BlurPixelsLabel = "Star Softness";
            public const string BlurPixelsTooltip = "Angular size of star blur in arcminutes (1-2 for sharp stars, higher values look out of focus)";

            public const string DistributionSection = "STAR DISTRIBUTION";
            public const string MinMagnitudeLabel = "Min Magnitude";
            public const string MaxMagnitudeLabel = "Max Magnitude";
            public const string MagnitudeBiasLabel = "Magnitude Bias";
            public const string ClusteringLabel = "Clustering";
            public const string PopulationBiasLabel = "Population Bias";
            public const string PopulationBiasTooltip = "Shift toward red (-1) or blue (+1) stars";
            public const string MainSequenceLabel = "Main Sequence";
            public const string MainSequenceTooltip = "Correlation between brightness and temperature";
            public const string RedGiantRarityLabel = "Red Giant Rarity";

            public const string GalacticStructureSection = "GALACTIC STRUCTURE";
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

            public const string Initializing = "Initializing starfield...";
            public const string NativeLoadError = "Native plugin failed to load. Check KSP.log for details.";

            public const string BloomThresholdLabel = "Bloom Threshold";
            public const string BloomThresholdTooltip = "HDR values above this trigger bloom - display 0-10 maps to actual 0-0.1 for fine control";
            public const string BloomIntensityLabel = "Bloom Intensity";
            public const string BloomIntensityTooltip = "Bloom strength - logarithmic scale gives more precision at low values (0-2 range)";
            public const string ColorSaturationLabel = "Color Saturation";
            public const string ColorSaturationTooltip = "Star color vividness. 0.5=Realistic (real sky), 1.0=Natural, 2.0=Vivid, 4.0=Hyper-saturated/Deep colors";
            public const string BeautySection = "BEAUTY";
        }
    }
}