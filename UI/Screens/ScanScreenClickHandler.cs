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
        private ClickZoneInputProcessor _processor;
        
        /// <summary>
        /// Creates a new click handler for the specified ScanScreen.
        /// </summary>
        public ScanScreenClickHandler(ScanScreen screen)
        {
            _screen = screen;
            ZoneManager = new ClickZoneManager();
            _processor = new ClickZoneInputProcessor(
                ZoneManager,
                elementId => _screen.OnElementHoverEnter(elementId),
                elementId => _screen.OnElementHoverExit(elementId));
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
            _processor.ProcessInput(displayRect);
        }
    }
}
