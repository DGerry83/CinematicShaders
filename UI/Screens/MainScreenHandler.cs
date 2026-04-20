using CinematicShaders.Core;
using CinematicShaders.UI.State;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Handles all MainScreen interactions.
    /// Bespoke logic for the main data display screen.
    /// </summary>
    public class MainScreenHandler
    {
        private readonly StarConsoleServices _services;
        private readonly ScreenRouter _router;
        private readonly StarCatalogHolographicDisplay _display;
        
        public MainScreenHandler(
            StarConsoleServices services, 
            ScreenRouter router,
            StarCatalogHolographicDisplay display)
        {
            _services = services;
            _router = router;
            _display = display;
        }
        
        public void OnElementClicked(string elementId)
        {
            switch (elementId)
            {
                case "name_value":
                    _display.EnterEditMode("name_value");
                    break;
                    
                case "search_input":
                    _display.EnterEditMode("search_input");
                    break;
                    
                case "save_button":
                    if (!string.IsNullOrEmpty(_display.EditingElementId))
                        _display.ExitEditMode(save: true);
                    else
                        _display.SaveStarName(_services.ActiveStar?.Name);
                    break;
                    
                case "reset_button":
                    if (!string.IsNullOrEmpty(_display.EditingElementId))
                        _display.ExitEditMode(save: false);
                    _display.ResetStarName();
                    break;
                    
                case "rescan_button":
                    _router.ShowConfirmRescan();
                    break;
                    
                case "scroll_up_glyph":
                    _display.ScrollSearchResults(-1);
                    break;
                    
                case "scroll_down_glyph":
                    _display.ScrollSearchResults(1);
                    break;
                    
                default:
                    if (elementId.StartsWith("result_"))
                        HandleResultClick(elementId);
                    break;
            }
        }
        
        private void HandleResultClick(string elementId)
        {
            if (!int.TryParse(elementId.Substring(7), out int index))
                return;
                
            var results = _display.FilteredResults;
            if (index < 0 || index >= results.Count)
                return;
                
            var star = results[index];
            if (star == null) return;
            
            // CRITICAL: Preserve existing behavior
            if (!string.IsNullOrEmpty(_display.EditingElementId))
                _display.ExitEditMode(save: false);
            
            _services.ActiveStar = star;
            _display.SelectStar(star);  // Calls existing working method
        }
    }
}
