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
    /// The main data display screen for the holographic console.
    /// Shows star information including HIP ID, name, distance, spectral type, magnitude, and constellation.
    /// </summary>
    /// <remarks>
    /// <para><b>Layer Configuration:</b></para>
    /// - Layer 1: Border frame and static header elements
    /// - Layer 2: Field labels ("HIP:", "NAME:", "DISTANCE:", etc.)
    /// - Layer 3: Dynamic value fields, buttons, and search input
    /// 
    /// <para><b>Animation Sequence:</b></para>
    /// 1. Border types on (0-2s)
    /// 2. Labels type on (2-3.5s)
    /// 3. Value fields and buttons appear in priority order (3.5s+)
    /// 
    /// <para><b>Interactions:</b></para>
    /// - Click on value fields to edit (name field is editable)
    /// - Click buttons (SAVE, RESET, RESCAN) for actions
    /// - Type in search box to find stars
    /// - Click search results to select stars
    /// 
    /// <para><b>Click Zone System:</b></para>
    /// Uses grid-based hit detection with ClickZone objects defined in
    /// HolographicLayoutConfig. Zones are enabled/disabled based on
    /// whether a star is currently selected.
    /// </remarks>
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
        /// Event fired when an interactive element is clicked.
        /// String parameter is the element ID (e.g., "name_value", "save_button").
        /// </summary>
        public event Action<string> OnElementClicked;
        
        /// <summary>
        /// Layer 3 priority order for type-on animation sequence.
        /// Elements appear in this order after Layer 2 completes.
        /// </summary>
        /// <remarks>
        /// Star data fields appear first, followed by search and action buttons.
        /// Elements not in this list use default priority (appear last).
        /// </remarks>
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
        
        /// <summary>
        /// Initializes a new MainScreen with the specified content and styling.
        /// </summary>
        /// <param name="borderLines">ASCII art lines for the border frame</param>
        /// <param name="labelLines">Label text lines for Layer 2</param>
        /// <param name="fontSize">Font size for text rendering</param>
        /// <param name="aspectRatio">Aspect ratio for layout (default 0.667 = 2:3)</param>
        public MainScreen(string[] borderLines, string[] labelLines, float fontSize, float aspectRatio = 0.667f)
        {
            ScreenName = "Main";
            _fontSize = fontSize;
            _aspectRatio = aspectRatio;
            
            // Add layers
            AddLayer(new BorderLayer(borderLines));
            AddLayer(new ContentLayer(labelLines));
            // ElementLayer is added separately via SetElements
        }
        
        /// <summary>
        /// Sets the elements for Layer 3 rendering.
        /// Must be called before the screen is shown to initialize value fields and buttons.
        /// </summary>
        /// <param name="elements">List of text elements for value fields and buttons</param>
        /// <remarks>
        /// This creates the ElementLayer which manages dynamic content.
        /// If SetTextures() was called before this method, the Layer 3 texture
        /// is applied automatically once elements are available.
        /// </remarks>
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
        /// Gets the ElementLayer for external access to cursor state management.
        /// </summary>
        /// <returns>The ElementLayer instance, or null if not yet initialized</returns>
        public ElementLayer GetElementLayer()
        {
            return _elementLayer;
        }
        
        /// <summary>
        /// Sets the cursor visibility state in the ElementLayer.
        /// </summary>
        /// <param name="elementId">The element ID to set cursor for</param>
        /// <param name="visible">True to show cursor, false to hide</param>
        /// <remarks>
        /// Used by HolographicDisplay to synchronize cursor blink state
        /// with the currently focused text input element.
        /// </remarks>
        public void SetCursorState(string elementId, bool visible)
        {
            _elementLayer?.SetCursorState(elementId, visible);
        }
        
        /// <summary>
        /// Marks the ElementLayer as dirty, forcing a redraw on next render.
        /// </summary>
        /// <remarks>
        /// Call this when element content changes (e.g., during typing)
        /// to ensure the texture is updated.
        /// </remarks>
        public void MarkElementLayerDirty()
        {
            _elementLayer?.MarkLayer3Dirty();
        }
        
        /// <summary>
        /// Sets the shared textures for rendering all three layers.
        /// </summary>
        /// <param name="l1">Layer 1 texture (border)</param>
        /// <param name="l2">Layer 2 texture (labels)</param>
        /// <param name="l3">Layer 3 texture (elements)</param>
        /// <remarks>
        /// Textures are assigned to their respective layers:
        /// - l1 → BorderLayer
        /// - l2 → ContentLayer  
        /// - l3 → ElementLayer (or deferred if elements not ready)
        /// </remarks>
        public override void SetTextures(RenderTexture l1, RenderTexture l2, RenderTexture l3)
        {
            _layer1Texture = l1;
            _layer2Texture = l2;
            
            // Apply l3 to ElementLayer (with deferred assignment if elements not ready)
            if (_elementLayer != null)
            {
                _elementLayer.SetLayer3Texture(l3);
            }
            else
            {
                _deferredLayer3Texture = l3;
            }
            
            // Set textures on border/content layers
            if (Layers.Count > 0 && Layers[0] is BorderLayer bl)
                bl.SetTargetTexture(l1);
            if (Layers.Count > 1 && Layers[1] is ContentLayer cl)
                cl.SetTargetTexture(l2);
        }
        
        /// <summary>
        /// Called when entering this screen. Initializes animations and click zones.
        /// </summary>
        /// <param name="context">Transition context with star selection state</param>
        /// <remarks>
        /// Sets up the sequencer for Layer 3 animations, initializes click zones
        /// based on whether a star is selected, and configures element visibility.
        /// Always call base.OnEnter() first when overriding.
        /// </remarks>
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
        
        /// <summary>
        /// Called when exiting this screen. Cleans up animations and click zones.
        /// </summary>
        /// <remarks>
        /// Unsubscribes from events, stops the sequencer, hides elements,
        /// and clears the box outline. Always call base.OnExit() when overriding.
        /// </remarks>
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
        /// Initializes click zones for grid-based hit detection.
        /// Zones are defined in HolographicLayoutConfig.
        /// </summary>
        /// <param name="hasStarSelected">Whether a star is currently selected</param>
        /// <remarks>
        /// Creates zones for:
        /// - Value fields (enabled only when star selected)
        /// - Action buttons (always enabled)
        /// - Search input (always enabled)
        /// - Search results (enabled only when star selected)
        /// </remarks>
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
        /// Handles mouse interaction for click zones.
        /// </summary>
        /// <param name="mousePos">Current mouse position in screen coordinates</param>
        /// <param name="displayRect">Display rectangle in screen coordinates</param>
        /// <param name="mouseDown">True if left mouse button was pressed this frame</param>
        /// <param name="mouseUp">True if left mouse button was released this frame</param>
        /// <remarks>
        /// Converts mouse position to grid coordinates, detects hover over click zones,
        /// updates the box outline visual, and fires OnElementClicked when a zone is clicked.
        /// </remarks>
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
        
        /// <summary>
        /// Called when a click zone is clicked.
        /// </summary>
        /// <param name="elementId">ID of the clicked element</param>
        private void OnZoneClicked(string elementId)
        {
            Debug.Log($"[MainScreen] Clicked: {elementId}");
            OnElementClicked?.Invoke(elementId);
        }
        
        /// <summary>
        /// Renders this screen.
        /// </summary>
        /// <param name="displayRect">Screen rectangle for rendering</param>
        /// <param name="textSystem">Native text system pointer</param>
        /// <remarks>
        /// Renders all three layers in order:
        /// 1. Border (Layer 1) - always rendered
        /// 2. Labels (Layer 2) - only if progress > 0
        /// 3. Elements (Layer 3) - only if progress > 0
        /// 
        /// Also handles mouse interaction during repaint events.
        /// </remarks>
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
        /// Updates element visibility based on whether a star is selected.
        /// </summary>
        /// <param name="hasStarSelected">True if a star is currently selected</param>
        /// <remarks>
        /// Value fields are only visible when a star is selected.
        /// Buttons and search input are always visible on the Main screen.
        /// </remarks>
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
        /// Triggers type-on animation for value fields when star data changes.
        /// </summary>
        /// <param name="startTime">Time offset for animation start</param>
        public void TriggerValueTypeOnAnimation(float startTime)
        {
            _elementLayer?.SetupMainScreenAnimation(hasStarSelected: true);
        }
        
        /// <summary>
        /// Starts the Layer 3 animation sequence when Layer 2 completes.
        /// </summary>
        private void StartLayer3Animation()
        {
            Debug.Log("[MainScreen] Layer 2 complete, starting Layer 3 animation");
            _sequencer?.StartSequence();
        }
        
        /// <summary>
        /// Updates this screen's animations.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last frame</param>
        /// <remarks>
        /// Also updates ElementLayer animations. Call base.Update() to maintain
        /// proper layer progress timing.
        /// </remarks>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            
            // Update ElementLayer animations
            _elementLayer?.UpdateAnimations(deltaTime);
        }
        
        /// <summary>
        /// Called when a star is selected. Updates display with star data.
        /// </summary>
        /// <param name="star">The selected star data</param>
        /// <remarks>
        /// Updates all value fields with the star's properties and notifies
        /// the sequencer of content changes for animation retriggering.
        /// </remarks>
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

        /// <summary>
        /// Called when the star selection is cleared. Clears all value fields.
        /// </summary>
        /// <remarks>
        /// Clears all value field text and updates visibility to hide star-specific fields.
        /// Buttons remain visible.
        /// </remarks>
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
            
            // Update element visibility to hide star-specific fields
            UpdateElementVisibility(hasStarSelected: false);
        }
    }
}
