using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Layout.ScreenLayouts
{
    /// <summary>
    /// Constraint-based layout definition for the ScanScreen.
    /// Reproduces the legacy grid position for the clickable SCAN area.
    /// Stores grid coordinates (cells) and converts to pixels on demand.
    /// </summary>
    public class ScanScreenLayout : ILayout
    {
        private readonly Dictionary<string, GridRegion> _elementGridAreas =
            new Dictionary<string, GridRegion>();

        /// <summary>
        /// Builds the layout structure within the given display area.
        /// Stores grid coordinates directly.
        /// </summary>
        public void Build(LayoutEngine engine, Rect displayArea)
        {
            _elementGridAreas.Clear();

            // scan_area: Grid(10, 3), Width 49, Height 9
            _elementGridAreas["scan_area"] = new GridRegion(
                GridPosition.At(10, 3), 49, 9);
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

        /// <summary>
        /// Validates the calculated layout against reference positions.
        /// </summary>
        public bool ValidateAgainst(Dictionary<string, Rect> reference, float tolerance)
        {
            bool valid = true;

            foreach (KeyValuePair<string, Rect> kvp in reference)
            {
                if (!_elementGridAreas.TryGetValue(kvp.Key, out GridRegion gridRegion))
                {
                    Debug.LogError(string.Format(
                        "[LayoutValidation] {0}: Missing in calculated layout", kvp.Key));
                    valid = false;
                    continue;
                }

                Rect calculated = GetArea(kvp.Key);

                float dx = Mathf.Abs(calculated.x - kvp.Value.x);
                float dy = Mathf.Abs(calculated.y - kvp.Value.y);
                float dw = Mathf.Abs(calculated.width - kvp.Value.width);
                float dh = Mathf.Abs(calculated.height - kvp.Value.height);

                if (dx > tolerance || dy > tolerance || dw > tolerance || dh > tolerance)
                {
                    Debug.LogError(string.Format(
                        "[LayoutValidation] {0}: Legacy={1}, Calculated={2}",
                        kvp.Key, kvp.Value, calculated));
                    valid = false;
                }
            }

            return valid;
        }
    }
}
