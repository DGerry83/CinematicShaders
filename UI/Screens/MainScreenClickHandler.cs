using UnityEngine;
using CinematicShaders.Core;
using CinematicShaders.UI;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Click handling for MainScreen.
    /// Implements IClickHandler with MainScreen-specific zone setup.
    /// </summary>
    public class MainScreenClickHandler : IClickHandler
    {
        public ClickZoneManager ZoneManager { get; private set; }
        private MainScreen _screen;
        private string _hoveredElementId = null;
        
        /// <summary>
        /// Creates a new click handler for the specified MainScreen.
        /// </summary>
        public MainScreenClickHandler(MainScreen screen)
        {
            _screen = screen;
            ZoneManager = new ClickZoneManager();
        }
        
        /// <summary>
        /// Sets up click zones for all MainScreen elements using constraint layout.
        /// Call this once during screen initialization.
        /// </summary>
        public void SetupZones()
        {
            ZoneManager.Clear();
            
            // Value fields (left column)
            RegisterZoneFromLayout("hip_value", "value", () => _screen.OnValueClicked("hip_value"));
            RegisterZoneFromLayout("name_value", "editable", () => _screen.OnValueClicked("name_value"));
            RegisterZoneFromLayout("distance_value", "value", () => _screen.OnValueClicked("distance_value"));
            RegisterZoneFromLayout("spectral_value", "value", () => _screen.OnValueClicked("spectral_value"));
            RegisterZoneFromLayout("mag_value", "value", () => _screen.OnValueClicked("mag_value"));
            RegisterZoneFromLayout("const_value", "value", () => _screen.OnValueClicked("const_value"));
            
            // Buttons
            RegisterZoneFromLayout("save_button", "button", () => _screen.OnSaveClicked());
            RegisterZoneFromLayout("reset_button", "button", () => _screen.OnResetClicked());
            RegisterZoneFromLayout("rescan_button", "button", () => _screen.OnRescanClicked());
            
            // Input field
            RegisterZoneFromLayout("search_input", "input", () => _screen.OnSearchClicked());
            
            // Selected star
            RegisterZoneFromLayout("selected_star", "value", () => _screen.OnSelectedStarClicked());
            
            // Search results (0-9)
            for (int i = 0; i < 10; i++)
            {
                int resultIndex = i; // Capture for closure
                RegisterZoneFromLayout($"result_{i}", "result", () => _screen.OnResultClicked(resultIndex));
            }
        }
        
        /// <summary>
        /// Helper to register a zone from constraint layout.
        /// </summary>
        private void RegisterZoneFromLayout(string elementId, string category, System.Action onClick)
        {
            GridRegion region = _screen.Layout.GetGridArea(elementId);
            if (region.Width > 0 && region.Height > 0)
            {
                ZoneManager.RegisterZone(elementId,
                    region.TopLeft.Column, region.TopLeft.Row,
                    region.Width, region.Height, category, onClick);
            }
        }
        
        /// <summary>
        /// Handles input and click detection.
        /// Called every frame by MainScreen.
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
            
            // Convert to grid coordinates using CURRENT display size
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
