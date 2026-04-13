using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Layout.ScreenLayouts
{
    /// <summary>
    /// Constraint-based layout definition for the ConfirmRescanScreen.
    /// Reproduces the legacy grid positions for YES and NO buttons.
    /// Stores grid coordinates (cells) and converts to pixels on demand.
    /// </summary>
    public class ConfirmRescanScreenLayout : ILayout
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

            // yes_button: Grid(3, 10), Width 5, Height 1
            _elementGridAreas["yes_button"] = new GridRegion(
                GridPosition.At(3, 10), 5, 1);

            // no_button: Grid(52, 10), Width 4, Height 1
            _elementGridAreas["no_button"] = new GridRegion(
                GridPosition.At(52, 10), 4, 1);
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
