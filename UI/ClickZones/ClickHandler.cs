using System;
using System.Collections.Generic;
using System.Linq;
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
            ModFileLogger.Log($"[ClickHandler] SetZones() called with {zones?.Count ?? 0} zones");
            
            if (zones == null)
            {
                ModFileLogger.Log("[ClickHandler] WARNING: zones is null!");
                _zones = new List<ClickZone>();
                return;
            }
            
            _zones = zones;
            
            // Log first few zones
            foreach (var zone in _zones.Take(3))
            {
                ModFileLogger.Log($"[ClickHandler] Zone set: {zone.ElementId}");
            }
            
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
            bool isMouseDown = (Event.current.type == EventType.MouseDown && Event.current.button == 0);
            bool isMouseUp = (Event.current.type == EventType.MouseUp && Event.current.button == 0);
            

            // Try grid-based hit detection first (more precise)
            ClickZone newHovered = FindZoneByGrid(mousePos, displayRect, isMouseDown); // Log details on mouse down
            
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
        /// Uses integer comparisons to avoid float precision issues.
        /// </summary>
        private ClickZone FindZoneByGrid(Vector2 mousePos, Rect displayRect, bool logDebug = false)
        {
            float localX = mousePos.x - displayRect.x;
            float localY = mousePos.y - displayRect.y;
            
            // Use the current display size for glyph-based coordinate conversion
            GridPosition gridPos = TerminalGridConfig.PixelToGrid(
                localX,
                localY,
                TerminalGridConfig.CurrentDisplaySize
            );
            
            // At start (only log on mouse down to avoid spam)
            if (logDebug)
            {
                ModFileLogger.Log($"[ClickHandler] FindZoneByGrid called, mouse: {mousePos}, display: {displayRect}");
                ModFileLogger.Log($"[ClickHandler] Converted to grid: {gridPos}");
                ModFileLogger.Log($"[ClickHandler] Checking {_zones.Count} zones");
            }

            foreach (var zone in _zones)
            {
                if (!zone.IsEnabled) continue;
                
                // Check if zone has grid rect
                if (zone.GridRect.width > 0 && zone.GridRect.height > 0)
                {
                    // Use integer comparisons to avoid float precision issues
                    int zoneLeft = Mathf.RoundToInt(zone.GridRect.x);
                    int zoneTop = Mathf.RoundToInt(zone.GridRect.y);
                    int zoneRight = zoneLeft + Mathf.RoundToInt(zone.GridRect.width);
                    int zoneBottom = zoneTop + Mathf.RoundToInt(zone.GridRect.height);
                    
                    bool colHit = gridPos.Column >= zoneLeft && gridPos.Column < zoneRight;
                    bool rowHit = gridPos.Row >= zoneTop && gridPos.Row < zoneBottom;
                    
                    if (colHit && rowHit)
                    {
                        // When zone found
                        if (logDebug)
                        {
                            ModFileLogger.Log($"[ClickHandler] Found zone: {zone.ElementId}");
                        }
                        return zone;
                    }
                }

            }
            
            // When no zone found
            if (logDebug)
            {
                ModFileLogger.Log("[ClickHandler] No zone found");
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
