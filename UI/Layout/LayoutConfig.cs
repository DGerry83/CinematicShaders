namespace CinematicShaders.UI.Layout
{
    /// <summary>
    /// Global configuration and feature flags for the constraint-based layout system.
    /// </summary>
    public static class LayoutConfig
    {
        /// <summary>
        /// Master feature flag that enables the constraint-based layout path.
        /// Defaults to false for backward compatibility.
        /// </summary>
        public static bool UseConstraintLayout { get; set; } = true;

        /// <summary>
        /// Enables layout validation in debug builds to compare old and new positions.
        /// </summary>
#if DEBUG
        public static bool ValidateLayouts { get; set; } = true;
#else
        public static bool ValidateLayouts { get; set; } = false;
#endif

        /// <summary>
        /// Tolerance in pixels for position validation between legacy and constraint layouts.
        /// </summary>
        public static float PositionTolerance { get; set; } = 2f;

        /// <summary>
        /// Path to the emergency fallback file. If this file exists, the legacy layout is forced.
        /// </summary>
        public const string FallbackFilePath = "GameData/CinematicShaders/PluginData/StarConsole_use_legacy_layout.txt";

        /// <summary>
        /// Checks whether the emergency fallback file exists, requesting legacy layout mode.
        /// </summary>
        public static bool IsEmergencyFallbackRequested()
        {
            return System.IO.File.Exists(FallbackFilePath);
        }
    }
}
