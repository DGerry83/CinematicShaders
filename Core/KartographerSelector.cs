using CinematicShaders.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CinematicShaders.Core
{
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
        /// Update text texture when tracked star changes
        /// </summary>
        private void UpdateTextTexture()
        {
            if (_textSystem == IntPtr.Zero)
            {
                InitializeTextSystem();
                if (_textSystem == IntPtr.Zero)
                    return;
            }

            // Build text for current star
            string text = BuildStarText(TrackedStar);
            
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
            float screenAspectLog = (float)Screen.width / Screen.height;
            float p2uX = (2.0f * screenAspectLog) / Screen.width;
            float p2uY = 2.0f / Screen.height;
            Debug.Log($"[KartographerSelector] TEXT DEBUG:");
            Debug.Log($"  Content: {text.Replace('\n', '|')}");
            Debug.Log($"  Longest line ({maxLineLen} chars): '{longestLine}'");
            Debug.Log($"  Measured: {measuredWidth:F1} x {measuredHeight:F1}px");
            Debug.Log($"  Bounds: {boundsWidth:F1} x {boundsHeight:F1}px");
            Debug.Log($"  Using: {_textWidthPixels:F1} x {_textHeightPixels:F1}px");
            Debug.Log($"  Screen: {Screen.width}x{Screen.height}, pixelsToUv=({p2uX:F6}, {p2uY:F6})");

            // DEBUG: Export atlas to file
            string atlasPath = Path.Combine(Path.GetTempPath(), "CinematicShaders_Atlas.pgm");
            StarfieldNative.CR_TextExportAtlas(_textSystem, atlasPath);
            Debug.Log($"[KartographerSelector] Atlas exported to: {atlasPath}");

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
            
            Debug.Log($"[KartographerSelector] Text rendered: {glyphCount} glyphs for '{text.Replace('\n', '|')}'");
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

            string jsonPath = Path.ChangeExtension(binPath, ".json");
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
            var star = new NamedStar
            {
                HipparcosID = hipId,
                Name = ExtractStringValue(starJson, "proper") ?? ExtractStringValue(starJson, "full_designation") ?? $"HIP {hipId}",
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
        /// Update projection and push to native plugin
        /// Call this from StarfieldCompositor.Update() or similar
        /// </summary>
        public void Update()
        {
            if (!SelectionCircleEnabled || TrackedStar == null)
            {
                TrackedStarScreenUV = new Vector2(-1, -1);
                PushToNative(false);
                return;
            }

            // Validate camera basis vectors are initialized (scene change resets them)
            // CameraForward.sqrMagnitude > 0.5f ensures valid camera data is present
            if (CameraForward.sqrMagnitude < 0.5f)
            {
                // Camera not ready yet, hide the tracking UI until it is
                PushToNative(false);
                return;
            }

            // Apply catalog rotation to star direction (HYG catalogs are rotated to match game coords)
            Vector3 rotatedDir = KartographerMath.ApplyCatalogRotation(
                TrackedStar.Direction,
                StarfieldSettings.RotationX,
                StarfieldSettings.RotationY,
                StarfieldSettings.RotationZ
            );

            // Project to screen space
            TrackedStarScreenUV = KartographerMath.WorldDirectionToScreenUV(
                rotatedDir,
                CameraRight,
                CameraUp,
                CameraForward,
                AspectRatio,
                VerticalFOV
            );

            // Update text texture if needed
            if (_textDirty || TrackedStar != null)
            {
                UpdateTextTexture();
            }

            // Push to native if on screen
            bool onScreen = KartographerMath.IsOnScreen(TrackedStarScreenUV);
            PushToNative(onScreen);
        }

        /// <summary>
        /// Push selection circle and info box params to native plugin.
        /// Merges with cached state so grid settings are preserved.
        /// </summary>
        private void PushToNative(bool visible)
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
            
            kartParams.DebugBoxTopLeftX = boxTopLeftX;
            kartParams.DebugBoxTopLeftY = boxTopLeftY;
            kartParams.DebugBoxSizeX = boxWidthUV;
            kartParams.DebugBoxSizeY = boxHeightUV;
            kartParams.DebugBoxThickness = 0.001f;
            kartParams.SelectionCircleEnabled = visible ? 1 : 0;
            kartParams.SelectionCircleCenterX = centerX;
            kartParams.SelectionCircleCenterY = centerY;
            kartParams.SelectionCircleT = 1.0f; // Steady (no flicker for now)
            kartParams.SelectionCircleIntensity = 0.002f;
            kartParams.SelectionCircleThickness = 0.001f;
            kartParams.SelectionCircleRadius = 0.02f;

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

            StarfieldNative.LastKartographerParams = kartParams;
            StarfieldNative.CR_StarfieldSetKartographerParams(ref kartParams);
        }

        /// <summary>
        /// Stop tracking and hide selection circle
        /// </summary>
        public void StopTracking()
        {
            TrackedStar = null;
            SelectionCircleEnabled = false;
            _textDirty = true; // Clear text on next update
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
        }
    }
}
