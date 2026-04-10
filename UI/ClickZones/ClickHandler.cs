using System;
using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.Native;
using CinematicShaders.Core;

namespace CinematicShaders.UI.ClickZones
{
    /// <summary>
    /// Handles click zone hit detection and hover highlighting.
    /// Layer-agnostic: works with UV coordinates across any layer.
    /// </summary>
    public class ClickHandler
    {
        private List<ClickZone> _zones = new List<ClickZone>();
        private ClickZone _hoveredZone = null;
        
        public List<ClickZone> Zones => _zones;
        
        /// <summary>
        /// Set the list of active click zones.
        /// </summary>
        public void SetZones(List<ClickZone> zones)
        {
            _zones = zones ?? new List<ClickZone>();
            _hoveredZone = null;
        }
        
        /// <summary>
        /// Update hit detection and draw hover highlight.
        /// Call from Render() or OnGUI().
        /// </summary>
        public void Update(Rect displayRect)
        {
            if (Event.current == null) return;
            
            Vector2 mousePos = Event.current.mousePosition;
            Vector2 mouseUV = ScreenToUV(mousePos, displayRect);
            
            // Find hovered zone
            ClickZone newHovered = null;
            foreach (var zone in _zones)
            {
                if (zone.IsEnabled && zone.ContainsUV(mouseUV))
                {
                    newHovered = zone;
                    break;
                }
            }
            
            // Handle hover change
            if (newHovered?.ElementId != _hoveredZone?.ElementId)
            {
                _hoveredZone = newHovered;
            }
            
            // Draw or clear highlight box
            if (_hoveredZone != null)
            {
                DrawHighlightBox(_hoveredZone.UVRect);
            }
            else
            {
                // Clear box - DISABLED: Box drawing needs proper struct toolkit implementation
                // StarfieldNative.CR_DrawCRTBox(0, 0, 0, 0, 0, 0, 0);
            }
            
            // Handle click
            if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                if (_hoveredZone != null)
                {
                    OnZoneClicked?.Invoke(_hoveredZone.ElementId);
                }
            }
        }
        
        private Vector2 ScreenToUV(Vector2 screenPos, Rect displayRect)
        {
            float u = (screenPos.x - displayRect.x) / displayRect.width;
            float v = (screenPos.y - displayRect.y) / displayRect.height;
            return new Vector2(u, v);
        }
        
        private void DrawHighlightBox(Rect uvRect)
        {
            // DISABLED: Box drawing needs proper struct toolkit implementation
            // TODO: Implement using CRTOverlayParams via struct generator
            
            // uint color = GetGridColorUint();
            // float thickness = 0.003f; // ~2-3px
            // StarfieldNative.CR_DrawCRTBox(1, uvRect.x, uvRect.y, 
            //     uvRect.xMax, uvRect.yMax, color, thickness);
        }
        
        private uint GetGridColorUint()
        {
            Color c = GetGridColor();
            uint r = (uint)(c.r * 255) & 0xFF;
            uint g = (uint)(c.g * 255) & 0xFF;
            uint b = (uint)(c.b * 255) & 0xFF;
            return 0xFF000000 | (r << 16) | (g << 8) | b;
        }
        
        private Color GetGridColor()
        {
            int colorIndex = StarfieldSettings.KartographerGridColor;
            switch (colorIndex)
            {
                case 0: return new Color(0.1f, 0.9f, 0.7f);  // Cyan (default)
                case 1: return new Color(1.0f, 0.65f, 0.0f); // Amber
                case 2: return new Color(0.85f, 0.95f, 1.0f); // Ice Blue
                case 3: return new Color(0.25f, 1.0f, 0.0f); // Matrix Green
                default: return new Color(0.1f, 0.9f, 0.7f);
            }
        }
        
        public event Action<string> OnZoneClicked;
    }
}
