using System;
using UnityEngine;
using CinematicShaders.UI.Screens.Layers;
using CinematicShaders.Native;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Confirmation dialog for rescan operation.
    /// Layers: 1 (border), 2 (warning text), 3 (YES/NO buttons)
    /// </summary>
    public class ConfirmRescanScreen : BaseScreen
    {
        private readonly float _fontSize;
        private readonly float _aspectRatio;
        
        public bool YesSelected { get; private set; }
        public bool NoSelected { get; private set; }
        
        public event System.Action OnYesClicked;
        public event System.Action OnNoClicked;
        
        public ConfirmRescanScreen(string[] borderLines, string[] textLines, float fontSize, float aspectRatio = 0.667f)
        {
            State = ScreenState.ConfirmRescan;
            ScreenName = "ConfirmRescan";
            _fontSize = fontSize;
            _aspectRatio = aspectRatio;
            
            // Add layers
            AddLayer(new BorderLayer(borderLines));
            AddLayer(new ContentLayer(textLines));
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
            
            // Render Layer 2: Warning text
            var contentLayer = Layers[1] as ContentLayer;
            if (contentLayer != null)
            {
                contentLayer.RenderToTexture(textSystem, color, _fontSize, _aspectRatio, Layer2Progress);
            }
            
            // Layer 3: YES/NO buttons are rendered separately by the display
            // as they require interactive hover states
        }
        
        /// <summary>
        /// Update hover states and handle clicks for YES/NO buttons.
        /// Call this from the display's interaction handling.
        /// </summary>
        public void UpdateInteraction(Vector2 mousePos, Rect displayRect, bool mouseDown, bool mouseUp)
        {
            // Calculate YES/NO button positions
            // These positions should match the [YES] and [NO] positions in CONFIRM_LAYER2_LINES
            float lineHeight = _fontSize * 1.33f; // Approximate line height
            float charWidth = _fontSize * 0.6f;   // Approximate char width
            
            // YES at position (3, 10) - approx
            float yesX = displayRect.x + (charWidth * 3);
            float yesY = displayRect.y + (lineHeight * 10);
            Rect yesRect = new Rect(yesX, yesY, charWidth * 6, lineHeight);
            
            // NO at position (47, 10) - approx
            float noX = displayRect.x + displayRect.width - (charWidth * 8);
            float noY = displayRect.y + (lineHeight * 10);
            Rect noRect = new Rect(noX, noY, charWidth * 6, lineHeight);
            
            // Update hover states
            bool wasYesSelected = YesSelected;
            bool wasNoSelected = NoSelected;
            
            YesSelected = yesRect.Contains(mousePos);
            NoSelected = noRect.Contains(mousePos);
            
            // Handle clicks
            if (mouseUp)
            {
                if (YesSelected && wasYesSelected)
                {
                    OnYesClicked?.Invoke();
                }
                else if (NoSelected && wasNoSelected)
                {
                    OnNoClicked?.Invoke();
                }
            }
        }
        
        public void ResetSelection()
        {
            YesSelected = false;
            NoSelected = false;
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
