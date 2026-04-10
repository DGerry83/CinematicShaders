using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Defines a clickable zone in grid coordinates for the CRT UI.
    /// Grid cell size: 12px wide × 27px tall (for 35pt font at 2:3 aspect)
    /// </summary>
    public struct ClickZone
    {
        /// <summary>Element identifier (e.g., "name_value", "save_button")</summary>
        public string ElementId;
        
        /// <summary>Position in grid coordinates (col, row, width in chars, height in rows)</summary>
        public Rect GridRect;
        
        /// <summary>Whether this zone is currently clickable</summary>
        public bool IsEnabled;
        
        /// <summary>Screen state this zone belongs to</summary>
        public string ScreenState;
        
        public ClickZone(string elementId, Rect gridRect, bool isEnabled = true, string screenState = "")
        {
            ElementId = elementId;
            GridRect = gridRect;
            IsEnabled = isEnabled;
            ScreenState = screenState;
        }
        
        /// <summary>
        /// Check if grid coordinates are within this zone
        /// </summary>
        public bool Contains(Vector2 gridPos)
        {
            return GridRect.Contains(gridPos);
        }
        
        /// <summary>
        /// Convert grid rectangle to screen pixels
        /// </summary>
        public Rect GetScreenRect(float cellWidth = 12f, float cellHeight = 27f)
        {
            return new Rect(
                GridRect.x * cellWidth,
                GridRect.y * cellHeight,
                GridRect.width * cellWidth,
                GridRect.height * cellHeight
            );
        }
        
        /// <summary>
        /// Convert grid rectangle to UV coordinates (0-1 range)
        /// </summary>
        public Rect GetUVRect(float textureWidth = 825f, float textureHeight = 450f, 
                              float cellWidth = 12f, float cellHeight = 27f)
        {
            float x = (GridRect.x * cellWidth) / textureWidth;
            float y = (GridRect.y * cellHeight) / textureHeight;
            float w = (GridRect.width * cellWidth) / textureWidth;
            float h = (GridRect.height * cellHeight) / textureHeight;
            return new Rect(x, y, w, h);
        }
    }
}
