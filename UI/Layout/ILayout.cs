using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Layout
{
    /// <summary>
    /// Interface for constraint-based screen layouts.
    /// </summary>
    public interface ILayout
    {
        /// <summary>
        /// Builds the layout structure within the given display area.
        /// </summary>
        void Build(LayoutEngine engine, Rect displayArea);

        /// <summary>
        /// Gets the calculated rectangle for the specified element.
        /// </summary>
        Rect GetArea(string elementId);

        /// <summary>
        /// Gets all element IDs defined in this layout.
        /// </summary>
        IEnumerable<string> GetElementIds();

        /// <summary>
        /// Validates the calculated layout against reference positions.
        /// </summary>
        bool ValidateAgainst(Dictionary<string, Rect> reference, float tolerance);
    }
}
