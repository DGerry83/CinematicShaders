using System;
using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.UI.Screens.Layers;
using CinematicShaders.Native;
using CinematicShaders.Core;
using CinematicShaders.UI.Animation;

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
        private RenderTexture _layer1Texture;
        private RenderTexture _layer2Texture;
        private Sequencer _sequencer;
        
        // Define Layer 3 priority order (simpler - for future interactive elements)
        protected override List<string> Layer3PriorityOrder => new List<string>
        {
            "scan_prompt" // If we add interactive elements later
        };
        
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
        
        public override void OnEnter(ScreenTransitionContext context)
        {
            base.OnEnter(context);
            
            _sequencer = new Sequencer(Layer3PriorityOrder);
            OnLayer2Complete += StartLayer3Animation;
            
            // NOTE: ScanScreen doesn't have ElementLayer currently.
            // When ElementLayer is added in future, call _elementLayer.ResetAllAnimationStates() here.
        }
        
        public override void OnExit()
        {
            base.OnExit();
            
            OnLayer2Complete -= StartLayer3Animation;
            _sequencer?.StopSequence();
            _sequencer = null;
        }
        
        private void StartLayer3Animation()
        {
            Debug.Log("[ScanScreen] Layer 2 complete, starting Layer 3");
            _sequencer?.StartSequence();
        }
        
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _sequencer?.Update();
        }
        
        public override void Render(Rect displayRect, IntPtr textSystem)
        {
            if (textSystem == IntPtr.Zero) return;
            
            // Only render during Repaint events and when Event.current is valid
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            
            uint color = GetGridColorUint();
            
            // Render Layer 1: Border
            var borderLayer = Layers[0] as BorderLayer;
            if (borderLayer != null && _layer1Texture != null)
            {
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
            
            // Render Layer 2: SCAN art
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
        
        /// <summary>
        /// Handle click detection. Returns true if SCAN art was clicked.
        /// </summary>
        public bool HandleClick(Vector2 mousePos, Rect displayRect, bool mouseDown)
        {
            // Only trigger on actual click, not hover
            if (mouseDown && displayRect.Contains(mousePos))
            {
                OnScanClicked?.Invoke();
                return true;
            }
            return false;
        }
        
        private Color GetGridColor()
        {
            // Use Kartographer grid colors from settings
            int colorIndex = StarfieldSettings.KartographerGridColor;
            switch (colorIndex)
            {
                case 0: return new Color(0.1f, 0.9f, 0.7f);  // Seafoam
                case 1: return new Color(1.0f, 0.65f, 0.0f); // Amber
                case 2: return new Color(0.85f, 0.95f, 1.0f); // White
                case 3: return new Color(0.25f, 1.0f, 0.0f);  // Green
                default: return new Color(0.1f, 0.9f, 0.7f);  // Default seafoam
            }
        }
        
        private uint GetGridColorUint()
        {
            Color c = GetGridColor();
            uint r = (uint)(c.r * 255) & 0xFF;
            uint g = (uint)(c.g * 255) & 0xFF;
            uint b = (uint)(c.b * 255) & 0xFF;
            return 0xFF000000 | (r << 16) | (g << 8) | b;  // ARGB format (A=FF)
        }
    }
}
