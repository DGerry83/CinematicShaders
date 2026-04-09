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
    /// Main screen showing star data with search results.
    /// Layers: 1 (border), 2 (labels), 3 (value fields, buttons)
    /// </summary>
    public class MainScreen : BaseScreen
    {
        private readonly float _fontSize;
        private readonly float _aspectRatio;
        private RenderTexture _layer1Texture;
        private RenderTexture _layer2Texture;
        private ElementLayer _elementLayer;
        
        // Layer 3 priority order - star data first, then search, then buttons
        protected override List<string> Layer3PriorityOrder => new List<string>
        {
            "hip_value",
            "name_value", 
            "distance_value",
            "spectral_value",
            "mag_value",
            "const_value",
            "selected_star",
            "search_input",
            "rescan_button",
            "save_button",
            "reset_button"
        };
        
        public MainScreen(string[] borderLines, string[] labelLines, float fontSize, float aspectRatio = 0.667f)
        {
            State = ScreenState.Main;
            ScreenName = "Main";
            _fontSize = fontSize;
            _aspectRatio = aspectRatio;
            
            // Add layers
            AddLayer(new BorderLayer(borderLines));
            AddLayer(new ContentLayer(labelLines));
            // ElementLayer is added separately via SetElements
        }
        
        /// <summary>
        /// Set the elements for Layer 3 rendering.
        /// </summary>
        public void SetElements(List<HolographicTextElement> elements)
        {
            _elementLayer = new ElementLayer(elements, _fontSize);
            AddLayer(_elementLayer);
        }
        
        /// <summary>
        /// Set the shared textures for rendering Layers 1 and 2.
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
            
            // Create sequencer with our priority order
            _sequencer = new Sequencer(Layer3PriorityOrder);
            
            // Subscribe to Layer 2 completion to start Layer 3
            OnLayer2Complete += StartLayer3Animation;
            
            // Show elements when entering Main screen
            if (_elementLayer != null)
            {
                bool hasStar = context?.HasStarSelected ?? false;
                
                _elementLayer.SetElementVisibility(true);
                
                // Set up type-on animation for elements
                // Element delays are relative to Layer3Delay (0 = starts at Layer3Delay)
                _elementLayer.SetupMainScreenAnimation(hasStarSelected: hasStar);
                
                // CRITICAL: Reset all animation states for fresh start on every screen entry
                _elementLayer.ResetAllAnimationStates();
                
                // Set the Layer 3 base delay for element timing calculations
                _elementLayer.SetLayer3Delay(Layer3Delay);
                
                // Register elements with sequencer
                _elementLayer.RegisterWithSequencer(_sequencer);
                
                // CRITICAL: Ensure buttons are visible even if no star selected
                // Buttons should always be visible on Main screen
                _elementLayer.SetElementVisibility("save_button", true);
                _elementLayer.SetElementVisibility("reset_button", true);
                _elementLayer.SetElementVisibility("rescan_button", true);
                _elementLayer.SetElementVisibility("search_input", true);
            }
        }
        
        public override void OnExit()
        {
            base.OnExit();
            
            // Unsubscribe from events
            OnLayer2Complete -= StartLayer3Animation;
            
            // Unregister from sequencer
            if (_elementLayer != null && _sequencer != null)
            {
                _elementLayer.UnregisterFromSequencer(_sequencer);
            }
            
            // Stop sequencer
            _sequencer?.StopSequence();
            _sequencer = null;
            
            // Hide elements when leaving Main screen
            _elementLayer?.SetElementVisibility(false);
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
            
            // Render Layer 3: Elements (value fields, buttons)
            // Pass PowerOnTime so elements can calculate their individual type-on progress
            if (_elementLayer != null && Layer3Progress > 0)
            {
                _elementLayer.RenderToTexture(textSystem, displayRect, PowerOnTime);
            }
        }
        
        /// <summary>
        /// Update Layer 3 element visibility based on star selection state.
        /// </summary>
        public void UpdateElementVisibility(bool hasStarSelected)
        {
            if (_elementLayer == null) return;
            
            // Value fields are only visible when a star is selected
            string[] valueIds = { "hip_value", "name_value", "distance_value", 
                                  "spectral_value", "mag_value", "const_value", "selected_star" };
            foreach (var id in valueIds)
            {
                _elementLayer.SetElementVisibility(id, hasStarSelected);
            }
            
            // Buttons are always visible on Main screen
            _elementLayer.SetElementVisibility("save_button", true);
            _elementLayer.SetElementVisibility("reset_button", true);
            _elementLayer.SetElementVisibility("rescan_button", true);
            _elementLayer.SetElementVisibility("search_input", true);
        }
        
        /// <summary>
        /// Trigger type-on animation for value fields when star data changes.
        /// </summary>
        public void TriggerValueTypeOnAnimation(float startTime)
        {
            _elementLayer?.SetupMainScreenAnimation(hasStarSelected: true);
        }
        
        // Start Layer 3 animation when Layer 2 completes
        private void StartLayer3Animation()
        {
            Debug.Log("[MainScreen] Layer 2 complete, starting Layer 3 animation");
            _sequencer?.StartSequence();
        }
        
        // Override Update to also update ElementLayer
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            
            // Update ElementLayer animations
            _elementLayer?.UpdateAnimations(deltaTime);
        }
        
        // Add method for star selection changes
        public void OnStarSelected()
        {
            // Notify ElementLayer of content changes
            var changedIds = _elementLayer?.OnContentChanged(new[] { 
                "hip_value", "name_value", "distance_value", 
                "spectral_value", "mag_value", "const_value", "selected_star" 
            });
            
            // Notify sequencer of changes
            if (changedIds != null && changedIds.Count > 0 && _sequencer != null)
            {
                _sequencer.OnElementsChanged(changedIds);
            }
        }

        public void OnStarDeselected()
        {
            // Clear value fields immediately (no animation)
            _elementLayer?.SetElementText("hip_value", "");
            _elementLayer?.SetElementText("name_value", "");
            _elementLayer?.SetElementText("distance_value", "");
            _elementLayer?.SetElementText("spectral_value", "");
            _elementLayer?.SetElementText("mag_value", "");
            _elementLayer?.SetElementText("const_value", "");
            _elementLayer?.SetElementText("selected_star", "");
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
