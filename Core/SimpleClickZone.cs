using System;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Simplified click zone using grid coordinates.
    /// Grid coordinates are size-agnostic - same for Large/Medium/Small.
    /// </summary>
    public class SimpleClickZone
    {
        /// <summary>
        /// Element identifier (e.g., "name_value", "save_button")
        /// </summary>
        public string ElementId { get; set; }
        
        /// <summary>
        /// Grid position (column, row, width, height in grid cells).
        /// NOT pixel coordinates.
        /// </summary>
        public Rect GridRect { get; set; }
        
        /// <summary>
        /// Category for styling/behavior ("value", "editable", "button", "input", "result")
        /// </summary>
        public string Category { get; set; }
        
        /// <summary>
        /// Whether this zone is currently clickable.
        /// </summary>
        public bool IsEnabled { get; set; } = true;
        
        /// <summary>
        /// Action to invoke when zone is clicked.
        /// Direct callback - no event chains.
        /// </summary>
        public Action OnClick { get; set; }
        
        /// <summary>
        /// Checks if a grid position is within this zone.
        /// </summary>
        /// <param name="col">Column (0 to GRID_COLUMNS-1)</param>
        /// <param name="row">Row (0 to GRID_ROWS-1)</param>
        /// <returns>True if position is inside zone</returns>
        public bool Contains(int col, int row)
        {
            return col >= GridRect.x &&
                   col < GridRect.x + GridRect.width &&
                   row >= GridRect.y &&
                   row < GridRect.y + GridRect.height;
        }
        
        /// <summary>
        /// Returns string representation for debugging.
        /// </summary>
        public override string ToString()
        {
            return $"{ElementId} at ({GridRect.x},{GridRect.y}) size ({GridRect.width},{GridRect.height})";
        }
    }
}
