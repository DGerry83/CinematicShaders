using System.Collections.Generic;

namespace CinematicShaders.UI.Content
{
    /// <summary>
    /// Defines the content contract for screens.
    /// Enables separation of content (strings/data) from behavior (screen logic).
    /// Supports future localization by allowing different implementations.
    /// </summary>
    public interface IScreenContent
    {
        /// <summary>
        /// Border/frame lines for Layer 1 rendering
        /// </summary>
        string[] BorderLines { get; }
        
        /// <summary>
        /// Content lines for Layer 2 rendering (labels, art, or text)
        /// </summary>
        string[] ContentLines { get; }
    }
}
