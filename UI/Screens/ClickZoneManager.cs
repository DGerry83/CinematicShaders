using System;
using System.Collections.Generic;
using System.Linq;
using CinematicShaders.Core;
using UnityEngine;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Manages click zones for a single screen using grid coordinates.
    /// Provides O(1) lookup for zones at grid positions.
    /// </summary>
    public class ClickZoneManager
    {
        /// <summary>
        /// Zones indexed by grid position for O(1) lookup.
        /// Key: (column, row), Value: Zone occupying that cell.
        /// A zone occupies multiple cells if width/height > 1.
        /// </summary>
        private readonly Dictionary<(int col, int row), SimpleClickZone> _zonesByPosition;
        
        /// <summary>
        /// Zones indexed by element ID for reverse lookup.
        /// </summary>
        private readonly Dictionary<string, SimpleClickZone> _zonesById;
        
        /// <summary>
        /// Creates a new empty zone manager.
        /// </summary>
        public ClickZoneManager()
        {
            _zonesByPosition = new Dictionary<(int, int), SimpleClickZone>();
            _zonesById = new Dictionary<string, SimpleClickZone>();
        }
        
        /// <summary>
        /// Registers a zone at the specified grid position.
        /// The zone will occupy all grid cells within its width and height.
        /// </summary>
        /// <param name="elementId">Unique element identifier</param>
        /// <param name="col">Starting column (0 to GRID_COLUMNS-1)</param>
        /// <param name="row">Starting row (0 to GRID_ROWS-1)</param>
        /// <param name="width">Width in grid cells</param>
        /// <param name="height">Height in grid cells</param>
        /// <param name="category">Zone category for styling</param>
        /// <param name="onClick">Callback when zone is clicked</param>
        public void RegisterZone(string elementId, int col, int row, 
                                int width, int height, string category, 
                                Action onClick)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(elementId))
                throw new ArgumentException("ElementId cannot be null or empty", nameof(elementId));
            
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Width and height must be positive", nameof(width));
            
            // Remove existing zone with same ID if present
            if (_zonesById.ContainsKey(elementId))
            {
                UnregisterZone(elementId);
            }
            
            // Create zone
            var zone = new SimpleClickZone
            {
                ElementId = elementId,
                GridRect = new Rect(col, row, width, height),
                Category = category,
                OnClick = onClick,
                IsEnabled = true
            };
            
            // Register at all grid cells the zone occupies
            for (int r = row; r < row + height && r < TerminalGridConfig.GRID_ROWS; r++)
            {
                for (int c = col; c < col + width && c < TerminalGridConfig.GRID_COLUMNS; c++)
                {
                    _zonesByPosition[(c, r)] = zone;
                }
            }
            
            // Register by ID
            _zonesById[elementId] = zone;
        }
        
        /// <summary>
        /// Unregisters a zone by element ID.
        /// </summary>
        public void UnregisterZone(string elementId)
        {
            if (!_zonesById.TryGetValue(elementId, out var zone))
                return;
            
            // Remove from position map
            int startCol = (int)zone.GridRect.x;
            int startRow = (int)zone.GridRect.y;
            int width = (int)zone.GridRect.width;
            int height = (int)zone.GridRect.height;
            
            for (int r = startRow; r < startRow + height; r++)
            {
                for (int c = startCol; c < startCol + width; c++)
                {
                    _zonesByPosition.Remove((c, r));
                }
            }
            
            // Remove from ID map
            _zonesById.Remove(elementId);
        }
        
        /// <summary>
        /// Finds the zone at the specified grid position.
        /// O(1) dictionary lookup.
        /// </summary>
        /// <param name="col">Column (0 to GRID_COLUMNS-1)</param>
        /// <param name="row">Row (0 to GRID_ROWS-1)</param>
        /// <returns>Zone at position, or null if none</returns>
        public SimpleClickZone FindZoneAt(int col, int row)
        {
            _zonesByPosition.TryGetValue((col, row), out var zone);
            return zone;
        }
        
        /// <summary>
        /// Finds a zone by its element ID.
        /// </summary>
        public SimpleClickZone FindZoneById(string elementId)
        {
            _zonesById.TryGetValue(elementId, out var zone);
            return zone;
        }
        
        /// <summary>
        /// Checks if a zone with the given ID is registered.
        /// </summary>
        public bool HasZone(string elementId)
        {
            return _zonesById.ContainsKey(elementId);
        }
        
        /// <summary>
        /// Clears all zones.
        /// </summary>
        public void Clear()
        {
            _zonesByPosition.Clear();
            _zonesById.Clear();
        }
        
        /// <summary>
        /// Gets all zones (for iteration or debugging).
        /// Returns distinct zones (each zone appears once even if it occupies multiple cells).
        /// </summary>
        public IEnumerable<SimpleClickZone> GetAllZones()
        {
            return _zonesById.Values;
        }
        
        /// <summary>
        /// Gets the count of registered zones.
        /// </summary>
        public int ZoneCount => _zonesById.Count;
    }
}
