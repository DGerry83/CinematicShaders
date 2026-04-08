using System;
using UnityEngine;
using CinematicShaders.UI.Screens.Layers;
using CinematicShaders.Native;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Scan screen shown when no JSON data is available.
    /// Layers: 1 (border), 2 (SCAN ASCII art)
    /// Interaction: Clicking SCAN art triggers rescan.
    /// </summary>
    public class ScanScreen : BaseScreen
    {
        private readonly float _fontSize;
        private readonly float _aspectRatio;
        
        public event System.Action OnScanClicked;
        
        public ScanScreen(string[] borderLines, string[] artLines, float fontSize, float aspectRatio = 0.667f)
        {
            State = ScreenState.Scan;
            ScreenName = "Scan";
            _fontSize = fontSize;
            _aspectRatio = aspectRatio;
            
            // Add layers
            AddLayer(new BorderLayer(borderLines));
            AddLayer(new ContentLayer(artLines));
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
                RenderLayerText(textSystem, text, color);
            }
            
            // Render Layer 2: SCAN art
            var contentLayer = Layers[1] as ContentLayer;
            if (contentLayer != null)
            {
                contentLayer.RenderToTexture(textSystem, color, _fontSize, _aspectRatio, Layer2Progress);
            }
        }
        
        /// <summary>
        /// Handle click detection. Returns true if SCAN art was clicked.
        /// </summary>
        public bool HandleClick(Vector2 mousePos, Rect displayRect)
        {
            // SCAN art is centered in the display
            // This is a simplified check - actual implementation may need refinement
            if (displayRect.Contains(mousePos))
            {
                OnScanClicked?.Invoke();
                return true;
            }
            return false;
        }
        
        private void RenderLayerText(IntPtr textSystem, string text, uint color)
        {
            if (textSystem == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
            
            int glyphCount = StarfieldNative.CR_TextLayoutEx(textSystem, text, _fontSize, 
                color, 0f, 0f, 0f, _aspectRatio);
            
            if (glyphCount > 0)
            {
                // Rendering happens via the layer's RenderToTexture in full implementation
            }
        }
        
        private uint GetGridColorUint()
        {
            Color color = new Color(0.1f, 0.9f, 0.7f);
            return ((uint)(color.a * 255) << 24) |
                   ((uint)(color.b * 255) << 16) |
                   ((uint)(color.g * 255) << 8) |
                   (uint)(color.r * 255);
        }
    }
}
