using UnityEngine;

namespace CinematicShaders.UI
{
    /// <summary>
    /// Display size presets for the holographic star catalog.
    /// Users manually select the size that works best for their preference.
    /// </summary>
    public enum HolographicDisplaySize
    {
        Small,   // 450x525 - compact
        Medium,  // 600x700 - default
        Large    // 800x933 - maximum readability
    }

    /// <summary>
    /// Layout configuration for the holographic star catalog display.
    /// Fixed sizes that users manually select based on preference.
    /// </summary>
    public static class HolographicLayoutConfig
    {
        // Fixed display sizes (all same aspect ratio as Large)
        // Large is the reference: 825x450 = 1.833 ratio
        public const float DISPLAY_WIDTH_SMALL = 550f;     // 300x1.833
        public const float DISPLAY_HEIGHT_SMALL = 300f;    // Compact height
        
        public const float DISPLAY_WIDTH_MEDIUM = 733f;    // 400x1.833
        public const float DISPLAY_HEIGHT_MEDIUM = 400f;   // Medium height
        
        public const float DISPLAY_WIDTH_LARGE = 825f;     // Reference
        public const float DISPLAY_HEIGHT_LARGE = 450f;    // Reference

        // Font sizes for each display size
        public const float FONT_SIZE_SMALL = 18f;
        public const float FONT_SIZE_MEDIUM = 24f;
        public const float FONT_SIZE_LARGE = 35f;  // 35pt for integer scaled width (12px) at 2:3 aspect
        
        public const float LINE_SPACING_SMALL = 24f;
        public const float LINE_SPACING_MEDIUM = 32f;
        public const float LINE_SPACING_LARGE = 42f;

        /// <summary>
        /// Get display dimensions for the selected size
        /// </summary>
        public static Vector2 GetDisplayDimensions(HolographicDisplaySize size)
        {
            switch (size)
            {
                case HolographicDisplaySize.Small:
                    return new Vector2(DISPLAY_WIDTH_SMALL, DISPLAY_HEIGHT_SMALL);
                case HolographicDisplaySize.Large:
                    return new Vector2(DISPLAY_WIDTH_LARGE, DISPLAY_HEIGHT_LARGE);
                case HolographicDisplaySize.Medium:
                default:
                    return new Vector2(DISPLAY_WIDTH_MEDIUM, DISPLAY_HEIGHT_MEDIUM);
            }
        }

        /// <summary>
        /// Get font size for the selected display size
        /// </summary>
        public static float GetFontSize(HolographicDisplaySize size)
        {
            switch (size)
            {
                case HolographicDisplaySize.Small:
                    return FONT_SIZE_SMALL;
                case HolographicDisplaySize.Large:
                    return FONT_SIZE_LARGE;
                case HolographicDisplaySize.Medium:
                default:
                    return FONT_SIZE_MEDIUM;
            }
        }

        /// <summary>
        /// Get line spacing for the selected display size
        /// </summary>
        public static float GetLineSpacing(HolographicDisplaySize size)
        {
            switch (size)
            {
                case HolographicDisplaySize.Small:
                    return LINE_SPACING_SMALL;
                case HolographicDisplaySize.Large:
                    return LINE_SPACING_LARGE;
                case HolographicDisplaySize.Medium:
                default:
                    return LINE_SPACING_MEDIUM;
            }
        }

        // ASCII border characters
        public const char BORDER_TOP_LEFT = '╔';
        public const char BORDER_TOP_RIGHT = '╗';
        public const char BORDER_BOTTOM_LEFT = '╚';
        public const char BORDER_BOTTOM_RIGHT = '╝';
        public const char BORDER_HORIZONTAL = '═';
        public const char BORDER_VERTICAL = '║';
        public const char BORDER_T_LEFT = '╠';
        public const char BORDER_T_RIGHT = '╣';
        public const char BORDER_T_UP = '╩';
        public const char BORDER_T_DOWN = '╦';
        public const char BORDER_CROSS = '╬';
    }
}
