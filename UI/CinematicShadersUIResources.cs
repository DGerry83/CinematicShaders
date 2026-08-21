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

            // Native text/HUD color: white ARGB passed to native text render calls
            public const uint HudTextWhiteArgb = 0xFFFFFFFF;

            // Star Console window chrome
            public static readonly Color CONSOLE_BORDER_GREY = new Color(0.7f, 0.7f, 0.7f, 1f);
            public static readonly Color CRT_BACKGROUND = Color.black;

            // Toolbar icon fallback when the texture is missing (distinct from INFO_ORANGE)
            public static readonly Color TOOLBAR_FALLBACK_ORANGE = new Color(1f, 0.5f, 0f);

            /// <summary>
            /// Navball icon palette (RGB) - indices match the native struct order:
            /// 0 Prograde, 1 Retrograde, 2 Normal, 3 AntiNormal, 4 Radial In, 5 Radial Out, 6 Maneuver
            /// </summary>
            public static readonly Color[] NavballIconColors = new Color[]
            {
                new Color(184f/255f, 220f/255f, 141f/255f),  // 0: Prograde - Sage green (greener)
                new Color(184f/255f, 220f/255f, 141f/255f),  // 1: Retrograde - Sage green (greener)
                new Color(182f/255f, 123f/255f, 182f/255f),  // 2: Normal - Purple
                new Color(182f/255f, 123f/255f, 182f/255f),  // 3: AntiNormal - Purple
                new Color(120f/255f, 210f/255f, 210f/255f),  // 4: Radial In - Brighter cyan
                new Color(120f/255f, 210f/255f, 210f/255f),  // 5: Radial Out - Brighter cyan
                new Color(122f/255f, 134f/255f, 210f/255f)   // 6: Maneuver - Brighter blue
            };

            /// <summary>
            /// Packs a Color as a uint in ARGB format (0xFFRRGGBB, A=FF) for native rendering.
            /// </summary>
            public static uint PackArgb(Color c)
            {
                uint r = (uint)(c.r * 255) & 0xFF;
                uint g = (uint)(c.g * 255) & 0xFF;
                uint b = (uint)(c.b * 255) & 0xFF;
                return 0xFF000000 | (r << 16) | (g << 8) | b;
            }

            public static class GridColors
            {
                public static readonly Color Seafoam = new Color(0.1f, 0.9f, 0.7f);
                public static readonly Color Amber = new Color(1.0f, 0.65f, 0.0f);
                public static readonly Color White = new Color(0.85f, 0.95f, 1.0f);
                public static readonly Color Green = new Color(0.25f, 1.0f, 0.0f);

                public static readonly Color[] All = { Seafoam, Amber, White, Green };
            }
            
            /// <summary>
            /// CRT display colors - mapped from Kartographer grid color selection.
            /// These are the colors used for the Star Console UI text.
            /// </summary>
            public static class CRTColors
            {
                public static readonly Color Seafoam = new Color(0.102f, 0.902f, 0.702f); // 26, 230, 179
                public static readonly Color Amber   = new Color(1.000f, 0.651f, 0.000f); // 255, 166, 0
                public static readonly Color White   = new Color(0.922f, 0.941f, 0.980f); // 235, 240, 250
                public static readonly Color Green   = new Color(0.251f, 1.000f, 0.000f); // 64, 255, 0

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
                    return PackArgb(GetColor(colorIndex));
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
                public const float HEIGHT_PADDING = 10f;
                public const float CLAMP_MARGIN = 5f;
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

            public static GUIStyle RichTextToggle()
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.toggle);
                style.richText = true;
                return style;
            }

            // Star Console title bar styles
            public static GUIStyle ConsoleTitle()
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.alignment = TextAnchor.MiddleCenter;
                style.fontStyle = FontStyle.Bold;
                return style;
            }

            public static GUIStyle ConsoleCloseButton()
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.button);
                style.fontSize = 12;
                style.padding = new RectOffset(2, 2, 2, 2);
                return style;
            }

            public static GUIStyle ConsolePwrButton()
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.button);
                style.fontSize = 11;
                style.alignment = TextAnchor.MiddleLeft;
                style.padding = new RectOffset(4, 4, 2, 2);
                return style;
            }

            public static GUIStyle ConsolePwrButtonActive()
            {
                GUIStyle style = new GUIStyle(ConsolePwrButton());
                style.normal.textColor = Colors.TOGGLE_ACTIVE_GREEN; // D6: snapped from (0.2,0.9,0.3)
                return style;
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

        #region Fonts
        public static class Fonts
        {
            // HUD/console font, shipped in PluginData/Fonts/
            public const string HudFontFileName = "AcPlus_Rainbow100_re_66.ttf";

            /// <summary>
            /// Builds the absolute HUD font path: ../PluginData/Fonts/ relative to the
            /// managed DLL (which sits in Plugins/). Shared by all native text users.
            /// </summary>
            public static string GetHudFontPath()
            {
                string assemblyPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(assemblyPath, "..", "PluginData", "Fonts", HudFontFileName));
            }
        }
        #endregion

        #region Textures
        public static class Textures
        {
            // GameDatabase texture URL for the toolbar button
            public const string ToolbarIconPath = "CinematicShaders/Icons/ToolbarIcon";

            // Navball icon PNGs live under GameData/CinematicShaders/PluginData/NavballIcons
            public const string NavballIconsFolder = "NavballIcons";

            // Navball icon textures - KSP (default) style
            public static readonly string[] NavballIconFileNamesKSP = {
                "prograde_sdf.png",
                "retrograde_sdf.png",
                "normal_sdf.png",
                "antinormal_sdf.png",
                "radial_in_sdf.png",
                "radial_out_sdf.png",
                "maneuver_sdf.png"
            };

            // Navball icon textures - Retro style
            public static readonly string[] NavballIconFileNamesRetro = {
                "prograde_retro_sdf.png",
                "retrograde_retro_sdf.png",
                "normal_retro_sdf.png",
                "antinormal_retro_sdf.png",
                "radial_in_retro_sdf.png",
                "radial_out_retro_sdf.png",
                "maneuver_retro_sdf.png"
            };

            public const string NavballHeadingIconKsp = "heading_sdf.png";
            public const string NavballHeadingIconRetro = "heading_retro_sdf.png";
        }
        #endregion
    }
}