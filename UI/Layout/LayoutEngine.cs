using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Layout
{
    /// <summary>
    /// Specifies the direction for layout splitting.
    /// </summary>
    public enum Direction
    {
        Horizontal,
        Vertical
    }

    /// <summary>
    /// Core engine for constraint-based layout calculations.
    /// </summary>
    public class LayoutEngine
    {
        /// <summary>
        /// Splits the available space according to the given constraints and direction.
        /// First pass allocates Length and Percentage constraints.
        /// Second pass distributes remaining space to Fill constraints by weight.
        /// </summary>
        public Rect[] Split(Direction direction, Constraint[] constraints, Rect availableSpace)
        {
            if (constraints == null || constraints.Length == 0)
            {
                return new Rect[0];
            }

            float totalSpace = direction == Direction.Horizontal ? availableSpace.width : availableSpace.height;
            float[] sizes = new float[constraints.Length];
            List<int> fillIndices = new List<int>();
            float allocated = 0f;
            float totalWeight = 0f;

            // First pass: allocate Length and Percentage constraints
            for (int i = 0; i < constraints.Length; i++)
            {
                Constraint constraint = constraints[i];
                if (constraint is FillConstraint fill)
                {
                    fillIndices.Add(i);
                    totalWeight += fill.Weight;
                }
                else
                {
                    float size = constraint.CalculateSize(totalSpace);
                    sizes[i] = size;
                    allocated += size;
                }
            }

            // Second pass: distribute remaining space to Fill constraints
            if (fillIndices.Count > 0)
            {
                float remaining = Mathf.Max(0f, totalSpace - allocated);
                if (totalWeight > 0f)
                {
                    for (int i = 0; i < fillIndices.Count; i++)
                    {
                        int index = fillIndices[i];
                        FillConstraint fill = (FillConstraint)constraints[index];
                        sizes[index] = remaining * (fill.Weight / totalWeight);
                    }
                }
                else
                {
                    // If total weight is zero, divide remaining space equally
                    float equalShare = remaining / fillIndices.Count;
                    for (int i = 0; i < fillIndices.Count; i++)
                    {
                        sizes[fillIndices[i]] = equalShare;
                    }
                }
            }

            // Build rectangles
            Rect[] results = new Rect[constraints.Length];
            float current = direction == Direction.Horizontal ? availableSpace.x : availableSpace.y;

            for (int i = 0; i < constraints.Length; i++)
            {
                if (direction == Direction.Horizontal)
                {
                    results[i] = new Rect(current, availableSpace.y, sizes[i], availableSpace.height);
                    current += sizes[i];
                }
                else
                {
                    results[i] = new Rect(availableSpace.x, current, availableSpace.width, sizes[i]);
                    current += sizes[i];
                }
            }

            return results;
        }

        /// <summary>
        /// Splits the available space horizontally according to the given constraints.
        /// </summary>
        public Rect[] SplitHorizontal(Rect space, params Constraint[] constraints)
        {
            return Split(Direction.Horizontal, constraints, space);
        }

        /// <summary>
        /// Splits the available space vertically according to the given constraints.
        /// </summary>
        public Rect[] SplitVertical(Rect space, params Constraint[] constraints)
        {
            return Split(Direction.Vertical, constraints, space);
        }

        /// <summary>
        /// Calculates the minimum size required to satisfy all non-fill constraints.
        /// </summary>
        public float CalculateMinSize(Constraint[] constraints, float availableSpace)
        {
            if (constraints == null || constraints.Length == 0)
            {
                return 0f;
            }

            float minSize = 0f;
            for (int i = 0; i < constraints.Length; i++)
            {
                if (!(constraints[i] is FillConstraint))
                {
                    minSize += constraints[i].CalculateSize(availableSpace);
                }
            }

            return minSize;
        }
    }
}
