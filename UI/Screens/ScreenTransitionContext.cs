namespace CinematicShaders.UI.Screens
{
    public class ScreenTransitionContext
    {
        public bool IsInitialStartup { get; set; }
        public string PreviousScreen { get; set; }
        public object UserData { get; set; }
        public bool HasStarSelected { get; set; }
        
        /// <summary>
        /// For SplashScreen: the screen to transition to after animation completes.
        /// Set by HolographicDisplay based on JSON availability check.
        /// </summary>
        public string TargetScreenName { get; set; }
    }
}
