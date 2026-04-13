using System;
using UnityEngine;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// A simple click zone for the simplified click system.
    /// Represents an interactive area on the screen defined by grid coordinates.
    /// </summary>
    public class SimpleClickZone
    {
        /// <summary>
        /// Unique identifier for this zone (e.g., "save_button", "hip_value").
        /// </summary>
        public string ElementId { get; set; }
        
        /// <summary>
        /// Grid rectangle defining the zone's position and size in grid cells.
        /// x=column, y=row, width=cells wide, height=cells tall.
        /// </summary>
        public Rect GridRect { get; set; }
        
        /// <summary>
        /// Category for styling and behavior (e.g., "button", "value", "input").
        /// </summary>
        public string Category { get; set; }
        
        /// <summary>
        /// Callback invoked when the zone is clicked.
        /// </summary>
        public Action OnClick { get; set; }
        
        /// <summary>
        /// Whether this zone is currently enabled for interaction.
        /// Disabled zones still exist but don't respond to clicks.
        /// </summary>
        public bool IsEnabled { get; set; } = true;
        
        /// <summary>
        /// Returns a string representation for debugging.
        /// </summary>
        public override string ToString()
        {
            return $"SimpleClickZone[{ElementId}] at ({GridRect.x},{GridRect.y}) size {GridRect.width}x{GridRect.height}";
        }
    }
}
