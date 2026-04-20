using UnityEngine;

namespace CinematicShaders.UI.Layout
{
    /// <summary>
    /// Extension methods for Unity Rect to support layout operations.
    /// </summary>
    public static class RectExtensions
    {
        /// <summary>
        /// Shrinks the rect uniformly on all sides by the given amount.
        /// </summary>
        public static Rect Shrink(this Rect rect, float amount)
        {
            return rect.Shrink(amount, amount, amount, amount);
        }

        /// <summary>
        /// Shrinks the rect by the specified amounts on each side.
        /// </summary>
        public static Rect Shrink(this Rect rect, float left, float right, float top, float bottom)
        {
            return new Rect(
                rect.x + left,
                rect.y + top,
                Mathf.Max(0f, rect.width - left - right),
                Mathf.Max(0f, rect.height - top - bottom)
            );
        }

        /// <summary>
        /// Offsets the rect by the given x and y amounts.
        /// </summary>
        public static Rect Offset(this Rect rect, float x, float y)
        {
            return new Rect(rect.x + x, rect.y + y, rect.width, rect.height);
        }

        /// <summary>
        /// Returns a new rect with the same position but different size.
        /// </summary>
        public static Rect WithSize(this Rect rect, float width, float height)
        {
            return new Rect(rect.x, rect.y, width, height);
        }

        /// <summary>
        /// Gets the top-left corner of the rect.
        /// </summary>
        public static Vector2 TopLeft(this Rect rect)
        {
            return new Vector2(rect.x, rect.y);
        }

        /// <summary>
        /// Gets the top-right corner of the rect.
        /// </summary>
        public static Vector2 TopRight(this Rect rect)
        {
            return new Vector2(rect.xMax, rect.y);
        }

        /// <summary>
        /// Gets the bottom-left corner of the rect.
        /// </summary>
        public static Vector2 BottomLeft(this Rect rect)
        {
            return new Vector2(rect.x, rect.yMax);
        }

        /// <summary>
        /// Gets the bottom-right corner of the rect.
        /// </summary>
        public static Vector2 BottomRight(this Rect rect)
        {
            return new Vector2(rect.xMax, rect.yMax);
        }
    }
}
