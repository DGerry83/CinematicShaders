using UnityEngine;
using CinematicShaders.Core;
using CinematicShaders.UI;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Click handling for ConfirmRescanScreen.
    /// Two zones: yes_button and no_button.
    /// </summary>
    public class ConfirmRescanClickHandler : IClickHandler
    {
        public ClickZoneManager ZoneManager { get; private set; }
        private ConfirmRescanScreen _screen;
        private string _hoveredElementId = null;
        
        /// <summary>
        /// Creates a new click handler for the specified ConfirmRescanScreen.
        /// </summary>
        public ConfirmRescanClickHandler(ConfirmRescanScreen screen)
        {
            _screen = screen;
            ZoneManager = new ClickZoneManager();
        }
        
        /// <summary>
        /// Sets up click zones for YES and NO buttons using constraint layout.
        /// Call this once during screen initialization.
        /// </summary>
        public void SetupZones()
        {
            ZoneManager.Clear();
            
            // Register YES button from constraint layout
            GridRegion yesRegion = _screen.Layout.GetGridArea("yes_button");
            ZoneManager.RegisterZone(
                "yes_button",
                yesRegion.TopLeft.Column,
                yesRegion.TopLeft.Row,
                yesRegion.Width,
                yesRegion.Height,
                "button",
                () => _screen.OnYesButtonClicked()
            );
            
            // Register NO button from constraint layout
            GridRegion noRegion = _screen.Layout.GetGridArea("no_button");
            ZoneManager.RegisterZone(
                "no_button",
                noRegion.TopLeft.Column,
                noRegion.TopLeft.Row,
                noRegion.Width,
                noRegion.Height,
                "button",
                () => _screen.OnNoButtonClicked()
            );
        }
        
        /// <summary>
        /// Handles input and click detection.
        /// Called every frame by ConfirmRescanScreen.
        /// </summary>
        public void HandleInput(Rect displayRect)
        {
            if (Event.current == null) return;
            
            // Only process mouse events
            if (Event.current.type != EventType.MouseDown && 
                Event.current.type != EventType.MouseMove &&
                Event.current.type != EventType.MouseUp)
            {
                return;
            }
            
            // Log mouse down events for debugging
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                ModFileLogger.Log("[ConfirmRescanClickHandler] MouseDown detected");
            }
            
            Vector2 mousePos = Event.current.mousePosition;
            
            // Check if mouse is within display
            if (!displayRect.Contains(mousePos))
            {
                if (_hoveredElementId != null)
                {
                    _screen.OnElementHoverExit(_hoveredElementId);
                    _hoveredElementId = null;
                }
                return;
            }
            
            // Convert to local coordinates
            float localX = mousePos.x - displayRect.x;
            float localY = mousePos.y - displayRect.y;
            
            // Convert to grid coordinates
            GridPosition gridPos = TerminalGridConfig.PixelToGrid(
                localX, localY, TerminalGridConfig.CurrentDisplaySize);
            
            // Find zone at grid position
            var zone = ZoneManager.FindZoneAt(gridPos.Column, gridPos.Row);
            
            // Log zone lookup for debugging
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                if (zone != null)
                    ModFileLogger.Log($"[ConfirmRescanClickHandler] Zone found: {zone.ElementId} at grid ({gridPos.Column},{gridPos.Row})");
                else
                    ModFileLogger.Log($"[ConfirmRescanClickHandler] No zone at grid ({gridPos.Column},{gridPos.Row})");
            }
            
            if (zone != null && zone.IsEnabled)
            {
                // Handle hover enter
                if (zone.ElementId != _hoveredElementId)
                {
                    if (_hoveredElementId != null)
                        _screen.OnElementHoverExit(_hoveredElementId);
                    
                    _hoveredElementId = zone.ElementId;
                    _screen.OnElementHoverEnter(zone.ElementId);
                }
                
                // Handle click
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    ModFileLogger.Log($"[ConfirmRescanClickHandler] Clicking zone: {zone.ElementId}");
                    zone.OnClick?.Invoke();
                }
            }
            else
            {
                // Handle hover exit
                if (_hoveredElementId != null)
                {
                    _screen.OnElementHoverExit(_hoveredElementId);
                    _hoveredElementId = null;
                }
            }
        }
    }
}
