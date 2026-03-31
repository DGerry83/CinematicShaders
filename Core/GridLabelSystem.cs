using CinematicShaders.Native;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Category for grid labels - used for UI organization
    /// </summary>
    public enum GridLabelType
    {
        System,      // Built-in labels like HUCK
        SOI,         // Sphere of Influence bodies
        OrbitInfo,   // Apoapsis, periapsis, nodes
        Custom,      // User-defined markers
        Debug        // Debug/info labels
    }

    /// <summary>
    /// Data for a single grid label positioned on the holographic sphere.
    /// </summary>
    public class GridLabel
    {
        public string Id;                    // Unique identifier (e.g., "huck", "kerbin_soi")
        public string Text;                  // Active text to display
        public string DefaultText;           // Original fallback text for variant resolution
        public float Latitude;               // -90 to 90 degrees
        public float Longitude;              // -180 to 180 degrees
        public float FontSizePixels;         // Font size for texture generation
        public bool Enabled;                 // Toggle state
        public GridLabelType LabelType;      // Category for UI grouping
        
        // Visual settings (new)
        public float Intensity = 1.0f;       // Brightness multiplier
        public Color OverrideColor = Color.clear;  // Color.clear = use grid color
        
        // Runtime data
        public RenderTexture Texture;        // Generated texture
        public bool TextureDirty;            // Needs regeneration
        public Vector3 WorldPosition;        // Position on unit sphere
        public Vector3 Tangent;              // Tangent vector (east/west)
        public Vector3 Bitangent;            // Bitangent vector (toward pole)
        public bool PositionDirty;           // Needs recalculation
        public float WorldSizeX;             // World-space width
        public float WorldSizeY;             // World-space height
        
        // Dynamic text throttling (new)
        public string LastText;              // Last text that was rendered
        public float LastUpdateTime;         // Time of last texture update
        public float MinUpdateInterval = 0.1f;  // Minimum seconds between updates (10 FPS)
        public bool ForceTextureUpdate = false; // Bypass throttling for real-time tuning
        public bool SnapToGrid = false;         // Align to grid cell bottom-left corner
        public string InitialsText;              // Active big-first-letter text (2-pass rendering)
        public string DefaultInitialsText;       // Original fallback initials for variant resolution
        public float InitialsFontSizeMultiplier = 1.3f;  // Size multiplier for initials
        public Dictionary<int, string> Variants;       // Grid-preset-aware text variants (key = max preset index)
        public Dictionary<int, string> InitialsVariants; // Matching initials variants for 2-pass rendering
        
        // Per-label positioning tunables
        public float RotationDegrees = 0f;   // Clockwise rotation around normal
        public float PaddingLeft = 0.12f;    // Fraction of WorldSizeX to nudge east
        public float PaddingBottom = 0.12f;  // Fraction of WorldSizeY to nudge north
        public float LineSpacing = 0f;       // Extra pixels between lines in texture
    }

    /// <summary>
    /// Manages all grid labels - texture generation, positioning, and rendering.
    /// Supports up to 8 labels simultaneously (limited by native constant buffer).
    /// </summary>
    public class GridLabelSystem
    {
        public const int MAX_LABELS = 8;
        
        private Dictionary<string, GridLabel> _labels = new Dictionary<string, GridLabel>();
        private List<GridLabel> _enabledLabels = new List<GridLabel>();
        private IntPtr _textSystem = IntPtr.Zero;
        private bool _initialized = false;
        
        // Cache for native texture bindings - avoid rebinding same texture
        private IntPtr[] _boundTextures = new IntPtr[MAX_LABELS];
        
        // Debug tracking
        private int _lastEnabledCount = -1;
        private uint _lastEnabledMask = 0;
        private int _lastGridPreset = -1;
        
        // Font configuration
        private const float DEFAULT_FONT_SIZE = 18f;
        private const int TEXTURE_SIZE = 256;
        private const string FONT_NAME = "Ac437_Rainbow100_re_66.ttf";
        
        /// <summary>
        /// Initializes the text system and registers built-in labels.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            
            if (!StarfieldNative.IsLoaded)
            {
                Debug.LogWarning("[GridLabelSystem] Native DLL not loaded, cannot initialize");
                return;
            }
            
            InitializeTextSystem();
            RegisterBuiltInLabels();
            
            // Apply current grid-size defaults and resolve variants for all registered labels
            int currentPreset = Mathf.Clamp(StarfieldSettings.KartographerGridSize, 0, 4);
            _lastGridPreset = currentPreset;
            foreach (var label in _labels.Values)
            {
                ApplyLabelDefaults(label, currentPreset);
                label.Text = ResolveVariant(label.Variants, label.DefaultText, currentPreset);
                label.InitialsText = ResolveVariant(label.InitialsVariants, label.DefaultInitialsText, currentPreset);
                label.PositionDirty = true;
                label.TextureDirty = true;
            }
            
            _initialized = true;
        }
        
        private void InitializeTextSystem()
        {
            if (_textSystem != IntPtr.Zero) return;
            
            // Build font path: ../PluginData/Fonts/Ac437_Rainbow100_re_66.ttf
            // C# DLL is in Plugins/, font is in PluginData/ at mod root level
            string assemblyPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string fontPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(assemblyPath, "..", "PluginData", "Fonts", FONT_NAME));
            
            if (!System.IO.File.Exists(fontPath))
            {
                Debug.LogError($"[GridLabelSystem] Font not found: {fontPath}");
                return;
            }
            
            try
            {
                _textSystem = StarfieldNative.CR_TextInit(
                    Texture2D.whiteTexture.GetNativeTexturePtr(),
                    fontPath);
                
                if (_textSystem == IntPtr.Zero)
                {
                    Debug.LogError("[GridLabelSystem] Failed to initialize text system");
                    return;
                }
                
                Debug.Log($"[GridLabelSystem] Text system initialized with font: {FONT_NAME}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GridLabelSystem] Exception initializing text system: {ex.Message}");
            }
        }
        
        private void RegisterBuiltInLabels()
        {
            // HUCK label - positioned near south pole
            RegisterLabel(new GridLabel
            {
                Id = "huck",
                Text = "H\nU\nC\nK\nv0.6.28",
                DefaultText = "H\nU\nC\nK\nv0.6.28",
                InitialsText = null,
                DefaultInitialsText = null,
                Latitude = -75f,
                Longitude = 0f,
                FontSizePixels = DEFAULT_FONT_SIZE,
                Enabled = true,
                LabelType = GridLabelType.System,
                TextureDirty = true,
                PositionDirty = true,
                SnapToGrid = true,
                Variants = new Dictionary<int, string>
                {
                    { 1, "OLOGRAPHIC\nNIVERSAL\nELESTIAL\nARTOGRAPHER\nv0.6.28" },  // Jumbo + Large
                    { 3, "H\nU\nC\nK\nv0.6.28" },  // Medium + Small
                    { 4, "H\nU\nC\nK" }  // Tiny: no version number
                },
                InitialsVariants = new Dictionary<int, string>
                {
                    { 1, "H\nU\nC\nK" }  // Jumbo + Large
                },
                RotationDegrees = -2f,
                PaddingLeft = 0.12f,
                PaddingBottom = 0.12f,
                LineSpacing = 6f
            });
            
            // Situation info display labels - dual-sided, positioned based on grid preset
            // These are managed by CinematicShadersAddon, not the label system directly
            // SnapToGrid = true so they align to grid cell corners at their assigned lat/lon
            RegisterLabel(new GridLabel
            {
                Id = "situation_a",
                Text = "SITUATION\nINFO\nDEBUG",
                DefaultText = "SITUATION\nINFO\nDEBUG",
                Latitude = 60f,  // Will be overridden based on grid preset
                Longitude = 0f,
                FontSizePixels = DEFAULT_FONT_SIZE,
                Enabled = false,
                LabelType = GridLabelType.Debug,
                TextureDirty = true,
                PositionDirty = true,
                SnapToGrid = true,  // Align to grid cell at assigned lat/lon
                RotationDegrees = 0f,
                PaddingLeft = 0.1f,
                PaddingBottom = 0.1f,
                LineSpacing = 4f
            });
            
            RegisterLabel(new GridLabel
            {
                Id = "situation_b",
                Text = "SITUATION\nINFO\nDEBUG",
                DefaultText = "SITUATION\nINFO\nDEBUG",
                Latitude = 60f,  // Will be overridden based on grid preset
                Longitude = 180f,  // Opposite side
                FontSizePixels = DEFAULT_FONT_SIZE,
                Enabled = false,
                LabelType = GridLabelType.Debug,
                TextureDirty = true,
                PositionDirty = true,
                SnapToGrid = true,  // Align to grid cell at assigned lat/lon
                RotationDegrees = 0f,
                PaddingLeft = 0.1f,
                PaddingBottom = 0.1f,
                LineSpacing = 4f
            });
        }
        
        /// <summary>
        /// Registers a new label. Replaces existing label with same ID.
        /// </summary>
        public void RegisterLabel(GridLabel label)
        {
            if (_labels.ContainsKey(label.Id))
            {
                // Clean up old label
                UnregisterLabel(label.Id);
            }
            
            _labels[label.Id] = label;
            Debug.Log($"[GridLabelSystem] Registered label: {label.Id} '{label.Text}'");
        }
        
        /// <summary>
        /// Unregisters and cleans up a label.
        /// </summary>
        public void UnregisterLabel(string id)
        {
            if (!_labels.TryGetValue(id, out var label)) return;
            
            if (label.Texture != null)
            {
                UnityEngine.Object.Destroy(label.Texture);
                label.Texture = null;
            }
            
            _labels.Remove(id);
            Debug.Log($"[GridLabelSystem] Unregistered label: {id}");
        }
        
        /// <summary>
        /// Enables or disables a label. Persists HUCK setting.
        /// </summary>
        public void SetLabelEnabled(string id, bool enabled)
        {
            if (!_labels.TryGetValue(id, out var label)) return;
            
            label.Enabled = enabled;
            
            // Persist HUCK setting
            if (id == "huck")
            {
                StarfieldSettings.EnableGridLabelHUCK = enabled;
                StarfieldSettings.Save();
            }
            
            Debug.Log($"[GridLabelSystem] Label {id} {(enabled ? "enabled" : "disabled")}");
        }
        
        /// <summary>
        /// Gets whether a label is enabled.
        /// </summary>
        public bool IsLabelEnabled(string id)
        {
            return _labels.TryGetValue(id, out var label) && label.Enabled;
        }
        
        /// <summary>
        /// Gets a label by ID.
        /// </summary>
        public GridLabel GetLabel(string id)
        {
            _labels.TryGetValue(id, out var label);
            return label;
        }
        
        /// <summary>
        /// Updates label position (lat/lon). Useful for dynamic labels like SOI.
        /// </summary>
        public void UpdateLabelPosition(string id, float latitude, float longitude)
        {
            if (!_labels.TryGetValue(id, out var label)) return;
            
            label.Latitude = latitude;
            label.Longitude = longitude;
            label.PositionDirty = true;
        }
        
        /// <summary>
        /// Updates all enabled labels and pushes to native plugin.
        /// Call this every frame when Kartographer is enabled.
        /// </summary>
        public void Update()
        {
            if (!_initialized)
            {
                Initialize();
                if (!_initialized) return;
            }
            
            if (_textSystem == IntPtr.Zero) return;
            
            // Build list of enabled labels (max 8)
            // Skip labels that are disabled (e.g., Tiny preset)
            _enabledLabels.Clear();
            foreach (var label in _labels.Values)
            {
                if (!label.Enabled) continue;
                _enabledLabels.Add(label);
                if (_enabledLabels.Count >= MAX_LABELS) break;
            }
            
            // If no labels enabled, clear native mask and exit early
            if (_enabledLabels.Count == 0)
            {
                var emptyParams = StarfieldNative.LastKartographerParams;
                if (emptyParams.GridLabelEnabledMask != 0)
                {
                    emptyParams.GridLabelEnabledMask = 0;
                    StarfieldNative.LastKartographerParams = emptyParams;
                    StarfieldNative.CR_StarfieldSetKartographerParams(ref emptyParams);
                }
                return;
            }
            
            // Debug: Log when enabled label count changes
            if (_enabledLabels.Count != _lastEnabledCount)
            {
                _lastEnabledCount = _enabledLabels.Count;
                Debug.Log($"[GridLabelSystem] Enabled labels changed: {_enabledLabels.Count} labels active");
                foreach (var l in _enabledLabels)
                {
                    Debug.Log($"[GridLabelSystem]  - {l.Id}: '{l.Text}' at ({l.Latitude:F1}°, {l.Longitude:F1}°)");
                }
            }
            
            // Detect grid preset changes and resolve label variants
            int currentPreset = Mathf.Clamp(StarfieldSettings.KartographerGridSize, 0, 4);
            if (currentPreset != _lastGridPreset)
            {
                int previousPreset = _lastGridPreset;
                _lastGridPreset = currentPreset;
                
                foreach (var label in _labels.Values)
                {
                    ApplyLabelDefaults(label, currentPreset);
                    
                    // If label was disabled (e.g., Tiny preset), re-enable when switching to larger preset
                    if (!label.Enabled && previousPreset == 4 && currentPreset < 4)
                    {
                        label.Enabled = true;
                    }
                    
                    // Only update text/texture if label is enabled
                    if (label.Enabled)
                    {
                        label.Text = ResolveVariant(label.Variants, label.DefaultText, currentPreset);
                        label.InitialsText = ResolveVariant(label.InitialsVariants, label.DefaultInitialsText, currentPreset);
                        label.TextureDirty = true;
                        
                        if (label.SnapToGrid)
                        {
                            label.PositionDirty = true;
                        }
                        
                        Debug.Log($"[GridLabelSystem] Preset changed to {currentPreset} for '{label.Id}': text='{label.Text}', font={label.FontSizePixels}, spacing={label.LineSpacing}");
                    }
                    else
                    {
                        Debug.Log($"[GridLabelSystem] Preset changed to {currentPreset}, label '{label.Id}' disabled (Tiny grid)");
                    }
                }
            }
            
            // Tie HUCK label intensity to grid intensity
            var huck = _enabledLabels.Find(l => l.Id == "huck");
            if (huck != null)
            {
                huck.Intensity = StarfieldSettings.KartographerGridIntensity / 0.002f; // Normalize so default grid intensity = 1.0
            }
            
            // Update each enabled label
            for (int i = 0; i < _enabledLabels.Count; i++)
            {
                var label = _enabledLabels[i];
                
                // Generate texture if needed (with throttling for dynamic text)
                if (label.Texture == null || label.TextureDirty || label.ForceTextureUpdate)
                {
                    bool shouldUpdate = true;
                    
                    if (label.ForceTextureUpdate)
                    {
                        // Real-time tuning: bypass all throttling
                        label.ForceTextureUpdate = false;
                    }
                    else
                    {
                        // Throttling: only update at most every MinUpdateInterval seconds
                        // Use unscaledTime so updates work when game is paused (timeScale = 0)
                        float timeSinceLastUpdate = Time.unscaledTime - label.LastUpdateTime;
                        if (timeSinceLastUpdate < label.MinUpdateInterval)
                        {
                            shouldUpdate = false;
                        }
                        // If TextureDirty is explicitly set, always regenerate (e.g. font size changed)
                        else if (!label.TextureDirty && label.Text == label.LastText)
                        {
                            // Text unchanged and no explicit dirty flag - no need to regenerate
                            shouldUpdate = false;
                        }
                    }
                    
                    if (shouldUpdate)
                    {
                        GenerateTexture(label);
                        label.LastText = label.Text;
                        label.LastUpdateTime = Time.unscaledTime;
                        label.TextureDirty = false;
                        Debug.Log($"[GridLabelSystem] Generated texture for '{label.Text}' at font={label.FontSizePixels}, size={label.WorldSizeX:F3}x{label.WorldSizeY:F3}");
                    }
                }
                
                // Update position if needed
                if (label.PositionDirty)
                {
                    CalculateTangentFrame(label);
                }
                
                // Push to native slot
                PushLabelToNative(label, i);
            }
            
            // Disable unused slots
            DisableUnusedSlots(_enabledLabels.Count);
        }
        
        /// <summary>
        /// Packs a Color into a uint (ARGB format for shader)
        /// </summary>
        private uint PackColor(Color color)
        {
            uint a = (uint)(color.a * 255) << 24;
            uint r = (uint)(color.r * 255) << 16;
            uint g = (uint)(color.g * 255) << 8;
            uint b = (uint)(color.b * 255);
            return a | r | g | b;
        }
        
        /// <summary>
        /// Resolves a grid-preset-aware variant string. Returns fallback if no variant matches.
        /// </summary>
        private string ResolveVariant(Dictionary<int, string> variants, string fallback, int preset)
        {
            if (variants == null || variants.Count == 0)
                return fallback;
            
            int bestThreshold = int.MaxValue;
            string bestText = fallback;
            
            foreach (var kvp in variants)
            {
                if (preset <= kvp.Key && kvp.Key < bestThreshold)
                {
                    bestThreshold = kvp.Key;
                    bestText = kvp.Value;
                }
            }
            return bestText;
        }
        
        /// <summary>
        /// Applies hard-coded defaults for the current grid preset.
        /// Labels are disabled for Tiny preset (preset 4) - grid is too dense.
        /// Final tuned values from debug session.
        /// </summary>
        private void ApplyLabelDefaults(GridLabel label, int preset)
        {
            if (label.Id != "huck") return;
            
            // Disable labels for Tiny preset - grid is too dense for readable labels
            if (preset == 4)
            {
                label.Enabled = false;
                return;
            }
            
            // Re-enable if coming from Tiny preset
            label.Enabled = true;
            
            switch (preset)
            {
                case 0: // Jumbo
                    label.RotationDegrees = -2f;
                    label.PaddingLeft = 0.10f;
                    label.PaddingBottom = 0.00f;
                    label.FontSizePixels = 18f;
                    label.LineSpacing = 4.5f;
                    break;
                case 1: // Large
                    label.RotationDegrees = -2f;
                    label.PaddingLeft = 0.12f;
                    label.PaddingBottom = 0.00f;
                    label.FontSizePixels = 21f;
                    label.LineSpacing = 5.3f;
                    break;
                case 2: // Medium
                    label.RotationDegrees = -2f;
                    label.PaddingLeft = 0.17f;
                    label.PaddingBottom = 0.07f;
                    label.FontSizePixels = 29f;
                    label.LineSpacing = 0f;
                    break;
                case 3: // Small
                    label.RotationDegrees = -2f;
                    label.PaddingLeft = 0.20f;
                    label.PaddingBottom = 0.70f;
                    label.FontSizePixels = 36f;
                    label.LineSpacing = 0f;
                    break;
            }
        }
        
        /// <summary>
        /// Sets per-label intensity (brightness multiplier)
        /// </summary>
        public void SetLabelIntensity(string id, float intensity)
        {
            if (_labels.TryGetValue(id, out var label))
            {
                label.Intensity = intensity;
            }
        }
        
        /// <summary>
        /// Sets per-label color override (Color.clear to use grid color)
        /// </summary>
        public void SetLabelColor(string id, Color color)
        {
            if (_labels.TryGetValue(id, out var label))
            {
                label.OverrideColor = color;
            }
        }
        
        private void GenerateTexture(GridLabel label)
        {
            if (_textSystem == IntPtr.Zero) return;
            
            // Create texture if needed
            if (label.Texture == null)
            {
                label.Texture = new RenderTexture(TEXTURE_SIZE, TEXTURE_SIZE, 0, RenderTextureFormat.ARGB32);
                label.Texture.enableRandomWrite = true;
                label.Texture.Create();
            }
            
            uint color = 0xFFFFFFFF; // White
            float boundsWidth, boundsHeight;
            
            if (!string.IsNullOrEmpty(label.InitialsText))
            {
                // Two-pass rendering: big initials + body text
                float initialsSize = label.FontSizePixels * label.InitialsFontSizeMultiplier;
                float hPadding = 4.0f;
                float vPadding = 2.0f;
                
                // First, layout initials to get actual bounds (not advances)
                int g1 = StarfieldNative.CR_TextLayoutEx(_textSystem, label.InitialsText, initialsSize, color, 0.0f, TEXTURE_SIZE * 0.5f, 0.0f);
                StarfieldNative.CR_TextGetBounds(_textSystem, out float iw, out float ih);
                
                // Layout body to get its bounds
                int bodyLineCount = label.Text.Split('\n').Length;
                float bodyExtraHeight = (bodyLineCount - 1) * label.LineSpacing;
                int g2 = StarfieldNative.CR_TextLayoutEx(_textSystem, label.Text, label.FontSizePixels, color, 0.0f, TEXTURE_SIZE * 0.5f, label.LineSpacing);
                StarfieldNative.CR_TextGetBounds(_textSystem, out float bw, out float bh);
                
                // Align first body line with first initial
                float bodyOriginY = initialsSize - label.FontSizePixels;
                
                boundsWidth = Mathf.Max(iw, iw + hPadding + bw);
                boundsHeight = Mathf.Max(ih, bodyOriginY + bh + bodyExtraHeight);
                float originY = TEXTURE_SIZE - boundsHeight - vPadding;
                
                // Pass 1: render initials (clears texture), aligned to bottom-left of texture
                g1 = StarfieldNative.CR_TextLayoutEx(_textSystem, label.InitialsText, initialsSize, color, 0.0f, originY, 0.0f);
                if (g1 > 0)
                {
                    StarfieldNative.CR_TextDispatchEx(
                        _textSystem,
                        label.Texture.GetNativeTexturePtr(),
                        g1,
                        TEXTURE_SIZE,
                        TEXTURE_SIZE,
                        1);
                }
                
                // Pass 2: render body next to initials (no clear)
                g2 = StarfieldNative.CR_TextLayoutEx(_textSystem, label.Text, label.FontSizePixels, color, iw + hPadding, originY + bodyOriginY, label.LineSpacing);
                if (g2 > 0)
                {
                    StarfieldNative.CR_TextDispatchEx(
                        _textSystem,
                        label.Texture.GetNativeTexturePtr(),
                        g2,
                        TEXTURE_SIZE,
                        TEXTURE_SIZE,
                        0);
                }
            }
            else
            {
                // Single-pass rendering, aligned to bottom-left of texture
                float vPadding = 2.0f;
                
                // Layout text at a temporary origin to get actual bounds (not advances)
                int glyphCount = StarfieldNative.CR_TextLayoutEx(_textSystem, label.Text, label.FontSizePixels, color, 0.0f, TEXTURE_SIZE * 0.5f, label.LineSpacing);
                
                if (glyphCount <= 0)
                {
                    Debug.LogWarning($"[GridLabelSystem] Text layout failed for '{label.Text}'");
                    return;
                }
                
                // Get actual rendered bounds (accounts for bitmap extents, not just advances)
                StarfieldNative.CR_TextGetBounds(_textSystem, out boundsWidth, out boundsHeight);
                
                // Re-layout with correct origin for final render
                float originY = TEXTURE_SIZE - boundsHeight - vPadding;
                glyphCount = StarfieldNative.CR_TextLayoutEx(_textSystem, label.Text, label.FontSizePixels, color, 0.0f, originY, label.LineSpacing);
                
                // Render glyphs (clears texture)
                StarfieldNative.CR_TextDispatchEx(
                    _textSystem,
                    label.Texture.GetNativeTexturePtr(),
                    glyphCount,
                    TEXTURE_SIZE,
                    TEXTURE_SIZE,
                    1);
            }
            
            // Calculate world size based on text aspect ratio
            float aspect = boundsWidth / Mathf.Max(boundsHeight, 1f);
            // Clamp aspect ratio to prevent extreme distortion for vertical text
            aspect = Mathf.Clamp(aspect, 0.4f, 2.5f);
            
            // Grid size preset: 0=Jumbo(8), 1=Large(12), 2=Medium(16), 3=Small(24), 4=Tiny(32)
            int[] gridMeridians = { 8, 12, 16, 24, 32 };
            int preset = Mathf.Clamp(StarfieldSettings.KartographerGridSize, 0, 4);
            float baseAngularSize = (Mathf.PI / gridMeridians[preset]) * 0.5f;
            
            label.WorldSizeY = baseAngularSize;
            label.WorldSizeX = label.WorldSizeY * aspect;
            
            // Bind texture to native slot
            int slot = _enabledLabels.IndexOf(label);
            if (slot >= 0)
            {
                StarfieldNative.CR_SetGridLabelTexture(slot, label.Texture.GetNativeTexturePtr());
                Debug.Log($"[GridLabelSystem] Generated texture for '{label.Text}' in slot {slot}, size {label.WorldSizeX:F3}x{label.WorldSizeY:F3}, aspect={aspect:F2}");
            }
        }
        
        private void CalculateTangentFrame(GridLabel label)
        {
            float latRad, lonRad;
            
            if (label.SnapToGrid)
            {
                // Snap to bottom-left corner of the grid cell containing the label's position
                // This allows any label to align to its assigned grid cell while keeping HUCK at south pole
                int[] gridMeridians = { 8, 12, 16, 24, 32 };
                int[] gridParallels = { 5, 8, 10, 15, 20 };
                int preset = Mathf.Clamp(StarfieldSettings.KartographerGridSize, 0, 4);
                int numLong = gridMeridians[preset];
                int numLat = gridParallels[preset];
                
                float thetaStep = 2.0f * Mathf.PI / numLong;
                float phiStep = Mathf.PI / numLat;
                
                // Convert label's latitude to phi (polar angle from north pole)
                float labelLatRad = label.Latitude * Mathf.Deg2Rad;
                float labelPhi = Mathf.PI / 2.0f - labelLatRad; // 0 at north pole, π at south pole
                
                // Find which latitude band (cell row) the label is in
                int latCell = Mathf.FloorToInt(labelPhi / phiStep);
                latCell = Mathf.Clamp(latCell, 0, numLat - 1);
                
                // Position at southern edge of that cell (bottom of cell)
                float phi = (latCell + 0.5f) * phiStep;
                latRad = Mathf.PI / 2.0f - phi;
                
                // Convert label's longitude to theta (-π to π)
                float labelLonRad = label.Longitude * Mathf.Deg2Rad;
                
                // Find which longitude band (cell column) the label is in
                float normalizedLon = labelLonRad + Mathf.PI; // 0 to 2π
                int lonCell = Mathf.FloorToInt(normalizedLon / thetaStep);
                lonCell = lonCell % numLong;
                
                // Position at western edge of that cell
                float lon = -Mathf.PI + (lonCell + 0.5f) * thetaStep;
                lonRad = lon;
            }
            else
            {
                latRad = label.Latitude * Mathf.Deg2Rad;
                lonRad = label.Longitude * Mathf.Deg2Rad;
            }
            
            // Standard spherical to Cartesian
            // x = cos(lat) * cos(lon)
            // y = sin(lat)
            // z = cos(lat) * sin(lon)
            label.WorldPosition = new Vector3(
                Mathf.Cos(latRad) * Mathf.Cos(lonRad),
                Mathf.Sin(latRad),
                Mathf.Cos(latRad) * Mathf.Sin(lonRad)
            );
            
            // Apply grid rotation (yaw/pitch)
            label.WorldPosition = KartographerMath.ApplyCatalogRotation(
                label.WorldPosition, 0,
                StarfieldSettings.KartographerRotationYaw,
                StarfieldSettings.KartographerRotationPitch);
            
            // Calculate tangent frame
            // Tangent: east/west along parallel = cross(up, position)
            label.Tangent = Vector3.Cross(Vector3.up, label.WorldPosition).normalized;
            
            // Bitangent: toward pole = cross(position, tangent)
            label.Bitangent = Vector3.Cross(label.WorldPosition, label.Tangent).normalized;
            
            label.PositionDirty = false;
        }
        
        private void PushLabelToNative(GridLabel label, int slot)
        {
            if (slot < 0 || slot >= MAX_LABELS) return;
            
            var nativeParams = StarfieldNative.LastKartographerParams;
            
            // Set enabled bit in mask
            nativeParams.GridLabelEnabledMask |= (1u << slot);
            
            // Shader uses bottom-left corner anchoring
            Vector3 pos = label.WorldPosition;
            Vector3 tangent = label.Tangent;
            Vector3 bitangent = label.Bitangent;
            
            if (label.SnapToGrid)
            {
                // Nudge east and north to nestle into the corner without overlapping grid lines
                pos += tangent * (label.WorldSizeX * label.PaddingLeft) + bitangent * (label.WorldSizeY * label.PaddingBottom);
                
                // Apply per-label rotation around the top-left corner
                float angleRad = label.RotationDegrees * Mathf.Deg2Rad;
                float cosA = Mathf.Cos(angleRad);
                float sinA = Mathf.Sin(angleRad);
                Vector3 normal = pos.normalized;
                
                Vector3 RotateAroundAxis(Vector3 vec, Vector3 axis, float c, float s)
                {
                    return vec * c + Vector3.Cross(axis, vec) * s;
                }
                
                // Rotate pos around top-left corner (using original bitangent)
                Vector3 topLeft = pos + label.Bitangent * label.WorldSizeY;
                Vector3 toBottomLeft = pos - topLeft;
                Vector3 vRot = RotateAroundAxis(toBottomLeft, normal, cosA, sinA);
                pos = topLeft + vRot;
                
                // Rotate the frame
                tangent = RotateAroundAxis(tangent, normal, cosA, sinA).normalized;
                bitangent = RotateAroundAxis(bitangent, normal, cosA, sinA).normalized;
            }
            else
            {
                // Legacy center-anchored labels: shift to bottom-left
                pos -= tangent * (label.WorldSizeX * 0.5f) + bitangent * (label.WorldSizeY * 0.5f);
            }
            
            // Pack data as Vector4 (pos.xyz + sizeX, tangent.xyz + sizeY)
            switch (slot)
            {
                case 0:
                    nativeParams.GridLabel0_PosTangentX = new Vector4(pos.x, pos.y, pos.z, label.WorldSizeX);
                    nativeParams.GridLabel0_TangentY = new Vector4(tangent.x, tangent.y, tangent.z, label.WorldSizeY);
                    break;
                case 1:
                    nativeParams.GridLabel1_PosTangentX = new Vector4(pos.x, pos.y, pos.z, label.WorldSizeX);
                    nativeParams.GridLabel1_TangentY = new Vector4(tangent.x, tangent.y, tangent.z, label.WorldSizeY);
                    break;
                case 2:
                    nativeParams.GridLabel2_PosTangentX = new Vector4(pos.x, pos.y, pos.z, label.WorldSizeX);
                    nativeParams.GridLabel2_TangentY = new Vector4(tangent.x, tangent.y, tangent.z, label.WorldSizeY);
                    break;
                case 3:
                    nativeParams.GridLabel3_PosTangentX = new Vector4(pos.x, pos.y, pos.z, label.WorldSizeX);
                    nativeParams.GridLabel3_TangentY = new Vector4(tangent.x, tangent.y, tangent.z, label.WorldSizeY);
                    break;
                case 4:
                    nativeParams.GridLabel4_PosTangentX = new Vector4(pos.x, pos.y, pos.z, label.WorldSizeX);
                    nativeParams.GridLabel4_TangentY = new Vector4(tangent.x, tangent.y, tangent.z, label.WorldSizeY);
                    break;
                case 5:
                    nativeParams.GridLabel5_PosTangentX = new Vector4(pos.x, pos.y, pos.z, label.WorldSizeX);
                    nativeParams.GridLabel5_TangentY = new Vector4(tangent.x, tangent.y, tangent.z, label.WorldSizeY);
                    break;
                case 6:
                    nativeParams.GridLabel6_PosTangentX = new Vector4(pos.x, pos.y, pos.z, label.WorldSizeX);
                    nativeParams.GridLabel6_TangentY = new Vector4(tangent.x, tangent.y, tangent.z, label.WorldSizeY);
                    break;
                case 7:
                    nativeParams.GridLabel7_PosTangentX = new Vector4(pos.x, pos.y, pos.z, label.WorldSizeX);
                    nativeParams.GridLabel7_TangentY = new Vector4(tangent.x, tangent.y, tangent.z, label.WorldSizeY);
                    break;
            }
            
            // Push per-label intensity and color
            switch (slot)
            {
                case 0:
                    nativeParams.LabelIntensity0 = label.Intensity;
                    nativeParams.LabelColor0 = label.OverrideColor == Color.clear ? 0u : PackColor(label.OverrideColor);
                    break;
                case 1:
                    nativeParams.LabelIntensity1 = label.Intensity;
                    nativeParams.LabelColor1 = label.OverrideColor == Color.clear ? 0u : PackColor(label.OverrideColor);
                    break;
                case 2:
                    nativeParams.LabelIntensity2 = label.Intensity;
                    nativeParams.LabelColor2 = label.OverrideColor == Color.clear ? 0u : PackColor(label.OverrideColor);
                    break;
                case 3:
                    nativeParams.LabelIntensity3 = label.Intensity;
                    nativeParams.LabelColor3 = label.OverrideColor == Color.clear ? 0u : PackColor(label.OverrideColor);
                    break;
                case 4:
                    nativeParams.LabelIntensity4 = label.Intensity;
                    nativeParams.LabelColor4 = label.OverrideColor == Color.clear ? 0u : PackColor(label.OverrideColor);
                    break;
                case 5:
                    nativeParams.LabelIntensity5 = label.Intensity;
                    nativeParams.LabelColor5 = label.OverrideColor == Color.clear ? 0u : PackColor(label.OverrideColor);
                    break;
                case 6:
                    nativeParams.LabelIntensity6 = label.Intensity;
                    nativeParams.LabelColor6 = label.OverrideColor == Color.clear ? 0u : PackColor(label.OverrideColor);
                    break;
                case 7:
                    nativeParams.LabelIntensity7 = label.Intensity;
                    nativeParams.LabelColor7 = label.OverrideColor == Color.clear ? 0u : PackColor(label.OverrideColor);
                    break;
            }
            
            StarfieldNative.LastKartographerParams = nativeParams;
            StarfieldNative.CR_StarfieldSetKartographerParams(ref nativeParams);
            
            // Bind texture for this slot (only if changed)
            if (label.Texture != null)
            {
                IntPtr texturePtr = label.Texture.GetNativeTexturePtr();
                if (_boundTextures[slot] != texturePtr)
                {
                    _boundTextures[slot] = texturePtr;
                    StarfieldNative.CR_SetGridLabelTexture(slot, texturePtr);
                }
            }
            
            // Debug: Log when mask changes
            if (nativeParams.GridLabelEnabledMask != _lastEnabledMask)
            {
                _lastEnabledMask = nativeParams.GridLabelEnabledMask;
                Debug.Log($"[GridLabelSystem] Pushed '{label.Text}' to slot {slot}: mask=0x{nativeParams.GridLabelEnabledMask:X}, pos=({label.WorldPosition.x:F3},{label.WorldPosition.y:F3},{label.WorldPosition.z:F3})");
            }
        }
        
        private void DisableUnusedSlots(int startSlot)
        {
            var nativeParams = StarfieldNative.LastKartographerParams;
            
            // Clear enabled bits for unused slots
            for (int i = startSlot; i < MAX_LABELS; i++)
            {
                nativeParams.GridLabelEnabledMask &= ~(1u << i);
                _boundTextures[i] = IntPtr.Zero;  // Clear texture binding cache
            }
            
            StarfieldNative.LastKartographerParams = nativeParams;
            StarfieldNative.CR_StarfieldSetKartographerParams(ref nativeParams);
        }
        
        /// <summary>
        /// Gets all labels of a specific type.
        /// </summary>
        public IEnumerable<GridLabel> GetLabelsByType(GridLabelType type)
        {
            foreach (var label in _labels.Values)
            {
                if (label.LabelType == type)
                    yield return label;
            }
        }
        
        /// <summary>
        /// Adds a dynamic SOI label.
        /// </summary>
        public void AddSOILabel(string bodyName, float latitude, float longitude)
        {
            string id = $"soi_{bodyName.ToLower()}";
            
            // Remove existing if present
            if (_labels.ContainsKey(id))
            {
                UnregisterLabel(id);
            }
            
            RegisterLabel(new GridLabel
            {
                Id = id,
                Text = bodyName,
                Latitude = latitude,
                Longitude = longitude,
                FontSizePixels = DEFAULT_FONT_SIZE + 2, // Slightly larger
                Enabled = true,
                LabelType = GridLabelType.SOI,
                TextureDirty = true,
                PositionDirty = true
            });
            
            Debug.Log($"[GridLabelSystem] Added SOI label for {bodyName}");
        }
        
        /// <summary>
        /// Removes an SOI label.
        /// </summary>
        public void RemoveSOILabel(string bodyName)
        {
            string id = $"soi_{bodyName.ToLower()}";
            UnregisterLabel(id);
        }
        
        /// <summary>
        /// Cleans up all resources.
        /// </summary>
        public void Shutdown()
        {
            foreach (var label in _labels.Values)
            {
                if (label.Texture != null)
                {
                    UnityEngine.Object.Destroy(label.Texture);
                    label.Texture = null;
                }
            }
            _labels.Clear();
            _enabledLabels.Clear();
            
            if (_textSystem != IntPtr.Zero)
            {
                StarfieldNative.CR_TextShutdown(_textSystem);
                _textSystem = IntPtr.Zero;
            }
            
            _initialized = false;
        }
    }
}
