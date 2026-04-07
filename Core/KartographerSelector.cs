using CinematicShaders.Native;
using CinematicShaders.Shaders.Starfield;
using CinematicShaders.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Animation phases for star selection UI
    /// </summary>
    public enum SelectionAnimationPhase
    {
        Circle,     // 0-0.4s: Circle flickers
        Box,        // 0.4s: Box snaps on
        Text,       // 0.4s-1.9s: Text types on
        Complete    // 1.9s+: Cursor blinks
    }

    /// <summary>
    /// Named star data from JSON sidecar
    /// </summary>
    public class NamedStar
    {
        public int HipparcosID;
        public string Name;
        public string SpectralType;
        public float Magnitude;
        public float DistanceLy;
        public string Constellation;
        public Vector3 Direction;
    }

    /// <summary>
    /// Tracks selected/hovered stars and projects them to screen space for HUD rendering.
    /// Phase 4: Added text rendering for info box.
    /// </summary>
    public class KartographerSelector
    {
        private Dictionary<int, NamedStar> _namedStars = new Dictionary<int, NamedStar>();
        private bool _jsonLoaded = false;
        private string _lastCatalogPath = "";

        // Path tracking for _Custom.json support
        private string _loadedJsonPath = "";
        private string _defaultJsonPath = "";
        private string _customJsonPath = "";

        // Public accessors for editor (future Phase 2)
        public string LoadedJsonPath => _loadedJsonPath;
        public string DefaultJsonPath => _defaultJsonPath;
        public string CustomJsonPath => _customJsonPath;
        public string CurrentCatalogBasePath => Path.ChangeExtension(_lastCatalogPath, null);

        // Tracking state
        public NamedStar TrackedStar { get; private set; }
        public Vector2 TrackedStarScreenUV { get; private set; }
        public bool IsTracking => TrackedStar != null && TrackedStarScreenUV.x >= 0f;

        // Cached camera basis from StarfieldCompositor
        public Vector3 CameraRight { get; set; }
        public Vector3 CameraUp { get; set; }
        public Vector3 CameraForward { get; set; }
        public float AspectRatio { get; set; } = 1.777f;
        public float VerticalFOV { get; set; } = 1.0472f; // ~60 degrees default

        // Enable/disable selection circle rendering
        public bool SelectionCircleEnabled { get; set; } = false;

        // ============================================================================
        // Hover/Click Selection State
        // ============================================================================
        private NamedStar _hoveredStar = null;
        private NamedStar _lockedStar = null;
        private Vector2 _hoveredStarUV = new Vector2(-1, -1);
        private float _selectionFlickerT = 1.0f;  // 0-1, animation progress
        private bool _wasMouseDown = false;
        private int _frameCounter = 0;
        private float _starHash = 0f;  // Hash of locked star for flicker variation
        private bool _mouseHoverMode = false;  // Enable mouse hover selection
        
        // Track last displayed star to prevent redundant text updates
        private NamedStar _lastLoggedHover = null;

        // ============================================================================
        // Sequential Animation State (Phase 1: Text Type-On System)
        // ============================================================================
        private SelectionAnimationPhase _animationPhase = SelectionAnimationPhase.Complete;
        private float _textTypeT = 0.0f;  // 0-1 progress for text type-on animation
        private int _lastLockedStarHIP = 0;  // For same-star reselection check
        private string _fullStarText = "";  // Complete text for current star
        private string _currentDisplayText = "";  // Text with cursor for rendering

        // ============================================================================
        // Text System (Phase 4)
        // ============================================================================
        private IntPtr _textSystem = IntPtr.Zero;
        private ComputeBuffer _glyphBuffer = null;
        private RenderTexture _textTexture = null;
        private string _lastText = null;
        private bool _textDirty = false;
        private static readonly float FONT_SIZE = 24f;
        
        // Text measurement for auto-sizing (actual bounds from native, updated when text changes)
        private float _textWidthPixels = 0f;
        private float _textHeightPixels = 0f;
        
        // Padding around text inside the box (pixels)
        private static readonly float BOX_PADDING_PIXELS = 20f;
        private static readonly float BOX_PADDING_BOTTOM_PIXELS = 72f;  // Extra padding on bottom

        // ============================================================================
        // Grid Label Text (HUCK) - Grid-Fixed Type (rotates with grid)
        // ============================================================================
        private RenderTexture _gridLabelTexture = null;
        private bool _gridLabelDirty = true;
        private static readonly float GRID_LABEL_BASE_SIZE = 18f;  // Regular text size (was 12)
        private static readonly float GRID_LABEL_LARGE_SIZE = 27f;  // First letter size (1.5x base)
        private static readonly int GRID_LABEL_TEXTURE_SIZE = 256;

        // ============================================================================

        /// <summary>
        /// Initialize the text system with the retro font
        /// </summary>
        public void InitializeTextSystem()
        {
            if (_textSystem != IntPtr.Zero)
                return; // Already initialized

            if (!StarfieldNative.IsLoaded)
            {
                Debug.LogWarning("[KartographerSelector] Cannot initialize text system - native DLL not loaded");
                return;
            }

            try
            {
                // Build font path: ../PluginData/Fonts/Ac437_Rainbow100_re_66.ttf
                // C# DLL is in Plugins/, font is in PluginData/ at mod root level
                string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string fontPath = Path.GetFullPath(Path.Combine(assemblyPath, "..", "PluginData", "Fonts", "Ac437_Rainbow100_re_66.ttf"));

                if (!File.Exists(fontPath))
                {
                    Debug.LogError($"[KartographerSelector] Font file not found: {fontPath}");
                    return;
                }

                // Initialize text system with device source texture
                _textSystem = StarfieldNative.CR_TextInit(
                    Texture2D.whiteTexture.GetNativeTexturePtr(),
                    fontPath);

                if (_textSystem == IntPtr.Zero)
                {
                    Debug.LogError("[KartographerSelector] Failed to initialize text system");
                    return;
                }

                Debug.Log($"[KartographerSelector] Text system initialized with font: {fontPath}");

                // Create text render texture (1024x1024 for lots of room)
                _textTexture = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32);
                _textTexture.enableRandomWrite = true;
                _textTexture.Create();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[KartographerSelector] Text system initialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Build formatted text string for the tracked star
        /// </summary>
        /// <summary>
        /// Get visual description for spectral type
        /// </summary>
        private string GetSpectralDescription(string spectralType)
        {
            if (string.IsNullOrEmpty(spectralType) || spectralType == "?")
                return "UNKNOWN";
            
            char type = spectralType[0];
            switch (type)
            {
                case 'O': return "O - BLUE SUPERGIANT";
                case 'B': return "B - BLUE-WHITE";
                case 'A': return "A - WHITE";
                case 'F': return "F - YELLOW-WHITE";
                case 'G': return "G - YELLOW";
                case 'K': return "L - ORANGE";
                case 'M': return "M - RED GIANT";
                case 'L': return "L - BROWN DWARF";
                default: return "?? UNKNOWN";
            }
        }

        /// <summary>
        /// Build formatted text for star info box
        /// Format: All caps, line breaks after each field
        /// </summary>
        private string BuildStarText(NamedStar star)
        {
            if (star == null)
                return "";

            var sb = new System.Text.StringBuilder();
            
            // NAME: <name>
            sb.Append("NAME: ");
            sb.Append(star.Name.ToUpper());
            sb.Append('\n');
            
            // DISTANCE: <distance> LY
            if (star.DistanceLy > 0)
            {
                sb.Append($"DISTANCE: {star.DistanceLy:F1} LY\n");
            }
            else
            {
                sb.Append("DISTANCE: UNKNOWN\n");
            }
            
            // MAGNITUDE: <mag>
            sb.Append($"MAGNITUDE: {star.Magnitude:F2}\n");
            
            // TYPE: <spectral description>
            string typeDesc = GetSpectralDescription(star.SpectralType);
            sb.Append($"TYPE: {typeDesc}\n");
            
            // CONSTELLATION: <constellation name>
            sb.Append($"CONSTELLATION: {star.Constellation.ToUpper()}\n");
            
            // HIP#####
            sb.Append($"HIP{star.HipparcosID}");

            return sb.ToString();
        }

        /// <summary>
        /// Build display text with cursor for type-on animation
        /// Progresses at ~15 characters per second over 1.5s
        /// </summary>
        private string BuildDisplayTextWithCursor(string fullText, float typeT)
        {
            if (string.IsNullOrEmpty(fullText))
                return "";

            // Type-on phase: progressively reveal characters
            if (typeT < 1.0f)
            {
                int visibleChars = (int)(fullText.Length * typeT);
                visibleChars = Mathf.Clamp(visibleChars, 0, fullText.Length);
                // Use ^| escape sequence - C++ will decode to U+258C LEFT HALF BLOCK
                return fullText.Substring(0, visibleChars) + "^|";
            }
            
            // Complete phase: full text with blinking cursor at 2Hz
            // 2Hz blink = 0.25s on, 0.25s off
            bool cursorVisible = (Time.time * 2.0f) % 2.0f < 1.0f;
            // Use ^| escape sequence - C++ will decode to U+258C LEFT HALF BLOCK
            return fullText + (cursorVisible ? "^|" : " ");
        }

        /// <summary>
        /// Get display text for current animation phase
        /// Returns empty during Circle phase, cursor only during Box phase,
        /// progressive text during Text phase, full text in Complete phase
        /// </summary>
        private string GetTextForCurrentPhase()
        {
            // No text during Circle phase (box not visible yet)
            if (_animationPhase == SelectionAnimationPhase.Circle)
                return "";
            
            // Box phase: show just cursor (box visible, text starting)
            if (_animationPhase == SelectionAnimationPhase.Box)
                return "^|";
            
            // Text phase: progressively reveal with cursor
            if (_animationPhase == SelectionAnimationPhase.Text)
                return BuildDisplayTextWithCursor(_fullStarText, _textTypeT);
            
            // Complete phase: full text with blinking cursor
            return BuildDisplayTextWithCursor(_fullStarText, 1.0f);
        }

        /// <summary>
        /// Start or continue animation for a newly locked star
        /// Checks for same-star reselection to avoid restarting animation
        /// </summary>
        private void StartAnimationForStar(NamedStar star)
        {
            if (star == null)
                return;

            // Check for same-star reselection - keep animation stable
            if (_lastLockedStarHIP == star.HipparcosID && _animationPhase == SelectionAnimationPhase.Complete)
            {
                // Same star, already complete - just ensure text is up to date
                _fullStarText = BuildStarText(star);
                _currentDisplayText = BuildDisplayTextWithCursor(_fullStarText, 1.0f);
                _textDirty = true;
                return;
            }

            // New star or re-selecting during animation - reset and start fresh
            _lastLockedStarHIP = star.HipparcosID;
            _animationPhase = SelectionAnimationPhase.Circle;
            _selectionFlickerT = 0.0f;
            _textTypeT = 0.0f;
            _fullStarText = BuildStarText(star);
            _currentDisplayText = "^|";  // Start with just cursor (escape sequence for U+258C)
            _textDirty = true;
            
            // Animation started
        }

        /// <summary>
        /// Update animation phases based on elapsed time
        /// Circle: 0-0.4s, Box: 0.4s, Text: 0.4s-1.9s, Complete: 1.9s+
        /// Text/cursor only appears once box is visible (Text phase and beyond)
        /// </summary>
        private void UpdateAnimation()
        {
            if (_lockedStar == null)
            {
                // Reset when no star locked
                _animationPhase = SelectionAnimationPhase.Complete;
                _selectionFlickerT = 1.0f;
                _textTypeT = 0.0f;
                return;
            }

            // Update circle flicker (0-0.4s)
            if (_animationPhase == SelectionAnimationPhase.Circle)
            {
                _selectionFlickerT += Time.deltaTime / 0.4f;
                if (_selectionFlickerT >= 1.0f)
                {
                    _selectionFlickerT = 1.0f;
                    _animationPhase = SelectionAnimationPhase.Box;
                    // Animation phase: Box
                }
            }

            // Box snaps on immediately when circle completes (0.4s)
            if (_animationPhase == SelectionAnimationPhase.Box)
            {
                _animationPhase = SelectionAnimationPhase.Text;
                // Animation phase: Text
            }

            // Text type-on (0.4s-1.9s = 1.5s duration)
            if (_animationPhase == SelectionAnimationPhase.Text)
            {
                _textTypeT += Time.deltaTime / 1.5f;
                if (_textTypeT >= 1.0f)
                {
                    _textTypeT = 1.0f;
                    _animationPhase = SelectionAnimationPhase.Complete;
                    // Animation phase: Complete
                }
            }

            // Update display text based on current phase (each frame for cursor blink)
            // Circle: empty, Box: cursor only, Text: progressive, Complete: full+blink
            string newDisplayText = GetTextForCurrentPhase();
            if (newDisplayText != _currentDisplayText)
            {
                _currentDisplayText = newDisplayText;
                _textDirty = true;
            }
        }

        /// <summary>
        /// Update text texture when tracked star changes
        /// Uses progressively built display text for type-on animation
        /// </summary>
        private void UpdateTextTexture()
        {
            if (_textSystem == IntPtr.Zero)
            {
                InitializeTextSystem();
                if (_textSystem == IntPtr.Zero)
                    return;
            }

            // Use the progressively built display text (with cursor) for animation
            // This will type on during Text phase and blink cursor in Complete phase
            string text = _currentDisplayText;
            
            // Skip if text hasn't changed
            if (text == _lastText && !_textDirty)
                return;

            _lastText = text;
            _textDirty = false;

            if (string.IsNullOrEmpty(text))
            {
                // Clear texture
                RenderTexture.active = _textTexture;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = null;
                return;
            }

            // Layout text in native code
            uint color = 0xFFFFFFFF; // White ARGB
            int glyphCount = StarfieldNative.CR_TextLayout(_textSystem, text, FONT_SIZE, color);

            if (glyphCount <= 0)
            {
                Debug.LogWarning("[KartographerSelector] Text layout returned 0 glyphs");
                return;
            }
            
            // Get ACTUAL rendered bounds (width from glyph coverage, height from line metrics)
            float measuredWidth, measuredHeight;
            StarfieldNative.CR_TextMeasure(_textSystem, text, FONT_SIZE, out measuredWidth, out measuredHeight);
            
            float boundsWidth, boundsHeight;
            StarfieldNative.CR_TextGetBounds(_textSystem, out boundsWidth, out boundsHeight);
            
            // Use bounds for width (actual pixel coverage including last glyph), measure for height (proper line spacing)
            _textWidthPixels = boundsWidth;
            _textHeightPixels = measuredHeight;
            
            // Log text content and dimensions for debugging
            string[] lines = text.Split('\n');
            int maxLineLen = 0;
            string longestLine = "";
            foreach (var line in lines)
            {
                if (line.Length > maxLineLen)
                {
                    maxLineLen = line.Length;
                    longestLine = line;
                }
            }
            // Get glyph data pointer from native
            System.IntPtr glyphPtr = StarfieldNative.CR_TextGetGlyphPtr(_textSystem);
            if (glyphPtr == System.IntPtr.Zero)
            {
                Debug.LogError("[KartographerSelector] Failed to get glyph pointer");
                return;
            }

            // Ensure compute buffer is sized correctly
            int bufferSize = glyphCount * System.Runtime.InteropServices.Marshal.SizeOf(typeof(StarfieldNative.GlyphData));
            if (_glyphBuffer == null || _glyphBuffer.count < glyphCount)
            {
                if (_glyphBuffer != null)
                    _glyphBuffer.Release();
                
                _glyphBuffer = new ComputeBuffer(Mathf.Max(glyphCount, 64), System.Runtime.InteropServices.Marshal.SizeOf(typeof(StarfieldNative.GlyphData)), ComputeBufferType.Default);
            }

            // Dispatch native compute shader to render text to texture
            // The glyph buffer is created/managed internally by the text system
            StarfieldNative.CR_TextDispatch(
                _textSystem,
                _textTexture.GetNativeTexturePtr(),
                glyphCount,
                1024,
                1024);
            
            // Set text texture for pixel shader sampling
            StarfieldNative.CR_SetTextTexture(_textTexture.GetNativeTexturePtr());
        }

        /// <summary>
        /// Load the JSON sidecar for the active catalog using simple parsing
        /// </summary>
        public void LoadJsonForCatalog(string binPath)
        {
            if (string.IsNullOrEmpty(binPath))
            {
                Debug.Log("[KartographerSelector] No active catalog path");
                return;
            }

            if (_jsonLoaded && _lastCatalogPath == binPath)
                return; // Already loaded

            // Check for _Custom.json first, fallback to .json
            string basePath = Path.ChangeExtension(binPath, null);  // Remove .bin
            string customPath = basePath + "_Custom.json";
            string defaultPath = basePath + ".json";

            string jsonPath = File.Exists(customPath) ? customPath : defaultPath;

            // Store which file we loaded for potential editor use
            _loadedJsonPath = jsonPath;
            _defaultJsonPath = defaultPath;
            _customJsonPath = customPath;

            if (!File.Exists(jsonPath))
            {
                Debug.Log($"[KartographerSelector] No JSON sidecar found: {jsonPath}");
                return;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);
                ParseJsonStars(json);
                
                _jsonLoaded = true;
                _lastCatalogPath = binPath;
                Debug.Log($"[KartographerSelector] Loaded {_namedStars.Count} named stars from {jsonPath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[KartographerSelector] Failed to load JSON: {ex.Message}");
            }
        }

        // ============================================================================
        // Grid Label Text (HUCK) Methods
        // ============================================================================

        /// <summary>
        /// Initialize the grid label texture (256x256)
        /// </summary>
        private void InitializeGridLabelTexture()
        {
            if (_gridLabelTexture != null)
                return;

            Debug.Log("[KartographerSelector] Creating grid label texture...");
            
            _gridLabelTexture = new RenderTexture(GRID_LABEL_TEXTURE_SIZE, GRID_LABEL_TEXTURE_SIZE, 0, RenderTextureFormat.ARGB32);
            _gridLabelTexture.enableRandomWrite = true;
            _gridLabelTexture.Create();
            _gridLabelDirty = true;
            
            Debug.Log($"[KartographerSelector] Grid label texture created: {GRID_LABEL_TEXTURE_SIZE}x{GRID_LABEL_TEXTURE_SIZE}, format=ARGB32, enableRandomWrite=True, IsCreated={_gridLabelTexture.IsCreated()}");
        }

        /// <summary>
        /// Build the HUCK grid label text
        /// For now using single font size (12px) - mixed sizes require text system changes
        /// </summary>
        private void BuildGridLabelTexture()
        {
            if (_textSystem == IntPtr.Zero)
            {
                InitializeTextSystem();
                if (_textSystem == IntPtr.Zero)
                    return;
            }

            if (_gridLabelTexture == null)
            {
                InitializeGridLabelTexture();
            }

            if (!_gridLabelDirty)
                return;

            _gridLabelDirty = false;

            Debug.Log("[KartographerSelector] Building grid label texture...");
            
            // Build multi-line text: "HOLOGRAPHIC\nUNIVERSAL\nCELESTIAL\nKARTOGRAPHER"
            string gridLabelText = "HOLOGRAPHIC\nUNIVERSAL\nCELESTIAL\nKARTOGRAPHER";
            uint color = 0xFFFFFFFF; // White ARGB

            // Layout and render text
            int glyphCount = StarfieldNative.CR_TextLayout(_textSystem, gridLabelText, GRID_LABEL_BASE_SIZE, color);
            
            Debug.Log($"[KartographerSelector] Grid label layout: {glyphCount} glyphs for text '{gridLabelText.Replace('\n', '|')}' at size {GRID_LABEL_BASE_SIZE}px");
            
            if (glyphCount <= 0)
            {
                Debug.LogWarning("[KartographerSelector] Grid label layout returned 0 glyphs");
                return;
            }

            // Clear texture
            RenderTexture.active = _gridLabelTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;
            Debug.Log("[KartographerSelector] Grid label texture cleared");

            // Render to texture
            IntPtr texturePtr = _gridLabelTexture.GetNativeTexturePtr();
            Debug.Log($"[KartographerSelector] Grid label texture native ptr: {texturePtr}");
            
            StarfieldNative.CR_TextDispatch(
                _textSystem,
                texturePtr,
                glyphCount,
                GRID_LABEL_TEXTURE_SIZE,
                GRID_LABEL_TEXTURE_SIZE);

            // Set the grid label texture for shader (slot 0 - legacy compatibility)
            StarfieldNative.CR_SetGridLabelTexture(0, texturePtr);
            Debug.Log($"[KartographerSelector] Grid label texture built and set to native. Texture: {GRID_LABEL_TEXTURE_SIZE}x{GRID_LABEL_TEXTURE_SIZE}, {glyphCount} glyphs");
        }

        /// <summary>
        /// Get the number of latitude lines (parallels) for current grid preset
        /// </summary>
        private int GetGridNumLat()
        {
            switch (StarfieldSettings.KartographerGridSize)
            {
                case 0: return 5;   // Jumbo
                case 1: return 8;   // Large
                case 2: return 10;  // Medium
                case 3: return 15;  // Small
                case 4: return 20;  // Tiny
                default: return 10; // Medium
            }
        }

        /// <summary>
        /// Calculate grid label tangent frame for tangent-plane projection
        /// Returns position, tangent, and bitangent vectors
        /// </summary>
        private void GetGridLabelTangentFrame(out Vector3 position, out Vector3 tangent, out Vector3 bitangent)
        {
            int numLat = GetGridNumLat();
            int numLong = GetGridNumLong();
            
            float phiStep = Mathf.PI / numLat;
            float thetaStep = 2.0f * Mathf.PI / numLong;

            // 1 cell up from south pole
            float phi = Mathf.PI - phiStep * 2.0f;
            
            // Align with meridian (use first meridian, offset to center in cell)
            float theta = -Mathf.PI + thetaStep * 0.5f;

            // Spherical to Cartesian (Y-up) - this is the normal (points outward from sphere center)
            float sinPhi = Mathf.Sin(phi);
            float x = sinPhi * Mathf.Cos(theta);
            float y = Mathf.Cos(phi);
            float z = sinPhi * Mathf.Sin(theta);
            Vector3 normal = new Vector3(x, y, z);

            // Calculate tangent frame at this point on the sphere
            // Tangent points along parallel (east/west direction on sphere)
            // This is dP/dtheta
            Vector3 unrotatedTangent = new Vector3(
                -sinPhi * Mathf.Sin(theta),
                0.0f,
                sinPhi * Mathf.Cos(theta)
            ).normalized;
            
            // Bitangent points toward north pole (up on the sphere surface)
            // This is dP/dphi
            Vector3 unrotatedBitangent = new Vector3(
                Mathf.Cos(phi) * Mathf.Cos(theta),
                -Mathf.Sin(phi),
                Mathf.Cos(phi) * Mathf.Sin(theta)
            ).normalized;

            // Apply grid rotation to position and frame
            normal = KartographerMath.ApplyCatalogRotation(normal, 0f,
                StarfieldSettings.KartographerRotationYaw,
                StarfieldSettings.KartographerRotationPitch);
            
            unrotatedTangent = KartographerMath.ApplyCatalogRotation(unrotatedTangent, 0f,
                StarfieldSettings.KartographerRotationYaw,
                StarfieldSettings.KartographerRotationPitch);
            
            unrotatedBitangent = KartographerMath.ApplyCatalogRotation(unrotatedBitangent, 0f,
                StarfieldSettings.KartographerRotationYaw,
                StarfieldSettings.KartographerRotationPitch);

            position = normal.normalized;
            
            // Re-orthonormalize after rotation
            tangent = unrotatedTangent.normalized;
            bitangent = Vector3.Cross(normal, tangent).normalized; // Ensure perpendicular
            tangent = Vector3.Cross(bitangent, normal).normalized; // Re-orthogonalize
        }

        /// <summary>
        /// Get number of longitude lines (meridians) for current grid preset
        /// </summary>
        private int GetGridNumLong()
        {
            switch (StarfieldSettings.KartographerGridSize)
            {
                case 0: return 8;   // Jumbo
                case 1: return 12;  // Large
                case 2: return 16;  // Medium
                case 3: return 24;  // Small
                case 4: return 32;  // Tiny
                default: return 16; // Medium
            }
        }

        /// <summary>
        /// Simple JSON parsing for star entries - extracts HIP ID and x,y,z
        /// </summary>
        private void ParseJsonStars(string json)
        {
            _namedStars.Clear();

            // Find the "stars" object in the JSON
            int starsStart = json.IndexOf("\"stars\":");
            if (starsStart < 0) return;

            int braceStart = json.IndexOf('{', starsStart);
            if (braceStart < 0) return;

            // Parse each star entry: "HIP_ID": { ... }
            int pos = braceStart + 1;
            int depth = 1;

            while (pos < json.Length && depth > 0)
            {
                // Find next quoted key (HIP ID)
                int quoteStart = json.IndexOf('"', pos);
                if (quoteStart < 0) break;

                int quoteEnd = json.IndexOf('"', quoteStart + 1);
                if (quoteEnd < 0) break;

                string hipIdStr = json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                if (!int.TryParse(hipIdStr, out int hipId))
                {
                    pos = quoteEnd + 1;
                    continue;
                }

                // Find the star object for this HIP ID
                int starBraceStart = json.IndexOf('{', quoteEnd);
                if (starBraceStart < 0) break;

                // Extract star properties
                int starBraceEnd = FindMatchingBrace(json, starBraceStart);
                if (starBraceEnd < 0) break;

                string starJson = json.Substring(starBraceStart, starBraceEnd - starBraceStart + 1);
                NamedStar star = ParseStarEntry(hipId, starJson);
                if (star != null)
                {
                    _namedStars[hipId] = star;
                }

                pos = starBraceEnd + 1;

                // Check for closing of stars object
                int nextChar = pos;
                while (nextChar < json.Length && char.IsWhiteSpace(json[nextChar])) nextChar++;
                if (nextChar < json.Length && json[nextChar] == '}')
                    break; // End of stars object
            }
        }

        /// <summary>
        /// Strips directional suffixes from full_designation for display purposes.
        /// "Epsilon Triangulum Australe" -> "EPSILON TRIANGULUM"
        /// "Asellus Borealis" -> "ASELLUS"
        /// </summary>
        public static string StripDirectionalSuffix(string fullDesignation)
        {
            if (string.IsNullOrEmpty(fullDesignation))
                return fullDesignation;
            
            string[] suffixes = new[] { " Australe", " Australis", " Borealis", " Posterior", " Prior" };
            string result = fullDesignation;
            
            foreach (var suffix in suffixes)
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length);
                    break;  // Only strip one suffix
                }
            }
            
            return result.ToUpper();
        }

        /// <summary>
        /// Find the matching closing brace for an opening brace at startIndex
        /// </summary>
        private int FindMatchingBrace(string json, int startIndex)
        {
            int depth = 1;
            int pos = startIndex + 1;
            bool inString = false;

            while (pos < json.Length && depth > 0)
            {
                char c = json[pos];
                if (c == '"' && (pos == 0 || json[pos - 1] != '\\'))
                {
                    inString = !inString;
                }
                else if (!inString)
                {
                    if (c == '{') depth++;
                    else if (c == '}') depth--;
                }
                pos++;
            }

            return depth == 0 ? pos - 1 : -1;
        }

        /// <summary>
        /// Parse individual star properties from JSON snippet
        /// </summary>
        private NamedStar ParseStarEntry(int hipId, string starJson)
        {
            string rawName = ExtractStringValue(starJson, "proper") ?? ExtractStringValue(starJson, "full_designation");
            var star = new NamedStar
            {
                HipparcosID = hipId,
                Name = StripDirectionalSuffix(rawName) ?? $"HIP {hipId}",
                SpectralType = ExtractStringValue(starJson, "spectral") ?? "?",
                Magnitude = ExtractFloatValue(starJson, "magnitude", 99f),
                DistanceLy = ExtractFloatValue(starJson, "distance_ly", 0f),
                Constellation = ExtractStringValue(starJson, "constellation") ?? "?"
            };

            // Extract direction vector
            float x = ExtractFloatValue(starJson, "x", 0f);
            float y = ExtractFloatValue(starJson, "y", 0f);
            float z = ExtractFloatValue(starJson, "z", 0f);
            star.Direction = new Vector3(x, y, z).normalized;

            // Only return if we got valid direction
            if (star.Direction.sqrMagnitude > 0.001f)
                return star;

            return null;
        }

        private string ExtractStringValue(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int keyPos = json.IndexOf(pattern);
            if (keyPos < 0) return null;

            int colonPos = json.IndexOf(':', keyPos);
            if (colonPos < 0) return null;

            int quoteStart = json.IndexOf('"', colonPos);
            if (quoteStart < 0) return null;

            int quoteEnd = json.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0) return null;

            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        private float ExtractFloatValue(string json, string key, float defaultVal)
        {
            string pattern = "\"" + key + "\"";
            int keyPos = json.IndexOf(pattern);
            if (keyPos < 0) return defaultVal;

            int colonPos = json.IndexOf(':', keyPos);
            if (colonPos < 0) return defaultVal;

            int commaPos = json.IndexOf(',', colonPos);
            int bracePos = json.IndexOf('}', colonPos);

            int endPos = commaPos > 0 && (bracePos < 0 || commaPos < bracePos) ? commaPos : bracePos;
            if (endPos < 0) endPos = json.Length;

            string valStr = json.Substring(colonPos + 1, endPos - colonPos - 1).Trim();
            if (float.TryParse(valStr, System.Globalization.NumberStyles.Float, 
                System.Globalization.CultureInfo.InvariantCulture, out float result))
            {
                return result;
            }

            return defaultVal;
        }

        /// <summary>
        /// Find and start tracking a specific star by HIP ID
        /// </summary>
        public void TrackStarByHipId(int hipId)
        {
            if (!_jsonLoaded)
            {
                Debug.Log("[KartographerSelector] Cannot track star - JSON not loaded");
                return;
            }

            if (_namedStars.TryGetValue(hipId, out var star))
            {
                TrackedStar = star;
                SelectionCircleEnabled = true;
                _textDirty = true; // Mark text for update
                Debug.Log($"[KartographerSelector] Now tracking: {star.Name} (HIP {hipId})");
            }
            else
            {
                Debug.LogWarning($"[KartographerSelector] Star HIP {hipId} not found in named stars");
            }
        }

        /// <summary>
        /// Enable/disable mouse hover selection mode
        /// </summary>
        public void SetMouseHoverMode(bool enabled)
        {
            _mouseHoverMode = enabled;
            Debug.Log($"[KartographerSelector] Mouse hover selection: {(enabled ? "ENABLED" : "DISABLED")}");
            if (!enabled)
            {
                // Clear hover state when disabling
                _hoveredStar = null;
                _hoveredStarUV = new Vector2(-1, -1);
            }
        }

        /// <summary>
        /// Select a star by HIP ID for display (as if clicked).
        /// Called from StarCatalogEditorWindow when user selects a star to edit.
        /// </summary>
        public void SelectStarByHipId(int hipId)
        {
            if (!_jsonLoaded || _namedStars.Count == 0)
            {
                Debug.LogWarning("[KartographerSelector] Cannot select star - JSON not loaded");
                return;
            }

            if (_namedStars.TryGetValue(hipId, out var star))
            {
                // Set as hovered (for display purposes)
                _hoveredStar = star;
                _hoveredStarUV = ProjectStarToUV(star);
                
                // Immediately lock it (as if clicked)
                _lockedStar = star;
                _starHash = star.HipparcosID * 0.123f;
                StartAnimationForStar(star);
                
                Debug.Log($"[KartographerSelector] Star selected via editor: {star.Name} (HIP {hipId})");
            }
            else
            {
                Debug.LogWarning($"[KartographerSelector] Cannot select - HIP {hipId} not found");
            }
        }

        /// <summary>
        /// Get the currently locked star (if any)
        /// </summary>
        public NamedStar GetLockedStar()
        {
            return _lockedStar;
        }

        /// <summary>
        /// Update projection and push to native plugin
        /// Handles hover/click selection for all named stars
        /// </summary>
        public void Update()
        {
            _frameCounter++;
            
            // Guard: Clear selection if Kartographer is disabled
            if (!StarfieldSettings.EnableKartographer)
            {
                if (_lockedStar != null || _hoveredStar != null)
                {
                    StopTracking();
                }
                else
                {
                    PushToNative(false);
                }
                return;
            }
            
            // Validate camera basis vectors are initialized (scene change resets them)
            if (CameraForward.sqrMagnitude < 0.5f)
            {
                PushToNative(false);
                return;
            }

            // Skip if no star data loaded
            if (!_jsonLoaded || _namedStars.Count == 0)
            {
                PushToNative(false);
                return;
            }

            // MOUSE HOVER MODE: Project stars and check mouse position
            if (_mouseHoverMode)
            {
                UpdateMouseHoverSelection();
            }
            else
            {
                PushToNative(false);
            }
        }

        /// <summary>
        /// Check if the mouse is currently over the mod UI window
        /// </summary>
        private bool IsMouseOverUI()
        {
            if (CinematicShadersWindow.Instance == null)
                return false;
            
            // Unity Input.mousePosition is bottom-left origin, GUI is top-left origin
            Vector2 mousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return CinematicShadersWindow.Instance.WindowRect.Contains(mousePos);
        }

        /// <summary>
        /// Update mouse hover selection logic
        /// </summary>
        private void UpdateMouseHoverSelection()
        {
            // Check if mouse is over UI (blocks new selection, but keeps locked star visible)
            bool mouseOverUI = IsMouseOverUI();

            // Get mouse position in UV space
            // NOTE: Unity's Input.mousePosition.y is bottom-up, screen UV is top-down
            Vector2 mouseUV = new Vector2(
                Input.mousePosition.x / Screen.width,
                1.0f - (Input.mousePosition.y / Screen.height)  // Flip Y
            );

            // Project all named stars and find nearest to mouse
            NamedStar nearestStar = null;
            Vector2 nearestUV = new Vector2(-1, -1);
            float nearestDist = float.MaxValue;
            
            // Threshold: ~0.02 UV units (~40px at 1080p, ~20px at 4K)
            const float HOVER_THRESHOLD = 0.02f;

            foreach (var star in _namedStars.Values)
            {
                Vector3 rotatedDir = KartographerMath.ApplyCatalogRotation(
                    star.Direction,
                    StarfieldSettings.RotationX,
                    StarfieldSettings.RotationY,
                    StarfieldSettings.RotationZ
                );

                Vector2 starUV = KartographerMath.WorldDirectionToScreenUV(
                    rotatedDir,
                    CameraRight,
                    CameraUp,
                    CameraForward,
                    AspectRatio,
                    VerticalFOV
                );

                // Skip stars behind camera
                if (starUV.x < 0)
                    continue;

                // Check if on screen (with margin)
                if (!KartographerMath.IsOnScreen(starUV, 0.1f))
                    continue;

                float dist = Vector2.Distance(mouseUV, starUV);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestStar = star;
                    nearestUV = starUV;
                }
            }

            // Handle hover state (blocked when mouse over UI - can't hover new stars)
            if (!mouseOverUI)
            {
                if (nearestDist < HOVER_THRESHOLD && nearestStar != null)
                {
                    if (_hoveredStar != nearestStar)
                    {
                        _hoveredStar = nearestStar;
                        _hoveredStarUV = nearestUV;
                        // Hover detected
                    }
                }
                else if (_hoveredStar != null)
                {
                    Debug.Log($"[KartographerSelector] HOVER CLEARED");
                    _hoveredStar = null;
                    _hoveredStarUV = new Vector2(-1, -1);
                }
            }

            // Handle mouse input (click to lock/unlock)
            bool isMouseDown = Input.GetMouseButton(0);
            bool mouseClicked = isMouseDown && !_wasMouseDown;
            _wasMouseDown = isMouseDown;

            // Block clicks when mouse is over UI (but still allow unlock via ESC)
            if (mouseClicked && !mouseOverUI)
            {
                if (_lockedStar != null)
                {
                    // Already locked - unlock
                    // Star unlocked
                    _lockedStar = null;
                    _lastLockedStarHIP = 0;  // Clear last locked star
                }
                else if (_hoveredStar != null)
                {
                    // Check for same-star reselection
                    if (_lastLockedStarHIP == _hoveredStar.HipparcosID && 
                        _animationPhase == SelectionAnimationPhase.Complete)
                    {
                        // Same star clicked again while complete - just re-lock without animation reset
                        _lockedStar = _hoveredStar;
                        Debug.Log($"[KartographerSelector] RE-LOCKED (stable): {_lockedStar.Name} (HIP {_lockedStar.HipparcosID})");
                    }
                    else
                    {
                        // Lock the hovered star and start animation
                        _lockedStar = _hoveredStar;
                        _starHash = _lockedStar.HipparcosID * 0.123f;  // Unique hash per star
                        StartAnimationForStar(_lockedStar);
                        // Star locked
                    }
                }
            }

            // Check for ESC to unlock
            if (_lockedStar != null && Input.GetKeyDown(KeyCode.Escape))
            {
                Debug.Log($"[KartographerSelector] UNLOCKED (ESC): {_lockedStar.Name}");
                _lockedStar = null;
                _lastLockedStarHIP = 0;  // Clear last locked star
            }

            // Update sequential animation phases
            UpdateAnimation();

            // Determine which star to display (locked takes priority)
            NamedStar displayStar = _lockedStar ?? _hoveredStar;
            Vector2 displayUV = _lockedStar != null ? 
                ProjectStarToUV(_lockedStar) : _hoveredStarUV;
            bool isHoverOnly = (_lockedStar == null && _hoveredStar != null);

            // Handle hover-only text (simple, no animation)
            if (isHoverOnly && displayStar != null)
            {
                // Hover mode: just show star name only, no animation
                _fullStarText = displayStar.Name.ToUpper();
                _currentDisplayText = _fullStarText;
                _textDirty = true;
            }

            // Update legacy tracking for compatibility with existing PushToNative
            if (displayStar != null)
            {
                TrackedStar = displayStar;
                TrackedStarScreenUV = displayUV;
                SelectionCircleEnabled = true;
                
                // Mark text dirty if star changed
                if (_textDirty || displayStar != _lastLoggedHover)
                {
                    _textDirty = true;
                    UpdateTextTexture();
                }
            }
            else
            {
                TrackedStar = null;
                TrackedStarScreenUV = new Vector2(-1, -1);
                SelectionCircleEnabled = false;
                // Clear animation state when nothing displayed
                _animationPhase = SelectionAnimationPhase.Complete;
                _lastLockedStarHIP = 0;
            }
            _lastLoggedHover = displayStar;

            // Push to native with hover vs locked intensity
            bool onScreen = KartographerMath.IsOnScreen(TrackedStarScreenUV);
            PushToNative(onScreen, isHoverOnly);
        }



        /// <summary>
        /// Convert mouse UV to world direction (inverse of projection)
        /// </summary>
        private Vector3 MouseUVToWorldDirection(Vector2 mouseUV)
        {
            // Convert UV to NDC
            float ndcX = (mouseUV.x - 0.5f) * 2.0f * AspectRatio;
            float ndcY = (mouseUV.y - 0.5f) * 2.0f;

            // Convert NDC to view direction
            float focalLength = 1.0f / Mathf.Tan(VerticalFOV * 0.5f);
            float vx = ndcX / focalLength;
            float vy = ndcY / focalLength;
            float vz = 1.0f;

            // ViewToWorld: world = v.x * right - v.y * up + v.z * forward
            Vector3 worldDir = vx * CameraRight - vy * CameraUp + vz * CameraForward;
            return worldDir.normalized;
        }

        /// <summary>
        /// Project a star to screen UV
        /// </summary>
        private Vector2 ProjectStarToUV(NamedStar star)
        {
            Vector3 rotatedDir = KartographerMath.ApplyCatalogRotation(
                star.Direction,
                StarfieldSettings.RotationX,
                StarfieldSettings.RotationY,
                StarfieldSettings.RotationZ
            );

            return KartographerMath.WorldDirectionToScreenUV(
                rotatedDir,
                CameraRight,
                CameraUp,
                CameraForward,
                AspectRatio,
                VerticalFOV
            );
        }

        /// <summary>
        /// Push selection circle and info box params to native plugin.
        /// Merges with cached state so grid settings are preserved.
        /// </summary>
        private void PushToNative(bool visible, bool isHoverOnly = false)
        {
            if (!StarfieldNative.IsLoaded)
                return;

            // Convert [0,1] screen UV to shader-uv space where center is (0,0) and Y is up.
            float u = TrackedStarScreenUV.x;
            float v = TrackedStarScreenUV.y;
            float centerX = (u - 0.5f) * 2.0f * AspectRatio;
            float centerY = (v - 0.5f) * 2.0f;

            float focalLength = VerticalFOV > 0.001f
                ? 1.0f / Mathf.Tan(VerticalFOV * 0.5f)
                : 1.732f;

            // Box positioned below and to the right of the selection circle.
            // In shader-uv: +X = right, +Y = down (input.uv.y=0 is top of screen).
            // So "below" means larger Y.
            float radius = 0.02f;
            float boxTopLeftX = centerX + radius + radius * 0.25f;
            float boxTopLeftY = centerY + radius + radius * 1.25f;

            // Merge with cached params so we don't stomp grid settings
            var kartParams = StarfieldNative.LastKartographerParams;
            kartParams.GridIntensity = StarfieldSettings.KartographerGridIntensity;
            kartParams.GridThickness = StarfieldSettings.KartographerGridThickness;
            kartParams.ChromaticAberrationStrength = StarfieldSettings.KartographerCAStrength;
            kartParams.VignetteStrength = StarfieldSettings.KartographerVignetteStrength;
            kartParams.VignetteStart = StarfieldSettings.KartographerVignetteStart;
            kartParams.VignetteEnd = StarfieldSettings.KartographerVignetteEnd;
            kartParams.PreRotationYaw = StarfieldSettings.KartographerRotationYaw;
            kartParams.PreRotationPitch = StarfieldSettings.KartographerRotationPitch;
            kartParams.GridSizePreset = StarfieldSettings.KartographerGridSize;
            kartParams.GridColorIndex = StarfieldSettings.KartographerGridColor;
            kartParams.DebugShapesEnabled = 0;
            kartParams.FocalLength = focalLength;
            // Calculate UV conversion for 1:1 pixel mapping (square pixels)
            // Shader UV space: X=[-aspect, aspect], Y=[-1, 1]
            // For 1:1 pixels: uvX * (Screen.height/2) = pixelCount (shader handles aspect internally)
            float screenHeight = Screen.height;
            float pixelsToUv = 2.0f / screenHeight;                      // same scale for X and Y
            
            // Box size: text bounds + padding on all sides
            float boxWidthUV = (_textWidthPixels + BOX_PADDING_PIXELS * 2) * pixelsToUv;
            float boxHeightUV = (_textHeightPixels + BOX_PADDING_PIXELS * 2) * pixelsToUv;
            
            // Minimum box size
            boxWidthUV = Mathf.Max(boxWidthUV, 0.08f);
            boxHeightUV = Mathf.Max(boxHeightUV, 0.06f);
            
            // Hover vs locked intensity and box visibility
            float intensity = isHoverOnly ? 0.001f : 0.002f;
            // Box only shows during/after Box phase (not during Circle flicker)
            // Hover never shows box, locked shows box only when animation phase >= Box
            bool showBox = !isHoverOnly && visible && _animationPhase >= SelectionAnimationPhase.Box;
            
            kartParams.DebugBoxTopLeftX = boxTopLeftX;
            kartParams.DebugBoxTopLeftY = boxTopLeftY;
            // Box size: 0 for hover (invisible), full size for locked
            kartParams.DebugBoxSizeX = showBox ? boxWidthUV : 0.0f;
            kartParams.DebugBoxSizeY = showBox ? boxHeightUV : 0.0f;
            kartParams.DebugBoxThickness = 0.001f;
            kartParams.SelectionCircleEnabled = visible ? 1 : 0;
            kartParams.SelectionCircleCenterX = centerX;
            kartParams.SelectionCircleCenterY = centerY;
            // Flicker T: hover = 1.0 (steady), locked = animated 0-1
            kartParams.SelectionCircleT = isHoverOnly ? 1.0f : _selectionFlickerT;
            kartParams.SelectionCircleIntensity = intensity;
            kartParams.SelectionCircleThickness = 0.001f;
            kartParams.SelectionCircleRadius = isHoverOnly ? 0.015f : 0.02f;
            kartParams.SelectionStarHash = _starHash;

            // Text params - TextAreaSize maps the ENTIRE 1024x1024 texture to shader UV
            // For 1:1 pixel mapping, use the texture dimensions (1024x1024), not the measured text bounds
            float textureSize = 1024f;
            float textWidthUV = textureSize * pixelsToUv;
            float textHeightUV = textureSize * pixelsToUv;
            
            // Left-align text at top of box with padding
            float textPaddingUV = 0.01f; // Small padding from edges
            float textOriginX = boxTopLeftX + textPaddingUV;
            float textOriginY = boxTopLeftY + textPaddingUV;
            
            kartParams.TextOriginX = textOriginX;
            kartParams.TextOriginY = textOriginY;
            // TextAreaSize matches actual text size - no stretching
            kartParams.TextAreaSizeX = textWidthUV;
            kartParams.TextAreaSizeY = textHeightUV;
            kartParams.SelectionTextT = 1.0f;

            // Save selection params to cache and send to native
            StarfieldNative.LastKartographerParams = kartParams;
            StarfieldNative.CR_StarfieldSetKartographerParams(ref kartParams);
        }
        

        /// <summary>
        /// Stop tracking and hide selection circle
        /// </summary>
        public void StopTracking()
        {
            TrackedStar = null;
            _lockedStar = null;
            SelectionCircleEnabled = false;
            _textDirty = true; // Clear text on next update
            _animationPhase = SelectionAnimationPhase.Complete;
            _lastLockedStarHIP = 0;
            _textTypeT = 0.0f;
            _selectionFlickerT = 1.0f;
            PushToNative(false);
        }

        /// <summary>
        /// Export font atlas to PGM file for debugging
        /// </summary>
        public void ExportFontAtlas()
        {
            if (_textSystem == IntPtr.Zero)
            {
                Debug.LogWarning("[KartographerSelector] Cannot export atlas - text system not initialized");
                return;
            }
            string path = Path.Combine(Path.GetTempPath(), "CinematicShaders_Atlas.pgm");
            StarfieldNative.CR_TextExportAtlas(_textSystem, path);
            Debug.Log($"[KartographerSelector] Font atlas exported to: {path}");
        }

        /// <summary>
        /// Export glyph debug files (raw, binary, SDF)
        /// </summary>
        public void ExportGlyphDebug()
        {
            if (_textSystem == IntPtr.Zero)
            {
                Debug.LogWarning("[KartographerSelector] Cannot export glyph debug - text system not initialized");
                return;
            }
            string basePath = Path.Combine(Path.GetTempPath(), "CinematicShaders_Glyph");
            StarfieldNative.CR_TextExportGlyphDebug(_textSystem, basePath);
            Debug.Log($"[KartographerSelector] Glyph debug exported to: {basePath}_*.pgm");
        }

        /// <summary>
        /// Export the rendered text texture to PNG for debugging
        /// </summary>
        public void ExportTextTexture()
        {
            if (_textTexture == null)
            {
                Debug.LogWarning("[KartographerSelector] Cannot export text texture - not initialized");
                return;
            }

            // Create a temporary Texture2D to read the RenderTexture
            RenderTexture.active = _textTexture;
            Texture2D tex = new Texture2D(_textTexture.width, _textTexture.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, _textTexture.width, _textTexture.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            // Encode to PNG and save
            byte[] pngData = tex.EncodeToPNG();
            string path = Path.Combine(Path.GetTempPath(), "CinematicShaders_TextTexture.png");
            File.WriteAllBytes(path, pngData);
            
            UnityEngine.Object.Destroy(tex);
            
            Debug.Log($"[KartographerSelector] Text texture exported to: {path}");
        }

        /// <summary>
        /// Export the grid label texture to PNG for debugging
        /// </summary>
        public void ExportGridLabelTexture()
        {
            // Auto-initialize if needed
            if (_textSystem == IntPtr.Zero)
            {
                InitializeTextSystem();
            }
            
            if (_gridLabelTexture == null)
            {
                InitializeGridLabelTexture();
            }
            
            // Force rebuild
            _gridLabelDirty = true;
            BuildGridLabelTexture();
            
            if (_gridLabelTexture == null)
            {
                Debug.LogError("[KartographerSelector] Export failed - texture is still null after initialization attempt");
                return;
            }

            // Create a temporary Texture2D to read the RenderTexture
            RenderTexture.active = _gridLabelTexture;
            Texture2D tex = new Texture2D(_gridLabelTexture.width, _gridLabelTexture.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, _gridLabelTexture.width, _gridLabelTexture.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            // Encode to PNG and save
            byte[] pngData = tex.EncodeToPNG();
            string path = Path.Combine(Path.GetTempPath(), "CinematicShaders_GridLabel.png");
            File.WriteAllBytes(path, pngData);
            
            UnityEngine.Object.Destroy(tex);
            
            Debug.Log($"[KartographerSelector] Grid label texture exported to: {path}");
            
            // Also dump current params using tangent frame
            Vector3 labelPos, labelTangent, labelBitangent;
            GetGridLabelTangentFrame(out labelPos, out labelTangent, out labelBitangent);
            
            int numLat = GetGridNumLat();
            float phiStep = Mathf.PI / numLat;
            float angularSize = phiStep * 0.8f;
            float worldSizeY = angularSize * 0.5f;
            float worldSizeX = worldSizeY * 1.5f;
            
            Debug.Log($"[KartographerSelector] Grid Label Debug State (Tangent Frame):");
            Debug.Log($"  Texture: {_gridLabelTexture.width}x{_gridLabelTexture.height}, IsCreated={_gridLabelTexture.IsCreated()}");
            Debug.Log($"  Position: ({labelPos.x:F4}, {labelPos.y:F4}, {labelPos.z:F4})");
            Debug.Log($"  Tangent: ({labelTangent.x:F4}, {labelTangent.y:F4}, {labelTangent.z:F4})");
            Debug.Log($"  Bitangent: ({labelBitangent.x:F4}, {labelBitangent.y:F4}, {labelBitangent.z:F4})");
            Debug.Log($"  World Size: ({worldSizeX:F4}, {worldSizeY:F4})");
            Debug.Log($"  Grid Preset: {StarfieldSettings.KartographerGridSize}, Rotation: Yaw={StarfieldSettings.KartographerRotationYaw:F2}, Pitch={StarfieldSettings.KartographerRotationPitch:F2}");
        }

        /// <summary>
        /// Cleanup resources
        /// </summary>
        public void Dispose()
        {
            if (_textSystem != IntPtr.Zero)
            {
                StarfieldNative.CR_TextShutdown(_textSystem);
                _textSystem = IntPtr.Zero;
            }

            if (_glyphBuffer != null)
            {
                _glyphBuffer.Release();
                _glyphBuffer = null;
            }

            if (_textTexture != null)
            {
                _textTexture.Release();
                UnityEngine.Object.Destroy(_textTexture);
                _textTexture = null;
            }

            if (_gridLabelTexture != null)
            {
                _gridLabelTexture.Release();
                UnityEngine.Object.Destroy(_gridLabelTexture);
                _gridLabelTexture = null;
            }
        }
    }
}
