using System;
using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.UI.Screens.Layers;
using CinematicShaders.Native;
using CinematicShaders.Core;
using CinematicShaders.UI.Animation;
using CinematicShaders.UI.Content;
using CinematicShaders.UI;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Confirmation dialog screen for rescan operations.
    /// Displays a warning message with YES/NO buttons for user confirmation.
    /// </summary>
    /// <remarks>
    /// <para><b>Layer Configuration:</b></para>
    /// - Layer 1: Border frame
    /// - Layer 2: Warning text and dialog content
    /// - Layer 3: YES/NO buttons (appear after content)
    /// 
    /// <para><b>Purpose:</b></para>
    /// This screen appears when the user initiates a catalog rescan while
    /// star data already exists. It confirms the user's intention to
    /// overwrite existing data before proceeding.
    /// 
    /// <para><b>Interactions:</b></para>
    /// - Click YES: Triggers OnYesClicked, proceeds with rescan
    /// - Click NO: Triggers OnNoClicked, returns to previous screen
    /// - Hover over buttons: Box outline appears
    /// 
    /// <para><b>Visual Design:</b></para>
    /// The dialog uses the standard border with warning text centered.
    /// Buttons appear in sequence after the warning text types on,
    /// following the Layer3PriorityOrder.
    /// </remarks>
    public class ConfirmRescanScreen : BaseScreen, IClickHandler
    {
        private readonly float _fontSize;
        private readonly float _aspectRatio;
        private RenderTexture _layer1Texture;
        private RenderTexture _layer2Texture;
        private Sequencer _sequencer;
        
        // Click zones for YES/NO buttons (legacy - kept for compatibility)
        private List<ClickZone> _clickZones = new List<ClickZone>();
        private ClickZone _hoveredZone = null;
        
        // New click handler (Simplified Click System)
        public ConfirmRescanClickHandler ClickHandler { get; private set; }
        public ClickZoneManager ZoneManager => ClickHandler?.ZoneManager;
        
        /// <summary>
        /// Layer 3 priority order for button appearance sequence.
        /// YES button appears first, followed by NO button.
        /// </summary>
        protected override List<string> Layer3PriorityOrder => new List<string>
        {
            "yes_button",
            "no_button"
        };
        
        /// <summary>
        /// Gets whether the YES button is currently selected (hovered).
        /// </summary>
        public bool YesSelected { get; private set; }
        
        /// <summary>
        /// Gets whether the NO button is currently selected (hovered).
        /// </summary>
        public bool NoSelected { get; private set; }
        
        /// <summary>
        /// Event fired when the YES button is clicked.
        /// Subscribe to proceed with the rescan operation.
        /// </summary>
        public event System.Action OnYesClicked;
        
        /// <summary>
        /// Event fired when the NO button is clicked.
        /// Subscribe to cancel and return to the previous screen.
        /// </summary>
        public event System.Action OnNoClicked;
        
        // Callback methods invoked by ConfirmRescanClickHandler
        
        public void OnYesButtonClicked()
        {
            ModFileLogger.Log("[ConfirmRescanScreen] OnYesButtonClicked");
            OnYesClicked?.Invoke();
        }
        
        public void OnNoButtonClicked()
        {
            ModFileLogger.Log("[ConfirmRescanScreen] OnNoButtonClicked");
            OnNoClicked?.Invoke();
        }
        
        public void OnElementHoverEnter(string elementId)
        {
            // Show hover highlight
            var zone = ClickHandler.ZoneManager.FindZoneById(elementId);
            if (zone != null)
            {
                // Convert grid rect to UV rect for native outline
                Rect uvRect = GridToUVRect(zone.GridRect);
                StarfieldNative.CR_SetBoxOutline(1, uvRect.xMin, uvRect.yMin, uvRect.xMax, uvRect.yMax);
            }
        }
        
        public void OnElementHoverExit(string elementId)
        {
            // Clear hover highlight
            StarfieldNative.CR_SetBoxOutline(0, 0, 0, 0, 0);
        }
        
        /// <summary>
        /// Converts a grid rect to UV coordinates for the native outline renderer.
        /// </summary>
        private Rect GridToUVRect(Rect gridRect)
        {
            float col = gridRect.x;
            float row = gridRect.y;
            float width = gridRect.width;
            float height = gridRect.height;
            
            float xMin = col / TerminalGridConfig.GRID_COLUMNS;
            float yMin = 1.0f - ((row + height) / TerminalGridConfig.GRID_ROWS);
            float xMax = (col + width) / TerminalGridConfig.GRID_COLUMNS;
            float yMax = 1.0f - (row / TerminalGridConfig.GRID_ROWS);
            
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }
        
        /// <summary>
        /// IClickHandler implementation - delegates to ClickHandler.
        /// </summary>
        public void HandleInput(Rect displayRect)
        {
            ClickHandler?.HandleInput(displayRect);
        }
        
        /// <summary>
        /// Initializes a new ConfirmRescanScreen with the specified content and styling.
        /// </summary>
        /// <param name="borderLines">ASCII art lines for the border frame</param>
        /// <param name="textLines">Warning text lines for the dialog</param>
        /// <param name="fontSize">Font size for text rendering</param>
        /// <param name="aspectRatio">Aspect ratio for layout (default 0.667 = 2:3)</param>
        public ConfirmRescanScreen(string[] borderLines, string[] textLines, float fontSize, float aspectRatio = 0.667f)
            : this(new CustomContent(borderLines, textLines), fontSize, aspectRatio)
        {
        }

        /// <summary>
        /// Initializes a new ConfirmRescanScreen using an IScreenContent provider.
        /// </summary>
        /// <param name="content">Content provider for border and content lines</param>
        /// <param name="fontSize">Font size for text rendering</param>
        /// <param name="aspectRatio">Aspect ratio for layout (default 0.667 = 2:3)</param>
        public ConfirmRescanScreen(IScreenContent content, float fontSize, float aspectRatio = 0.667f)
        {
            ScreenName = "ConfirmRescan";
            _fontSize = fontSize;
            _aspectRatio = aspectRatio;
            
            // Add layers using content
            AddLayer(new BorderLayer(content.BorderLines));
            AddLayer(new ContentLayer(content.ContentLines));
        }

        // Private helper class for backward compatibility
        private class CustomContent : IScreenContent
        {
            public string[] BorderLines { get; }
            public string[] ContentLines { get; }
            public CustomContent(string[] border, string[] content)
            {
                BorderLines = border;
                ContentLines = content;
            }
        }
        
        /// <summary>
        /// Sets the shared textures for rendering.
        /// ConfirmRescanScreen uses l1 and l2, ignores l3.
        /// </summary>
        /// <param name="l1">Layer 1 texture (border)</param>
        /// <param name="l2">Layer 2 texture (warning text)</param>
        /// <param name="l3">Layer 3 texture (ignored)</param>
        /// <remarks>
        /// Layer 3 is not used because buttons are rendered through
        /// the native shader system rather than to a texture.
        /// </remarks>
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
        
        /// <summary>
        /// Called when entering this screen. Initializes animations and click zones.
        /// </summary>
        /// <param name="context">Transition context</param>
        public override void OnEnter(ScreenTransitionContext context)
        {
            base.OnEnter(context);
            
            _sequencer = new Sequencer(Layer3PriorityOrder);
            OnLayer2Complete += StartLayer3Animation;
            
            // NEW: Create and setup click handler (Simplified Click System)
            ClickHandler = new ConfirmRescanClickHandler(this);
            ClickHandler.SetupZones();
            
            // Initialize YES/NO button zones (legacy - kept for compatibility)
            _clickZones.Clear();
            if (UnifiedGridConfig.USE_UNIFIED_GRID)
            {
                var yesDef = UnifiedGridRegistry.ConfirmRescanElements["yes_button"];
                var noDef = UnifiedGridRegistry.ConfirmRescanElements["no_button"];
                
                _clickZones.Add(new ClickZone(yesDef));
                _clickZones.Add(new ClickZone(noDef));
            }
            else
            {
                _clickZones.Add(new ClickZone("yes_button", HolographicLayoutConfig.ZONE_YES_BUTTON, true));
                _clickZones.Add(new ClickZone("no_button", HolographicLayoutConfig.ZONE_NO_BUTTON, true));
            }
            _hoveredZone = null;
        }
        
        /// <summary>
        /// Called when exiting this screen. Cleans up animations and hover state.
        /// </summary>
        public override void OnExit()
        {
            base.OnExit();
            
            OnLayer2Complete -= StartLayer3Animation;
            _sequencer?.StopSequence();
            _sequencer = null;
            
            // Clear click zones and hover state
            _clickZones.Clear();
            _hoveredZone = null;
            StarfieldNative.CR_SetBoxOutline(0, 0, 0, 0, 0);
        }
        
        /// <summary>
        /// Handles mouse interaction for YES/NO buttons.
        /// </summary>
        /// <param name="mousePos">Current mouse position in screen coordinates</param>
        /// <param name="displayRect">Display rectangle in screen coordinates</param>
        /// <param name="mouseDown">True if left mouse button was pressed this frame</param>
        /// <param name="mouseUp">True if left mouse button was released this frame</param>
        /// <remarks>
        /// Detects hover over button zones, updates the box outline visual,
        /// and fires OnYesClicked or OnNoClicked when a button is clicked.
        /// </remarks>
        public void HandleMouse(Vector2 mousePos, Rect displayRect, bool mouseDown, bool mouseUp)
        {
            Vector2 gridPos = MouseToGrid(mousePos, displayRect);
            
            ClickZone newHovered = null;
            foreach (var zone in _clickZones)
            {
                if (zone.IsEnabled && zone.Contains(gridPos))
                {
                    newHovered = zone;
                    break;
                }
            }
            
            if (newHovered?.ElementId != _hoveredZone?.ElementId)
            {
                _hoveredZone = newHovered;
                
                if (_hoveredZone != null)
                {
                    Rect uvRect = _hoveredZone.GetUVRect();
                    StarfieldNative.CR_SetBoxOutline(1, uvRect.xMin, uvRect.yMin, uvRect.xMax, uvRect.yMax);
                }
                else
                {
                    StarfieldNative.CR_SetBoxOutline(0, 0, 0, 0, 0);
                }
            }
            
            if (mouseUp && _hoveredZone != null)
            {
                if (_hoveredZone.ElementId == "yes_button")
                    OnYesClicked?.Invoke();
                else if (_hoveredZone.ElementId == "no_button")
                    OnNoClicked?.Invoke();
            }
        }
        
        /// <summary>
        /// Starts Layer 3 animation when Layer 2 completes.
        /// </summary>
        private void StartLayer3Animation()
        {
            Debug.Log("[ConfirmRescanScreen] Layer 2 complete, starting Layer 3");
            _sequencer?.StartSequence();
        }
        
        /// <summary>
        /// Updates this screen's animations.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last frame</param>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _sequencer?.Update();
        }
        
        /// <summary>
        /// Renders this screen.
        /// </summary>
        /// <param name="displayRect">Screen rectangle for rendering</param>
        /// <param name="textSystem">Native text system pointer</param>
        /// <remarks>
        /// Renders Layer 1 (border) and Layer 2 (warning text).
        /// YES/NO buttons are rendered separately by the native system
        /// to support interactive hover states.
        /// 
        /// Also handles mouse interaction during repaint events.
        /// </remarks>
        public override void Render(Rect displayRect, IntPtr textSystem)
        {
            if (textSystem == IntPtr.Zero) return;
            
            // Only render during Repaint events and when Event.current is valid
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            
            uint color = CinematicShadersUIResources.Colors.CRTColors.GetColorUint(StarfieldSettings.KartographerGridColor);
            
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
            
            // Render Layer 2: Warning text
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
            
            // Layer 3: YES/NO buttons are rendered separately by the display
            // as they require interactive hover states
            
            // Handle mouse interaction via new click handler (Simplified Click System)
            if (Event.current != null)
            {
                HandleInput(displayRect);
            }
        }
        
        /// <summary>
        /// Legacy method for updating button hover states and handling clicks.
        /// </summary>
        /// <param name="mousePos">Mouse position in screen coordinates</param>
        /// <param name="displayRect">Display rectangle</param>
        /// <param name="mouseDown">True if mouse button pressed</param>
        /// <param name="mouseUp">True if mouse button released</param>
        /// <remarks>
        /// This method uses pixel-based positioning rather than grid-based.
        /// New code should use HandleMouse() for consistency with other screens.
        /// 
        /// Kept for backwards compatibility and potential external callers.
        /// </remarks>
        public void UpdateInteraction(Vector2 mousePos, Rect displayRect, bool mouseDown, bool mouseUp)
        {
            // Calculate YES/NO button positions
            float lineHeight = _fontSize * 1.33f;
            float charWidth = _fontSize * 0.6f;
            
            float yesX = displayRect.x + (charWidth * 3);
            float yesY = displayRect.y + (lineHeight * 10);
            Rect yesRect = new Rect(yesX, yesY, charWidth * 6, lineHeight);
            
            float noX = displayRect.x + displayRect.width - (charWidth * 8);
            float noY = displayRect.y + (lineHeight * 10);
            Rect noRect = new Rect(noX, noY, charWidth * 6, lineHeight);
            
            bool wasYesSelected = YesSelected;
            bool wasNoSelected = NoSelected;
            
            YesSelected = yesRect.Contains(mousePos);
            NoSelected = noRect.Contains(mousePos);
            
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
        
        /// <summary>
        /// Resets the YES/NO selection state.
        /// </summary>
        /// <remarks>
        /// Call this when transitioning away to ensure clean state
        /// for the next time this screen is shown.
        /// </remarks>
        public void ResetSelection()
        {
            YesSelected = false;
            NoSelected = false;
        }
    }
}
