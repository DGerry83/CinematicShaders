using UnityEngine;
using CinematicShaders.Core;
using CinematicShaders.UI;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Click handling for ScanScreen.
    /// Single large zone covering the SCAN ASCII art.
    /// </summary>
    public class ScanScreenClickHandler : IClickHandler
    {
        public ClickZoneManager ZoneManager { get; private set; }
        private ScanScreen _screen;
        private string _hoveredElementId = null;
        
        /// <summary>
        /// Creates a new click handler for the specified ScanScreen.
        /// </summary>
        public ScanScreenClickHandler(ScanScreen screen)
        {
            _screen = screen;
            ZoneManager = new ClickZoneManager();
        }
        
        /// <summary>
        /// Sets up click zone for the SCAN area using constraint layout.
        /// Call this once during screen initialization.
        /// </summary>
        public void SetupZones()
        {
            ZoneManager.Clear();
            
            // Get scan_area region from constraint layout
            GridRegion scanRegion = _screen.Layout.GetGridArea("scan_area");
            
            // Register large scan zone using grid coordinates
            ZoneManager.RegisterZone(
                "scan_area",
                scanRegion.TopLeft.Column,
                scanRegion.TopLeft.Row,
                scanRegion.Width,
                scanRegion.Height,
                "scan",
                () => _screen.OnScanAreaClicked()
            );
        }
        
        /// <summary>
        /// Handles input and click detection.
        /// Called every frame by ScanScreen.
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
