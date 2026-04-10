using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Defines a clickable zone for the CRT UI.
    /// Supports both grid-based (legacy) and UV-based (Contract 7) coordinate systems.
    /// </summary>
    public class ClickZone
    {
        /// <summary>Element identifier (e.g., "name_value", "save_button")</summary>
        public string ElementId { get; set; }
        
        /// <summary>Position in grid coordinates (col, row, width in chars, height in rows) - LEGACY</summary>
        public Rect GridRect { get; set; }
        
        /// <summary>Position in UV coordinates (0-1 range) - Contract 7</summary>
        public Rect UVRect { get; set; }
        
        /// <summary>Whether this zone is currently clickable</summary>
        public bool IsEnabled { get; set; } = true;
        
        /// <summary>Screen state this zone belongs to - LEGACY</summary>
        public string ScreenState { get; set; }
        
        /// <summary>Category of zone: "button", "value", "input", "result" - Contract 7</summary>
        public string Category { get; set; }
        
        /// <summary>
        /// Legacy constructor for grid-based zones.
        /// </summary>
        public ClickZone(string elementId, Rect gridRect, bool isEnabled = true, string screenState = "")
        {
            ElementId = elementId;
            GridRect = gridRect;
            IsEnabled = isEnabled;
            ScreenState = screenState;
        }
        
        /// <summary>
        /// Default constructor for UV-based zones (Contract 7).
        /// </summary>
        public ClickZone()
        {
        }
        
        /// <summary>
        /// Check if grid coordinates are within this zone (legacy).
        /// </summary>
        public bool Contains(Vector2 gridPos)
        {
            return GridRect.Contains(gridPos);
        }
        
        /// <summary>
        /// Check if UV coordinates are within this zone (Contract 7).
        /// </summary>
        public bool ContainsUV(Vector2 uv)
        {
            return UVRect.Contains(uv);
        }
        
        /// <summary>
        /// Convert grid rectangle to screen pixels.
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
        /// Convert grid rectangle to UV coordinates (0-1 range).
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
