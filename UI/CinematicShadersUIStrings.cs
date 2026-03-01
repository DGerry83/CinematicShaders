namespace CinematicShaders.UI
{
    public static class CinematicShadersUIStrings
    {
        public static class Common
        {
            public const string WindowTitle = "Cinematic Shaders";
        }

        public static class GTAO
        {
            // Section Headers
            public const string SamplingSection = "SAMPLING";
            public const string ShadowStrengthSection = "SHADOW STRENGTH";
            public const string FilteringSection = "FILTERING";
            public const string DistanceFadeSection = "DISTANCE FADE";
            public const string AdvancedSection = "ADVANCED";

            // Tab
            public const string TabName = "GTAO";

            // Sampling Controls
            public const string QualityLabel = "Quality";
            public const string RadiusLabel = "Radius";
            public const string DetailRangeLabel = "Detail Range";
            public const string DetailRangeTooltip = "Higher = better distant detail, lower FPS";

            // Shadow Strength
            public const string IntensityLabel = "Intensity";

            // Filtering
            public const string EdgeSharpnessLabel = "Edge Sharpness";
            public const string DepthToleranceLabel = "Depth Tolerance";

            // Distance Fade
            public const string StartFadeLabel = "Start Fade";
            public const string EndFadeLabel = "End Fade";
            public const string EdgeHardnessLabel = "Edge Hardness";
            public const string EdgeHardnessTooltip = "0.5=Soft, 1.0=Linear, 3.0=Sharp";

            // Advanced
            public const string DistributionLabel = "Distribution";
            public const string EnableToggle = " Enable Ground-Truth AO";
            public const string RawAOOutputToggle = " Show Raw AO Output";
            public const string DeferredWarning = "GTAO requires deferred rendering.";

            // Distribution Options
            public const string DistributionLinear = "Linear";
            public const string DistributionQuadratic = "Quadratic";
            public const string DistributionCubic = "Cubic";

            // Quality Presets
            public const string QualityLow = "Low";
            public const string QualityMedium = "Medium";
            public const string QualityHigh = "High";
            public const string QualityUltra = "Ultra";
        }
    }
}