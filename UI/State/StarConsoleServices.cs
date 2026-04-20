using CinematicShaders.Core;

namespace CinematicShaders.UI.State
{
    /// <summary>
    /// Shared mutable state and services for all Star Console screens.
    /// This is the single source of truth for cross-screen data.
    /// </summary>
    public class StarConsoleServices
    {
        // Reference to the native selector
        public KartographerSelector Selector { get; set; }
        
        // Currently selected/visible star
        public NamedStar ActiveStar { get; set; }
        
        // Search/filter state
        public string SearchQuery { get; set; } = "";
        
        // JSON paths for persistence
        public string CustomJsonPath { get; set; } = "";
        public string DefaultJsonPath { get; set; } = "";
    }
}
