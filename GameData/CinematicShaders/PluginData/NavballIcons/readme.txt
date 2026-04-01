Navball Icon SDF Textures
==========================

Generated: 2026-04-01
Tool: MSDFgen v1.13.0 with Skia
Source: SVG icons from assets/ folder

Generation Parameters:
- Size: 128x128 pixels
- Mode: MSDF (Multi-channel Signed Distance Field)
- pxrange: 4 (distance range in pixels)
- autoframe: Enabled (auto-fit to content)

Files:
------
prograde_sdf.png     - Prograde indicator (velocity direction)
retrograde_sdf.png   - Retrograde indicator (opposite velocity)
normal_sdf.png       - Normal indicator (orbit normal)
antinormal_sdf.png   - AntiNormal indicator (opposite normal)
radial_in_sdf.png    - Radial In indicator (toward body center)
radial_out_sdf.png   - Radial Out indicator (away from body center)
maneuver_sdf.png     - Maneuver indicator (burn vector direction)

Usage:
------
These textures are loaded by NavballLabelManager.cs at runtime.
They are rendered using the Kartographer grid label system.

Slot Assignments:
- Slot 3: Prograde
- Slot 4: Retrograde
- Slot 5: Normal
- Slot 6: AntiNormal
- Slot 7: Radial In
- Slot 8: Radial Out
- Slot 9: Maneuver

Note: These are single-channel SDF textures. The RGB channels contain
identical distance field data for compatibility with the multi-channel
rendering pipeline.
