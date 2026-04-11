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
            
            // DEBUG: Log all zones when set
            ModFileLogger.Log($"[ClickHandler] SetZones called with {_zones.Count} zones:");
            foreach (var zone in _zones)
            {
                ModFileLogger.Log($"[ClickHandler]   Zone '{zone.ElementId}': GridRect=[{zone.GridRect.x:F0},{zone.GridRect.y:F0},{zone.GridRect.width:F0},{zone.GridRect.height:F0}], Enabled={zone.IsEnabled}");
            }
        }
        
        // DEBUG: Track last logged position to avoid spam
        private Vector2 _lastLoggedPos = Vector2.zero;
        private string _lastLoggedZone = null;
        
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
            
            // DEBUG: Log EVERY mouse down for debugging
            if (isMouseDown)
            {
                ModFileLogger.Log($"[ClickHandler] === MOUSE DOWN ===");
                ModFileLogger.Log($"[ClickHandler] Mouse screen position: ({mousePos.x:F1}, {mousePos.y:F1})");
                ModFileLogger.Log($"[ClickHandler] DisplayRect: x={displayRect.x:F0}, y={displayRect.y:F0}, width={displayRect.width:F0}, height={displayRect.height:F0}");
                ModFileLogger.Log($"[ClickHandler] Zone count: {_zones.Count}");
            }
            
            // DEBUG: Track last logged position to avoid spam on hover
            bool shouldLog = false;
            if (Vector2.Distance(mousePos, _lastLoggedPos) > 50f)
            {
                shouldLog = true;
                _lastLoggedPos = mousePos;
            }
            
            // Try grid-based hit detection first (more precise)
            ClickZone newHovered = FindZoneByGrid(mousePos, displayRect, isMouseDown); // Log details on mouse down
            
            // DEBUG: Log zone detection on mouse up
            if (isMouseUp)
            {
                string zoneName = newHovered?.ElementId ?? "none";
                ModFileLogger.Log($"[ClickHandler] MOUSE UP - hovered zone: {zoneName}");
                if (_hoveredZone != null && newHovered == _hoveredZone)
                {
                    ModFileLogger.Log($"[ClickHandler] CLICK DETECTED on zone: {_hoveredZone.ElementId}");
                }
            }
            
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
            
            GridPosition gridPos = TerminalGridConfig.PixelToGrid(
                localX,
                localY,
                displayRect.width,
                displayRect.height
            );
            
            // DEBUG: Log grid conversion
            if (logDebug)
            {
                float cellWidth = displayRect.width / TerminalGridConfig.GRID_COLUMNS;
                float cellHeight = displayRect.height / TerminalGridConfig.GRID_ROWS;
                ModFileLogger.Log($"[ClickHandler] Local pixel: ({localX:F1}, {localY:F1})");
                ModFileLogger.Log($"[ClickHandler] Cell size: ({cellWidth:F1}, {cellHeight:F1})");
                ModFileLogger.Log($"[ClickHandler] Grid position: ({gridPos.Column}, {gridPos.Row})");
            }
            
            if (logDebug)
            {
                ModFileLogger.Log($"[ClickHandler] Checking {_zones.Count} zones...");
            }
            
            foreach (var zone in _zones)
            {
                if (!zone.IsEnabled) 
                {
                    if (logDebug) ModFileLogger.Log($"[ClickHandler]   Zone '{zone.ElementId}' is DISABLED");
                    continue;
                }
                
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
                    
                    if (logDebug)
                    {
                        ModFileLogger.Log($"[ClickHandler]   Check '{zone.ElementId}': grid=({gridPos.Column},{gridPos.Row}) vs zone=[{zoneLeft}-{zoneRight},{zoneTop}-{zoneBottom}] colHit={colHit}, rowHit={rowHit}");
                    }
                    
                    if (colHit && rowHit)
                    {
                        if (logDebug) ModFileLogger.Log($"[ClickHandler]   >>> HIT '{zone.ElementId}' <<<");
                        return zone;
                    }
                }
                else if (logDebug)
                {
                    ModFileLogger.Log($"[ClickHandler]   Zone '{zone.ElementId}' has no GridRect");
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
