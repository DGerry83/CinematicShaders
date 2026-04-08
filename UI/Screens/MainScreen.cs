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
        private RenderTexture _layer1Texture;
        private RenderTexture _layer2Texture;
        
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
        
        /// <summary>
        /// Set the shared textures for rendering
        /// </summary>
        public void SetTextures(RenderTexture layer1Texture, RenderTexture layer2Texture)
        {
            _layer1Texture = layer1Texture;
            _layer2Texture = layer2Texture;
            
            // Set textures on layers
            if (Layers.Count > 0 && Layers[0] is BorderLayer bl)
                bl.SetTargetTexture(layer1Texture);
            if (Layers.Count > 1 && Layers[1] is ContentLayer cl)
                cl.SetTargetTexture(layer2Texture);
        }
        
        public override void Render(Rect displayRect, IntPtr textSystem)
        {
            if (textSystem == IntPtr.Zero) return;
            
            // Only render during Repaint events and when Event.current is valid
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            
            uint color = GetGridColorUint();
            
            // Render Layer 1: Border to texture, then draw
            var borderLayer = Layers[0] as BorderLayer;
            if (borderLayer != null && _layer1Texture != null)
            {
                // Render with type-on progress
                borderLayer.RenderToTexture(textSystem, color, _fontSize, _aspectRatio, Layer1Progress);
                
                // Draw the texture to screen
                Graphics.DrawTexture(
                    displayRect,
                    _layer1Texture,
                    new Rect(0, 1, 1, -1),  // Flip Y
                    0, 0, 0, 0,
                    Color.white,
                    null
                );
            }
            
            // Render Layer 2: Labels to texture, then draw
            var contentLayer = Layers[1] as ContentLayer;
            if (contentLayer != null && _layer2Texture != null && Layer2Progress > 0)
            {
                contentLayer.RenderToTexture(textSystem, color, _fontSize, _aspectRatio, Layer2Progress);
                
                // Draw the texture to screen
                Graphics.DrawTexture(
                    displayRect,
                    _layer2Texture,
                    new Rect(0, 1, 1, -1),  // Flip Y
                    0, 0, 0, 0,
                    Color.white,
                    null
                );
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
