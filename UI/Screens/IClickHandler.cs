using UnityEngine;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Interface for screen-specific click handling.
    /// Each screen (Main, Scan, Confirm) implements this to handle its own click logic.
    /// </summary>
    public interface IClickHandler
    {
        /// <summary>
        /// Called when the screen should handle input and click detection.
        /// Implementation should:
        /// 1. Get mouse position
        /// 2. Convert to grid coordinates using TerminalGridConfig.PixelToGrid
        /// 3. Look up zone using ZoneManager.FindZoneAt
        /// 4. Invoke OnClick callback if zone found and enabled
        /// </summary>
        /// <param name="displayRect">Current display rectangle in screen pixels</param>
        void HandleInput(Rect displayRect);
        
        /// <summary>
        /// Gets the zone manager for this screen.
        /// Screen should create and populate this in its initialization.
        /// </summary>
        ClickZoneManager ZoneManager { get; }
    }
}
