using UnityEngine;
using CinematicShaders.Core;
using CinematicShaders.UI;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Shared helper that processes mouse input against a ClickZoneManager.
    /// Handles coordinate conversion, zone lookup, hover tracking, and click dispatch.
    /// </summary>
    public class ClickZoneInputProcessor
    {
        private ClickZoneManager _zoneManager;
        private string _hoveredElementId;
        private System.Action<string> _onHoverEnter;
        private System.Action<string> _onHoverExit;
        private System.Action _onMouseDown;
        private System.Action<SimpleClickZone, int, int> _onZoneEvaluated;
        private System.Action<SimpleClickZone> _onZoneClick;

        /// <summary>
        /// Creates a new input processor for the specified zone manager and callbacks.
        /// </summary>
        public ClickZoneInputProcessor(
            ClickZoneManager zoneManager,
            System.Action<string> onHoverEnter,
            System.Action<string> onHoverExit,
            System.Action onMouseDown = null,
            System.Action<SimpleClickZone, int, int> onZoneEvaluated = null,
            System.Action<SimpleClickZone> onZoneClick = null)
        {
            _zoneManager = zoneManager;
            _onHoverEnter = onHoverEnter;
            _onHoverExit = onHoverExit;
            _onMouseDown = onMouseDown;
            _onZoneEvaluated = onZoneEvaluated;
            _onZoneClick = onZoneClick;
        }

        /// <summary>
        /// Processes the current input event against the given display rectangle.
        /// </summary>
        public void ProcessInput(Rect displayRect)
        {
            if (Event.current == null) return;

            if (Event.current.type != EventType.MouseDown &&
                Event.current.type != EventType.MouseMove &&
                Event.current.type != EventType.MouseUp)
            {
                return;
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _onMouseDown?.Invoke();
            }

            Vector2 mousePos = Event.current.mousePosition;

            if (!displayRect.Contains(mousePos))
            {
                ClearHover();
                return;
            }

            float localX = mousePos.x - displayRect.x;
            float localY = mousePos.y - displayRect.y;

            GridPosition gridPos = TerminalGridConfig.PixelToGrid(
                localX, localY, TerminalGridConfig.CurrentDisplaySize);

            var zone = _zoneManager.FindZoneAt(gridPos.Column, gridPos.Row);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _onZoneEvaluated?.Invoke(zone, gridPos.Column, gridPos.Row);
            }

            if (zone != null && zone.IsEnabled)
            {
                if (zone.ElementId != _hoveredElementId)
                {
                    if (_hoveredElementId != null)
                        _onHoverExit?.Invoke(_hoveredElementId);

                    _hoveredElementId = zone.ElementId;
                    _onHoverEnter?.Invoke(zone.ElementId);
                }

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    _onZoneClick?.Invoke(zone);
                    zone.OnClick?.Invoke();
                }
            }
            else
            {
                ClearHover();
            }
        }

        /// <summary>
        /// Clears the current hover state and fires the hover exit callback if needed.
        /// </summary>
        public void ClearHover()
        {
            if (_hoveredElementId != null)
            {
                _onHoverExit?.Invoke(_hoveredElementId);
                _hoveredElementId = null;
            }
        }
    }
}
