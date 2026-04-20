namespace CinematicShaders.UI.Content
{
    /// <summary>
    /// Content for the Main screen showing star data with search results.
    /// </summary>
    public class MainScreenContent : IScreenContent
    {
        /// <summary>
        /// Default instance using built-in English content
        /// </summary>
        public static readonly MainScreenContent Default = new MainScreenContent();
        
        /// <inheritdoc/>
        public string[] BorderLines => new string[]
        {
            "╔════[STAR DATA]═══════════════════╦╦═════[RESULTS]═══════╗",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "╟──────────────────────────────────╢║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "╚══════════════════════════════════╩╩═════════════════════╝"
        };
        
        /// <inheritdoc/>
        public string[] ContentLines => new string[]
        {
            "                                                           ",
            "  HIP:                                                     ",
            "  NAME:                                                    ",
            "  DISTANCE:                                                ",
            "  SPECTRAL:                                                ",
            "  MAG:                                                     ",
            "  CONST:                                                   ",
            "                                                           ",
            "                 [SAVE]   [RESET]                          ",
            "                                                           ",
            "  SEARCH                  [RESCAN]                         ",
            "  ►                                    ▲               ▼   ",
            "                                                           "
        };
        
        // Future: Localization support
        // public static IScreenContent LoadLocalized(string locale) { ... }
    }
}
