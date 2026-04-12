using System.Collections.Generic;
using CinematicShaders.Core;
using CinematicShaders.UI;

namespace CinematicShaders.ClickZones
{
    /// <summary>
    /// Generates click zone sets for all screens and display sizes.
    /// </summary>
    public static class ZoneSetGenerator
    {
        /// <summary>
        /// Generates zone sets for MainScreen (all display sizes).
        /// </summary>
        public static Dictionary<HolographicDisplaySize, List<ClickZone>> GenerateMainScreenZones()
        {
            var zoneSets = new Dictionary<HolographicDisplaySize, List<ClickZone>>();
            
            // Generate zones for each display size
            zoneSets[HolographicDisplaySize.Large] = GenerateMainScreenZonesForSize(HolographicDisplaySize.Large);
            zoneSets[HolographicDisplaySize.Medium] = GenerateMainScreenZonesForSize(HolographicDisplaySize.Medium);
            zoneSets[HolographicDisplaySize.Small] = GenerateMainScreenZonesForSize(HolographicDisplaySize.Small);
            
            return zoneSets;
        }
        
        /// <summary>
        /// Generates MainScreen zones for a specific display size.
        /// Grid positions are the same across sizes, but zones are stored per-size for consistency.
        /// </summary>
        private static List<ClickZone> GenerateMainScreenZonesForSize(HolographicDisplaySize size)
        {
            var zones = new List<ClickZone>();
            
            // Get element definitions from UnifiedGridRegistry
            var elementDefinitions = UnifiedGridRegistry.MainScreenElements;
            
            foreach (var kvp in elementDefinitions)
            {
                var definition = kvp.Value;
                zones.Add(new ClickZone(definition));
            }
            
            // Add search result zones (0-9)
            for (int i = 0; i < 10; i++)
            {
                var resultDef = UnifiedGridRegistry.GetSearchResultElement(i);
                zones.Add(new ClickZone(resultDef));
            }
            
            return zones;
        }
        
        /// <summary>
        /// Generates zone set for ScanScreen.
        /// </summary>
        public static Dictionary<HolographicDisplaySize, List<ClickZone>> GenerateScanScreenZones()
        {
            var zoneSets = new Dictionary<HolographicDisplaySize, List<ClickZone>>();
            
            // Scan screen has one zone: scan_area
            // Same across all sizes (relative positioning)
            var zones = new List<ClickZone>();
            
            var scanDef = UnifiedGridRegistry.ScanScreenElements["scan_area"];
            zones.Add(new ClickZone(scanDef));
            
            zoneSets[HolographicDisplaySize.Large] = zones;
            zoneSets[HolographicDisplaySize.Medium] = zones;
            zoneSets[HolographicDisplaySize.Small] = zones;
            
            return zoneSets;
        }
        
        /// <summary>
        /// Generates zone set for ConfirmRescanScreen.
        /// </summary>
        public static Dictionary<HolographicDisplaySize, List<ClickZone>> GenerateConfirmScreenZones()
        {
            var zoneSets = new Dictionary<HolographicDisplaySize, List<ClickZone>>();
            
            var zones = new List<ClickZone>();
            
            var yesDef = UnifiedGridRegistry.ConfirmRescanElements["yes_button"];
            var noDef = UnifiedGridRegistry.ConfirmRescanElements["no_button"];
            
            zones.Add(new ClickZone(yesDef));
            zones.Add(new ClickZone(noDef));
            
            zoneSets[HolographicDisplaySize.Large] = zones;
            zoneSets[HolographicDisplaySize.Medium] = zones;
            zoneSets[HolographicDisplaySize.Small] = zones;
            
            return zoneSets;
        }
    }
}
