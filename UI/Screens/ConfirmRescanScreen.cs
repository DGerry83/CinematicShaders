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
    /// Confirmation dialog for rescan operation.
    /// Layers: 1 (border), 2 (warning text), 3 (YES/NO buttons)
    /// </summary>
    public class ConfirmRescanScreen : BaseScreen
    {
        private readonly float _fontSize;
        private readonly float _aspectRatio;
        private RenderTexture _layer1Texture;
        private RenderTexture _layer2Texture;
        private Sequencer _sequencer;
        
        // Click zones for YES/NO buttons
        private List<ClickZone> _clickZones = new List<ClickZone>();
        private ClickZone? _hoveredZone = null;
        
        // Define Layer 3 priority order - buttons appear in sequence
        protected override List<string> Layer3PriorityOrder => new List<string>
        {
            "yes_button",
            "no_button"
        };
        
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
        
        public void SetLayer3Texture(RenderTexture layer3Texture)
        {
            // ConfirmRescanScreen doesn't use dynamic Layer 3 content, but implements interface
        }
        
        public override void OnEnter(ScreenTransitionContext context)
        {
            base.OnEnter(context);
            
            _sequencer = new Sequencer(Layer3PriorityOrder);
            OnLayer2Complete += StartLayer3Animation;
            
            // Initialize click zones for YES/NO buttons
            _clickZones.Clear();
            _clickZones.Add(new ClickZone("yes_button", HolographicLayoutConfig.ZONE_YES_BUTTON, true));
            _clickZones.Add(new ClickZone("no_button", HolographicLayoutConfig.ZONE_NO_BUTTON, true));
            _hoveredZone = null;
        }
        
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
        /// Handle mouse interaction for YES/NO buttons
        /// </summary>
        public void HandleMouse(Vector2 mousePos, Rect displayRect, bool mouseDown, bool mouseUp)
        {
            Vector2 gridPos = MouseToGrid(mousePos, displayRect);
            
            ClickZone? newHovered = null;
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
            
            if (mouseUp && _hoveredZone.HasValue)
            {
                if (_hoveredZone.Value.ElementId == "yes_button")
                    OnYesClicked?.Invoke();
                else if (_hoveredZone.Value.ElementId == "no_button")
                    OnNoClicked?.Invoke();
            }
        }
        
        private Vector2 MouseToGrid(Vector2 mousePos, Rect displayRect)
        {
            float localX = mousePos.x - displayRect.x;
            float localY = mousePos.y - displayRect.y;
            float gridX = localX / HolographicLayoutConfig.GRID_CELL_WIDTH;
            float gridY = localY / HolographicLayoutConfig.GRID_CELL_HEIGHT;
            return new Vector2(gridX, gridY);
        }
        
        private void StartLayer3Animation()
        {
            Debug.Log("[ConfirmRescanScreen] Layer 2 complete, starting Layer 3");
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
        /// Update hover states and handle clicks for YES/NO buttons.
        /// Call this from the display's interaction handling.
        /// </summary>
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
        
        public void ResetSelection()
        {
            YesSelected = false;
            NoSelected = false;
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
