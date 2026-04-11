using UnityEngine;

namespace CinematicShaders.UI
{
    public static class CinematicShadersUIResources
    {
        #region Colors
        public static class Colors
        {
            public static readonly Color TOGGLE_ACTIVE_GREEN = new Color(0.2f, 0.9f, 0.2f);
            public static readonly Color READONLY_ON_GREEN = new Color(0.2f, 0.9f, 0.2f);
            public static readonly Color READONLY_OFF_RED = new Color(0.9f, 0.2f, 0.2f);
            public static readonly Color INFO_ORANGE = new Color(1f, 0.5490196f, 0f);
            public static readonly Color TEXT_DIM = Color.gray;

            public static class GridColors
            {
                public static readonly Color Seafoam = new Color(0.1f, 0.9f, 0.7f);
                public static readonly Color Amber = new Color(1.0f, 0.65f, 0.0f);
                public static readonly Color White = new Color(0.85f, 0.95f, 1.0f);
                public static readonly Color Green = new Color(0.25f, 1.0f, 0.0f);

                public static readonly Color[] All = { Seafoam, Amber, White, Green };
            }
            
            /// <summary>
            /// CRT display colors - custom mapped from Kartographer grid color selection.
            /// These may differ from the actual grid colors for visual consistency on the CRT display.
            /// </summary>
            public static class CRTColors
            {
                // Current test mapping:
                // Seafoam selection -> RED
                // Amber selection -> BLUE
                // White selection -> GREEN
                // Green selection -> MAGENTA
                public static readonly Color Seafoam = new Color(1.0f, 0.0f, 0.0f);
                public static readonly Color Amber = new Color(0.0f, 0.0f, 1.0f);
                public static readonly Color White = new Color(0.0f, 1.0f, 0.0f);
                public static readonly Color Green = new Color(1.0f, 0.0f, 1.0f);

                public static readonly Color[] All = { Seafoam, Amber, White, Green };
                
                /// <summary>
                /// Gets the CRT color based on the Kartographer grid color index.
                /// </summary>
                public static Color GetColor(int colorIndex)
                {
                    switch (colorIndex)
                    {
                        case 0: return Seafoam;
                        case 1: return Amber;
                        case 2: return White;
                        case 3: return Green;
                        default: return Seafoam;
                    }
                }
                
                /// <summary>
                /// Gets the CRT color as a uint in ARGB format for native rendering.
                /// </summary>
                public static uint GetColorUint(int colorIndex)
                {
                    Color c = GetColor(colorIndex);
                    uint r = (uint)(c.r * 255) & 0xFF;
                    uint g = (uint)(c.g * 255) & 0xFF;
                    uint b = (uint)(c.b * 255) & 0xFF;
                    return 0xFF000000 | (r << 16) | (g << 8) | b;  // ARGB format (A=FF)
                }
            }
        }
        #endregion

        #region Layout
        public static class Layout
        {
            public static class Tabs
            {
                public const float BUTTON_WIDTH = 130f;
                public const float BUTTON_HEIGHT = 30f;
            }

            public static class Labels
            {
                public const float DEFAULT_WIDTH = 80f;
                public const float VALUE_WIDTH = 50f;
                public const float SLIDER_WIDTH = 120f;
            }

            public static class Dropdowns
            {
                public const float DEBUG_LABEL_WIDTH = 60f;
                public const float DEBUG_BUTTON_WIDTH = 150f;
                public const float QUALITY_BUTTON_WIDTH = 100f;
            }

            public static class Spacing
            {
                public const float TIGHT = 4f;
                public const float NORMAL = 10f;
                public const float LARGE = 15f;
            }

            public static class Tooltip
            {
                public const float OFFSET_X = 15f;
                public const float OFFSET_Y = 15f;
                public const float MAX_WIDTH = 250f;
                public const float PADDING = 20f;
                public const float WINDOW_CLAMP_X = 300f;
                public const float WINDOW_CLAMP_Y = 480f;
            }
        }
        #endregion

        #region Styles
        public static class Styles
        {
            public static GUIStyle Window()
            {
                return new GUIStyle(HighLogic.Skin.window);
            }

            public static GUIStyle TabButton()
            {
                return new GUIStyle(HighLogic.Skin.button);
            }

            public static GUIStyle TabButtonActive()
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.button);
                style.normal.textColor = Colors.TOGGLE_ACTIVE_GREEN;
                style.fontStyle = FontStyle.Bold;
                return style;
            }

            public static GUIStyle ButtonActive()
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.button);
                style.normal.textColor = Colors.TOGGLE_ACTIVE_GREEN;
                style.fontStyle = FontStyle.Bold;
                return style;
            }

            public static GUIStyle ToggleActive()
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.toggle);
                style.normal.textColor = Colors.TOGGLE_ACTIVE_GREEN;
                style.onNormal.textColor = Colors.TOGGLE_ACTIVE_GREEN;
                style.fontStyle = FontStyle.Bold;
                return style;
            }

            public static GUIStyle Help()
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.normal.textColor = Colors.INFO_ORANGE;
                style.wordWrap = true;
                style.fontSize = 10;
                return style;
            }

            public static GUIStyle SmallHelp()
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.fontSize = 10;
                style.normal.textColor = Colors.TEXT_DIM;
                return style;
            }

            public static GUIStyle Error()
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.normal.textColor = Color.red;
                style.wordWrap = true;
                return style;
            }

            public static GUIStyle DropdownBox()
            {
                return new GUIStyle(HighLogic.Skin.box);
            }

            public static GUIStyle Tooltip()
            {
                return new GUIStyle(HighLogic.Skin.box);
            }

            public static GUIStyle ColorButton(Color backgroundColor)
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.button);
                Texture2D tex = MakeColorTexture(backgroundColor);
                style.normal.background = tex;
                style.normal.textColor = Color.black;
                style.hover.background = tex;
                style.hover.textColor = Color.black;
                style.active.background = tex;
                style.active.textColor = Color.black;
                style.focused.background = tex;
                style.focused.textColor = Color.black;
                style.onNormal.background = tex;
                style.onNormal.textColor = Color.black;
                style.onHover.background = tex;
                style.onHover.textColor = Color.black;
                style.onActive.background = tex;
                style.onActive.textColor = Color.black;
                style.onFocused.background = tex;
                style.onFocused.textColor = Color.black;
                return style;
            }

            private static Texture2D MakeColorTexture(Color color)
            {
                Texture2D tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, color);
                tex.Apply();
                return tex;
            }
        }
        #endregion
    }
}