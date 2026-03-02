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
    }
}