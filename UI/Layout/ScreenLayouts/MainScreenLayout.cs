using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Layout.ScreenLayouts
{
    /// <summary>
    /// Constraint-based layout definition for the MainScreen.
    /// Derives all element positions from constraint splits, matching the
    /// pattern established by ScanScreenLayout.
    /// </summary>
    public class MainScreenLayout : ILayout
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

            // Major structural split: header (row 0), content (rows 1-11), footer (row 12)
            Rect[] verticalSplits = engine.SplitVertical(displayArea,
                Constraint.Length(cellHeight),   // Row 0: top border
                Constraint.Fill(1),               // Rows 1-11: content area
                Constraint.Length(cellHeight)    // Row 12: bottom border
            );
            Rect contentArea = verticalSplits[1];

            // Split content into left panel (38 cols) and right panel (21 cols)
            Rect[] horizontalSplits = engine.SplitHorizontal(contentArea,
                Constraint.Length(cellWidth * 38),
                Constraint.Fill(1)
            );
            Rect leftPanel = horizontalSplits[0];
            Rect rightPanel = horizontalSplits[1];

            // Split left panel into 11 rows (display rows 1 through 11)
            Constraint[] leftRowConstraints = new Constraint[11];
            for (int i = 0; i < 11; i++)
            {
                leftRowConstraints[i] = Constraint.Length(cellHeight);
            }
            Rect[] leftRows = engine.SplitVertical(leftPanel, leftRowConstraints);

            // Split right panel into 11 rows (10 for results + 1 for pagination)
            Constraint[] resultRowConstraints = new Constraint[11];
            for (int i = 0; i < 11; i++)
            {
                resultRowConstraints[i] = Constraint.Length(cellHeight);
            }
            Rect[] resultRows = engine.SplitVertical(rightPanel, resultRowConstraints);

            // NOTE: All element positions must be derived from engine.Split...() results.
            // For label/value rows, use nested SplitHorizontal:
            //   engine.SplitHorizontal(row, Constraint.Length(labelWidth), Constraint.Length(valueWidth), Constraint.Fill(1));
            // Never use new Rect(...) with manual arithmetic.

            // Left column value fields (rows 1-6) — derived from leftRows sub-rects
            Rect[] row0Splits = engine.SplitHorizontal(leftRows[0],
                Constraint.Length(cellWidth * 12),
                Constraint.Length(cellWidth * 20),
                Constraint.Fill(1)
            );
            _pixelAreas["hip_value"] = row0Splits[1];

            Rect[] row1Splits = engine.SplitHorizontal(leftRows[1],
                Constraint.Length(cellWidth * 12),
                Constraint.Length(cellWidth * 25),
                Constraint.Fill(1)
            );
            _pixelAreas["name_value"] = row1Splits[1];

            Rect[] row2Splits = engine.SplitHorizontal(leftRows[2],
                Constraint.Length(cellWidth * 12),
                Constraint.Length(cellWidth * 20),
                Constraint.Fill(1)
            );
            _pixelAreas["distance_value"] = row2Splits[1];

            Rect[] row3Splits = engine.SplitHorizontal(leftRows[3],
                Constraint.Length(cellWidth * 12),
                Constraint.Length(cellWidth * 15),
                Constraint.Fill(1)
            );
            _pixelAreas["spectral_value"] = row3Splits[1];

            Rect[] row4Splits = engine.SplitHorizontal(leftRows[4],
                Constraint.Length(cellWidth * 12),
                Constraint.Length(cellWidth * 15),
                Constraint.Fill(1)
            );
            _pixelAreas["mag_value"] = row4Splits[1];

            Rect[] row5Splits = engine.SplitHorizontal(leftRows[5],
                Constraint.Length(cellWidth * 12),
                Constraint.Length(cellWidth * 20),
                Constraint.Fill(1)
            );
            _pixelAreas["const_value"] = row5Splits[1];

            // Row 8: selected_star, save_button, reset_button
            Rect[] row8Splits = engine.SplitHorizontal(leftRows[7],
                Constraint.Length(cellWidth * 4),   // margin
                Constraint.Length(cellWidth * 12),  // selected_star
                Constraint.Length(cellWidth * 1),   // gap
                Constraint.Length(cellWidth * 7),   // save_button
                Constraint.Length(cellWidth * 3),   // gap
                Constraint.Length(cellWidth * 8),   // reset_button
                Constraint.Fill(1)                  // remainder
            );
            _pixelAreas["selected_star"] = row8Splits[1];
            _pixelAreas["save_button"] = row8Splits[3];
            _pixelAreas["reset_button"] = row8Splits[5];

            // Row 10: rescan_button
            Rect[] row10Splits = engine.SplitHorizontal(leftRows[9],
                Constraint.Length(cellWidth * 27),  // margin
                Constraint.Length(cellWidth * 8),   // rescan_button
                Constraint.Fill(1)                  // remainder
            );
            _pixelAreas["rescan_button"] = row10Splits[1];

            // Row 11: search_input
            Rect[] row11Splits = engine.SplitHorizontal(leftRows[10],
                Constraint.Length(cellWidth * 4),   // margin
                Constraint.Length(cellWidth * 25),  // search_input
                Constraint.Fill(1)                  // remainder
            );
            _pixelAreas["search_input"] = row11Splits[1];

            // Search results (rows 1-10 in right panel)
            for (int i = 0; i < 10; i++)
            {
                _pixelAreas[string.Format("result_{0}", i)] = resultRows[i];
            }
            
            // Page number row (row 11) — narrow region between arrows at cols 39 and 55
            Rect[] pageNumberSplits = engine.SplitHorizontal(resultRows[10],
                Constraint.Length(cellWidth * 1),   // col 38 (right panel start)
                Constraint.Length(cellWidth * 1),   // col 39 (▲ arrow)
                Constraint.Length(cellWidth * 15),  // cols 40-54 (page number)
                Constraint.Length(cellWidth * 1),   // col 55 (▼ arrow)
                Constraint.Fill(1)                  // cols 56-58 (remainder)
            );
            _pixelAreas["page_number"] = pageNumberSplits[2];

            // Convert pixel areas to grid areas
            foreach (var kvp in _pixelAreas)
            {
                _gridAreas[kvp.Key] = RectToGridRegion(kvp.Value, cellWidth, cellHeight);
            }

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
