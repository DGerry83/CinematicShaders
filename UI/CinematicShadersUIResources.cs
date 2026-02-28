using UnityEngine;

namespace CinematicShaders.UI
{
    public static class CinematicShadersUIResources
    {
        private static Texture2D CreateTexture(Color color)
        {
            Color[] pixels = new Color[4];
            for (int i = 0; i < 4; i++) pixels[i] = color;
            Texture2D result = new Texture2D(2, 2);
            result.SetPixels(pixels);
            result.Apply();
            return result;
        }

        public static class Colors
        {
            public static readonly Color TOGGLE_ACTIVE_GREEN = new Color(0.2f, 0.9f, 0.2f);
            public static readonly Color INFO_ORANGE = new Color(1f, 0.5490196f, 0f);
        }

        public static class Styles
        {
            public static GUIStyle Window()
            {
                return new GUIStyle(HighLogic.Skin.window);
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
        }
    }
}