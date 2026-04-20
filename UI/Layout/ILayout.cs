using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Layout
{
    /// <summary>
    /// Interface for constraint-based screen layouts.
    /// Layouts store grid coordinates (cells) and convert to pixels on demand.
    /// </summary>
    public interface ILayout
    {
        /// <summary>
        /// Builds the layout structure within the given display area.
        /// </summary>
        void Build(LayoutEngine engine, Rect displayArea);

        /// <summary>
        /// Gets the grid region for the specified element.
        /// Grid coordinates are in cells (column, row, width, height).
        /// </summary>
        GridRegion GetGridArea(string elementId);

        /// <summary>
        /// Gets the pixel rectangle for the specified element.
        /// Converts grid coordinates to pixels using current glyph metrics.
        /// </summary>
        Rect GetArea(string elementId);

        /// <summary>
        /// Gets all element IDs defined in this layout.
        /// </summary>
        IEnumerable<string> GetElementIds();

    }
}
