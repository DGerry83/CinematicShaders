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
        
        // Click zone for SCAN area
        private ClickZone _scanZone;
        private bool _scanHovered = false;
        
        // Define Layer 3 priority order (simpler - for future interactive elements)
        protected override List<string> Layer3PriorityOrder => new List<string>
        {
            "scan_prompt" // If we add interactive elements later
        };
        
        public event System.Action OnScanClicked;
        
        public ScanScreen(string[] borderLines, string[] artLines, float fontSize, float aspectRatio = 0.667f)
        {
            ScreenName = "Scan";
            _fontSize = fontSize;
            _aspectRatio = aspectRatio;
            
            // Add layers
            AddLayer(new BorderLayer(borderLines));
            AddLayer(new ContentLayer(artLines));
        }
        
        /// <summary>
        /// Set the shared textures for rendering. ScanScreen uses l1/l2, ignores l3.
        /// </summary>
        public override void SetTextures(RenderTexture l1, RenderTexture l2, RenderTexture l3)
        {
            _layer1Texture = l1;
            _layer2Texture = l2;
            // Ignore l3 - this screen doesn't use Layer 3
            
            if (Layers.Count > 0 && Layers[0] is BorderLayer bl)
                bl.SetTargetTexture(l1);
            if (Layers.Count > 1 && Layers[1] is ContentLayer cl)
                cl.SetTargetTexture(l2);
        }
        
        public override void OnEnter(ScreenTransitionContext context)
        {
            base.OnEnter(context);
            
            _sequencer = new Sequencer(Layer3PriorityOrder);
            OnLayer2Complete += StartLayer3Animation;
            
            // Initialize single large click zone for SCAN area
            _scanZone = new ClickZone("scan_area", HolographicLayoutConfig.ZONE_SCAN_AREA, true);
            _scanHovered = false;
        }
        
        public override void OnExit()
        {
            base.OnExit();
            
            OnLayer2Complete -= StartLayer3Animation;
            _sequencer?.StopSequence();
            _sequencer = null;
            
            // Clear hover state
            _scanHovered = false;
            StarfieldNative.CR_SetBoxOutline(0, 0, 0, 0, 0);
        }
        
        /// <summary>
        /// Handle mouse interaction for SCAN area
        /// </summary>
        public void HandleMouse(Vector2 mousePos, Rect displayRect, bool mouseDown, bool mouseUp)
        {
            Vector2 gridPos = MouseToGrid(mousePos, displayRect);
            
            bool wasHovered = _scanHovered;
            _scanHovered = _scanZone.Contains(gridPos);
            
            // Update box outline on hover change
            if (_scanHovered != wasHovered)
            {
                if (_scanHovered)
                {
                    Rect uvRect = _scanZone.GetUVRect();
                    StarfieldNative.CR_SetBoxOutline(1, uvRect.xMin, uvRect.yMin, uvRect.xMax, uvRect.yMax);
                }
                else
                {
                    StarfieldNative.CR_SetBoxOutline(0, 0, 0, 0, 0);
                }
            }
            
            // Handle click
            if (mouseUp && _scanHovered)
            {
                OnScanClicked?.Invoke();
            }
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
            if (borderLayer != null && _layer1Texture != null && _layer1Texture.IsCreated())
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
            if (contentLayer != null && _layer2Texture != null && _layer2Texture.IsCreated() && Layer2Progress > 0)
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
            
            // Handle mouse interaction
            if (Event.current != null)
            {
                Vector2 mousePos = Event.current.mousePosition;
                bool mouseDown = Event.current.type == EventType.MouseDown && Event.current.button == 0;
                bool mouseUp = Event.current.type == EventType.MouseUp && Event.current.button == 0;
                HandleMouse(mousePos, displayRect, mouseDown, mouseUp);
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
    }
}
