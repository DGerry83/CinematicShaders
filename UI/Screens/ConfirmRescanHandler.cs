using CinematicShaders.UI.State;

namespace CinematicShaders.UI.Screens
{
    public class ConfirmRescanHandler
    {
        private readonly ScreenRouter _router;
        private readonly StarCatalogHolographicDisplay _display;
        
        public ConfirmRescanHandler(ScreenRouter router, StarCatalogHolographicDisplay display)
        {
            _router = router;
            _display = display;
        }
        
        public void OnYesClicked()
        {
            _display.ConfirmRescan();  // Existing working method
        }
        
        public void OnNoClicked()
        {
            _router.ShowMain();
        }
    }
}
