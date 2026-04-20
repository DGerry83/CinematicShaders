using System;
using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI
{
    /// <summary>
    /// Represents a position on the terminal grid (59×13)
    /// </summary>
    public readonly struct GridPosition : IEquatable<GridPosition>
    {
        public readonly int Column;
        public readonly int Row;

        public GridPosition(int column, int row)
        {
            Column = column;
            Row = row;
        }

        /// <summary>
        /// Factory method to create a GridPosition at the specified coordinates
        /// </summary>
        public static GridPosition At(int col, int row)
        {
            return new GridPosition(col, row);
        }

        /// <summary>
        /// Get position offset to the right by specified count
        /// </summary>
        public GridPosition Right(int count = 1)
        {
            return new GridPosition(Column + count, Row);
        }

        /// <summary>
        /// Get position offset to the left by specified count
        /// </summary>
        public GridPosition Left(int count = 1)
        {
            return new GridPosition(Column - count, Row);
        }

        /// <summary>
        /// Get position offset upward by specified count
        /// </summary>
        public GridPosition Up(int count = 1)
        {
            return new GridPosition(Column, Row - count);
        }

        /// <summary>
        /// Get position offset downward by specified count
        /// </summary>
        public GridPosition Down(int count = 1)
        {
            return new GridPosition(Column, Row + count);
        }

        public bool Equals(GridPosition other)
        {
            return Column == other.Column && Row == other.Row;
        }

        public override bool Equals(object obj)
        {
            return obj is GridPosition other && Equals(other);
        }

        public override int GetHashCode()
        {
            // Manual hash code combination for .NET Framework 4.8 compatibility
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Column;
                hash = hash * 31 + Row;
                return hash;
            }
        }

        public static bool operator ==(GridPosition left, GridPosition right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GridPosition left, GridPosition right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"({Column}, {Row})";
        }
    }

    /// <summary>
    /// Defines the dimensions of the terminal grid
    /// </summary>
    public readonly struct GridDimensions : IEquatable<GridDimensions>
    {
        public readonly int Columns;
        public readonly int Rows;

        public int TotalCells => Columns * Rows;

        public GridDimensions(int columns, int rows)
        {
            Columns = columns;
            Rows = rows;
        }

        public bool Equals(GridDimensions other)
        {
            return Columns == other.Columns && Rows == other.Rows;
        }

        public override bool Equals(object obj)
        {
            return obj is GridDimensions other && Equals(other);
        }

        public override int GetHashCode()
        {
            // Manual hash code combination for .NET Framework 4.8 compatibility
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Columns;
                hash = hash * 31 + Rows;
                return hash;
            }
        }
    }

    /// <summary>
    /// Represents a rectangular region on the terminal grid
    /// </summary>
    public readonly struct GridRegion : IEquatable<GridRegion>
    {
        public readonly GridPosition TopLeft;
        public readonly int Width;
        public readonly int Height;

        /// <summary>
        /// Bottom-right corner position (inclusive)
        /// </summary>
        public GridPosition BottomRight => new GridPosition(TopLeft.Column + Width - 1, TopLeft.Row + Height - 1);

        public GridRegion(GridPosition topLeft, int width, int height)
        {
            TopLeft = topLeft;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// Check if a grid position is contained within this region
        /// </summary>
        public bool Contains(GridPosition position)
        {
            return position.Column >= TopLeft.Column &&
                   position.Column < TopLeft.Column + Width &&
                   position.Row >= TopLeft.Row &&
                   position.Row < TopLeft.Row + Height;
        }

        /// <summary>
        /// Get all positions within this region
        /// </summary>
        public IEnumerable<GridPosition> GetAllPositions()
        {
            for (int row = TopLeft.Row; row < TopLeft.Row + Height; row++)
            {
                for (int col = TopLeft.Column; col < TopLeft.Column + Width; col++)
                {
                    yield return new GridPosition(col, row);
                }
            }
        }

        public bool Equals(GridRegion other)
        {
            return TopLeft.Equals(other.TopLeft) && Width == other.Width && Height == other.Height;
        }

        public override bool Equals(object obj)
        {
            return obj is GridRegion other && Equals(other);
        }

        public override int GetHashCode()
        {
            // Manual hash code combination for .NET Framework 4.8 compatibility
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + TopLeft.GetHashCode();
                hash = hash * 31 + Width;
                hash = hash * 31 + Height;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"[{TopLeft} - {BottomRight}] ({Width}×{Height})";
        }
    }
}
