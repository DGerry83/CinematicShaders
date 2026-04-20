namespace CinematicShaders.UI.Content
{
    /// <summary>
    /// Content for the Scan screen shown when no JSON data is available.
    /// </summary>
    public class ScanScreenContent : IScreenContent
    {
        /// <summary>
        /// Default instance using built-in English content
        /// </summary>
        public static readonly ScanScreenContent Default = new ScanScreenContent();
        
        /// <inheritdoc/>
        public string[] BorderLines => new string[]
        {
            "╔═══════════════════════[NO DATA]═════════════════════════╗",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "╚═════════════════════════════════════════════════════════╝"
        };
        
        /// <inheritdoc/>
        public string[] ContentLines => new string[]
        {
            "                                                           ",
            "                                                           ",
            "                                                           ",
            "          ╔════════════════════════════════════╗           ",
            "          ║ ███████╗ ██████╗ █████╗ ███╗   ██╗ ║           ",
            "          ║ ██╔════╝██╔════╝██╔══██╗████╗  ██║ ║           ",
            "          ║ ███████╗██║     ███████║██╔██╗ ██║ ║           ",
            "          ║ ╚════██║██║     ██╔══██║██║╚██╗██║ ║           ",
            "          ║ ███████║╚██████╗██║  ██║██║ ╚████║ ║           ",
            "          ║ ╚══════╝ ╚═════╝╚═╝  ╚═╝╚═╝  ╚═══╝ ║           ",
            "          ╚════════════════════════════════════╝           ",
            "                                                           ",
            "                                                           "
        };
    }
}
