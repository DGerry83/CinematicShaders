using System;

namespace CinematicShaders.UI
{
    /// <summary>
    /// Configuration constants for the unified 59×13 grid system.
    /// </summary>
    public static class UnifiedGridConfig
    {
        /// <summary>
        /// Feature flag to enable unified grid system.
        /// Default: false (use legacy system until migration complete)
        /// </summary>
        public const bool USE_UNIFIED_GRID = true;
        
        /// <summary>
        /// Number of columns in the unified grid (matches Layer 1 border)
        /// </summary>
        public const int GRID_COLUMNS = 59;
        
        /// <summary>
        /// Number of rows in the unified grid
        /// </summary>
        public const int GRID_ROWS = 13;
    }
}
