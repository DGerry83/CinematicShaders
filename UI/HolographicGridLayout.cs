using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI
{
    /// <summary>
    /// Defines the holographic terminal grid layout with element positions and click zones.
    /// Uses a 59×13 grid coordinate system that matches the Layer 1 border.
    /// </summary>
    public static class HolographicGridLayout
    {
        #region Element Positions

        /// <summary>
        /// Element positions on the 59×13 grid.
        /// Keys correspond to UI element IDs, values are grid coordinates.
        /// </summary>
        public static readonly Dictionary<string, GridPosition> ElementPositions = 
            new Dictionary<string, GridPosition>
        {
            // Left column values (after labels)
            ["hip_value"] = GridPosition.At(12, 1),
            ["name_value"] = GridPosition.At(12, 2),
            ["distance_value"] = GridPosition.At(12, 3),
            ["spectral_value"] = GridPosition.At(12, 4),
            ["mag_value"] = GridPosition.At(12, 5),
            ["const_value"] = GridPosition.At(12, 6),
            ["selected_star"] = GridPosition.At(4, 8),

            // Right column search results
            ["result_0"] = GridPosition.At(38, 1),
            ["result_1"] = GridPosition.At(38, 2),
            ["result_2"] = GridPosition.At(38, 3),
            ["result_3"] = GridPosition.At(38, 4),
            ["result_4"] = GridPosition.At(38, 5),
            ["result_5"] = GridPosition.At(38, 6),
            ["result_6"] = GridPosition.At(38, 7),
            ["result_7"] = GridPosition.At(38, 8),
            ["result_8"] = GridPosition.At(38, 9),
            ["result_9"] = GridPosition.At(38, 10),

            // Bottom area
            // NOTE: Buttons ([SAVE], [RESET], [RESCAN]) are drawn on Layer 2, not Layer 3
            // They exist as HolographicTextElement objects for click detection only
            ["search_input"] = GridPosition.At(4, 11),   // Row 11: cursor renders here
        };

        #endregion

        #region Click Zones

        /// <summary>
        /// Clickable regions on the grid.
        /// Keys are zone identifiers, values define the grid region bounds.
        /// Grid coordinates are source of truth; UVRect calculated from GridRect for backward compatibility.
        /// </summary>
        public static readonly Dictionary<string, GridRegion> ClickZones = 
            new Dictionary<string, GridRegion>
        {
            // Value field zones (left column)
            ["hip_value"] = new GridRegion(GridPosition.At(6, 1), 20, 1),
            ["name_value"] = new GridRegion(GridPosition.At(6, 2), 25, 1),
            ["distance_value"] = new GridRegion(GridPosition.At(11, 3), 20, 1),
            ["spectral_value"] = new GridRegion(GridPosition.At(11, 4), 15, 1),
            ["mag_value"] = new GridRegion(GridPosition.At(6, 5), 15, 1),
            ["const_value"] = new GridRegion(GridPosition.At(8, 6), 20, 1),
            
            // Button zones (match visual positions in ContentLines)
            ["save_button"] = new GridRegion(GridPosition.At(17, 8), 7, 1),     // Row 8: "[SAVE]"
            ["reset_button"] = new GridRegion(GridPosition.At(27, 8), 8, 1),   // Row 8: "[RESET]"
            ["rescan_button"] = new GridRegion(GridPosition.At(27, 10), 8, 1), // Row 10: "[RESCAN]"
            
            // Input zone
            ["search_input"] = new GridRegion(GridPosition.At(4, 11), 25, 1),   // Row 11: "►" cursor position
        };

        /// <summary>
        /// Get a search result zone by index (0-9).
        /// </summary>
        public static GridRegion GetResultZone(int index)
        {
            if (index < 0 || index >= 10)
                return new GridRegion(GridPosition.At(38, 1), 20, 1);
            
            // Results in right column, rows 1-10
            int row = 1 + index;
            if (row > 6) row += 1; // Skip row 7 (gap)
            return new GridRegion(GridPosition.At(38, row), 20, 1);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get the grid position for an element by ID.
        /// Returns null if the element ID is not found.
        /// </summary>
        public static GridPosition? GetElementPosition(string elementId)
        {
            if (ElementPositions.TryGetValue(elementId, out var position))
            {
                return position;
            }
            return null;
        }

        /// <summary>
        /// Get the click zone for a zone ID.
        /// Returns null if the zone ID is not found.
        /// </summary>
        public static GridRegion? GetClickZone(string zoneId)
        {
            if (ClickZones.TryGetValue(zoneId, out var region))
            {
                return region;
            }
            return null;
        }

        /// <summary>
        /// Find which click zone (if any) contains the given grid position.
        /// Returns the zone ID or null if no zone contains the position.
        /// </summary>
        public static string FindZoneAtPosition(GridPosition position)
        {
            foreach (var kvp in ClickZones)
            {
                if (kvp.Value.Contains(position))
                {
                    return kvp.Key;
                }
            }
            return null;
        }

        /// <summary>
        /// Convert a screen pixel position to a zone ID.
        /// Returns the zone ID or null if the position is not within any zone.
        /// </summary>
        public static string FindZoneAtPixel(float x, float y, float displayWidth, float displayHeight)
        {
            var gridPos = TerminalGridConfig.PixelToGrid(x, y, displayWidth, displayHeight);
            return FindZoneAtPosition(gridPos);
        }

        #endregion
    }
}
