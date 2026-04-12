using System;
using UnityEngine;

namespace CinematicShaders.UI
{
    /// <summary>
    /// Terminal grid configuration and coordinate conversion utilities.
    /// Defines the 59×13 grid that matches the Layer 1 border.
    /// </summary>
    public static class TerminalGridConfig
    {
        /// <summary>
        /// Number of columns in the terminal grid (59 = Layer 1 border width)
        /// </summary>
        public const int GRID_COLUMNS = 59;

        /// <summary>
        /// Number of rows in the terminal grid (13 = Layer 1 border height)
        /// </summary>
        public const int GRID_ROWS = 13;

        /// <summary>
        /// Default grid dimensions
        /// </summary>
        public static readonly GridDimensions Dimensions = new GridDimensions(GRID_COLUMNS, GRID_ROWS);

        /// <summary>
        /// Glyph metrics for each display size.
        /// Large is ground truth - derived from working 825×450 display.
        /// </summary>
        public static class GlyphMetrics
        {
            // Large (35pt) - Ground truth from working display
            public const float GLYPH_WIDTH_LARGE = 14.0f;      // 825/59 = 13.98 ≈ 14
            public const float GLYPH_HEIGHT_LARGE = 34.6f;     // 450/13 = 34.6
            
            // Medium (24pt) - Scaled from Large: 24/35 = 0.69
            public const float GLYPH_WIDTH_MEDIUM = 10.0f;     // 14 * 0.69 ≈ 10
            public const float GLYPH_HEIGHT_MEDIUM = 24.0f;    // 34.6 * 0.69 ≈ 24
            
            // Small (18pt) - Scaled from Large: 18/35 = 0.51
            public const float GLYPH_WIDTH_SMALL = 7.0f;       // 14 * 0.51 ≈ 7
            public const float GLYPH_HEIGHT_SMALL = 18.0f;     // 34.6 * 0.51 ≈ 18
            
            public static (float width, float height) GetGlyphMetrics(HolographicDisplaySize size)
            {
                switch (size)
                {
                    case HolographicDisplaySize.Small:
                        return (GLYPH_WIDTH_SMALL, GLYPH_HEIGHT_SMALL);
                    case HolographicDisplaySize.Medium:
                        return (GLYPH_WIDTH_MEDIUM, GLYPH_HEIGHT_MEDIUM);
                    case HolographicDisplaySize.Large:
                        return (GLYPH_WIDTH_LARGE, GLYPH_HEIGHT_LARGE);
                    default:
                        return (GLYPH_WIDTH_LARGE, GLYPH_HEIGHT_LARGE);
                }
            }
        }

        /// <summary>
        /// Calculate display dimensions from glyph metrics.
        /// Large uses hardcoded values for compatibility.
        /// </summary>
        public static Vector2 GetDisplayDimensions(HolographicDisplaySize size)
        {
            if (size == HolographicDisplaySize.Large)
            {
                // Ground truth: hardcoded working dimensions
                return new Vector2(825f, 450f);
            }
            
            // Small/Medium: calculated from glyph metrics
            var (glyphWidth, glyphHeight) = GlyphMetrics.GetGlyphMetrics(size);
            return new Vector2(
                glyphWidth * GRID_COLUMNS,
                glyphHeight * GRID_ROWS
            );
        }

        /// <summary>
        /// Convert grid coordinates to pixel position using glyph metrics.
        /// </summary>
        public static Vector2 GridToPixel(int column, int row, HolographicDisplaySize size)
        {
            var (glyphWidth, glyphHeight) = GlyphMetrics.GetGlyphMetrics(size);
            return new Vector2(
                column * glyphWidth,
                row * glyphHeight
            );
        }

        /// <summary>
        /// Convert pixel position to grid coordinates using glyph metrics.
        /// </summary>
        public static GridPosition PixelToGrid(float x, float y, HolographicDisplaySize size)
        {
            var (glyphWidth, glyphHeight) = GlyphMetrics.GetGlyphMetrics(size);
            int col = Mathf.FloorToInt(x / glyphWidth);
            int row = Mathf.FloorToInt(y / glyphHeight);
            
            col = Mathf.Clamp(col, 0, GRID_COLUMNS - 1);
            row = Mathf.Clamp(row, 0, GRID_ROWS - 1);
            
            return new GridPosition(col, row);
        }

        /// <summary>
        /// Convert a grid region to pixel rectangle for rendering.
        /// Uses the unified 59×13 grid system.
        /// </summary>
        /// <param name="region">Grid region (columns/rows on 59×13 grid)</param>
        /// <param name="displayWidth">Target display width in pixels</param>
        /// <param name="displayHeight">Target display height in pixels</param>
        /// <returns>Pixel rectangle for Unity rendering</returns>
        [Obsolete("Use GridToPixel with size parameter instead")]
        public static Rect GridToPixelRect(GridRegion region, float displayWidth, float displayHeight)
        {
            float cellWidth = displayWidth / GRID_COLUMNS;
            float cellHeight = displayHeight / GRID_ROWS;

            float x = region.TopLeft.Column * cellWidth;
            float y = region.TopLeft.Row * cellHeight;
            float width = region.Width * cellWidth;
            float height = region.Height * cellHeight;

            return new Rect(x, y, width, height);
        }

        /// <summary>
        /// Convert pixel coordinates to the nearest grid position.
        /// Accounts for texture flip (row 0 at bottom visually due to Rect(0,1,1,-1)).
        /// </summary>
        /// <param name="x">Pixel X coordinate</param>
        /// <param name="y">Pixel Y coordinate</param>
        /// <param name="displayWidth">Total display width in pixels</param>
        /// <param name="displayHeight">Total display height in pixels</param>
        /// <returns>Grid position (clamped to valid grid bounds)</returns>
        [Obsolete("Use PixelToGrid with size parameter instead")]
        public static GridPosition PixelToGrid(float x, float y, float displayWidth, float displayHeight)
        {
            float cellWidth = displayWidth / GRID_COLUMNS;
            float cellHeight = displayHeight / GRID_ROWS;

            int col = Mathf.FloorToInt(x / cellWidth);
            // DEBUG: Using direct Y to diagnose offset issue
            int row = Mathf.FloorToInt(y / cellHeight);

            // Clamp to valid grid bounds
            col = Mathf.Clamp(col, 0, GRID_COLUMNS - 1);
            row = Mathf.Clamp(row, 0, GRID_ROWS - 1);

            return new GridPosition(col, row);
        }

        /// <summary>
        /// Get the pixel rectangle for a single grid cell.
        /// </summary>
        /// <param name="position">Grid position</param>
        /// <param name="displayWidth">Total display width in pixels</param>
        /// <param name="displayHeight">Total display height in pixels</param>
        /// <returns>Pixel rectangle for the cell</returns>
        [Obsolete("Use GridToPixel with size parameter instead")]
        public static Rect GetCellRect(GridPosition position, float displayWidth, float displayHeight)
        {
            return GridToPixelRect(new GridRegion(position, 1, 1), displayWidth, displayHeight);
        }

        /// <summary>
        /// Calculate the size of a single grid cell in pixels.
        /// </summary>
        /// <param name="displayWidth">Total display width in pixels</param>
        /// <param name="displayHeight">Total display height in pixels</param>
        /// <returns>Cell size as a Vector2 (width, height)</returns>
        [Obsolete("Use GlyphMetrics.GetGlyphMetrics instead")]
        public static Vector2 GetCellSize(float displayWidth, float displayHeight)
        {
            return new Vector2(
                displayWidth / GRID_COLUMNS,
                displayHeight / GRID_ROWS
            );
        }

        /// <summary>
        /// Check if a grid position is within the valid grid bounds.
        /// </summary>
        public static bool IsValidPosition(GridPosition position)
        {
            return position.Column >= 0 && position.Column < GRID_COLUMNS &&
                   position.Row >= 0 && position.Row < GRID_ROWS;
        }

        /// <summary>
        /// Validate that grid coordinates are within bounds.
        /// </summary>
        /// <param name="column">Column index (0 to GRID_COLUMNS-1)</param>
        /// <param name="row">Row index (0 to GRID_ROWS-1)</param>
        /// <returns>True if coordinates are valid</returns>
        public static bool IsValidGridCoordinate(int column, int row)
        {
            return column >= 0 && column < GRID_COLUMNS &&
                   row >= 0 && row < GRID_ROWS;
        }

        /// <summary>
        /// Clamp grid coordinates to valid bounds.
        /// </summary>
        /// <param name="column">Column index (will be clamped)</param>
        /// <param name="row">Row index (will be clamped)</param>
        /// <returns>Clamped GridPosition</returns>
        public static GridPosition ClampToGrid(int column, int row)
        {
            return new GridPosition(
                Mathf.Clamp(column, 0, GRID_COLUMNS - 1),
                Mathf.Clamp(row, 0, GRID_ROWS - 1)
            );
        }
    }
}
