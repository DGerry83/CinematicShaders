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
        /// Sets up click zone for the SCAN area using tight bounding box
        /// computed from the actual ASCII art content lines.
        /// Call this once during screen initialization.
        /// </summary>
        public void SetupZones()
        {
            ZoneManager.Clear();
            
            // Compute tight bounding box from actual SCAN art content
            string[] lines = _screen.ContentLines;
            int minRow = int.MaxValue, maxRow = int.MinValue;
            int minCol = int.MaxValue, maxCol = int.MinValue;
            
            for (int row = 0; row < lines.Length; row++)
            {
                string line = lines[row];
                int firstNonSpace = -1, lastNonSpace = -1;
                for (int col = 0; col < line.Length; col++)
                {
                    if (line[col] != ' ')
                    {
                        if (firstNonSpace == -1) firstNonSpace = col;
                        lastNonSpace = col;
                    }
                }
                if (firstNonSpace != -1)
                {
                    minRow = Mathf.Min(minRow, row);
                    maxRow = Mathf.Max(maxRow, row);
                    minCol = Mathf.Min(minCol, firstNonSpace);
                    maxCol = Mathf.Max(maxCol, lastNonSpace);
                }
            }
            
            if (minRow != int.MaxValue)
            {
                ZoneManager.RegisterZone(
                    "scan_area",
                    minCol,
                    minRow,
                    maxCol - minCol + 1,
                    maxRow - minRow + 1,
                    "scan",
                    () => _screen.OnScanAreaClicked()
                );
            }
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
