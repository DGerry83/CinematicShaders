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
        /// Sets up click zones for all MainScreen elements.
        /// Call this once during screen initialization.
        /// </summary>
        public void SetupZones()
        {
            ZoneManager.Clear();
            
            // Value fields (left column)
            var hipDef = UnifiedGridRegistry.MainScreenElements["hip_value"];
            ZoneManager.RegisterZone("hip_value", 
                (int)hipDef.Position.Column, (int)hipDef.Position.Row,
                hipDef.Width, hipDef.Height, "value",
                () => _screen.OnValueClicked("hip_value"));
            
            var nameDef = UnifiedGridRegistry.MainScreenElements["name_value"];
            ZoneManager.RegisterZone("name_value",
                (int)nameDef.Position.Column, (int)nameDef.Position.Row,
                nameDef.Width, nameDef.Height, "editable",
                () => _screen.OnValueClicked("name_value"));
            
            var distDef = UnifiedGridRegistry.MainScreenElements["distance_value"];
            ZoneManager.RegisterZone("distance_value",
                (int)distDef.Position.Column, (int)distDef.Position.Row,
                distDef.Width, distDef.Height, "value",
                () => _screen.OnValueClicked("distance_value"));
            
            var specDef = UnifiedGridRegistry.MainScreenElements["spectral_value"];
            ZoneManager.RegisterZone("spectral_value",
                (int)specDef.Position.Column, (int)specDef.Position.Row,
                specDef.Width, specDef.Height, "value",
                () => _screen.OnValueClicked("spectral_value"));
            
            var magDef = UnifiedGridRegistry.MainScreenElements["mag_value"];
            ZoneManager.RegisterZone("mag_value",
                (int)magDef.Position.Column, (int)magDef.Position.Row,
                magDef.Width, magDef.Height, "value",
                () => _screen.OnValueClicked("mag_value"));
            
            var constDef = UnifiedGridRegistry.MainScreenElements["const_value"];
            ZoneManager.RegisterZone("const_value",
                (int)constDef.Position.Column, (int)constDef.Position.Row,
                constDef.Width, constDef.Height, "value",
                () => _screen.OnValueClicked("const_value"));
            
            // Buttons
            var saveDef = UnifiedGridRegistry.MainScreenElements["save_button"];
            ZoneManager.RegisterZone("save_button",
                (int)saveDef.Position.Column, (int)saveDef.Position.Row,
                saveDef.Width, saveDef.Height, "button",
                () => _screen.OnSaveClicked());
            
            var resetDef = UnifiedGridRegistry.MainScreenElements["reset_button"];
            ZoneManager.RegisterZone("reset_button",
                (int)resetDef.Position.Column, (int)resetDef.Position.Row,
                resetDef.Width, resetDef.Height, "button",
                () => _screen.OnResetClicked());
            
            var rescanDef = UnifiedGridRegistry.MainScreenElements["rescan_button"];
            ZoneManager.RegisterZone("rescan_button",
                (int)rescanDef.Position.Column, (int)rescanDef.Position.Row,
                rescanDef.Width, rescanDef.Height, "button",
                () => _screen.OnRescanClicked());
            
            // Input field
            var searchDef = UnifiedGridRegistry.MainScreenElements["search_input"];
            ZoneManager.RegisterZone("search_input",
                (int)searchDef.Position.Column, (int)searchDef.Position.Row,
                searchDef.Width, searchDef.Height, "input",
                () => _screen.OnSearchClicked());
            
            // Selected star
            var selectedDef = UnifiedGridRegistry.MainScreenElements["selected_star"];
            ZoneManager.RegisterZone("selected_star",
                (int)selectedDef.Position.Column, (int)selectedDef.Position.Row,
                selectedDef.Width, selectedDef.Height, "value",
                () => _screen.OnSelectedStarClicked());
            
            // Search results (0-9)
            for (int i = 0; i < 10; i++)
            {
                int resultIndex = i; // Capture for closure
                var resultDef = UnifiedGridRegistry.GetSearchResultElement(i);
                ZoneManager.RegisterZone($"result_{i}",
                    (int)resultDef.Position.Column, (int)resultDef.Position.Row,
                    resultDef.Width, resultDef.Height, "result",
                    () => _screen.OnResultClicked(resultIndex));
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
