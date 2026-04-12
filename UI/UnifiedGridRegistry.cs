using System;
using System.Collections.Generic;
using CinematicShaders.Core;

namespace CinematicShaders.UI
{
    /// <summary>
    /// Central registry for all grid element definitions.
    /// Single source of truth for the 59×13 grid layout.
    /// </summary>
    public static class UnifiedGridRegistry
    {
        // Main screen elements
        public static readonly Dictionary<string, GridElementDefinition> MainScreenElements = 
            new Dictionary<string, GridElementDefinition>
        {
            // Left column value fields
            ["hip_value"] = new GridElementDefinition
            {
                ElementId = "hip_value",
                Position = GridPosition.At(12, 1),
                Width = 20,
                Height = 1,
                Type = ElementType.Value,
                Priority = 0
            },
            ["name_value"] = new GridElementDefinition
            {
                ElementId = "name_value",
                Position = GridPosition.At(12, 2),
                Width = 25,
                Height = 1,
                Type = ElementType.Editable,
                Priority = 1
            },
            ["distance_value"] = new GridElementDefinition
            {
                ElementId = "distance_value",
                Position = GridPosition.At(12, 3),
                Width = 20,
                Height = 1,
                Type = ElementType.Value,
                Priority = 2
            },
            ["spectral_value"] = new GridElementDefinition
            {
                ElementId = "spectral_value",
                Position = GridPosition.At(12, 4),
                Width = 15,
                Height = 1,
                Type = ElementType.Value,
                Priority = 3
            },
            ["mag_value"] = new GridElementDefinition
            {
                ElementId = "mag_value",
                Position = GridPosition.At(12, 5),
                Width = 15,
                Height = 1,
                Type = ElementType.Value,
                Priority = 4
            },
            ["const_value"] = new GridElementDefinition
            {
                ElementId = "const_value",
                Position = GridPosition.At(12, 6),
                Width = 20,
                Height = 1,
                Type = ElementType.Value,
                Priority = 5
            },
            
            // Search
            ["search_input"] = new GridElementDefinition
            {
                ElementId = "search_input",
                Position = GridPosition.At(4, 11),
                Width = 25,
                Height = 1,
                Type = ElementType.Input,
                Priority = 20
            },
            ["selected_star"] = new GridElementDefinition
            {
                ElementId = "selected_star",
                Position = GridPosition.At(4, 8),
                Width = 12,
                Height = 1,
                Type = ElementType.Value,
                Priority = 6
            },
            
            // Buttons (for click zones only - drawn in Layer 2)
            ["save_button"] = new GridElementDefinition
            {
                ElementId = "save_button",
                Position = GridPosition.At(17, 8),
                Width = 7,
                Height = 1,
                Type = ElementType.Button,
                Priority = 10,
                VisibleByDefault = true
            },
            ["reset_button"] = new GridElementDefinition
            {
                ElementId = "reset_button",
                Position = GridPosition.At(27, 8),
                Width = 8,
                Height = 1,
                Type = ElementType.Button,
                Priority = 11,
                VisibleByDefault = true
            },
            ["rescan_button"] = new GridElementDefinition
            {
                ElementId = "rescan_button",
                Position = GridPosition.At(27, 10),
                Width = 8,
                Height = 1,
                Type = ElementType.Button,
                Priority = 12,
                VisibleByDefault = true
            },
            
            // Search results (rows 1-10, right column)
            // Generated dynamically via GetSearchResultElement(int index)
        };
        
        /// <summary>
        /// Get a search result element definition by index (0-9).
        /// </summary>
        /// <param name="index">Result index (0-9)</param>
        /// <returns>Grid element definition for the search result</returns>
        public static GridElementDefinition GetSearchResultElement(int index)
        {
            if (index < 0 || index >= 10)
                throw new ArgumentOutOfRangeException(nameof(index));
            
            // Results in right column, rows 1-10
            int row = 1 + index;  // Simple sequential placement
            
            return new GridElementDefinition
            {
                ElementId = $"result_{index}",
                Position = GridPosition.At(38, row),
                Width = 20,
                Height = 1,
                Type = ElementType.SearchResult,
                Priority = 30 + index,
                VisibleByDefault = false
            };
        }
        
        // Scan screen elements
        public static readonly Dictionary<string, GridElementDefinition> ScanScreenElements = 
            new Dictionary<string, GridElementDefinition>
        {
            ["scan_area"] = new GridElementDefinition
            {
                ElementId = "scan_area",
                Position = GridPosition.At(10, 3),
                Width = 49,
                Height = 9,
                Type = ElementType.Button,  // Clickable
                Priority = 0
            }
        };
        
        // Confirm rescan screen elements
        public static readonly Dictionary<string, GridElementDefinition> ConfirmRescanElements = 
            new Dictionary<string, GridElementDefinition>
        {
            ["yes_button"] = new GridElementDefinition
            {
                ElementId = "yes_button",
                Position = GridPosition.At(15, 11),
                Width = 6,
                Height = 1,
                Type = ElementType.Button,
                Priority = 0
            },
            ["no_button"] = new GridElementDefinition
            {
                ElementId = "no_button",
                Position = GridPosition.At(48, 11),  // Adjusted for 59×13 grid
                Width = 5,
                Height = 1,
                Type = ElementType.Button,
                Priority = 1
            }
        };
        
        /// <summary>
        /// Get all click zones for a screen.
        /// Automatically derived from element definitions.
        /// </summary>
        /// <param name="elements">Dictionary of element definitions</param>
        /// <returns>List of click zones</returns>
        public static List<ClickZone> GetClickZonesForScreen(
            Dictionary<string, GridElementDefinition> elements)
        {
            var zones = new List<ClickZone>();
            foreach (var element in elements.Values)
            {
                zones.Add(element.GetClickZone());
            }
            return zones;
        }
    }
}
