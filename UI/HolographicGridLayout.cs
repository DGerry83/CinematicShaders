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
            ["result_0"] = GridPosition.At(42, 1),
            ["result_1"] = GridPosition.At(42, 2),
            ["result_2"] = GridPosition.At(42, 3),
            ["result_3"] = GridPosition.At(42, 4),
            ["result_4"] = GridPosition.At(42, 5),
            ["result_5"] = GridPosition.At(42, 6),
            ["result_6"] = GridPosition.At(42, 7),
            ["result_7"] = GridPosition.At(42, 8),
            ["result_8"] = GridPosition.At(42, 9),
            ["result_9"] = GridPosition.At(42, 10),

            // Bottom area
            ["search_input"] = GridPosition.At(6, 11),
            ["rescan_button"] = GridPosition.At(35, 10),
            ["save_button"] = GridPosition.At(20, 8),
            ["reset_button"] = GridPosition.At(32, 8),
        };

        #endregion

        #region Click Zones

        /// <summary>
        /// Clickable regions on the grid.
        /// Keys are zone identifiers, values define the grid region bounds.
        /// </summary>
        public static readonly Dictionary<string, GridRegion> ClickZones = 
            new Dictionary<string, GridRegion>
        {
            // Left column value areas
            ["hip_zone"] = new GridRegion(GridPosition.At(12, 1), 8, 1),
            ["name_zone"] = new GridRegion(GridPosition.At(12, 2), 12, 1),
            ["distance_zone"] = new GridRegion(GridPosition.At(12, 3), 10, 1),
            ["spectral_zone"] = new GridRegion(GridPosition.At(12, 4), 8, 1),
            ["mag_zone"] = new GridRegion(GridPosition.At(12, 5), 8, 1),
            ["const_zone"] = new GridRegion(GridPosition.At(12, 6), 12, 1),

            // Search result rows (right column)
            ["result_0_zone"] = new GridRegion(GridPosition.At(42, 1), 16, 1),
            ["result_1_zone"] = new GridRegion(GridPosition.At(42, 2), 16, 1),
            ["result_2_zone"] = new GridRegion(GridPosition.At(42, 3), 16, 1),
            ["result_3_zone"] = new GridRegion(GridPosition.At(42, 4), 16, 1),
            ["result_4_zone"] = new GridRegion(GridPosition.At(42, 5), 16, 1),
            ["result_5_zone"] = new GridRegion(GridPosition.At(42, 6), 16, 1),
            ["result_6_zone"] = new GridRegion(GridPosition.At(42, 7), 16, 1),
            ["result_7_zone"] = new GridRegion(GridPosition.At(42, 8), 16, 1),
            ["result_8_zone"] = new GridRegion(GridPosition.At(42, 9), 16, 1),
            ["result_9_zone"] = new GridRegion(GridPosition.At(42, 10), 16, 1),

            // Input and buttons
            ["search_input_zone"] = new GridRegion(GridPosition.At(6, 11), 20, 1),
            ["rescan_button_zone"] = new GridRegion(GridPosition.At(35, 10), 10, 1),
            ["save_button_zone"] = new GridRegion(GridPosition.At(20, 8), 10, 1),
            ["reset_button_zone"] = new GridRegion(GridPosition.At(32, 8), 10, 1),
        };

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
