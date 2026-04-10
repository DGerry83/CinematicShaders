namespace CinematicShaders.UI.Screens
{
    public class ScreenTransitionContext
    {
        public bool IsInitialStartup { get; set; }
        public string PreviousScreen { get; set; }
        public object UserData { get; set; }
        public bool HasStarSelected { get; set; }
    }
}
