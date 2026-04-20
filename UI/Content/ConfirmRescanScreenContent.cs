namespace CinematicShaders.UI.Content
{
    /// <summary>
    /// Content for the confirmation dialog for rescan operation.
    /// </summary>
    public class ConfirmRescanScreenContent : IScreenContent
    {
        /// <summary>
        /// Default instance using built-in English content
        /// </summary>
        public static readonly ConfirmRescanScreenContent Default = new ConfirmRescanScreenContent();
        
        /// <inheritdoc/>
        public string[] BorderLines => new string[]
        {
            "╔════════════════════[ARE YOU SURE?]══════════════════════╗",
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
            "                !STAR NAMES WILL BE RESET!                 ",
            "                                                           ",
            "                                                           ",
            "                                                           ",
            "                                                           ",
            "                                                           ",
            "                                                           ",
            "   [YES]                                            [NO]   ",
            "                                                           ",
            "                                                           "
        };
    }
}
