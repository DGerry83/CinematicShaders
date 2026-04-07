using UnityEngine;

namespace CinematicShaders.UI
{
    /// <summary>
    /// Layout configuration for the holographic star catalog display.
    /// All positions are based on 4K reference resolution (3840x2160).
    /// </summary>
    public static class HolographicLayoutConfig
    {
        // Base 4K resolution
        public const float BASE_WIDTH = 3840f;
        public const float BASE_HEIGHT = 2160f;

        // Display size at 4K (will be scaled)
        public const float DISPLAY_WIDTH_4K = 600f;
        public const float DISPLAY_HEIGHT_4K = 700f;

        // Font size at 4K
        public const float FONT_SIZE_4K = 24f;
        public const float LINE_SPACING_4K = 32f;

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

        // Element positions (4K coordinates) - MATCH ASCII ART EXACTLY
        // Main panel left column
        public static readonly Rect HIP_LABEL_POS = new Rect(80, 180, 80, 32);
        public static readonly Rect HIP_VALUE_POS = new Rect(160, 180, 200, 32);
        public static readonly Rect NAME_LABEL_POS = new Rect(80, 212, 80, 32);
        public static readonly Rect NAME_VALUE_POS = new Rect(160, 212, 200, 32);
        public static readonly Rect DISTANCE_LABEL_POS = new Rect(80, 244, 80, 32);
        public static readonly Rect DISTANCE_VALUE_POS = new Rect(160, 244, 200, 32);
        public static readonly Rect SPECTRAL_LABEL_POS = new Rect(80, 276, 80, 32);
        public static readonly Rect SPECTRAL_VALUE_POS = new Rect(160, 276, 200, 32);
        public static readonly Rect MAG_LABEL_POS = new Rect(80, 308, 80, 32);
        public static readonly Rect MAG_VALUE_POS = new Rect(160, 308, 200, 32);
        public static readonly Rect CONST_LABEL_POS = new Rect(80, 340, 80, 32);
        public static readonly Rect CONST_VALUE_POS = new Rect(160, 340, 200, 32);

        // Buttons
        public static readonly Rect SAVE_BUTTON_POS = new Rect(280, 380, 80, 32);
        public static readonly Rect RESET_BUTTON_POS = new Rect(360, 380, 80, 32);

        // Search area (bottom left)
        public static readonly Rect SEARCH_LABEL_POS = new Rect(80, 600, 80, 32);
        public static readonly Rect SEARCH_INPUT_POS = new Rect(160, 600, 200, 32);
        public static readonly Rect RESCAN_BUTTON_POS = new Rect(360, 600, 80, 32);
        public static readonly Rect SELECTED_STAR_POS = new Rect(80, 632, 400, 32);

        // Results column header
        public static readonly Rect RESULTS_HEADER_POS = new Rect(380, 80, 200, 32);

        // Results rows (10 max, calculated positions)
        public static Rect GetResultRowPos(int index)
        {
            if (index < 0 || index >= 10)
                return new Rect(380, 120, 200, 32);
            return new Rect(380, 120 + (index * 32), 200, 32);
        }

        // Scaling helper
        public static float GetScaleFactor()
        {
            float scale = Screen.height / BASE_HEIGHT;
            return Mathf.Clamp(scale, 0.33f, 1.5f);  // Min 720p, max 150%
        }

        /// <summary>
        /// Scale a rect from 4K reference coordinates to target resolution
        /// </summary>
        public static Rect ScaleRect(Rect rect4K, float scaleFactor)
        {
            return new Rect(
                rect4K.x * scaleFactor,
                rect4K.y * scaleFactor,
                rect4K.width * scaleFactor,
                rect4K.height * scaleFactor
            );
        }

        /// <summary>
        /// Get display rect at current scale positioned at x, y
        /// </summary>
        public static Rect GetDisplayRect(float x, float y, float scaleFactor)
        {
            return new Rect(
                x,
                y,
                DISPLAY_WIDTH_4K * scaleFactor,
                DISPLAY_HEIGHT_4K * scaleFactor
            );
        }

        /// <summary>
        /// Get scaled font size for current resolution
        /// </summary>
        public static float GetScaledFontSize(float scaleFactor)
        {
            return FONT_SIZE_4K * scaleFactor;
        }

        /// <summary>
        /// Get scaled line spacing for current resolution
        /// </summary>
        public static float GetScaledLineSpacing(float scaleFactor)
        {
            return LINE_SPACING_4K * scaleFactor;
        }
    }
}
