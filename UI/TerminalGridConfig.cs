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
        /// Convert a grid region to pixel rectangle for rendering.
        /// </summary>
        /// <param name="region">Grid region to convert</param>
        /// <param name="displayWidth">Total display width in pixels</param>
        /// <param name="displayHeight">Total display height in pixels</param>
        /// <returns>Pixel rectangle in screen coordinates</returns>
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
        /// </summary>
        /// <param name="x">Pixel X coordinate</param>
        /// <param name="y">Pixel Y coordinate</param>
        /// <param name="displayWidth">Total display width in pixels</param>
        /// <param name="displayHeight">Total display height in pixels</param>
        /// <returns>Grid position (clamped to valid grid bounds)</returns>
        public static GridPosition PixelToGrid(float x, float y, float displayWidth, float displayHeight)
        {
            float cellWidth = displayWidth / GRID_COLUMNS;
            float cellHeight = displayHeight / GRID_ROWS;

            int col = Mathf.FloorToInt(x / cellWidth);
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
    }
}
