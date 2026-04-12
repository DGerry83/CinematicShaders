using System.Collections.Generic;
using System.Linq;
using CinematicShaders.Core;
using CinematicShaders.UI;

namespace CinematicShaders.ClickZones
{
    /// <summary>
    /// Zone registry for a single screen, containing zones for all display sizes.
    /// </summary>
    public class ScreenZoneRegistry
    {
        /// <summary>
        /// Screen identifier (e.g., "Main", "Scan", "Confirm")
        /// </summary>
        public string ScreenName { get; set; }
        
        /// <summary>
        /// Zone sets for each display size.
        /// Key: DisplaySize, Value: List of zones for that size.
        /// </summary>
        public Dictionary<HolographicDisplaySize, List<ClickZone>> ZoneSets { get; set; }
            = new Dictionary<HolographicDisplaySize, List<ClickZone>>();
        
        /// <summary>
        /// Gets zones for the current display size.
        /// Called at click-time - always returns correct zones for current size.
        /// </summary>
        public List<ClickZone> GetZonesForCurrentSize()
        {
            return GetZonesForSize(TerminalGridConfig.CurrentDisplaySize);
        }
        
        /// <summary>
        /// Gets zones for a specific display size.
        /// </summary>
        public List<ClickZone> GetZonesForSize(HolographicDisplaySize size)
        {
            if (ZoneSets.TryGetValue(size, out var zones))
            {
                return zones;
            }
            return new List<ClickZone>();
        }
        
        /// <summary>
        /// Finds a zone at the given grid position for the current display size.
        /// </summary>
        public ClickZone FindZoneAt(GridPosition gridPos)
        {
            var zones = GetZonesForCurrentSize();
            return zones.FirstOrDefault(z => ContainsPosition(z, gridPos));
        }
        
        /// <summary>
        /// Checks if a zone contains the given grid position.
        /// </summary>
        private bool ContainsPosition(ClickZone zone, GridPosition pos)
        {
            return pos.Column >= zone.GridRect.x &&
                   pos.Column < zone.GridRect.x + zone.GridRect.width &&
                   pos.Row >= zone.GridRect.y &&
                   pos.Row < zone.GridRect.y + zone.GridRect.height;
        }
        
        /// <summary>
        /// Finds a zone by its element ID for the current display size.
        /// </summary>
        public ClickZone FindZoneById(string elementId)
        {
            var zones = GetZonesForCurrentSize();
            return zones.FirstOrDefault(z => z.ElementId == elementId);
        }
    }
}
