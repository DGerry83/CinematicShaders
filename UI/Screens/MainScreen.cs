using System;
using UnityEngine;
using CinematicShaders.UI.Screens.Layers;
using CinematicShaders.Native;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Main screen showing star data with search results.
    /// Layers: 1 (border), 2 (labels), 3 (value fields - handled separately)
    /// </summary>
    public class MainScreen : BaseScreen
    {
        private readonly float _fontSize;
        private readonly float _aspectRatio;
        
        public MainScreen(string[] borderLines, string[] labelLines, float fontSize, float aspectRatio = 0.667f)
        {
            State = ScreenState.Main;
            ScreenName = "Main";
            _fontSize = fontSize;
            _aspectRatio = aspectRatio;
            
            // Add layers
            AddLayer(new BorderLayer(borderLines));
            AddLayer(new ContentLayer(labelLines));
        }
        
        public override void Render(Rect displayRect, IntPtr textSystem)
        {
            if (textSystem == IntPtr.Zero) return;
            
            uint color = GetGridColorUint();
            
            // Render Layer 1: Border
            var borderLayer = Layers[0] as BorderLayer;
            if (borderLayer != null)
            {
                string text = borderLayer.GetTextForProgress(Layer1Progress);
                RenderLayer(textSystem, text, color, 0);
            }
            
            // Render Layer 2: Labels
            var contentLayer = Layers[1] as ContentLayer;
            if (contentLayer != null)
            {
                contentLayer.RenderToTexture(textSystem, color, _fontSize, _aspectRatio, Layer2Progress);
            }
        }
        
        private void RenderLayer(IntPtr textSystem, string text, uint color, int layerIndex)
        {
            if (textSystem == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
            
            // Note: This is a simplified render - actual implementation will use
            // the screen manager's shared texture. For now, this demonstrates the pattern.
            int glyphCount = StarfieldNative.CR_TextLayoutEx(textSystem, text, _fontSize, 
                color, 0f, 0f, 0f, _aspectRatio);
            
            if (glyphCount > 0)
            {
                // Rendering happens via the layer's RenderToTexture in full implementation
            }
        }
        
        private uint GetGridColorUint()
        {
            // Default seafoam grid color (0.1, 0.9, 0.7)
            Color color = new Color(0.1f, 0.9f, 0.7f);
            return ((uint)(color.a * 255) << 24) |
                   ((uint)(color.b * 255) << 16) |
                   ((uint)(color.g * 255) << 8) |
                   (uint)(color.r * 255);
        }
    }
}
