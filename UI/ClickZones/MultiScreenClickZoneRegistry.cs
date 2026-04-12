using System.Collections.Generic;
using CinematicShaders.Core;
using CinematicShaders.UI;

namespace CinematicShaders.ClickZones
{
    /// <summary>
    /// Global registry for all screens' click zones across all display sizes.
    /// This is the main entry point for click zone lookups.
    /// </summary>
    public static class MultiScreenClickZoneRegistry
    {
        /// <summary>
        /// All screen registries indexed by screen name.
        /// </summary>
        private static readonly Dictionary<string, ScreenZoneRegistry> _registries 
            = new Dictionary<string, ScreenZoneRegistry>();
        
        /// <summary>
        /// Registers a screen with its zone sets for all display sizes.
        /// Call this during screen initialization.
        /// </summary>
        /// <param name="screenName">Screen identifier (e.g., "Main", "Scan")</param>
        /// <param name="zoneSets">Zone sets for each display size</param>
        public static void RegisterScreen(string screenName, 
            Dictionary<HolographicDisplaySize, List<ClickZone>> zoneSets)
        {
            _registries[screenName] = new ScreenZoneRegistry
            {
                ScreenName = screenName,
                ZoneSets = zoneSets
            };
        }
        
        /// <summary>
        /// Gets zones for the specified screen at the current display size.
        /// Call this on every click check - returns zones appropriate for current size.
        /// </summary>
        public static List<ClickZone> GetZones(string screenName)
        {
            if (_registries.TryGetValue(screenName, out var registry))
            {
                return registry.GetZonesForCurrentSize();
            }
            return new List<ClickZone>();
        }
        
        /// <summary>
        /// Finds a zone at the given grid position for the specified screen.
        /// Uses current display size automatically.
        /// </summary>
        public static ClickZone FindZone(string screenName, GridPosition gridPos)
        {
            if (_registries.TryGetValue(screenName, out var registry))
            {
                return registry.FindZoneAt(gridPos);
            }
            return null;
        }
        
        /// <summary>
        /// Finds a zone by element ID for the specified screen.
        /// Uses current display size automatically.
        /// </summary>
        public static ClickZone FindZoneById(string screenName, string elementId)
        {
            if (_registries.TryGetValue(screenName, out var registry))
            {
                return registry.FindZoneById(elementId);
            }
            return null;
        }
        
        /// <summary>
        /// Checks if a screen is registered.
        /// </summary>
        public static bool IsScreenRegistered(string screenName)
        {
            return _registries.ContainsKey(screenName);
        }
        
        /// <summary>
        /// Clears all registrations. Use for testing or complete reset.
        /// </summary>
        public static void ClearAll()
        {
            _registries.Clear();
        }
    }
}
