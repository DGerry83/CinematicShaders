using CinematicShaders.Native;
using System.Collections.Generic;
using System.IO;
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
        public Vector3 Direction;
    }

    /// <summary>
    /// Tracks selected/hovered stars and projects them to screen space for HUD rendering.
    /// Phase 2/3 hybrid: JSON loading + projection math with visual debug via selection circle.
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
                Magnitude = ExtractFloatValue(starJson, "magnitude", 99f)
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

            // Push to native if on screen
            bool onScreen = KartographerMath.IsOnScreen(TrackedStarScreenUV);
            PushToNative(onScreen);
        }

        /// <summary>
        /// Push selection circle params to native plugin
        /// </summary>
        private void PushToNative(bool visible)
        {
            if (!StarfieldNative.IsLoaded)
                return;

            // Convert [0,1] screen UV to shader-uv space where center is (0,0)
            float u = TrackedStarScreenUV.x;
            float v = TrackedStarScreenUV.y;
            float centerX = (u - 0.5f) * 2.0f * AspectRatio;
            float centerY = (v - 0.5f) * 2.0f;

            float focalLength = VerticalFOV > 0.001f
                ? 1.0f / Mathf.Tan(VerticalFOV * 0.5f)
                : 1.732f;

            // Get current params first
            var kartParams = new StarfieldNative.KartographerParamsNative
            {
                GridIntensity = StarfieldSettings.KartographerGridIntensity,
                GridThickness = StarfieldSettings.KartographerGridThickness,
                ChromaticAberrationStrength = StarfieldSettings.KartographerCAStrength,
                VignetteStrength = StarfieldSettings.KartographerVignetteStrength,
                VignetteStart = StarfieldSettings.KartographerVignetteStart,
                VignetteEnd = StarfieldSettings.KartographerVignetteEnd,
                PreRotationYaw = StarfieldSettings.KartographerRotationYaw,
                PreRotationPitch = StarfieldSettings.KartographerRotationPitch,
                GridSizePreset = StarfieldSettings.KartographerGridSize,
                GridColorIndex = StarfieldSettings.KartographerGridColor,
                DebugShapesEnabled = 0,
                FocalLength = focalLength,
                SelectionCircleEnabled = visible ? 1 : 0,
                SelectionCircleCenterX = centerX,
                SelectionCircleCenterY = centerY,
                SelectionCircleT = 1.0f, // Steady (no flicker for now)
                SelectionCircleIntensity = 0.002f,
                SelectionCircleThickness = 0.001f,
                SelectionCircleRadius = 0.03f
            };

            StarfieldNative.CR_StarfieldSetKartographerParams(ref kartParams);
        }

        /// <summary>
        /// Stop tracking and hide selection circle
        /// </summary>
        public void StopTracking()
        {
            TrackedStar = null;
            SelectionCircleEnabled = false;
            PushToNative(false);
        }
    }
}
