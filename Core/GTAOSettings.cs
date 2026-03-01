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

        // Future expansion for GTAO parameters:
        // public static float EffectRadius { get; set; } = 2.0f;
        // public static float Intensity { get; set; } = 0.8f;
        // public static int SliceCount { get; set; } = 2;
        // public static int StepsPerSlice { get; set; } = 4;
    }
}