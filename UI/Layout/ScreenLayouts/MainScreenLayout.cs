using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Layout.ScreenLayouts
{
    /// <summary>
    /// Constraint-based layout definition for the MainScreen.
    /// Reproduces the legacy 59×13 grid positions from UnifiedGridRegistry
    /// using the constraint layout system.
    /// </summary>
    public class MainScreenLayout : ILayout
    {
        private readonly Dictionary<string, Rect> _elementAreas =
            new Dictionary<string, Rect>();

        /// <summary>
        /// Builds the layout structure within the given display area.
        /// Uses constraints to divide the display into regions matching the 59×13 grid.
        /// </summary>
        public void Build(LayoutEngine engine, Rect displayArea)
        {
            _elementAreas.Clear();

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
            _elementAreas["hip_value"] = leftRows[0]
                .Offset(12f * glyphWidth, 0f)
                .WithSize(20f * glyphWidth, glyphHeight);

            _elementAreas["name_value"] = leftRows[1]
                .Offset(12f * glyphWidth, 0f)
                .WithSize(25f * glyphWidth, glyphHeight);

            _elementAreas["distance_value"] = leftRows[2]
                .Offset(12f * glyphWidth, 0f)
                .WithSize(20f * glyphWidth, glyphHeight);

            _elementAreas["spectral_value"] = leftRows[3]
                .Offset(12f * glyphWidth, 0f)
                .WithSize(15f * glyphWidth, glyphHeight);

            _elementAreas["mag_value"] = leftRows[4]
                .Offset(12f * glyphWidth, 0f)
                .WithSize(15f * glyphWidth, glyphHeight);

            _elementAreas["const_value"] = leftRows[5]
                .Offset(12f * glyphWidth, 0f)
                .WithSize(20f * glyphWidth, glyphHeight);

            // Row 8: selected star, save button, reset button
            _elementAreas["selected_star"] = leftRows[7]
                .Offset(4f * glyphWidth, 0f)
                .WithSize(12f * glyphWidth, glyphHeight);

            _elementAreas["save_button"] = leftRows[7]
                .Offset(17f * glyphWidth, 0f)
                .WithSize(7f * glyphWidth, glyphHeight);

            _elementAreas["reset_button"] = leftRows[7]
                .Offset(27f * glyphWidth, 0f)
                .WithSize(8f * glyphWidth, glyphHeight);

            // Row 10: rescan button
            _elementAreas["rescan_button"] = leftRows[9]
                .Offset(27f * glyphWidth, 0f)
                .WithSize(8f * glyphWidth, glyphHeight);

            // Row 11: search input
            _elementAreas["search_input"] = leftRows[10]
                .Offset(4f * glyphWidth, 0f)
                .WithSize(25f * glyphWidth, glyphHeight);

            // Split right panel into 10 rows for search results (rows 1-10)
            Constraint[] resultRowConstraints = new Constraint[10];
            for (int i = 0; i < 10; i++)
            {
                resultRowConstraints[i] = Constraint.Length(glyphHeight);
            }
            Rect[] resultRows = engine.SplitVertical(rightPanel, resultRowConstraints);

            for (int i = 0; i < 10; i++)
            {
                _elementAreas[string.Format("result_{0}", i)] = resultRows[i]
                    .WithSize(20f * glyphWidth, glyphHeight);
            }
        }

        /// <summary>
        /// Gets the calculated rectangle for the specified element.
        /// </summary>
        public Rect GetArea(string elementId)
        {
            return _elementAreas.TryGetValue(elementId, out Rect rect)
                ? rect
                : Rect.zero;
        }

        /// <summary>
        /// Gets all element IDs defined in this layout.
        /// </summary>
        public IEnumerable<string> GetElementIds()
        {
            return _elementAreas.Keys;
        }

        /// <summary>
        /// Validates the calculated layout against reference positions.
        /// Compares position (x, y) and size (width, height) within tolerance.
        /// </summary>
        public bool ValidateAgainst(Dictionary<string, Rect> reference, float tolerance)
        {
            bool valid = true;

            foreach (KeyValuePair<string, Rect> kvp in reference)
            {
                if (!_elementAreas.TryGetValue(kvp.Key, out Rect calculated))
                {
                    Debug.LogError(string.Format(
                        "[LayoutValidation] {0}: Missing in calculated layout", kvp.Key));
                    valid = false;
                    continue;
                }

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

        /// <summary>
        /// Calculates a search result element position dynamically.
        /// This mirrors UnifiedGridRegistry.GetSearchResultElement() in the new layout system.
        /// </summary>
        public Rect GetSearchResultElement(int index)
        {
            if (index < 0 || index >= 10)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index));
            }

            string key = string.Format("result_{0}", index);
            return GetArea(key);
        }
    }
}
