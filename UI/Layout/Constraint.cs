using UnityEngine;

namespace CinematicShaders.UI.Layout
{
    /// <summary>
    /// Abstract base class for layout constraints that determine how available space is allocated.
    /// </summary>
    public abstract class Constraint
    {
        /// <summary>
        /// Calculates the size this constraint requests from the available space.
        /// </summary>
        /// <param name="availableSpace">Total space available for distribution.</param>
        /// <returns>The size allocated to this constraint.</returns>
        public abstract float CalculateSize(float availableSpace);

        /// <summary>
        /// Creates a fixed-length constraint in pixels.
        /// </summary>
        public static Constraint Length(float pixels)
        {
            return new LengthConstraint(pixels);
        }

        /// <summary>
        /// Creates a fill constraint that distributes remaining space proportionally by weight.
        /// </summary>
        public static Constraint Fill(float weight = 1f)
        {
            return new FillConstraint(weight);
        }

        /// <summary>
        /// Creates a percentage constraint that requests a fixed percentage of available space.
        /// </summary>
        public static Constraint Percentage(float percent)
        {
            return new PercentageConstraint(percent);
        }
    }

    /// <summary>
    /// A constraint with a fixed pixel length.
    /// </summary>
    public class LengthConstraint : Constraint
    {
        public float Length { get; }

        public LengthConstraint(float length)
        {
            Length = Mathf.Max(0f, length);
        }

        public override float CalculateSize(float availableSpace)
        {
            return Length;
        }
    }

    /// <summary>
    /// A constraint that fills remaining space proportionally by weight.
    /// </summary>
    public class FillConstraint : Constraint
    {
        public float Weight { get; }

        public FillConstraint(float weight)
        {
            Weight = Mathf.Max(0f, weight);
        }

        public override float CalculateSize(float availableSpace)
        {
            return 0f;
        }
    }

    /// <summary>
    /// A constraint that requests a percentage of the available space.
    /// </summary>
    public class PercentageConstraint : Constraint
    {
        public float Percent { get; }

        public PercentageConstraint(float percent)
        {
            Percent = Mathf.Clamp01(percent);
        }

        public override float CalculateSize(float availableSpace)
        {
            return availableSpace * Percent;
        }
    }
}
