using UnityEngine;
using CinematicShaders.Core;
using CinematicShaders.UI;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Click handling for ConfirmRescanScreen.
    /// Two zones: yes_button and no_button.
    /// </summary>
    public class ConfirmRescanClickHandler : IClickHandler
    {
        public ClickZoneManager ZoneManager { get; private set; }
        private ConfirmRescanScreen _screen;
        private ClickZoneInputProcessor _processor;
        
        /// <summary>
        /// Creates a new click handler for the specified ConfirmRescanScreen.
        /// </summary>
        public ConfirmRescanClickHandler(ConfirmRescanScreen screen)
        {
            _screen = screen;
            ZoneManager = new ClickZoneManager();
            _processor = new ClickZoneInputProcessor(
                ZoneManager,
                elementId => _screen.OnElementHoverEnter(elementId),
                elementId => _screen.OnElementHoverExit(elementId),
                onMouseDown: () => ModFileLogger.Log("[ConfirmRescanClickHandler] MouseDown detected"),
                onZoneEvaluated: (zone, col, row) =>
                {
                    if (zone != null)
                        ModFileLogger.Log($"[ConfirmRescanClickHandler] Zone found: {zone.ElementId} at grid ({col},{row})");
                    else
                        ModFileLogger.Log($"[ConfirmRescanClickHandler] No zone at grid ({col},{row})");
                },
                onZoneClick: (zone) => ModFileLogger.Log($"[ConfirmRescanClickHandler] Clicking zone: {zone.ElementId}"));
        }
        
        /// <summary>
        /// Sets up click zones for YES and NO buttons using constraint layout.
        /// Call this once during screen initialization.
        /// </summary>
        public void SetupZones()
        {
            ZoneManager.Clear();
            
            // Register YES button from constraint layout
            GridRegion yesRegion = _screen.Layout.GetGridArea("yes_button");
            ZoneManager.RegisterZone(
                "yes_button",
                yesRegion.TopLeft.Column,
                yesRegion.TopLeft.Row,
                yesRegion.Width,
                yesRegion.Height,
                "button",
                () => _screen.OnYesButtonClicked()
            );
            
            // Register NO button from constraint layout
            GridRegion noRegion = _screen.Layout.GetGridArea("no_button");
            ZoneManager.RegisterZone(
                "no_button",
                noRegion.TopLeft.Column,
                noRegion.TopLeft.Row,
                noRegion.Width,
                noRegion.Height,
                "button",
                () => _screen.OnNoButtonClicked()
            );
        }
        
        /// <summary>
        /// Handles input and click detection.
        /// Called every frame by ConfirmRescanScreen.
        /// </summary>
        public void HandleInput(Rect displayRect)
        {
            _processor.ProcessInput(displayRect);
        }
    }
}
