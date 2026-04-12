using System;
using UnityEngine;
using CinematicShaders.Core;

namespace CinematicShaders.UI
{
    /// <summary>
    /// Element types for the unified grid system.
    /// </summary>
    public enum ElementType
    {
        Value,          // Read-only value field
        Editable,       // Click to edit (NAME field)
        Button,         // Clickable button
        Input,          // Search input field
        SearchResult,   // Clickable result row
        Label           // Static label (Layer 2, not interactive)
    }

    /// <summary>
    /// Defines a UI element on the unified 59×13 grid.
    /// This is the single source of truth for element positioning.
    /// </summary>
    public class GridElementDefinition
    {
        /// <summary>Unique element identifier (e.g., "hip_value", "save_button")</summary>
        public string ElementId { get; set; }
        
        /// <summary>Top-left position on the 59×13 grid</summary>
        public GridPosition Position { get; set; }
        
        /// <summary>Width in grid columns</summary>
        public int Width { get; set; }
        
        /// <summary>Height in grid rows (typically 1 for text elements)</summary>
        public int Height { get; set; }
        
        /// <summary>Element type for behavior determination</summary>
        public ElementType Type { get; set; }
        
        /// <summary>Animation priority order (lower = earlier)</summary>
        public int Priority { get; set; }
        
        /// <summary>Default visibility</summary>
        public bool VisibleByDefault { get; set; } = true;
        
        /// <summary>
        /// Get the grid region for this element.
        /// </summary>
        public GridRegion GetGridRegion()
        {
            return new GridRegion(Position, Width, Height);
        }
        
        /// <summary>
        /// Get pixel rectangle for rendering at specified display size.
        /// </summary>
        /// <param name="displayWidth">Total display width in pixels</param>
        /// <param name="displayHeight">Total display height in pixels</param>
        /// <returns>Pixel rectangle in screen coordinates</returns>
        public Rect GetPixelRect(float displayWidth, float displayHeight)
        {
            return TerminalGridConfig.GridToPixelRect(GetGridRegion(), displayWidth, displayHeight);
        }
        
        /// <summary>
        /// Get the click zone for this element.
        /// Automatically derived from grid position.
        /// </summary>
        public ClickZone GetClickZone()
        {
            return new ClickZone
            {
                ElementId = ElementId,
                GridRect = new Rect(Position.Column, Position.Row, Width, Height),
                Category = GetCategoryForType(Type),
                IsEnabled = true
            };
        }
        
        private string GetCategoryForType(ElementType type)
        {
            switch (type)
            {
                case ElementType.Button:
                    return "button";
                case ElementType.Editable:
                    return "value";
                case ElementType.Input:
                    return "input";
                case ElementType.SearchResult:
                    return "result";
                default:
                    return "value";
            }
        }
    }
}
