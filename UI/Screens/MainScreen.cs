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
        private RenderTexture _deferredLayer3Texture;  // Stores texture if SetLayer3Texture called before SetElements
        
        // Click zone tracking for grid-based hit detection
        private List<ClickZone> _clickZones = new List<ClickZone>();
        private ClickZone? _hoveredZone = null;
        
        /// <summary>
        /// Event fired when an interactive element is clicked
        /// </summary>
        public event Action<string> OnElementClicked;
        
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
            
            // Apply deferred texture if SetLayer3Texture was called before we had elements
            if (_deferredLayer3Texture != null)
            {
                ModFileLogger.Log("[MainScreen] SetElements - applying deferred Layer 3 texture");
                _elementLayer.SetLayer3Texture(_deferredLayer3Texture);
                _deferredLayer3Texture = null;
            }
        }
        
        /// <summary>
        /// Get the ElementLayer for external access (e.g., cursor state management).
        /// </summary>
        public ElementLayer GetElementLayer()
        {
            return _elementLayer;
        }
        
        /// <summary>
        /// Set cursor state in ElementLayer. Passes through to ElementLayer.SetCursorState().
        /// </summary>
        public void SetCursorState(string elementId, bool visible)
        {
            _elementLayer?.SetCursorState(elementId, visible);
        }
        
        /// <summary>
        /// Mark the ElementLayer as dirty to trigger a redraw.
        /// </summary>
        public void MarkElementLayerDirty()
        {
            _elementLayer?.MarkLayer3Dirty();
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
        
        /// <summary>
        /// Set the Layer 3 texture for single-texture rendering.
        /// Supports deferred assignment if called before SetElements.
        /// </summary>
        public override void SetLayer3Texture(RenderTexture layer3Texture)
        {
            ModFileLogger.Log($"[MainScreen] SetLayer3Texture ENTER - instance {GetHashCode()}, layer3Texture is {(layer3Texture != null ? "valid" : "NULL")}, _elementLayer is {(_elementLayer != null ? "set" : "NULL")}");
            
            if (_elementLayer == null)
            {
                ModFileLogger.Log($"[MainScreen] SetLayer3Texture - DEFERRED, _elementLayer is null");
                _deferredLayer3Texture = layer3Texture;
                return;
            }
            
            ModFileLogger.Log($"[MainScreen] SetLayer3Texture - applying immediately");
            _elementLayer.SetLayer3Texture(layer3Texture);
        }
        
        public override void OnEnter(ScreenTransitionContext context)
        {
            base.OnEnter(context);
            
            // Create sequencer with our priority order
            _sequencer = new Sequencer(Layer3PriorityOrder);
            
            // Subscribe to Layer 2 completion to start Layer 3
            OnLayer2Complete += StartLayer3Animation;
            
            // Initialize click zones for grid-based hit detection
            InitializeClickZones(context?.HasStarSelected ?? false);
            
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
            
            // Clear click zones and hover state
            _clickZones.Clear();
            _hoveredZone = null;
            StarfieldNative.CR_SetBoxOutline(0, 0, 0, 0, 0);
        }
        
        /// <summary>
        /// Initialize click zones for grid-based hit detection.
        /// These are approximate positions - user will tune via debug exports.
        /// </summary>
        private void InitializeClickZones(bool hasStarSelected)
        {
            _clickZones.Clear();
            
            // Value fields (left column) - only name_value is editable, others clickable for consistency
            _clickZones.Add(new ClickZone("hip_value", HolographicLayoutConfig.ZONE_HIP_VALUE, hasStarSelected));
            _clickZones.Add(new ClickZone("name_value", HolographicLayoutConfig.ZONE_NAME_VALUE, hasStarSelected));
            _clickZones.Add(new ClickZone("distance_value", HolographicLayoutConfig.ZONE_DISTANCE_VALUE, hasStarSelected));
            _clickZones.Add(new ClickZone("spectral_value", HolographicLayoutConfig.ZONE_SPECTRAL_VALUE, hasStarSelected));
            _clickZones.Add(new ClickZone("mag_value", HolographicLayoutConfig.ZONE_MAG_VALUE, hasStarSelected));
            _clickZones.Add(new ClickZone("const_value", HolographicLayoutConfig.ZONE_CONST_VALUE, hasStarSelected));
            
            // Buttons (always enabled)
            _clickZones.Add(new ClickZone("save_button", HolographicLayoutConfig.ZONE_SAVE_BUTTON, true));
            _clickZones.Add(new ClickZone("reset_button", HolographicLayoutConfig.ZONE_RESET_BUTTON, true));
            _clickZones.Add(new ClickZone("rescan_button", HolographicLayoutConfig.ZONE_RESCAN_BUTTON, true));
            
            // Search input (always enabled)
            _clickZones.Add(new ClickZone("search_input", HolographicLayoutConfig.ZONE_SEARCH_INPUT, true));
            
            // Search results (10 rows) - enabled only when star selected
            for (int i = 0; i < 10; i++)
            {
                _clickZones.Add(new ClickZone($"result_{i}", HolographicLayoutConfig.GetResultZone(i), hasStarSelected));
            }
        }
        
        /// <summary>
        /// Handle mouse interaction for click zones
        /// </summary>
        public void HandleMouse(Vector2 mousePos, Rect displayRect, bool mouseDown, bool mouseUp)
        {
            // Convert mouse position to grid coordinates
            Vector2 gridPos = MouseToGrid(mousePos, displayRect);
            
            // Find hovered zone
            ClickZone? newHovered = null;
            foreach (var zone in _clickZones)
            {
                if (zone.IsEnabled && zone.Contains(gridPos))
                {
                    newHovered = zone;
                    break;
                }
            }
            
            // Handle hover change
            if (newHovered?.ElementId != _hoveredZone?.ElementId)
            {
                _hoveredZone = newHovered;
                
                // Set box outline in shader via native call
                if (_hoveredZone.HasValue)
                {
                    Rect uvRect = _hoveredZone.Value.GetUVRect();
                    StarfieldNative.CR_SetBoxOutline(1, uvRect.xMin, uvRect.yMin, uvRect.xMax, uvRect.yMax);
                }
                else
                {
                    StarfieldNative.CR_SetBoxOutline(0, 0, 0, 0, 0);
                }
            }
            
            // Handle click
            if (mouseUp && _hoveredZone.HasValue)
            {
                OnZoneClicked(_hoveredZone.Value.ElementId);
            }
        }
        
        private Vector2 MouseToGrid(Vector2 mousePos, Rect displayRect)
        {
            // Convert screen mouse position to local display coordinates
            float localX = mousePos.x - displayRect.x;
            float localY = mousePos.y - displayRect.y;
            
            // Convert to grid coordinates
            float gridX = localX / HolographicLayoutConfig.GRID_CELL_WIDTH;
            float gridY = localY / HolographicLayoutConfig.GRID_CELL_HEIGHT;
            
            return new Vector2(gridX, gridY);
        }
        
        private void OnZoneClicked(string elementId)
        {
            Debug.Log($"[MainScreen] Clicked: {elementId}");
            OnElementClicked?.Invoke(elementId);
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
            if (borderLayer != null && _layer1Texture != null && _layer1Texture.IsCreated())
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
            
            // Render Layer 3: Elements (value fields, buttons)
            // Pass PowerOnTime so elements can calculate their individual type-on progress
            // DEBUG: ModFileLogger.Log($"[MainScreen] Render - Layer3Progress={Layer3Progress}, _elementLayer is {(_elementLayer != null ? "valid" : "NULL")}");
            if (_elementLayer != null && Layer3Progress > 0)
            {
                _elementLayer.RenderToTexture(textSystem, displayRect, PowerOnTime);
            }
            
            // Handle mouse interaction for click zones
            if (Event.current != null)
            {
                Vector2 mousePos = Event.current.mousePosition;
                bool mouseDown = Event.current.type == EventType.MouseDown && Event.current.button == 0;
                bool mouseUp = Event.current.type == EventType.MouseUp && Event.current.button == 0;
                HandleMouse(mousePos, displayRect, mouseDown, mouseUp);
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
        public void OnStarSelected(NamedStar star)
        {
            ModFileLogger.Log($"[MainScreen] OnStarSelected called for HIP {star.HipparcosID}");
            if (star == null) return;
            
            // Update ElementLayer element values with star data
            _elementLayer?.SetElementText("hip_value", star.HipparcosID.ToString());
            _elementLayer?.SetElementText("name_value", star.Name);
            _elementLayer?.SetElementText("distance_value", $"{star.DistanceLy:F1} LY");
            _elementLayer?.SetElementText("spectral_value", star.SpectralType);
            _elementLayer?.SetElementText("mag_value", star.Magnitude.ToString("F2"));
            _elementLayer?.SetElementText("const_value", star.Constellation);
            _elementLayer?.SetElementText("selected_star", star.Name);
            
            // Notify ElementLayer of content changes for animation
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
            // CRITICAL: Clear the star data display
            // Clear value fields immediately (no animation)
            _elementLayer?.SetElementText("hip_value", "");
            _elementLayer?.SetElementText("name_value", "");
            _elementLayer?.SetElementText("distance_value", "");
            _elementLayer?.SetElementText("spectral_value", "");
            _elementLayer?.SetElementText("mag_value", "");
            _elementLayer?.SetElementText("const_value", "");
            _elementLayer?.SetElementText("selected_star", "");
            
            // Update element visibility to hide star-specific fields
            UpdateElementVisibility(hasStarSelected: false);
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
