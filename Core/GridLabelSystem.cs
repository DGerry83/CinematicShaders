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
        public string Text;                  // Text to display
        public float Latitude;               // -90 to 90 degrees
        public float Longitude;              // -180 to 180 degrees
        public float FontSizePixels;         // Font size for texture generation
        public bool Enabled;                 // Toggle state
        public GridLabelType LabelType;      // Category for UI grouping
        
        // Runtime data
        public RenderTexture Texture;        // Generated texture
        public bool TextureDirty;            // Needs regeneration
        public Vector3 WorldPosition;        // Position on unit sphere
        public Vector3 Tangent;              // Tangent vector (east/west)
        public Vector3 Bitangent;            // Bitangent vector (toward pole)
        public bool PositionDirty;           // Needs recalculation
        public float WorldSizeX;             // World-space width
        public float WorldSizeY;             // World-space height
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
                Text = "HUCK",
                Latitude = -75f,
                Longitude = 0f,
                FontSizePixels = DEFAULT_FONT_SIZE,
                Enabled = StarfieldSettings.EnableGridLabelHUCK,
                LabelType = GridLabelType.System,
                TextureDirty = true,
                PositionDirty = true
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
            _enabledLabels.Clear();
            foreach (var label in _labels.Values)
            {
                if (!label.Enabled) continue;
                _enabledLabels.Add(label);
                if (_enabledLabels.Count >= MAX_LABELS) break;
            }
            
            // Update each enabled label
            for (int i = 0; i < _enabledLabels.Count; i++)
            {
                var label = _enabledLabels[i];
                
                // Generate texture if needed
                if (label.Texture == null || label.TextureDirty)
                {
                    GenerateTexture(label);
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
            
            // Layout text
            uint color = 0xFFFFFFFF; // White
            int glyphCount = StarfieldNative.CR_TextLayout(_textSystem, label.Text, label.FontSizePixels, color);
            
            if (glyphCount <= 0)
            {
                Debug.LogWarning($"[GridLabelSystem] Text layout failed for '{label.Text}'");
                label.TextureDirty = false;
                return;
            }
            
            // Get text dimensions
            StarfieldNative.CR_TextGetBounds(_textSystem, out float boundsWidth, out float boundsHeight);
            
            // Clear texture
            RenderTexture.active = label.Texture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;
            
            // Render glyphs
            StarfieldNative.CR_TextDispatch(
                _textSystem,
                label.Texture.GetNativeTexturePtr(),
                glyphCount,
                TEXTURE_SIZE,
                TEXTURE_SIZE);
            
            // Calculate world size based on text aspect ratio
            float aspect = boundsWidth / Mathf.Max(boundsHeight, 1f);
            // Grid size preset: 0=Jumbo(8), 1=Large(12), 2=Medium(16), 3=Small(24), 4=Tiny(32)
            int[] gridMeridians = { 8, 12, 16, 24, 32 };
            int preset = Mathf.Clamp(StarfieldSettings.KartographerGridSize, 0, 4);
            float baseAngularSize = (Mathf.PI / gridMeridians[preset]) * 0.5f;
            
            label.WorldSizeY = baseAngularSize;
            label.WorldSizeX = label.WorldSizeY * aspect;
            
            label.TextureDirty = false;
            
            // Bind texture to native slot
            int slot = _enabledLabels.IndexOf(label);
            if (slot >= 0)
            {
                StarfieldNative.CR_SetGridLabelTexture(slot, label.Texture.GetNativeTexturePtr());
            }
        }
        
        private void CalculateTangentFrame(GridLabel label)
        {
            // Convert lat/lon to world position on unit sphere
            float latRad = label.Latitude * Mathf.Deg2Rad;
            float lonRad = label.Longitude * Mathf.Deg2Rad;
            
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
            
            // Pack data based on slot
            switch (slot)
            {
                case 0:
                    nativeParams.GridLabel0_PosX = label.WorldPosition.x;
                    nativeParams.GridLabel0_PosY = label.WorldPosition.y;
                    nativeParams.GridLabel0_PosZ = label.WorldPosition.z;
                    nativeParams.GridLabel0_TangentX = label.Tangent.x;
                    nativeParams.GridLabel0_TangentY = label.Tangent.y;
                    nativeParams.GridLabel0_TangentZ = label.Tangent.z;
                    nativeParams.GridLabel0_SizeX = label.WorldSizeX;
                    nativeParams.GridLabel0_SizeY = label.WorldSizeY;
                    break;
                case 1:
                    nativeParams.GridLabel1_PosX = label.WorldPosition.x;
                    nativeParams.GridLabel1_PosY = label.WorldPosition.y;
                    nativeParams.GridLabel1_PosZ = label.WorldPosition.z;
                    nativeParams.GridLabel1_TangentX = label.Tangent.x;
                    nativeParams.GridLabel1_TangentY = label.Tangent.y;
                    nativeParams.GridLabel1_TangentZ = label.Tangent.z;
                    nativeParams.GridLabel1_SizeX = label.WorldSizeX;
                    nativeParams.GridLabel1_SizeY = label.WorldSizeY;
                    break;
                case 2:
                    nativeParams.GridLabel2_PosX = label.WorldPosition.x;
                    nativeParams.GridLabel2_PosY = label.WorldPosition.y;
                    nativeParams.GridLabel2_PosZ = label.WorldPosition.z;
                    nativeParams.GridLabel2_TangentX = label.Tangent.x;
                    nativeParams.GridLabel2_TangentY = label.Tangent.y;
                    nativeParams.GridLabel2_TangentZ = label.Tangent.z;
                    nativeParams.GridLabel2_SizeX = label.WorldSizeX;
                    nativeParams.GridLabel2_SizeY = label.WorldSizeY;
                    break;
                case 3:
                    nativeParams.GridLabel3_PosX = label.WorldPosition.x;
                    nativeParams.GridLabel3_PosY = label.WorldPosition.y;
                    nativeParams.GridLabel3_PosZ = label.WorldPosition.z;
                    nativeParams.GridLabel3_TangentX = label.Tangent.x;
                    nativeParams.GridLabel3_TangentY = label.Tangent.y;
                    nativeParams.GridLabel3_TangentZ = label.Tangent.z;
                    nativeParams.GridLabel3_SizeX = label.WorldSizeX;
                    nativeParams.GridLabel3_SizeY = label.WorldSizeY;
                    break;
                case 4:
                    nativeParams.GridLabel4_PosX = label.WorldPosition.x;
                    nativeParams.GridLabel4_PosY = label.WorldPosition.y;
                    nativeParams.GridLabel4_PosZ = label.WorldPosition.z;
                    nativeParams.GridLabel4_TangentX = label.Tangent.x;
                    nativeParams.GridLabel4_TangentY = label.Tangent.y;
                    nativeParams.GridLabel4_TangentZ = label.Tangent.z;
                    nativeParams.GridLabel4_SizeX = label.WorldSizeX;
                    nativeParams.GridLabel4_SizeY = label.WorldSizeY;
                    break;
                case 5:
                    nativeParams.GridLabel5_PosX = label.WorldPosition.x;
                    nativeParams.GridLabel5_PosY = label.WorldPosition.y;
                    nativeParams.GridLabel5_PosZ = label.WorldPosition.z;
                    nativeParams.GridLabel5_TangentX = label.Tangent.x;
                    nativeParams.GridLabel5_TangentY = label.Tangent.y;
                    nativeParams.GridLabel5_TangentZ = label.Tangent.z;
                    nativeParams.GridLabel5_SizeX = label.WorldSizeX;
                    nativeParams.GridLabel5_SizeY = label.WorldSizeY;
                    break;
                case 6:
                    nativeParams.GridLabel6_PosX = label.WorldPosition.x;
                    nativeParams.GridLabel6_PosY = label.WorldPosition.y;
                    nativeParams.GridLabel6_PosZ = label.WorldPosition.z;
                    nativeParams.GridLabel6_TangentX = label.Tangent.x;
                    nativeParams.GridLabel6_TangentY = label.Tangent.y;
                    nativeParams.GridLabel6_TangentZ = label.Tangent.z;
                    nativeParams.GridLabel6_SizeX = label.WorldSizeX;
                    nativeParams.GridLabel6_SizeY = label.WorldSizeY;
                    break;
                case 7:
                    nativeParams.GridLabel7_PosX = label.WorldPosition.x;
                    nativeParams.GridLabel7_PosY = label.WorldPosition.y;
                    nativeParams.GridLabel7_PosZ = label.WorldPosition.z;
                    nativeParams.GridLabel7_TangentX = label.Tangent.x;
                    nativeParams.GridLabel7_TangentY = label.Tangent.y;
                    nativeParams.GridLabel7_TangentZ = label.Tangent.z;
                    nativeParams.GridLabel7_SizeX = label.WorldSizeX;
                    nativeParams.GridLabel7_SizeY = label.WorldSizeY;
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
