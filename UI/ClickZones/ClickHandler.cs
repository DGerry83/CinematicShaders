using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CinematicShaders.Native;
using CinematicShaders.Core;
using CinematicShaders.ClickZones;

namespace CinematicShaders.UI.ClickZones
{
    /// <summary>
    /// Handles click zone hit detection and hover highlighting using the MultiScreenClickZoneRegistry.
    /// Layer-agnostic: works with UV coordinates across any layer.
    /// Supports both UV-based and grid-based hit detection (grid is primary, UV is fallback).
    /// </summary>
    public class ClickHandler
    {
        // Current screen being tracked ("Main", "Scan", "Confirm")
        private string _currentScreenName = "Main";
        
        // Legacy zone storage for backward compatibility
        private List<ClickZone> _legacyZones = new List<ClickZone>();
        private ClickZone _hoveredZone = null;
        
        /// <summary>
        /// Legacy accessor for backward compatibility.
        /// Returns zones from registry when USE_UNIFIED_GRID=true, legacy zones otherwise.
        /// </summary>
        public List<ClickZone> Zones 
        { 
            get
            {
                if (UnifiedGridConfig.USE_UNIFIED_GRID)
                {
                    return MultiScreenClickZoneRegistry.GetZones(_currentScreenName);
                }
                return _legacyZones;
            }
        }
        
        /// <summary>
        /// Sets the current screen for click detection.
        /// Call this when switching screens.
        /// </summary>
        public void SetScreen(string screenName)
        {
            _currentScreenName = screenName ?? "Main";
            ModFileLogger.Log($"[ClickHandler] Screen set to: {_currentScreenName}");
            _hoveredZone = null;
        }
        
        /// <summary>
        /// Legacy method - sets zones directly.
        /// Only used when USE_UNIFIED_GRID = false.
        /// </summary>
        public void SetZones(List<ClickZone> zones)
        {
            ModFileLogger.Log($"[ClickHandler] SetZones() called with {zones?.Count ?? 0} zones");
            
            if (zones == null)
            {
                ModFileLogger.Log("[ClickHandler] WARNING: zones is null!");
                _legacyZones = new List<ClickZone>();
                return;
            }
            
            _legacyZones = zones;
            
            // Log first few zones
            foreach (var zone in _legacyZones.Take(3))
            {
                ModFileLogger.Log($"[ClickHandler] Zone set: {zone.ElementId}");
            }
            
            _hoveredZone = null;
        }
        
        /// <summary>
        /// Update hit detection and draw hover highlight.
        /// Uses registry when USE_UNIFIED_GRID = true, legacy zones otherwise.
        /// Call from Render() or OnGUI().
        /// </summary>
        public void Update(Rect displayRect)
        {
            if (UnifiedGridConfig.USE_UNIFIED_GRID)
            {
                UpdateUsingRegistry(displayRect);
            }
            else
            {
                UpdateUsingLegacy(displayRect);
            }
        }
        
        /// <summary>
        /// New stateless update using MultiScreenClickZoneRegistry.
        /// Queries registry on each frame - always fresh zones for current size.
        /// </summary>
        private void UpdateUsingRegistry(Rect displayRect)
        {
            if (Event.current == null) return;
            
            // Get zones for current screen - always fresh from registry!
            var zones = MultiScreenClickZoneRegistry.GetZones(_currentScreenName);
            
            if (zones == null || zones.Count == 0)
            {
                return;
            }
            
            Vector2 mousePos = Event.current.mousePosition;
            bool isMouseDown = (Event.current.type == EventType.MouseDown && Event.current.button == 0);
            bool isMouseUp = (Event.current.type == EventType.MouseUp && Event.current.button == 0);
            
            // Try grid-based hit detection first (more precise)
            ClickZone newHovered = FindZoneByGrid(mousePos, displayRect, zones, isMouseDown);
            
            // Fallback to UV if grid detection fails
            if (newHovered == null)
            {
                Vector2 mouseUV = ScreenToUV(mousePos, displayRect);
                newHovered = FindZoneByUV(mouseUV, zones);
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
            if (isMouseUp && _hoveredZone != null)
            {
                OnZoneClicked?.Invoke(_hoveredZone.ElementId);
            }
        }
        
        /// <summary>
        /// Legacy update using stored zones.
        /// </summary>
        private void UpdateUsingLegacy(Rect displayRect)
        {
            if (Event.current == null) return;
            
            Vector2 mousePos = Event.current.mousePosition;
            bool isMouseDown = (Event.current.type == EventType.MouseDown && Event.current.button == 0);
            bool isMouseUp = (Event.current.type == EventType.MouseUp && Event.current.button == 0);
            
            // Try grid-based hit detection first (more precise)
            ClickZone newHovered = FindZoneByGrid(mousePos, displayRect, _legacyZones, isMouseDown);
            
            // Fallback to UV if grid detection fails
            if (newHovered == null)
            {
                Vector2 mouseUV = ScreenToUV(mousePos, displayRect);
                newHovered = FindZoneByUV(mouseUV, _legacyZones);
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
            if (isMouseUp && _hoveredZone != null)
            {
                OnZoneClicked?.Invoke(_hoveredZone.ElementId);
            }
        }
        
        /// <summary>
        /// Find zone using grid-based coordinates (more precise alignment).
        /// Uses integer comparisons to avoid float precision issues.
        /// </summary>
        private ClickZone FindZoneByGrid(Vector2 mousePos, Rect displayRect, List<ClickZone> zones, bool logDebug = false)
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
                ModFileLogger.Log($"[ClickHandler] Checking {zones.Count} zones");
            }

            foreach (var zone in zones)
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
        private ClickZone FindZoneByUV(Vector2 mouseUV, List<ClickZone> zones)
        {
            foreach (var zone in zones)
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
