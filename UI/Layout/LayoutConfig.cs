namespace CinematicShaders.UI.Layout
{
    /// <summary>
    /// Global configuration for the constraint-based layout system.
    /// </summary>
    public static class LayoutConfig
    {
        /// <summary>
        /// Enables layout validation in debug builds.
        /// </summary>
#if DEBUG
        public static bool ValidateLayouts { get; set; } = true;
#else
        public static bool ValidateLayouts { get; set; } = false;
#endif

        /// <summary>
        /// Tolerance in pixels for position validation.
        /// </summary>
        public static float PositionTolerance { get; set; } = 2f;
    }
}
