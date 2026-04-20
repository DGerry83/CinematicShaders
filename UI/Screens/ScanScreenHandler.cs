using CinematicShaders.UI.State;

namespace CinematicShaders.UI.Screens
{
    public class ScanScreenHandler
    {
        private readonly ScreenRouter _router;
        private readonly StarCatalogHolographicDisplay _display;
        
        public ScanScreenHandler(ScreenRouter router, StarCatalogHolographicDisplay display)
        {
            _router = router;
            _display = display;
        }
        
        public void OnScanClicked()
        {
            _display.ScanCatalog();  // Existing working method
        }
    }
}
