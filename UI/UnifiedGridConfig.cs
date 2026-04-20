using System;

namespace CinematicShaders.UI
{
    /// <summary>
    /// Configuration constants for the unified 59×13 grid system.
    /// </summary>
    public static class UnifiedGridConfig
    {

        /// <summary>
        /// Number of columns in the unified grid (matches Layer 1 border)
        /// </summary>
        public const int GRID_COLUMNS = 59;
        
        /// <summary>
        /// Number of rows in the unified grid
        /// </summary>
        public const int GRID_ROWS = 13;
        
        /// <summary>
        /// Debug mode: Draw click zones as visible blocks.
        /// </summary>
        public const bool DEBUG_DRAW_CLICK_ZONES = true; // Set to true to enable
    }
}
