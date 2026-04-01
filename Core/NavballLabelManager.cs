using CinematicShaders.Native;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Icon style options for navball indicators
    /// </summary>
    public enum NavballIconStyle
    {
        SDF,    // High-quality SDF icons from SVG
        ASCII   // Retro ASCII art style (future implementation)
    }

    /// <summary>
    /// Manages the 7 orbital direction indicators (navball labels) for the Kartographer grid.
    /// 
    /// Slot Assignments (using existing 12-label system):
    /// - Slot 3: Prograde (orbit velocity direction)
    /// - Slot 4: Retrograde (opposite of velocity)
    /// - Slot 5: Normal (orbit normal, perpendicular to orbital plane)
    /// - Slot 6: AntiNormal (opposite of normal)
    /// - Slot 7: Radial In (toward center of gravity)
    /// - Slot 8: Radial Out (away from center of gravity)
    /// - Slot 9: Maneuver (burn vector of active maneuver node)
    /// 
    /// Positioning: Dynamic world-space vectors (NOT grid-snapped)
    /// Updates: Every frame during flight scene
    /// </summary>
    public class NavballLabelManager
    {
        // Slot assignments in the 12-label grid system
        private const int PROGRADE_SLOT = 3;
        private const int RETROGRADE_SLOT = 4;
        private const int NORMAL_SLOT = 5;
        private const int ANTINORMAL_SLOT = 6;
        private const int RADIAL_IN_SLOT = 7;
        private const int RADIAL_OUT_SLOT = 8;
        private const int MANEUVER_SLOT = 9;

        // Label IDs for registration
        private const string PROGRADE_ID = "navball_prograde";
        private const string RETROGRADE_ID = "navball_retrograde";
        private const string NORMAL_ID = "navball_normal";
        private const string ANTINORMAL_ID = "navball_antinormal";
        private const string RADIAL_IN_ID = "navball_radial_in";
        private const string RADIAL_OUT_ID = "navball_radial_out";
        private const string MANEUVER_ID = "navball_maneuver";

        /// <summary>
        /// KSP standard navball colors (RGB)
        /// </summary>
        public static readonly Dictionary<string, Color> NavballColors = new Dictionary<string, Color>
        {
            { PROGRADE_ID, new Color(0.0f, 1.0f, 0.0f) },      // Green
            { RETROGRADE_ID, new Color(1.0f, 0.0f, 0.0f) },    // Red
            { NORMAL_ID, new Color(0.0f, 0.5f, 1.0f) },        // Blue
            { ANTINORMAL_ID, new Color(1.0f, 0.0f, 1.0f) },    // Magenta
            { RADIAL_IN_ID, new Color(1.0f, 0.8f, 0.0f) },     // Yellow/Orange
            { RADIAL_OUT_ID, new Color(1.0f, 1.0f, 1.0f) },    // White
            { MANEUVER_ID, new Color(1.0f, 0.5f, 0.0f) }       // Orange (KSP maneuver node color)
        };

        // Grid label system reference
        private GridLabelSystem _labelSystem;

        // Runtime state
        private bool _initialized = false;
        private bool _enabled = false;
        private bool _useNavballColors = false;
        private NavballIconStyle _iconStyle = NavballIconStyle.SDF;

        // Texture cache
        private Dictionary<string, RenderTexture> _sdfTextures = new Dictionary<string, RenderTexture>();
        private bool _texturesLoaded = false;

        // Orbit vector cache (for debugging)
        private Vector3d _lastPrograde;
        private Vector3d _lastNormal;
        private Vector3d _lastRadialOut;
        private Vector3d? _lastManeuver;

        /// <summary>
        /// Initialize the navball label manager and register labels with the grid system.
        /// </summary>
        public void Initialize(GridLabelSystem labelSystem)
        {
            if (_initialized) return;

            _labelSystem = labelSystem ?? throw new ArgumentNullException(nameof(labelSystem));

            try
            {
                // Register all 7 navball labels
                RegisterNavballLabel(PROGRADE_ID, PROGRADE_SLOT, "Prograde", NavballColors[PROGRADE_ID]);
                RegisterNavballLabel(RETROGRADE_ID, RETROGRADE_SLOT, "Retrograde", NavballColors[RETROGRADE_ID]);
                RegisterNavballLabel(NORMAL_ID, NORMAL_SLOT, "Normal", NavballColors[NORMAL_ID]);
                RegisterNavballLabel(ANTINORMAL_ID, ANTINORMAL_SLOT, "AntiNormal", NavballColors[ANTINORMAL_ID]);
                RegisterNavballLabel(RADIAL_IN_ID, RADIAL_IN_SLOT, "Radial In", NavballColors[RADIAL_IN_ID]);
                RegisterNavballLabel(RADIAL_OUT_ID, RADIAL_OUT_SLOT, "Radial Out", NavballColors[RADIAL_OUT_ID]);
                RegisterNavballLabel(MANEUVER_ID, MANEUVER_SLOT, "Maneuver", NavballColors[MANEUVER_ID]);

                _initialized = true;
                Debug.Log("[NavballLabelManager] Initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NavballLabelManager] Initialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Register a single navball label with the grid system.
        /// </summary>
        private void RegisterNavballLabel(string id, int slot, string displayName, Color defaultColor)
        {
            // Unregister if already exists (for reinitialization)
            var existing = _labelSystem.GetLabel(id);
            if (existing != null)
            {
                _labelSystem.UnregisterLabel(id);
            }

            var label = new GridLabel
            {
                Id = id,
                Text = displayName,  // Used for identification, not displayed
                DefaultText = displayName,
                FontSizePixels = 18f,
                Enabled = false,  // Disabled until explicitly enabled
                LabelType = GridLabelType.OrbitInfo,
                TextureDirty = true,
                PositionDirty = true,
                WorldSizeX = 0.08f,  // Base size, will be adjusted
                WorldSizeY = 0.08f,
                Intensity = 1.0f,
                OverrideColor = Color.clear,  // Use grid color by default
                SnapToGrid = false  // CRITICAL: Dynamic positioning, NOT grid-snapped
            };

            _labelSystem.RegisterLabel(label);
            Debug.Log($"[NavballLabelManager] Registered label '{id}' in slot {slot}");
        }

        /// <summary>
        /// Load SDF textures from the NavballIcons folder.
        /// </summary>
        private void LoadTextures()
        {
            if (_texturesLoaded) return;

            try
            {
                string basePath = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "..", "PluginData", "NavballIcons");

                LoadSDFTexture(PROGRADE_ID, Path.Combine(basePath, "prograde_sdf.png"));
                LoadSDFTexture(RETROGRADE_ID, Path.Combine(basePath, "retrograde_sdf.png"));
                LoadSDFTexture(NORMAL_ID, Path.Combine(basePath, "normal_sdf.png"));
                LoadSDFTexture(ANTINORMAL_ID, Path.Combine(basePath, "antinormal_sdf.png"));
                LoadSDFTexture(RADIAL_IN_ID, Path.Combine(basePath, "radial_in_sdf.png"));
                LoadSDFTexture(RADIAL_OUT_ID, Path.Combine(basePath, "radial_out_sdf.png"));
                LoadSDFTexture(MANEUVER_ID, Path.Combine(basePath, "maneuver_sdf.png"));

                _texturesLoaded = true;
                Debug.Log("[NavballLabelManager] All SDF textures loaded successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NavballLabelManager] Failed to load textures: {ex.Message}");
            }
        }

        /// <summary>
        /// Load a single SDF texture and bind it to the native plugin.
        /// </summary>
        private void LoadSDFTexture(string labelId, string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[NavballLabelManager] Texture not found: {filePath}");
                return;
            }

            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);

                // Load as Texture2D (MSDF textures are RGB, but we treat them as coverage)
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                tex.LoadImage(fileData);

                // Create RenderTexture for native plugin (must be ARGB32 for compatibility)
                RenderTexture rt = new RenderTexture(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                rt.enableRandomWrite = true;
                rt.Create();

                // Copy data
                Graphics.Blit(tex, rt);

                // Cache and bind
                _sdfTextures[labelId] = rt;

                // Bind to appropriate slot
                int slot = GetSlotForLabelId(labelId);
                if (slot >= 0)
                {
                    StarfieldNative.CR_SetGridLabelTexture(slot, rt.GetNativeTexturePtr());
                }

                UnityEngine.Object.Destroy(tex);  // Clean up temporary texture

                Debug.Log($"[NavballLabelManager] Loaded texture for '{labelId}' ({filePath})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NavballLabelManager] Failed to load texture for '{labelId}': {ex.Message}");
            }
        }

        /// <summary>
        /// Get the slot number for a label ID.
        /// </summary>
        private int GetSlotForLabelId(string id)
        {
            switch (id)
            {
                case PROGRADE_ID: return PROGRADE_SLOT;
                case RETROGRADE_ID: return RETROGRADE_SLOT;
                case NORMAL_ID: return NORMAL_SLOT;
                case ANTINORMAL_ID: return ANTINORMAL_SLOT;
                case RADIAL_IN_ID: return RADIAL_IN_SLOT;
                case RADIAL_OUT_ID: return RADIAL_OUT_SLOT;
                case MANEUVER_ID: return MANEUVER_SLOT;
                default: return -1;
            }
        }

        /// <summary>
        /// Main update loop - call every frame when enabled.
        /// Calculates orbit vectors and updates label positions.
        /// </summary>
        public void Update()
        {
            if (!_initialized || !_enabled) return;

            // Only operate in Flight scene with active vessel
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                SetLabelsEnabled(false);
                return;
            }

            if (FlightGlobals.ActiveVessel?.orbit == null)
            {
                SetLabelsEnabled(false);
                return;
            }

            // Load textures on first enabled frame
            if (!_texturesLoaded)
            {
                LoadTextures();
            }

            // Calculate orbit vectors
            Orbit orbit = FlightGlobals.ActiveVessel.orbit;
            Vector3d pos = orbit.pos;      // Position relative to body
            Vector3d vel = orbit.vel;      // Velocity vector

            // Calculate 6 orbital directions
            Vector3d prograde = vel.normalized;
            Vector3d retrograde = -prograde;
            Vector3d normal = Vector3d.Cross(pos, vel).normalized;
            Vector3d antinormal = -normal;
            Vector3d radialOut = pos.normalized;
            Vector3d radialIn = -radialOut;

            // Get maneuver node direction (if active maneuver node exists)
            Vector3d? maneuverDirection = GetManeuverNodeDirection();

            // Cache for debugging
            _lastPrograde = prograde;
            _lastNormal = normal;
            _lastRadialOut = radialOut;

            // Update label positions
            UpdateLabelPosition(PROGRADE_ID, prograde);
            UpdateLabelPosition(RETROGRADE_ID, retrograde);
            UpdateLabelPosition(NORMAL_ID, normal);
            UpdateLabelPosition(ANTINORMAL_ID, antinormal);
            UpdateLabelPosition(RADIAL_IN_ID, radialIn);
            UpdateLabelPosition(RADIAL_OUT_ID, radialOut);
            
            // Update maneuver node position (only if maneuver node exists)
            if (maneuverDirection.HasValue)
            {
                UpdateLabelPosition(MANEUVER_ID, maneuverDirection.Value);
                SetLabelEnabled(MANEUVER_ID, true);
            }
            else
            {
                SetLabelEnabled(MANEUVER_ID, false);
            }

            // Ensure labels are enabled
            SetLabelsEnabled(true);
        }

        /// <summary>
        /// Update the world position and tangent frame for a label.
        /// </summary>
        private void UpdateLabelPosition(string id, Vector3d direction)
        {
            var label = _labelSystem.GetLabel(id);
            if (label == null) return;

            // Convert to Unity Vector3
            Vector3 dir = new Vector3((float)direction.x, (float)direction.y, (float)direction.z);

            // Direction is already on unit sphere (normalized)
            label.WorldPosition = dir;

            // Calculate tangent frame
            // Tangent: perpendicular to position, pointing "east" (roughly)
            label.Tangent = Vector3.Cross(Vector3.up, label.WorldPosition).normalized;

            // Bitangent: perpendicular to both position and tangent (pointing toward pole)
            label.Bitangent = Vector3.Cross(label.WorldPosition, label.Tangent).normalized;

            // Handle degenerate case at poles
            if (label.Tangent.sqrMagnitude < 0.001f)
            {
                label.Tangent = Vector3.right;
                label.Bitangent = Vector3.Cross(label.WorldPosition, label.Tangent).normalized;
            }

            label.PositionDirty = true;
        }

        /// <summary>
        /// Enable or disable all navball labels.
        /// </summary>
        private void SetLabelsEnabled(bool enabled)
        {
            SetLabelEnabled(PROGRADE_ID, enabled);
            SetLabelEnabled(RETROGRADE_ID, enabled);
            SetLabelEnabled(NORMAL_ID, enabled);
            SetLabelEnabled(ANTINORMAL_ID, enabled);
            SetLabelEnabled(RADIAL_IN_ID, enabled);
            SetLabelEnabled(RADIAL_OUT_ID, enabled);
            // Note: Maneuver label is managed separately based on node existence
        }

        /// <summary>
        /// Enable or disable a single label.
        /// </summary>
        private void SetLabelEnabled(string id, bool enabled)
        {
            var label = _labelSystem.GetLabel(id);
            if (label != null && label.Enabled != enabled)
            {
                label.Enabled = enabled;
                _labelSystem.SetLabelEnabled(id, enabled);
            }
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
                // Hide all labels when disabled
                SetLabelsEnabled(false);
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

            // Update color overrides for all labels
            foreach (var kvp in NavballColors)
            {
                var label = _labelSystem.GetLabel(kvp.Key);
                if (label != null)
                {
                    label.OverrideColor = useNavballColors ? kvp.Value : Color.clear;
                }
            }

            Debug.Log($"[NavballLabelManager] Using {(useNavballColors ? "navball" : "grid")} colors");
        }

        /// <summary>
        /// Get whether navball colors are being used.
        /// </summary>
        public bool IsUsingNavballColors => _useNavballColors;

        /// <summary>
        /// Set the icon style (SDF or ASCII).
        /// Note: ASCII style is future work; currently only SDF is supported.
        /// </summary>
        public void SetIconStyle(NavballIconStyle style)
        {
            if (_iconStyle == style) return;

            _iconStyle = style;

            // Future: Reload textures based on style
            if (style == NavballIconStyle.ASCII)
            {
                Debug.LogWarning("[NavballLabelManager] ASCII style not yet implemented, using SDF");
                _iconStyle = NavballIconStyle.SDF;
            }

            Debug.Log($"[NavballLabelManager] Icon style set to {_iconStyle}");
        }

        /// <summary>
        /// Get the current icon style.
        /// </summary>
        public NavballIconStyle IconStyle => _iconStyle;

        /// <summary>
        /// Clean up resources when the manager is destroyed.
        /// </summary>
        public void Shutdown()
        {
            // Disable all labels
            SetLabelsEnabled(false);

            // Clean up textures
            foreach (var kvp in _sdfTextures)
            {
                if (kvp.Value != null)
                {
                    UnityEngine.Object.Destroy(kvp.Value);
                }
            }
            _sdfTextures.Clear();
            _texturesLoaded = false;

            _initialized = false;
            Debug.Log("[NavballLabelManager] Shutdown complete");
        }

        /// <summary>
        /// Get the direction of the active maneuver node burn vector.
        /// Returns null if no maneuver node is active.
        /// </summary>
        private Vector3d? GetManeuverNodeDirection()
        {
            try
            {
                // Check if vessel has an active maneuver node
                if (FlightGlobals.ActiveVessel?.patchedConicSolver?.maneuverNodes == null)
                    return null;

                var nodes = FlightGlobals.ActiveVessel.patchedConicSolver.maneuverNodes;
                if (nodes.Count == 0)
                    return null;

                // Get the first (next) maneuver node
                var node = nodes[0];
                if (node?.patch == null)
                    return null;

                // Get the burn vector from the maneuver node
                // The deltaV vector is in the node's orbit reference frame
                Vector3d burnVector = node.GetBurnVector(node.patch);
                
                if (burnVector.sqrMagnitude < 0.0001)
                    return null;

                Vector3d direction = burnVector.normalized;
                _lastManeuver = direction;
                return direction;
            }
            catch (Exception ex)
            {
                // Silently handle errors - maneuver node may not be valid
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
            sb.AppendLine($"Textures Loaded: {_texturesLoaded}");
            sb.AppendLine($"Use Navball Colors: {_useNavballColors}");
            sb.AppendLine($"Icon Style: {_iconStyle}");
            sb.AppendLine();
            sb.AppendLine("Last Orbit Vectors:");
            sb.AppendLine($"  Prograde: {_lastPrograde}");
            sb.AppendLine($"  Normal: {_lastNormal}");
            sb.AppendLine($"  Radial Out: {_lastRadialOut}");
            sb.AppendLine($"  Maneuver: {_lastManeuver}");
            return sb.ToString();
        }
    }
}
