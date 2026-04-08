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
        
        public const float DISPLAY_WIDTH_LARGE = 825f;   // +25px wider
        public const float DISPLAY_HEIGHT_LARGE = 450f;  // 450px tall target

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

        // Element positions - GRID BASED for 35pt Large
        // 35pt: glyph = 12×27px (scaled 2:3), grid cell = 12px wide × 27px tall
        // Pixel position = grid_coord × glyph_size
        
        // Main panel left column - VALUE positions (after Layer 2 labels)
        // Labels end at: HIP:(6), NAME:(7), DISTANCE:(11), SPECTRAL:(11), MAG:(6), CONST:(8)
        // Height 32px for consistent rendering with other Layer 3 elements
        public static readonly Rect HIP_VALUE_POS = new Rect(72, 64, 300, 32);        // col 6 × 12 = 72, row 2 × 32 = 64
        public static readonly Rect NAME_VALUE_POS = new Rect(84, 96, 300, 32);       // col 7 × 12 = 84, row 3 × 32 = 96
        public static readonly Rect DISTANCE_VALUE_POS = new Rect(132, 128, 250, 32); // col 11 × 12 = 132, row 4 × 32 = 128
        public static readonly Rect SPECTRAL_VALUE_POS = new Rect(132, 160, 200, 32); // col 11 × 12 = 132, row 5 × 32 = 160
        public static readonly Rect MAG_VALUE_POS = new Rect(72, 192, 200, 32);       // col 6 × 12 = 72, row 6 × 32 = 192
        public static readonly Rect CONST_VALUE_POS = new Rect(96, 224, 250, 32);     // col 8 × 12 = 96, row 7 × 32 = 224

        // Buttons at specified grid positions
        // [SAVE] at 17,8  [RESET] at 27,8  [RESCAN] at 27,10
        // Height 32px for consistent rendering with result rows
        public static readonly Rect SAVE_BUTTON_POS = new Rect(204, 256, 84, 32);     // col 17 × 12 = 204, row 8 × 32 = 256 (7 chars "[SAVE]" × 12 = 84)
        public static readonly Rect RESET_BUTTON_POS = new Rect(324, 256, 96, 32);    // col 27 × 12 = 324, row 8 × 32 = 256 (8 chars "[RESET]" × 12 = 96)

        // Search area
        // Height 32px for consistent rendering
        public static readonly Rect SEARCH_INPUT_POS = new Rect(96, 320, 300, 32);    // col 8 × 12 = 96, row 10 × 32 = 320 (after "SEARCH")
        public static readonly Rect RESCAN_BUTTON_POS = new Rect(324, 320, 96, 32);   // col 27 × 12 = 324, row 10 × 32 = 320
        public static readonly Rect SELECTED_STAR_POS = new Rect(72, 368, 400, 32);   // row 11.5 × 32 ≈ 368

        // Results rows (10 max, calculated positions)
        public static Rect GetResultRowPos(int index)
        {
            if (index < 0 || index >= 10)
                return new Rect(380, 120, 200, 32);
            return new Rect(380, 120 + (index * 32), 200, 32);
        }

    }
}
