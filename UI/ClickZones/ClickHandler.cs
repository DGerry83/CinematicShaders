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
    /// Supports both UV-based and grid-based hit detection (grid is primary, UV is fallback).
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
        /// Supports both UV-based and grid-based zones.
        /// Call from Render() or OnGUI().
        /// </summary>
        public void Update(Rect displayRect)
        {
            if (Event.current == null) return;
            
            Vector2 mousePos = Event.current.mousePosition;
            
            // Try grid-based hit detection first (more precise)
            ClickZone newHovered = FindZoneByGrid(mousePos, displayRect);
            
            // Fallback to UV if grid detection fails
            if (newHovered == null)
            {
                Vector2 mouseUV = ScreenToUV(mousePos, displayRect);
                newHovered = FindZoneByUV(mouseUV);
            }
            
            // Handle hover change
            if (newHovered?.ElementId != _hoveredZone?.ElementId)
            {
                _hoveredZone = newHovered;
            }
            
            // Draw or clear highlight box
            if (_hoveredZone != null)
            {
                DrawHighlightBox(_hoveredZone);
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
        
        /// <summary>
        /// Find zone using grid-based coordinates (more precise alignment).
        /// </summary>
        private ClickZone FindZoneByGrid(Vector2 mousePos, Rect displayRect)
        {
            GridPosition gridPos = TerminalGridConfig.PixelToGrid(
                mousePos.x - displayRect.x,
                mousePos.y - displayRect.y,
                displayRect.width,
                displayRect.height
            );
            
            foreach (var zone in _zones)
            {
                if (!zone.IsEnabled) continue;
                
                // Check if zone has grid rect
                if (zone.GridRect.width > 0 && zone.GridRect.height > 0)
                {
                    if (zone.GridRect.Contains(new Vector2(gridPos.Column, gridPos.Row)))
                    {
                        return zone;
                    }
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Find zone using UV coordinates (fallback).
        /// </summary>
        private ClickZone FindZoneByUV(Vector2 mouseUV)
        {
            foreach (var zone in _zones)
            {
                if (zone.IsEnabled && zone.UVRect.width > 0 && zone.ContainsUV(mouseUV))
                {
                    return zone;
                }
            }
            return null;
        }
        
        private Vector2 ScreenToUV(Vector2 screenPos, Rect displayRect)
        {
            float u = (screenPos.x - displayRect.x) / displayRect.width;
            float v = (screenPos.y - displayRect.y) / displayRect.height;
            return new Vector2(u, v);
        }
        
        /// <summary>
        /// Draw highlight box around the zone.
        /// Uses grid rect if available, falls back to UV rect.
        /// </summary>
        private void DrawHighlightBox(ClickZone zone)
        {
            // DISABLED: Box drawing needs proper struct toolkit implementation
            // When enabled, use zone.GridRect or zone.UVRect based on availability
        }
        
        public event Action<string> OnZoneClicked;
    }
}
