namespace CinematicShaders.UI.Layout
{
    /// <summary>
    /// Global configuration and feature flags for the constraint-based layout system.
    /// </summary>
    public static class LayoutConfig
    {
        /// <summary>
        /// Master feature flag that enables the constraint-based layout path.
        /// Defaults to true - constraint layout is now the primary system.
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
    }
}
