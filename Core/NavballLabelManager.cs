using CinematicShaders.Native;
using CinematicShaders.Native.Structs;
using CinematicShaders.Shaders.Starfield;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using static CinematicShaders.Core.StarfieldSettings;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Manages the 7 orbital direction indicators (navball icons) using screen-space projection.
    /// 
    /// Icons:
    /// - Index 0: Prograde (orbit velocity direction)
    /// - Index 1: Retrograde (opposite of velocity)
    /// - Index 2: Normal (orbit normal, perpendicular to orbital plane)
    /// - Index 3: AntiNormal (opposite of normal)
    /// - Index 4: Radial In (toward center of gravity)
    /// - Index 5: Radial Out (away from center of gravity)
    /// - Index 6: Maneuver (burn vector of active maneuver node)
    /// 
    /// Positioning: Screen-space projection via KartographerMath.WorldDirectionToScreenUV()
    /// Rendering: Direct to native KartographerParams struct (bypassing GridLabelSystem)
    /// Updates: Every frame during flight scene
    /// </summary>
    public class NavballLabelManager
    {
        // Icon indices in the native struct
        private const int PROGRADE = 0;
        private const int RETROGRADE = 1;
        private const int NORMAL = 2;
        private const int ANTINORMAL = 3;
        private const int RADIAL_IN = 4;
        private const int RADIAL_OUT = 5;
        private const int MANEUVER = 6;
        private const int ICON_COUNT = 7;

        /// <summary>
        /// KSP standard navball colors (RGB)
        /// </summary>
        public static readonly Color[] IconColors = new Color[ICON_COUNT]
        {
            new Color(0.0f, 1.0f, 0.0f),      // 0: Prograde - Green
            new Color(1.0f, 0.0f, 0.0f),      // 1: Retrograde - Red
            new Color(0.0f, 0.5f, 1.0f),      // 2: Normal - Blue
            new Color(1.0f, 0.0f, 1.0f),      // 3: AntiNormal - Magenta
            new Color(1.0f, 0.8f, 0.0f),      // 4: Radial In - Yellow/Orange
            new Color(1.0f, 1.0f, 1.0f),      // 5: Radial Out - White
            new Color(1.0f, 0.5f, 0.0f)       // 6: Maneuver - Orange
        };

        public static readonly string[] IconNames = new string[ICON_COUNT]
        {
            "Prograde", "Retrograde", "Normal", "AntiNormal",
            "Radial In", "Radial Out", "Maneuver"
        };

        // Runtime state
        private bool _initialized = false;
        private bool _enabled = false;
        private bool _useNavballColors = false;
        private int _offscreenMode = 0; // 0 = world-space, 1 = edge-clamp

        // Per-icon state for hysteresis and edge-clamping
        private class IconState
        {
            public string Name;
            public Vector3d WorldDirection;
            public Vector2 ScreenNDC;        // Calculated position (-aspect to aspect, -1 to 1)
            public float Intensity;
            public uint PackedColor;
            public bool IsInEdgeMode;        // Hysteresis state
            public bool IsVisible;
        }
        
        private IconState[] _iconStates = new IconState[ICON_COUNT];

        // Orbit vector cache (for debugging)
        private Vector3d _lastPrograde;
        private Vector3d _lastNormal;
        private Vector3d _lastRadialOut;
        private Vector3d? _lastManeuver;

        // Texture loading
        private static readonly string[] IconFileNames = {
            "prograde_sdf.png",
            "retrograde_sdf.png",
            "normal_sdf.png",
            "antinormal_sdf.png",
            "radial_in_sdf.png",
            "radial_out_sdf.png",
            "maneuver_sdf.png"
        };
        private const int ICON_TEXTURE_SIZE = 128;
        private Texture2D[] _iconTextures;
        private bool _texturesLoaded = false;
        private bool _texturesUploaded = false;

        /// <summary>
        /// Load navball icon textures from PNG files.
        /// </summary>
        public void LoadTextures()
        {
            if (_texturesLoaded) return;

            try
            {
                // Build path to GameData/CinematicShaders/PluginData/NavballIcons
                // Following the same pattern as StarCatalogManager
                string basePath = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "CinematicShaders", "PluginData", "NavballIcons");

                ModFileLogger.Log($"[NavballLabelManager] Loading textures from: {basePath}");

                _iconTextures = new Texture2D[ICON_COUNT];
                bool allLoaded = true;

                for (int i = 0; i < ICON_COUNT; i++)
                {
                    string filePath = Path.Combine(basePath, IconFileNames[i]);
                    if (!File.Exists(filePath))
                    {
                        ModFileLogger.LogError($"[NavballLabelManager] Missing texture: {filePath}");
                        allLoaded = false;
                        continue;
                    }

                    byte[] bytes = File.ReadAllBytes(filePath);
                    // LoadImage() properly handles PNG format
                    _iconTextures[i] = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    _iconTextures[i].LoadImage(bytes);
                }

                if (!allLoaded)
                {
                    ModFileLogger.LogError("[NavballLabelManager] Some textures failed to load");
                    return;
                }

                _texturesLoaded = true;
                ModFileLogger.Log("[NavballLabelManager] Textures loaded, will upload when device ready");
            }
            catch (Exception ex)
            {
                ModFileLogger.LogError($"[NavballLabelManager] Failed to load textures: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempt to upload textures to native. Called from Update() when device is ready.
        /// </summary>
        private void TryUploadTextures()
        {
            try
            {
                int result = StarfieldNative.CR_SetNavballIconTextures(
                    _iconTextures.Select(t => t.GetNativeTexturePtr()).ToArray(), 
                    ICON_TEXTURE_SIZE, ICON_TEXTURE_SIZE);
                
                if (result == 0)
                {
                    _texturesUploaded = true;
                    ModFileLogger.Log("[NavballLabelManager] Textures uploaded to native successfully");
                }
                else if (result == -1)
                {
                    // Device not ready yet, will retry next frame
                    // No logging to avoid spam
                }
                else
                {
                    ModFileLogger.LogError($"[NavballLabelManager] Failed to upload textures to native, error code: {result}");
                }
            }
            catch (Exception ex)
            {
                ModFileLogger.LogError($"[NavballLabelManager] Exception uploading textures: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize the navball label manager.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            try
            {
                // Initialize icon states
                for (int i = 0; i < ICON_COUNT; i++)
                {
                    _iconStates[i] = new IconState
                    {
                        Name = IconNames[i],
                        PackedColor = PackColor(IconColors[i]),
                        Intensity = 0f,
                        IsInEdgeMode = false,
                        IsVisible = false
                    };
                }

                // Load textures
                LoadTextures();

                _initialized = true;
                Debug.Log("[NavballLabelManager] Initialized successfully (screen-space mode)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NavballLabelManager] Initialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Main update loop - call every frame when enabled.
        /// Calculates orbit vectors and updates native struct params.
        /// </summary>
        public void Update()
        {
            if (!_initialized || !_enabled) return;

            // Try to upload textures if loaded but not yet uploaded
            // (device may not have been ready during Initialize)
            if (_texturesLoaded && !_texturesUploaded)
            {
                TryUploadTextures();
            }

            // Only operate in Flight scene with active vessel
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                DisableAllIcons();
                return;
            }

            if (FlightGlobals.ActiveVessel?.orbit == null)
            {
                DisableAllIcons();
                return;
            }

            // Get camera basis from StarfieldCompositor
            Vector3 right = StarfieldCompositor.CameraRightSurface;
            Vector3 up = StarfieldCompositor.CameraUpSurface;
            Vector3 forward = StarfieldCompositor.CameraForwardSurface;
            float aspect = StarfieldCompositor.CameraAspect;
            float vfov = StarfieldCompositor.CachedVerticalFOV;

            if (aspect <= 0 || vfov <= 0)
            {
                // Camera not ready
                return;
            }

            // Calculate orbit vectors using KSP's built-in world-space properties
            // (already in surface frame, matching the working approach in VesselTargetSelector)
            Vector3d pos = FlightGlobals.ActiveVessel.GetWorldPos3D();
            Vector3d vel = FlightGlobals.ActiveVessel.obt_velocity;

            // Use fixed celestial up axis (transformed to surface frame) for consistent orbital plane reference
            // This matches KSP's navball and NavHud behavior, preventing drift in eccentric orbits
            Vector3d upAxisSurface = (Planetarium.Rotation * FlightGlobals.upAxis).normalized;

            Vector3d prograde = vel.normalized;
            Vector3d retrograde = -prograde;
            // Normal is perpendicular to velocity and celestial up (fixed reference)
            Vector3d normal = Vector3d.Cross(prograde, upAxisSurface).normalized;
            Vector3d antinormal = -normal;
            // Radial out points away from body center (perpendicular to prograde in orbital plane)
            // Cross(normal, prograde) rotates 90° from prograde toward radial
            Vector3d radialOut = Vector3d.Cross(normal, prograde).normalized;
            // Ensure radial points away from body center (flip if needed based on position)
            if (Vector3d.Dot(radialOut, pos) < 0)
                radialOut = -radialOut;
            Vector3d radialIn = -radialOut;

            // Cache for debugging
            _lastPrograde = prograde;
            _lastNormal = normal;
            _lastRadialOut = radialOut;

            // Get maneuver node direction (surface frame to match camera)
            Vector3d? maneuverDirection = GetManeuverNodeDirection();

            // Update each icon
            UpdateIcon(PROGRADE, prograde, right, up, forward, aspect, vfov);
            UpdateIcon(RETROGRADE, retrograde, right, up, forward, aspect, vfov);
            UpdateIcon(NORMAL, normal, right, up, forward, aspect, vfov);
            UpdateIcon(ANTINORMAL, antinormal, right, up, forward, aspect, vfov);
            UpdateIcon(RADIAL_IN, radialIn, right, up, forward, aspect, vfov);
            UpdateIcon(RADIAL_OUT, radialOut, right, up, forward, aspect, vfov);

            // Maneuver icon - only show if node exists
            if (maneuverDirection.HasValue)
            {
                UpdateIcon(MANEUVER, maneuverDirection.Value, right, up, forward, aspect, vfov);
                _iconStates[MANEUVER].IsVisible = true;
            }
            else
            {
                _iconStates[MANEUVER].Intensity = 0f;
                _iconStates[MANEUVER].IsVisible = false;
            }

            // Push to native
            UpdateNativeParams();
        }

        /// <summary>
        /// Update a single icon's position and intensity.
        /// </summary>
        private void UpdateIcon(int index, Vector3d worldDir,
            Vector3 right, Vector3 up, Vector3 forward, float aspect, float vfov)
        {
            var state = _iconStates[index];
            state.WorldDirection = worldDir;

            // Project to screen UV [0,1]
            Vector2 uv = KartographerMath.WorldDirectionToScreenUV(
                (Vector3)worldDir, right, up, forward, aspect, vfov);

            // Hysteresis settings
            float margin = StarfieldSettings.KartographerNavballHysteresisMargin > 0 
                ? StarfieldSettings.KartographerNavballHysteresisMargin 
                : 0.05f;
            _offscreenMode = StarfieldSettings.KartographerNavballOffscreenMode;

            // Determine off-screen status
            bool isOffScreen = (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1);
            bool isInSafeZone = (uv.x >= margin && uv.x <= (1 - margin) && 
                                uv.y >= margin && uv.y <= (1 - margin));

            // Hysteresis logic
            if (_offscreenMode == 1) // Edge-clamp mode
            {
                if (state.IsInEdgeMode && isInSafeZone)
                {
                    state.IsInEdgeMode = false;
                }
                else if (!state.IsInEdgeMode && isOffScreen)
                {
                    state.IsInEdgeMode = true;
                }
            }
            else // World-space mode (disappear off-screen)
            {
                state.IsInEdgeMode = false;
                if (isOffScreen)
                {
                    state.Intensity = 0f;
                    state.IsVisible = false;
                    return;
                }
            }

            // Calculate final position
            if (state.IsInEdgeMode)
            {
                state.ScreenNDC = CalculateEdgePosition(uv, aspect);
            }
            else
            {
                state.ScreenNDC = new Vector2((uv.x - 0.5f) * 2 * aspect, (uv.y - 0.5f) * 2);
            }

            // Calculate intensity based on angle from camera forward
            float angle = Vector3.Angle((Vector3)worldDir, forward);
            float maxAngle = StarfieldSettings.KartographerNavballMaxAngle > 0 
                ? StarfieldSettings.KartographerNavballMaxAngle 
                : 90f;
            float minIntensity = StarfieldSettings.KartographerNavballMinIntensity > 0 
                ? StarfieldSettings.KartographerNavballMinIntensity 
                : 0.33f;

            float t = Mathf.Clamp01(angle / maxAngle);
            state.Intensity = Mathf.Lerp(1.0f, minIntensity, t);
            state.IsVisible = state.Intensity > 0.001f;
        }

        /// <summary>
        /// Calculate edge-clamped position for off-screen icons.
        /// </summary>
        private Vector2 CalculateEdgePosition(Vector2 uv, float aspect)
        {
            Vector2 center = new Vector2(0.5f, 0.5f);
            Vector2 dir = (uv - center).normalized;

            // Calculate intersection with each screen edge
            float tX = (dir.x > 0) ? (1.0f - center.x) / dir.x : (0 - center.x) / dir.x;
            float tY = (dir.y > 0) ? (1.0f - center.y) / dir.y : (0 - center.y) / dir.y;
            
            // Use the smaller positive t (closest edge in ray direction)
            float t = Mathf.Min(Mathf.Abs(tX), Mathf.Abs(tY));
            
            Vector2 edgeUV = center + dir * t;
            
            // Clamp to valid range and convert to NDC
            edgeUV = Vector2.Max(Vector2.zero, Vector2.Min(Vector2.one, edgeUV));
            return new Vector2((edgeUV.x - 0.5f) * 2 * aspect, (edgeUV.y - 0.5f) * 2);
        }

        /// <summary>
        /// Push all icon data to the native KartographerParams struct.
        /// </summary>
        private void UpdateNativeParams()
        {
            var kartParams = StarfieldNative.LastKartographerParams;

            // Build enabled mask
            int mask = 0;
            for (int i = 0; i < ICON_COUNT; i++)
            {
                if (_iconStates[i].IsVisible && _iconStates[i].Intensity > 0.001f)
                {
                    mask |= (1 << i);
                }
            }
            kartParams.NavballEnabledMask = mask;
            kartParams.NavballOffscreenMode = _offscreenMode;
            kartParams.NavballIconSize = StarfieldSettings.KartographerNavballIconSize > 0 
                ? StarfieldSettings.KartographerNavballIconSize 
                : 0.05f;
            kartParams.NavballIconThickness = StarfieldSettings.KartographerNavballIconThickness > 0 
                ? StarfieldSettings.KartographerNavballIconThickness 
                : 0.002f;
            kartParams.NavballMinIntensity = StarfieldSettings.KartographerNavballMinIntensity > 0 
                ? StarfieldSettings.KartographerNavballMinIntensity 
                : 0.33f;
            kartParams.NavballMaxAngle = StarfieldSettings.KartographerNavballMaxAngle > 0 
                ? StarfieldSettings.KartographerNavballMaxAngle 
                : 90f;
            kartParams.NavballHysteresisMargin = StarfieldSettings.KartographerNavballHysteresisMargin > 0 
                ? StarfieldSettings.KartographerNavballHysteresisMargin 
                : 0.05f;

            // Set per-icon data
            SetIconParams(ref kartParams, 0, _iconStates[0]);
            SetIconParams(ref kartParams, 1, _iconStates[1]);
            SetIconParams(ref kartParams, 2, _iconStates[2]);
            SetIconParams(ref kartParams, 3, _iconStates[3]);
            SetIconParams(ref kartParams, 4, _iconStates[4]);
            SetIconParams(ref kartParams, 5, _iconStates[5]);
            SetIconParams(ref kartParams, 6, _iconStates[6]);

            StarfieldNative.CR_StarfieldSetKartographerParams(ref kartParams);
        }

        private void SetIconParams(ref KartographerParamsNative kartParams, int index, IconState state)
        {
            // Pack color (or use 0 for grid color)
            uint color = _useNavballColors ? state.PackedColor : 0u;

            switch (index)
            {
                case 0: // Prograde
                    kartParams.NavballIcon0_X = state.ScreenNDC.x;
                    kartParams.NavballIcon0_Y = state.ScreenNDC.y;
                    kartParams.NavballIcon0_Intensity = state.Intensity;
                    kartParams.NavballIcon0_Color = color;
                    break;
                case 1: // Retrograde
                    kartParams.NavballIcon1_X = state.ScreenNDC.x;
                    kartParams.NavballIcon1_Y = state.ScreenNDC.y;
                    kartParams.NavballIcon1_Intensity = state.Intensity;
                    kartParams.NavballIcon1_Color = color;
                    break;
                case 2: // Normal
                    kartParams.NavballIcon2_X = state.ScreenNDC.x;
                    kartParams.NavballIcon2_Y = state.ScreenNDC.y;
                    kartParams.NavballIcon2_Intensity = state.Intensity;
                    kartParams.NavballIcon2_Color = color;
                    break;
                case 3: // AntiNormal
                    kartParams.NavballIcon3_X = state.ScreenNDC.x;
                    kartParams.NavballIcon3_Y = state.ScreenNDC.y;
                    kartParams.NavballIcon3_Intensity = state.Intensity;
                    kartParams.NavballIcon3_Color = color;
                    break;
                case 4: // Radial In
                    kartParams.NavballIcon4_X = state.ScreenNDC.x;
                    kartParams.NavballIcon4_Y = state.ScreenNDC.y;
                    kartParams.NavballIcon4_Intensity = state.Intensity;
                    kartParams.NavballIcon4_Color = color;
                    break;
                case 5: // Radial Out
                    kartParams.NavballIcon5_X = state.ScreenNDC.x;
                    kartParams.NavballIcon5_Y = state.ScreenNDC.y;
                    kartParams.NavballIcon5_Intensity = state.Intensity;
                    kartParams.NavballIcon5_Color = color;
                    break;
                case 6: // Maneuver
                    kartParams.NavballIcon6_X = state.ScreenNDC.x;
                    kartParams.NavballIcon6_Y = state.ScreenNDC.y;
                    kartParams.NavballIcon6_Intensity = state.Intensity;
                    kartParams.NavballIcon6_Color = color;
                    break;
            }
        }

        /// <summary>
        /// Disable all icons (set intensity to 0).
        /// </summary>
        private void DisableAllIcons()
        {
            for (int i = 0; i < ICON_COUNT; i++)
            {
                _iconStates[i].Intensity = 0f;
                _iconStates[i].IsVisible = false;
            }
            UpdateNativeParams();
        }

        /// <summary>
        /// Set the enabled state of the navball label system.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            if (_enabled == enabled) return;

            _enabled = enabled;

            if (!enabled)
            {
                DisableAllIcons();
                Debug.Log("[NavballLabelManager] Disabled");
            }
            else
            {
                Debug.Log("[NavballLabelManager] Enabled");
            }
        }

        /// <summary>
        /// Check if the manager is enabled.
        /// </summary>
        public bool IsEnabled => _enabled;

        /// <summary>
        /// Set whether to use KSP standard navball colors or grid colors.
        /// </summary>
        public void SetUseNavballColors(bool useNavballColors)
        {
            if (_useNavballColors == useNavballColors) return;

            _useNavballColors = useNavballColors;
            Debug.Log($"[NavballLabelManager] Using {(useNavballColors ? "navball" : "grid")} colors");
        }

        /// <summary>
        /// Get whether navball colors are being used.
        /// </summary>
        public bool IsUsingNavballColors => _useNavballColors;

        /// <summary>
        /// Set the off-screen behavior mode.
        /// </summary>
        public void SetOffscreenMode(int mode)
        {
            if (_offscreenMode == mode) return;
            _offscreenMode = mode;
            Debug.Log($"[NavballLabelManager] Offscreen mode: {(mode == 0 ? "world-space" : "edge-clamp")}");
        }

        /// <summary>
        /// Set the icon style (SDF or ASCII).
        /// Note: Kept for API compatibility, but SDF is always used now.
        /// </summary>
        public void SetIconStyle(NavballIconStyle style)
        {
            // Kept for backward compatibility with UI
            // The new screen-space system only uses SDF
            Debug.Log($"[NavballLabelManager] Icon style set to {style} (note: screen-space uses SDF)");
        }

        /// <summary>
        /// Pack a Unity Color into uint ARGB format for GPU.
        /// </summary>
        private uint PackColor(Color c)
        {
            return ((uint)(c.a * 255) << 24) |
                   ((uint)(c.r * 255) << 16) |
                   ((uint)(c.g * 255) << 8) |
                   ((uint)(c.b * 255));
        }

        /// <summary>
        /// Get the direction of the active maneuver node burn vector.
        /// Returns null if no maneuver node is active.
        /// </summary>
        private Vector3d? GetManeuverNodeDirection()
        {
            try
            {
                if (FlightGlobals.ActiveVessel?.patchedConicSolver?.maneuverNodes == null)
                    return null;

                var nodes = FlightGlobals.ActiveVessel.patchedConicSolver.maneuverNodes;
                if (nodes.Count == 0)
                    return null;

                var node = nodes[0];
                if (node?.patch == null)
                    return null;

                // GetBurnVector returns the burn vector in rotating world (surface) space.
                Vector3d burnVector = node.GetBurnVector(node.patch);
                
                if (burnVector.sqrMagnitude < 0.0001)
                    return null;

                Vector3d direction = burnVector.normalized;
                _lastManeuver = direction;
                return direction;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NavballLabelManager] Failed to get maneuver direction: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get debug information about the current state.
        /// </summary>
        public string GetDebugInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== NavballLabelManager Debug ===");
            sb.AppendLine($"Initialized: {_initialized}");
            sb.AppendLine($"Enabled: {_enabled}");
            sb.AppendLine($"Offscreen Mode: {(_offscreenMode == 0 ? "world-space" : "edge-clamp")}");
            sb.AppendLine($"Use Navball Colors: {_useNavballColors}");
            sb.AppendLine();
            sb.AppendLine("Last Orbit Vectors:");
            sb.AppendLine($"  Prograde: {_lastPrograde}");
            sb.AppendLine($"  Normal: {_lastNormal}");
            sb.AppendLine($"  Radial Out: {_lastRadialOut}");
            sb.AppendLine($"  Maneuver: {_lastManeuver}");
            sb.AppendLine();
            sb.AppendLine("Icon States:");
            for (int i = 0; i < ICON_COUNT; i++)
            {
                var s = _iconStates[i];
                sb.AppendLine($"  {i}: {s.Name} - Visible:{s.IsVisible} Intensity:{s.Intensity:F3} EdgeMode:{s.IsInEdgeMode}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Dump comprehensive debug information to the custom log file.
        /// Called from UI debug button.
        /// </summary>
        public void DumpDebugInfo(Vector3? targetTrackerPos = null)
        {
            if (!_initialized)
            {
                ModFileLogger.Log("[Navball Debug] Manager not initialized");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("========== NAVBALL DEBUG DUMP ==========");
            sb.AppendLine($"Timestamp: {System.DateTime.Now:HH:mm:ss.fff}");
            sb.AppendLine();

            // Ship pointing vector (camera forward in surface frame)
            Vector3 shipForward = StarfieldCompositor.CameraForwardSurface;
            sb.AppendLine("SHIP POINTING (Camera Forward - Surface Frame):");
            sb.AppendLine($"  X: {shipForward.x:F6}, Y: {shipForward.y:F6}, Z: {shipForward.z:F6}");
            sb.AppendLine();

            // Camera basis
            sb.AppendLine("CAMERA BASIS (Surface Frame):");
            sb.AppendLine($"  Right:   X: {StarfieldCompositor.CameraRightSurface.x:F6}, Y: {StarfieldCompositor.CameraRightSurface.y:F6}, Z: {StarfieldCompositor.CameraRightSurface.z:F6}");
            sb.AppendLine($"  Up:      X: {StarfieldCompositor.CameraUpSurface.x:F6}, Y: {StarfieldCompositor.CameraUpSurface.y:F6}, Z: {StarfieldCompositor.CameraUpSurface.z:F6}");
            sb.AppendLine($"  Forward: X: {StarfieldCompositor.CameraForwardSurface.x:F6}, Y: {StarfieldCompositor.CameraForwardSurface.y:F6}, Z: {StarfieldCompositor.CameraForwardSurface.z:F6}");
            sb.AppendLine($"  Aspect: {StarfieldCompositor.CameraAspect:F4}, VFOV: {StarfieldCompositor.CachedVerticalFOV:F4}");
            sb.AppendLine();

            // Navball icon positions
            sb.AppendLine("NAVBALL ICON POSITIONS (Screen NDC - x:-aspect to aspect, y:-1 to 1):");
            for (int i = 0; i < ICON_COUNT; i++)
            {
                var s = _iconStates[i];
                sb.AppendLine($"  [{i}] {s.Name}: X: {s.ScreenNDC.x:F4}, Y: {s.ScreenNDC.y:F4}, Intensity: {s.Intensity:F3}, Visible: {s.IsVisible}");
            }
            sb.AppendLine();

            // Raw orbit vectors (world directions)
            sb.AppendLine("RAW ORBIT VECTORS (World Space):");
            sb.AppendLine($"  Prograde:    X: {_lastPrograde.x:F6}, Y: {_lastPrograde.y:F6}, Z: {_lastPrograde.z:F6}");
            sb.AppendLine($"  Normal:      X: {_lastNormal.x:F6}, Y: {_lastNormal.y:F6}, Z: {_lastNormal.z:F6}");
            sb.AppendLine($"  Radial Out:  X: {_lastRadialOut.x:F6}, Y: {_lastRadialOut.y:F6}, Z: {_lastRadialOut.z:F6}");
            if (_lastManeuver.HasValue)
                sb.AppendLine($"  Maneuver:    X: {_lastManeuver.Value.x:F6}, Y: {_lastManeuver.Value.y:F6}, Z: {_lastManeuver.Value.z:F6}");
            sb.AppendLine();

            // Target tracker position (if available)
            if (targetTrackerPos.HasValue)
            {
                sb.AppendLine("TARGET TRACKER POSITION (for reference - known working):");
                sb.AppendLine($"  X: {targetTrackerPos.Value.x:F4}, Y: {targetTrackerPos.Value.y:F4}");
            }
            else
            {
                sb.AppendLine("TARGET TRACKER: Not available (no target set)");
            }

            sb.AppendLine("========== END DEBUG DUMP ==========");
            ModFileLogger.Log(sb.ToString());
        }
    }
}
