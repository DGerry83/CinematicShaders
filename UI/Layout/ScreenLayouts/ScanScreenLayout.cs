using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Layout.ScreenLayouts
{
    /// <summary>
    /// Layout for the Scan screen.
    /// The scan screen is simple - just a full-screen scan button/area.
    /// </summary>
    public class ScanScreenLayout : ILayout
    {
        private readonly Dictionary<string, GridRegion> _gridAreas = new Dictionary<string, GridRegion>();
        private readonly Dictionary<string, Rect> _pixelAreas = new Dictionary<string, Rect>();
        private bool _isBuilt = false;
        private Rect _displayArea;

        public void Build(LayoutEngine layout, Rect displayArea)
        {
            if (_isBuilt) return;

            _displayArea = displayArea;

            int columns = TerminalGridConfig.GRID_COLUMNS;  // 59
            int rows = TerminalGridConfig.GRID_ROWS;        // 13

            float cellWidth = displayArea.width / columns;
            float cellHeight = displayArea.height / rows;

            Rect[] areas = layout.Split(
                Direction.Vertical,
                new[]
                {
                    Constraint.Length(cellHeight),   // Top margin (title area)
                    Constraint.Fill(1),               // Scan button area
                    Constraint.Length(cellHeight * 2) // Bottom margin (hint text area)
                },
                displayArea
            );

            _pixelAreas["scan_area"] = areas[1];
            _pixelAreas["title_area"] = areas[0];
            _pixelAreas["hint_area"] = areas[2];

            _gridAreas["scan_area"] = RectToGridRegion(areas[1], cellWidth, cellHeight);
            _gridAreas["title_area"] = RectToGridRegion(areas[0], cellWidth, cellHeight);
            _gridAreas["hint_area"] = RectToGridRegion(areas[2], cellWidth, cellHeight);

            _isBuilt = true;
        }

        public GridRegion GetGridArea(string elementId)
        {
            return _gridAreas.TryGetValue(elementId, out GridRegion region)
                ? region
                : new GridRegion(GridPosition.At(0, 0), 0, 0);
        }

        public Rect GetArea(string elementId)
        {
            return _pixelAreas.TryGetValue(elementId, out Rect area)
                ? area
                : Rect.zero;
        }

        public IEnumerable<string> GetElementIds()
        {
            return _gridAreas.Keys;
        }

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
