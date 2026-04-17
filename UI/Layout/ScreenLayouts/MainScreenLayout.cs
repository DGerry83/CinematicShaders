using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Layout.ScreenLayouts
{
    /// <summary>
    /// Constraint-based layout definition for the MainScreen.
    /// Reproduces the legacy 59x13 grid positions from UnifiedGridRegistry
    /// using the constraint layout system.
    /// Stores grid coordinates (cells) and converts to pixels on demand.
    /// </summary>
    public class MainScreenLayout : ILayout
    {
        private readonly Dictionary<string, GridRegion> _elementGridAreas =
            new Dictionary<string, GridRegion>();

        /// <summary>
        /// Builds the layout structure within the given display area.
        /// Stores grid coordinates directly (column, row, width, height in cells).
        /// </summary>
        public void Build(LayoutEngine engine, Rect displayArea)
        {
            _elementGridAreas.Clear();

            // Get glyph metrics for potential pixel calculations
            var (glyphWidth, glyphHeight) = TerminalGridConfig.GlyphMetrics.GetGlyphMetrics(
                TerminalGridConfig.CurrentDisplaySize
            );

            // Major structural split: header (row 0), content (rows 1-11), footer (row 12)
            Rect[] verticalSplits = engine.SplitVertical(displayArea,
                Constraint.Length(glyphHeight),  // Row 0: top border
                Constraint.Fill(1),               // Rows 1-11: content area
                Constraint.Length(glyphHeight)   // Row 12: bottom border
            );
            Rect contentArea = verticalSplits[1];

            // Split content into left panel (cols 0-37) and right panel (cols 38-58)
            float leftPanelWidth = 38f * glyphWidth;
            Rect[] horizontalSplits = engine.SplitHorizontal(contentArea,
                Constraint.Length(leftPanelWidth),
                Constraint.Fill(1)
            );
            Rect leftPanel = horizontalSplits[0];
            Rect rightPanel = horizontalSplits[1];

            // Split left panel into 11 rows (display rows 1 through 11)
            Constraint[] leftRowConstraints = new Constraint[11];
            for (int i = 0; i < 11; i++)
            {
                leftRowConstraints[i] = Constraint.Length(glyphHeight);
            }
            Rect[] leftRows = engine.SplitVertical(leftPanel, leftRowConstraints);

            // Left column value fields (rows 1-6)
            // Store as grid coordinates: (column, row, width, height)
            _elementGridAreas["hip_value"] = new GridRegion(
                GridPosition.At(12, 1), 20, 1);

            _elementGridAreas["name_value"] = new GridRegion(
                GridPosition.At(12, 2), 25, 1);

            _elementGridAreas["distance_value"] = new GridRegion(
                GridPosition.At(12, 3), 20, 1);

            _elementGridAreas["spectral_value"] = new GridRegion(
                GridPosition.At(12, 4), 15, 1);

            _elementGridAreas["mag_value"] = new GridRegion(
                GridPosition.At(12, 5), 15, 1);

            _elementGridAreas["const_value"] = new GridRegion(
                GridPosition.At(12, 6), 20, 1);

            // Row 8: selected star, save button, reset button
            _elementGridAreas["selected_star"] = new GridRegion(
                GridPosition.At(4, 8), 12, 1);

            _elementGridAreas["save_button"] = new GridRegion(
                GridPosition.At(17, 8), 7, 1);

            _elementGridAreas["reset_button"] = new GridRegion(
                GridPosition.At(27, 8), 8, 1);

            // Row 10: rescan button
            _elementGridAreas["rescan_button"] = new GridRegion(
                GridPosition.At(27, 10), 8, 1);

            // Row 11: search input
            _elementGridAreas["search_input"] = new GridRegion(
                GridPosition.At(4, 11), 25, 1);

            // Split right panel into 10 rows for search results (rows 1-10)
            Constraint[] resultRowConstraints = new Constraint[10];
            for (int i = 0; i < 10; i++)
            {
                resultRowConstraints[i] = Constraint.Length(glyphHeight);
            }
            Rect[] resultRows = engine.SplitVertical(rightPanel, resultRowConstraints);

            for (int i = 0; i < 10; i++)
            {
                _elementGridAreas[string.Format("result_{0}", i)] = new GridRegion(
                    GridPosition.At(38, 1 + i), 20, 1);
            }
        }

        /// <summary>
        /// Gets the grid region for the specified element.
        /// Grid coordinates are in cells (column, row, width, height).
        /// </summary>
        public GridRegion GetGridArea(string elementId)
        {
            return _elementGridAreas.TryGetValue(elementId, out GridRegion region)
                ? region
                : new GridRegion(GridPosition.At(0, 0), 0, 0);
        }

        /// <summary>
        /// Gets the pixel rectangle for the specified element.
        /// Converts grid coordinates to pixels using current glyph metrics.
        /// </summary>
        public Rect GetArea(string elementId)
        {
            if (!_elementGridAreas.TryGetValue(elementId, out GridRegion gridRegion))
                return Rect.zero;

            var (glyphWidth, glyphHeight) = TerminalGridConfig.GlyphMetrics.GetGlyphMetrics(
                TerminalGridConfig.CurrentDisplaySize
            );

            float x = gridRegion.TopLeft.Column * glyphWidth;
            float y = gridRegion.TopLeft.Row * glyphHeight;
            float width = gridRegion.Width * glyphWidth;
            float height = gridRegion.Height * glyphHeight;

            return new Rect(x, y, width, height);
        }

        /// <summary>
        /// Gets all element IDs defined in this layout.
        /// </summary>
        public IEnumerable<string> GetElementIds()
        {
            return _elementGridAreas.Keys;
        }

    }
}
