using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Layout.ScreenLayouts
{
    /// <summary>
    /// Constraint-based layout definition for the ConfirmRescanScreen.
    /// Derives YES and NO button positions from constraint splits,
    /// matching the pattern established by ScanScreenLayout.
    /// </summary>
    public class ConfirmRescanScreenLayout : ILayout
    {
        private readonly Dictionary<string, GridRegion> _gridAreas = new Dictionary<string, GridRegion>();
        private readonly Dictionary<string, Rect> _pixelAreas = new Dictionary<string, Rect>();
        private bool _isBuilt = false;
        private Rect _displayArea;

        /// <summary>
        /// Builds the layout structure within the given display area.
        /// Derives pixel and grid regions from constraint splits.
        /// </summary>
        public void Build(LayoutEngine engine, Rect displayArea)
        {
            if (_isBuilt) return;

            _displayArea = displayArea;

            int columns = TerminalGridConfig.GRID_COLUMNS;
            int rows = TerminalGridConfig.GRID_ROWS;
            float cellWidth = displayArea.width / columns;
            float cellHeight = displayArea.height / rows;

            // Split display area vertically to isolate the button row (row 10)
            Rect[] verticalSplits = engine.SplitVertical(displayArea,
                Constraint.Length(cellHeight * 10), // Rows 0-9: top content
                Constraint.Length(cellHeight),      // Row 10: button row
                Constraint.Length(cellHeight * 2)   // Rows 11-12: bottom margin
            );
            Rect buttonRow = verticalSplits[1];

            // Split button row horizontally to place YES and NO buttons
            Rect[] buttonSplits = engine.SplitHorizontal(buttonRow,
                Constraint.Length(cellWidth * 3),   // Left margin
                Constraint.Length(cellWidth * 5),   // YES button
                Constraint.Fill(1),                  // Gap between buttons
                Constraint.Length(cellWidth * 4),   // NO button
                Constraint.Length(cellWidth * 3)    // Right margin
            );

            _pixelAreas["yes_button"] = buttonSplits[1];
            _pixelAreas["no_button"] = buttonSplits[3];

            _gridAreas["yes_button"] = RectToGridRegion(buttonSplits[1], cellWidth, cellHeight);
            _gridAreas["no_button"] = RectToGridRegion(buttonSplits[3], cellWidth, cellHeight);

            _isBuilt = true;
        }

        /// <summary>
        /// Gets the grid region for the specified element.
        /// Grid coordinates are in cells (column, row, width, height).
        /// </summary>
        public GridRegion GetGridArea(string elementId)
        {
            return _gridAreas.TryGetValue(elementId, out GridRegion region)
                ? region
                : new GridRegion(GridPosition.At(0, 0), 0, 0);
        }

        /// <summary>
        /// Gets the pixel rectangle for the specified element.
        /// </summary>
        public Rect GetArea(string elementId)
        {
            return _pixelAreas.TryGetValue(elementId, out Rect area)
                ? area
                : Rect.zero;
        }

        /// <summary>
        /// Gets all element IDs defined in this layout.
        /// </summary>
        public IEnumerable<string> GetElementIds()
        {
            return _gridAreas.Keys;
        }

        /// <summary>
        /// Invalidates the built layout so it will be rebuilt on the next call.
        /// </summary>
        public void Invalidate()
        {
            _isBuilt = false;
            _gridAreas.Clear();
            _pixelAreas.Clear();
        }

        private GridRegion RectToGridRegion(Rect rect, float cellWidth, float cellHeight)
        {
            int col = Mathf.RoundToInt(rect.x / cellWidth);
            int row = Mathf.RoundToInt(rect.y / cellHeight);
            int width = Mathf.RoundToInt(rect.width / cellWidth);
            int height = Mathf.RoundToInt(rect.height / cellHeight);
            return new GridRegion(GridPosition.At(col, row), width, height);
        }
    }
}
