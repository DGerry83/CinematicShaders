using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.Core;

namespace CinematicShaders.UI.ClickZones
{
    /// <summary>
    /// Defines all clickable zones for the MainScreen.
    /// Coordinates are in UV space (0-1 across the display).
    /// </summary>
    public static class MainScreenClickZones
    {
        // Button zones (Layer 2/3)
        public static readonly ClickZone SAVE_BUTTON = new ClickZone {
            ElementId = "save_button",
            UVRect = new Rect(0.35f, 0.65f, 0.10f, 0.05f),
            Category = "button",
            IsEnabled = true
        };
        
        public static readonly ClickZone RESET_BUTTON = new ClickZone {
            ElementId = "reset_button",
            UVRect = new Rect(0.48f, 0.65f, 0.12f, 0.05f),
            Category = "button",
            IsEnabled = true
        };
        
        public static readonly ClickZone RESCAN_BUTTON = new ClickZone {
            ElementId = "rescan_button",
            UVRect = new Rect(0.65f, 0.77f, 0.15f, 0.05f),
            Category = "button",
            IsEnabled = true
        };
        
        // Input zone
        public static readonly ClickZone SEARCH_INPUT = new ClickZone {
            ElementId = "search_input",
            UVRect = new Rect(0.15f, 0.77f, 0.40f, 0.05f),
            Category = "input",
            IsEnabled = true
        };
        
        // Value field zones (Layer 3)
        public static readonly ClickZone NAME_VALUE = new ClickZone {
            ElementId = "name_value",
            UVRect = new Rect(0.18f, 0.18f, 0.35f, 0.05f),
            Category = "value",
            IsEnabled = true
        };
        
        public static readonly ClickZone HIP_VALUE = new ClickZone {
            ElementId = "hip_value",
            UVRect = new Rect(0.18f, 0.10f, 0.20f, 0.05f),
            Category = "value",
            IsEnabled = true
        };
        
        public static readonly ClickZone DISTANCE_VALUE = new ClickZone {
            ElementId = "distance_value",
            UVRect = new Rect(0.18f, 0.26f, 0.25f, 0.05f),
            Category = "value",
            IsEnabled = true
        };
        
        public static readonly ClickZone SPECTRAL_VALUE = new ClickZone {
            ElementId = "spectral_value",
            UVRect = new Rect(0.18f, 0.34f, 0.15f, 0.05f),
            Category = "value",
            IsEnabled = true
        };
        
        public static readonly ClickZone MAG_VALUE = new ClickZone {
            ElementId = "mag_value",
            UVRect = new Rect(0.18f, 0.42f, 0.15f, 0.05f),
            Category = "value",
            IsEnabled = true
        };
        
        public static readonly ClickZone CONST_VALUE = new ClickZone {
            ElementId = "const_value",
            UVRect = new Rect(0.18f, 0.50f, 0.30f, 0.05f),
            Category = "value",
            IsEnabled = true
        };
        
        /// <summary>
        /// Get a search result zone by index (0-9).
        /// </summary>
        public static ClickZone GetResultZone(int index)
        {
            float y = 0.12f + (index * 0.06f);
            return new ClickZone {
                ElementId = $"result_{index}",
                UVRect = new Rect(0.58f, y, 0.35f, 0.05f),
                Category = "result",
                IsEnabled = true
            };
        }
        
        /// <summary>
        /// Get all click zones for the main screen.
        /// </summary>
        public static List<ClickZone> GetAllZones()
        {
            var zones = new List<ClickZone> {
                SAVE_BUTTON, RESET_BUTTON, RESCAN_BUTTON,
                SEARCH_INPUT, NAME_VALUE, HIP_VALUE,
                DISTANCE_VALUE, SPECTRAL_VALUE, MAG_VALUE, CONST_VALUE
            };
            
            // Add search result zones (10 rows)
            for (int i = 0; i < 10; i++)
            {
                zones.Add(GetResultZone(i));
            }
            
            return zones;
        }
        
        /// <summary>
        /// Get only the value field zones (enabled when star selected).
        /// </summary>
        public static List<ClickZone> GetValueZones()
        {
            return new List<ClickZone> {
                NAME_VALUE, HIP_VALUE, DISTANCE_VALUE,
                SPECTRAL_VALUE, MAG_VALUE, CONST_VALUE
            };
        }
    }
}
