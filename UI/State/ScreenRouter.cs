using CinematicShaders.Core;
using UnityEngine;

namespace CinematicShaders.UI.State
{
    /// <summary>
    /// Explicit screen transition rules.
    /// All transitions go through here so they're easy to find and modify.
    /// </summary>
    public class ScreenRouter
    {
        private readonly Screens.ScreenManager _screenManager;
        private readonly StarConsoleServices _services;
        
        public ScreenRouter(Screens.ScreenManager screenManager, StarConsoleServices services)
        {
            _screenManager = screenManager;
            _services = services;
        }
        
        public void ShowMain(Screens.ScreenTransitionContext context = null)
        {
            _screenManager.TransitionTo("Main", context ?? new Screens.ScreenTransitionContext());
        }
        
        public void ShowScan()
        {
            _screenManager.TransitionTo("Scan");
        }
        
        public void ShowConfirmRescan()
        {
            _screenManager.TransitionTo("ConfirmRescan");
        }
        
        public void ShowInfo(NamedStar star)
        {
            _services.ActiveStar = star;
            
            // Guard for now - InfoScreen not yet implemented
            var infoScreen = _screenManager.GetScreen("Info");
            if (infoScreen == null)
            {
                Debug.LogWarning("[ScreenRouter] Info screen not yet registered");
                return;
            }
            
            _screenManager.TransitionTo("Info", new Screens.ScreenTransitionContext 
            { 
                HasStarSelected = star != null 
            });
        }
    }
}
