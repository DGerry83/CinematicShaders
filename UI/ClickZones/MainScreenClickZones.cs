using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.Core;

namespace CinematicShaders.UI.ClickZones
{
    /// <summary>
    /// Defines all clickable zones for the MainScreen.
    /// Grid coordinates are source of truth; UVRect calculated from GridRect for backward compatibility.
    /// </summary>
    public static class MainScreenClickZones
    {
        /// <summary>
        /// Get all click zones for the main screen.
        /// Grid coordinates are source of truth; UVRect calculated from GridRect.
        /// </summary>
        public static List<ClickZone> GetAllZones()
        {
            var zones = new List<ClickZone>();
            
            // Create zones from grid layout
            foreach (var kvp in HolographicGridLayout.ClickZones)
            {
                string category = kvp.Key.EndsWith("_button") ? "button" : 
                                 kvp.Key.EndsWith("_input") ? "input" : "value";
                
                zones.Add(new ClickZone 
                {
                    ElementId = kvp.Key,
                    GridRect = new Rect(kvp.Value.TopLeft.Column, kvp.Value.TopLeft.Row, 
                                       kvp.Value.Width, kvp.Value.Height),
                    // UVRect will be calculated by ClickHandler or zone itself
                    Category = category,
                    IsEnabled = true
                });
            }
            
            // Add search result zones (10 rows)
            for (int i = 0; i < 10; i++)
            {
                var gridRegion = HolographicGridLayout.GetResultZone(i);
                zones.Add(new ClickZone 
                {
                    ElementId = $"result_{i}",
                    GridRect = new Rect(gridRegion.TopLeft.Column, gridRegion.TopLeft.Row,
                                       gridRegion.Width, gridRegion.Height),
                    Category = "result",
                    IsEnabled = true
                });
            }
            
            return zones;
        }

        /// <summary>
        /// Get only the value field zones (enabled when star selected).
        /// </summary>
        public static List<ClickZone> GetValueZones()
        {
            return new List<ClickZone> {
                GetZone("name_value"),
                GetZone("hip_value"),
                GetZone("distance_value"),
                GetZone("spectral_value"),
                GetZone("mag_value"),
                GetZone("const_value")
            };
        }

        /// <summary>
        /// Get a single zone by element ID from grid layout.
        /// </summary>
        private static ClickZone GetZone(string elementId)
        {
            if (HolographicGridLayout.ClickZones.TryGetValue(elementId, out GridRegion region))
            {
                string category = elementId.EndsWith("_button") ? "button" : 
                                 elementId.EndsWith("_input") ? "input" : "value";
                
                return new ClickZone 
                {
                    ElementId = elementId,
                    GridRect = new Rect(region.TopLeft.Column, region.TopLeft.Row,
                                       region.Width, region.Height),
                    Category = category,
                    IsEnabled = true
                };
            }
            return null;
        }
    }
}
