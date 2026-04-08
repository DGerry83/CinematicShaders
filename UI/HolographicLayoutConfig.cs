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
        // Fixed display sizes (no auto-scaling)
        public const float DISPLAY_WIDTH_SMALL = 450f;
        public const float DISPLAY_HEIGHT_SMALL = 525f;
        
        public const float DISPLAY_WIDTH_MEDIUM = 600f;
        public const float DISPLAY_HEIGHT_MEDIUM = 700f;
        
        public const float DISPLAY_WIDTH_LARGE = 800f;
        public const float DISPLAY_HEIGHT_LARGE = 933f;

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

        // Element positions - GRID BASED (font grid coordinates × glyph size)
        // 35pt Large: glyph = 12×27px (scaled 2:3), grid = 59×13
        // Field positions align with Layer 2 labels from font_layout_guide.json
        
        // Main panel left column - VALUE positions (grid-based for 35pt)
        public static readonly Rect HIP_VALUE_POS = new Rect(72, 54, 300, 27);      // grid (6,2)
        public static readonly Rect NAME_VALUE_POS = new Rect(84, 81, 300, 27);     // grid (7,3)
        public static readonly Rect DISTANCE_VALUE_POS = new Rect(132, 108, 250, 27); // grid (11,4)
        public static readonly Rect SPECTRAL_VALUE_POS = new Rect(132, 135, 200, 27); // grid (11,5)
        public static readonly Rect MAG_VALUE_POS = new Rect(72, 162, 200, 27);     // grid (6,6)
        public static readonly Rect CONST_VALUE_POS = new Rect(96, 189, 250, 27);   // grid (8,7)

        // Buttons
        public static readonly Rect SAVE_BUTTON_POS = new Rect(280, 240, 100, 27);
        public static readonly Rect RESET_BUTTON_POS = new Rect(400, 240, 100, 27);

        // Search area
        public static readonly Rect SEARCH_INPUT_POS = new Rect(96, 270, 300, 27);  // grid (8,10)
        public static readonly Rect RESCAN_BUTTON_POS = new Rect(420, 270, 120, 27);
        public static readonly Rect SELECTED_STAR_POS = new Rect(72, 310, 400, 27);

        // Results rows (10 max, calculated positions)
        public static Rect GetResultRowPos(int index)
        {
            if (index < 0 || index >= 10)
                return new Rect(380, 120, 200, 32);
            return new Rect(380, 120 + (index * 32), 200, 32);
        }

    }
}
