using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// JSON file availability state for a catalog
    /// </summary>
    public enum JsonAvailability
    {
        None,           // No JSON files exist
        DefaultOnly,    // Only .json exists
        CustomOnly,     // Only _Custom.json exists
        Both            // Both .json and _Custom.json exist
    }

    /// <summary>
    /// JSON file paths for a catalog
    /// </summary>
    public struct JsonPaths
    {
        public string CatalogPath;      // Path to .bin file
        public string DefaultJsonPath;  // Path to .json file
        public string CustomJsonPath;   // Path to _Custom.json file
        
        public JsonAvailability GetAvailability()
        {
            bool hasDefault = !string.IsNullOrEmpty(DefaultJsonPath) && File.Exists(DefaultJsonPath);
            bool hasCustom = !string.IsNullOrEmpty(CustomJsonPath) && File.Exists(CustomJsonPath);
            
            if (hasCustom && hasDefault) return JsonAvailability.Both;
            if (hasCustom) return JsonAvailability.CustomOnly;
            if (hasDefault) return JsonAvailability.DefaultOnly;
            return JsonAvailability.None;
        }
        
        public string GetActiveJsonPath()
        {
            var availability = GetAvailability();
            switch (availability)
            {
                case JsonAvailability.CustomOnly:
                    return CustomJsonPath;
                case JsonAvailability.DefaultOnly:
                    return DefaultJsonPath;
                case JsonAvailability.Both:
                    return CustomJsonPath; // Prefer custom
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Event args for catalog change
    /// </summary>
    public class CatalogChangedEventArgs : EventArgs
    {
        public string OldCatalogPath { get; set; }
        public string NewCatalogPath { get; set; }
        public StarCatalogInfo OldCatalogInfo { get; set; }
        public StarCatalogInfo NewCatalogInfo { get; set; }
        public JsonPaths OldJsonPaths { get; set; }
        public JsonPaths NewJsonPaths { get; set; }
    }

    /// <summary>
    /// Event args for JSON state change
    /// </summary>
    public class JsonStateChangedEventArgs : EventArgs
    {
        public string CatalogPath { get; set; }
        public JsonPaths JsonPaths { get; set; }
        public JsonAvailability OldAvailability { get; set; }
        public JsonAvailability NewAvailability { get; set; }
        public string OldActiveJsonPath { get; set; }
        public string NewActiveJsonPath { get; set; }
    }

    /// <summary>
    /// Event args for star data change
    /// </summary>
    public class StarDataChangedEventArgs : EventArgs
    {
        public string CatalogPath { get; set; }
        public string JsonPath { get; set; }
        public int StarCount { get; set; }
        public bool IsReload { get; set; }
    }

    /// <summary>
    /// Centralized manager for catalog and JSON state.
    /// Provides events for state changes and ensures consistency.
    /// This is the single source of truth for JSON/catalog state.
    /// </summary>
    public static class StarCatalogStateManager
    {
        // ============================================================================
        // Private State
        // ============================================================================
        
        private static string _currentCatalogPath;
        private static StarCatalogInfo _currentCatalogInfo;
        private static JsonPaths _currentJsonPaths;
        private static Dictionary<int, NamedStar> _namedStars = new Dictionary<int, NamedStar>();
        private static bool _isInitialized = false;
        
        // ============================================================================
        // Public State Properties (Read-only)
        // ============================================================================
        
        /// <summary>
        /// Current catalog file path (absolute)
        /// </summary>
        public static string CurrentCatalogPath => _currentCatalogPath;
        
        /// <summary>
        /// Current catalog metadata
        /// </summary>
        public static StarCatalogInfo CurrentCatalogInfo => _currentCatalogInfo;
        
        /// <summary>
        /// JSON paths for current catalog
        /// </summary>
        public static JsonPaths CurrentJsonPaths => _currentJsonPaths;
        
        /// <summary>
        /// Currently active JSON file path (null if none)
        /// </summary>
        public static string ActiveJsonPath => _currentJsonPaths.GetActiveJsonPath();
        
        /// <summary>
        /// JSON availability state for current catalog
        /// </summary>
        public static JsonAvailability CurrentJsonAvailability => _currentJsonPaths.GetAvailability();
        
        /// <summary>
        /// Star data cache (loaded from JSON)
        /// </summary>
        public static IReadOnlyDictionary<int, NamedStar> NamedStars => _namedStars;
        
        /// <summary>
        /// True if manager has been initialized
        /// </summary>
        public static bool IsInitialized => _isInitialized;
        
        // ============================================================================
        // Events
        // ============================================================================
        
        /// <summary>
        /// Fired when catalog changes (different .bin file)
        /// </summary>
        public static event Action<CatalogChangedEventArgs> OnCatalogChanged;
        
        /// <summary>
        /// Fired when JSON state changes (created/deleted/modified)
        /// </summary>
        public static event Action<JsonStateChangedEventArgs> OnJsonStateChanged;
        
        /// <summary>
        /// Fired when star data changes (loaded from JSON)
        /// </summary>
        public static event Action<StarDataChangedEventArgs> OnStarDataChanged;
        
        /// <summary>
        /// Fired when any state changes (convenience event)
        /// </summary>
        public static event Action OnStateChanged;
        
        // ============================================================================
        // Public API
        // ============================================================================
        
        /// <summary>
        /// Initialize the manager with a catalog path
        /// </summary>
        public static void Initialize(string catalogPath)
        {
            ModFileLogger.Log($"[StarCatalogStateManager] Initialize - ENTER - catalogPath={catalogPath}, _isInitialized={_isInitialized}");
            if (_isInitialized)
            {
                ModFileLogger.LogWarning("[StarCatalogStateManager] Initialize - Already initialized, call SetCatalog() to change catalog");
                return;
            }
            
            SetCatalog(catalogPath);
            _isInitialized = true;
            ModFileLogger.Log("[StarCatalogStateManager] Initialize - EXIT");
        }
        
        /// <summary>
        /// Change the active catalog. Fires OnCatalogChanged, OnJsonStateChanged, OnStarDataChanged.
        /// </summary>
        public static void SetCatalog(string catalogPath)
        {
            ModFileLogger.Log($"[StarCatalogStateManager] SetCatalog - ENTER - catalogPath={catalogPath}");
            if (string.IsNullOrEmpty(catalogPath))
            {
                ModFileLogger.LogWarning("[StarCatalogStateManager] SetCatalog - ABORT: null/empty catalog path");
                return;
            }
            
            string absolutePath = Path.IsPathRooted(catalogPath) 
                ? catalogPath 
                : Path.Combine(KSPUtil.ApplicationRootPath, catalogPath);
            
            // Normalize path for comparison
            absolutePath = Path.GetFullPath(absolutePath);
            ModFileLogger.Log($"[StarCatalogStateManager] SetCatalog - absolutePath={absolutePath}");
            
            // Normalize both paths for comparison to prevent false positives
            string normalizedCurrent = string.IsNullOrEmpty(_currentCatalogPath) ? "" : Path.GetFullPath(_currentCatalogPath);
            ModFileLogger.Log($"[StarCatalogStateManager] SetCatalog - normalizedCurrent={normalizedCurrent}");
            
            // Check if actually changing
            if (normalizedCurrent == absolutePath)
            {
                ModFileLogger.Log($"[StarCatalogStateManager] SetCatalog - Catalog already set to: {absolutePath}, skipping");
                return;
            }
            
            // Store old state for event
            var oldCatalogPath = _currentCatalogPath;
            var oldCatalogInfo = _currentCatalogInfo;
            var oldJsonPaths = _currentJsonPaths;
            var oldAvailability = oldJsonPaths.GetAvailability();
            var oldActiveJsonPath = oldJsonPaths.GetActiveJsonPath();
            ModFileLogger.Log($"[StarCatalogStateManager] SetCatalog - Old state: path={oldCatalogPath}, availability={oldAvailability}");
            
            // Update catalog state
            _currentCatalogPath = absolutePath;
            _currentCatalogInfo = StarCatalogManager.ReadCatalogHeader(absolutePath);
            _currentJsonPaths = GetJsonPathsForCatalog(absolutePath);
            
            var newAvailability = _currentJsonPaths.GetAvailability();
            var newActiveJsonPath = _currentJsonPaths.GetActiveJsonPath();
            ModFileLogger.Log($"[StarCatalogStateManager] SetCatalog - New state: path={_currentCatalogPath}, availability={newAvailability}");
            
            // Fire catalog changed event
            ModFileLogger.Log("[StarCatalogStateManager] SetCatalog - Invoking OnCatalogChanged...");
            OnCatalogChanged?.Invoke(new CatalogChangedEventArgs
            {
                OldCatalogPath = oldCatalogPath,
                NewCatalogPath = _currentCatalogPath,
                OldCatalogInfo = oldCatalogInfo,
                NewCatalogInfo = _currentCatalogInfo,
                OldJsonPaths = oldJsonPaths,
                NewJsonPaths = _currentJsonPaths
            });
            ModFileLogger.Log("[StarCatalogStateManager] SetCatalog - OnCatalogChanged invoked");
            
            // Fire JSON state changed event if availability changed
            if (oldAvailability != newAvailability)
            {
                ModFileLogger.Log($"[StarCatalogStateManager] SetCatalog - Availability changed, invoking OnJsonStateChanged...");
                OnJsonStateChanged?.Invoke(new JsonStateChangedEventArgs
                {
                    CatalogPath = _currentCatalogPath,
                    JsonPaths = _currentJsonPaths,
                    OldAvailability = oldAvailability,
                    NewAvailability = newAvailability,
                    OldActiveJsonPath = oldActiveJsonPath,
                    NewActiveJsonPath = newActiveJsonPath
                });
                ModFileLogger.Log("[StarCatalogStateManager] SetCatalog - OnJsonStateChanged invoked");
            }
            else
            {
                ModFileLogger.Log("[StarCatalogStateManager] SetCatalog - Availability unchanged, skipping OnJsonStateChanged");
            }
            
            // Load star data from new JSON
            ModFileLogger.Log("[StarCatalogStateManager] SetCatalog - Calling LoadStarData()...");
            LoadStarData();
            ModFileLogger.Log("[StarCatalogStateManager] SetCatalog - LoadStarData() completed");
            
            // Fire general state changed
            ModFileLogger.Log("[StarCatalogStateManager] SetCatalog - Invoking OnStateChanged...");
            OnStateChanged?.Invoke();
            ModFileLogger.Log("[StarCatalogStateManager] SetCatalog - OnStateChanged invoked");
            ModFileLogger.Log("[StarCatalogStateManager] SetCatalog - EXIT");
        }
        
        /// <summary>
        /// Refresh JSON state (check if files exist, reload if needed)
        /// </summary>
        public static void RefreshJsonState()
        {
            ModFileLogger.Log("[StarCatalogStateManager] RefreshJsonState - ENTER");
            ModFileLogger.Log($"[StarCatalogStateManager] RefreshJsonState - _currentCatalogPath={_currentCatalogPath}, IsInitialized={IsInitialized}");
            
            if (string.IsNullOrEmpty(_currentCatalogPath))
            {
                ModFileLogger.LogWarning("[StarCatalogStateManager] RefreshJsonState - ABORT: _currentCatalogPath is null/empty");
                return;
            }
            
            var oldAvailability = _currentJsonPaths.GetAvailability();
            var oldActiveJsonPath = _currentJsonPaths.GetActiveJsonPath();
            ModFileLogger.Log($"[StarCatalogStateManager] RefreshJsonState - BEFORE: oldAvailability={oldAvailability}, oldActiveJsonPath={oldActiveJsonPath}");
            ModFileLogger.Log($"[StarCatalogStateManager] RefreshJsonState - Current paths: Default={_currentJsonPaths.DefaultJsonPath}, Custom={_currentJsonPaths.CustomJsonPath}");
            
            // Re-check file existence
            _currentJsonPaths = GetJsonPathsForCatalog(_currentCatalogPath);
            
            var newAvailability = _currentJsonPaths.GetAvailability();
            var newActiveJsonPath = _currentJsonPaths.GetActiveJsonPath();
            ModFileLogger.Log($"[StarCatalogStateManager] RefreshJsonState - AFTER: newAvailability={newAvailability}, newActiveJsonPath={newActiveJsonPath}");
            
            bool stateChanged = oldAvailability != newAvailability || oldActiveJsonPath != newActiveJsonPath;
            ModFileLogger.Log($"[StarCatalogStateManager] RefreshJsonState - stateChanged={stateChanged}");
            
            if (stateChanged)
            {
                ModFileLogger.Log($"[StarCatalogStateManager] RefreshJsonState - Invoking OnJsonStateChanged event...");
                
                OnJsonStateChanged?.Invoke(new JsonStateChangedEventArgs
                {
                    CatalogPath = _currentCatalogPath,
                    JsonPaths = _currentJsonPaths,
                    OldAvailability = oldAvailability,
                    NewAvailability = newAvailability,
                    OldActiveJsonPath = oldActiveJsonPath,
                    NewActiveJsonPath = newActiveJsonPath
                });
                ModFileLogger.Log("[StarCatalogStateManager] RefreshJsonState - OnJsonStateChanged event invoked");
                
                // Reload star data if active JSON changed
                if (oldActiveJsonPath != newActiveJsonPath)
                {
                    ModFileLogger.Log("[StarCatalogStateManager] RefreshJsonState - Active JSON changed, calling LoadStarData()...");
                    LoadStarData();
                    ModFileLogger.Log("[StarCatalogStateManager] RefreshJsonState - LoadStarData() completed");
                }
                
                ModFileLogger.Log("[StarCatalogStateManager] RefreshJsonState - Invoking OnStateChanged event...");
                OnStateChanged?.Invoke();
                ModFileLogger.Log("[StarCatalogStateManager] RefreshJsonState - OnStateChanged event invoked");
            }
            else
            {
                ModFileLogger.Log("[StarCatalogStateManager] RefreshJsonState - No state change, skipping events");
            }
            ModFileLogger.Log("[StarCatalogStateManager] RefreshJsonState - EXIT");
        }
        
        /// <summary>
        /// Force reload star data from current JSON
        /// </summary>
        public static void ReloadStarData()
        {
            LoadStarData();
        }
        
        /// <summary>
        /// Get JSON paths for a given catalog path
        /// </summary>
        public static JsonPaths GetJsonPathsForCatalog(string catalogPath)
        {
            if (string.IsNullOrEmpty(catalogPath))
                return new JsonPaths();
            
            string absolutePath = Path.IsPathRooted(catalogPath) 
                ? catalogPath 
                : Path.Combine(KSPUtil.ApplicationRootPath, catalogPath);
            
            string basePath = Path.ChangeExtension(absolutePath, null);
            
            return new JsonPaths
            {
                CatalogPath = absolutePath,
                CustomJsonPath = basePath + "_Custom.json",
                DefaultJsonPath = basePath + ".json"
            };
        }
        
        /// <summary>
        /// Check if current catalog has valid JSON
        /// </summary>
        public static bool HasValidJson()
        {
            return _currentJsonPaths.GetAvailability() != JsonAvailability.None;
        }
        
        // ============================================================================
        // Private Methods
        // ============================================================================
        
        private static void LoadStarData()
        {
            string jsonPath = _currentJsonPaths.GetActiveJsonPath();
            
            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            {
                Debug.Log($"[StarCatalogStateManager] No valid JSON to load for: {_currentCatalogPath}");
                _namedStars.Clear();
                
                OnStarDataChanged?.Invoke(new StarDataChangedEventArgs
                {
                    CatalogPath = _currentCatalogPath,
                    JsonPath = null,
                    StarCount = 0,
                    IsReload = false
                });
                
                return;
            }
            
            try
            {
                string json = File.ReadAllText(jsonPath);
                var newStars = ParseJsonStars(json);
                
                _namedStars.Clear();
                foreach (var star in newStars)
                {
                    _namedStars[star.HipparcosID] = star;
                }
                
                Debug.Log($"[StarCatalogStateManager] Loaded {_namedStars.Count} stars from: {jsonPath}");
                
                OnStarDataChanged?.Invoke(new StarDataChangedEventArgs
                {
                    CatalogPath = _currentCatalogPath,
                    JsonPath = jsonPath,
                    StarCount = _namedStars.Count,
                    IsReload = false
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StarCatalogStateManager] Failed to load JSON: {ex.Message}");
                _namedStars.Clear();
                
                OnStarDataChanged?.Invoke(new StarDataChangedEventArgs
                {
                    CatalogPath = _currentCatalogPath,
                    JsonPath = jsonPath,
                    StarCount = 0,
                    IsReload = false
                });
            }
        }
        
        private static List<NamedStar> ParseJsonStars(string json)
        {
            var stars = new List<NamedStar>();
            
            // Simple JSON parsing (same pattern as existing code)
            int starsStart = json.IndexOf("\"stars\":");
            if (starsStart < 0) return stars;
            
            int braceStart = json.IndexOf('{', starsStart);
            if (braceStart < 0) return stars;
            
            int pos = braceStart + 1;
            int depth = 1;
            
            while (pos < json.Length && depth > 0)
            {
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
                
                int starBraceStart = json.IndexOf('{', quoteEnd);
                if (starBraceStart < 0) break;
                
                int starBraceEnd = FindMatchingBrace(json, starBraceStart);
                if (starBraceEnd < 0) break;
                
                string starJson = json.Substring(starBraceStart, starBraceEnd - starBraceStart + 1);
                NamedStar star = ParseStarEntry(hipId, starJson);
                if (star != null)
                {
                    stars.Add(star);
                }
                
                pos = starBraceEnd + 1;
                
                int nextChar = pos;
                while (nextChar < json.Length && char.IsWhiteSpace(json[nextChar])) nextChar++;
                if (nextChar < json.Length && json[nextChar] == '}')
                    break;
            }
            
            return stars;
        }
        
        private static int FindMatchingBrace(string json, int startIndex)
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
        
        private static NamedStar ParseStarEntry(int hipId, string starJson)
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
            
            float x = ExtractFloatValue(starJson, "x", 0f);
            float y = ExtractFloatValue(starJson, "y", 0f);
            float z = ExtractFloatValue(starJson, "z", 0f);
            star.Direction = new Vector3(x, y, z).normalized;
            
            if (star.Direction.sqrMagnitude > 0.001f)
                return star;
            
            return null;
        }
        
        private static string StripDirectionalSuffix(string fullDesignation)
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
                    break;
                }
            }
            
            return result.ToUpper();
        }
        
        private static string ExtractStringValue(string json, string key)
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
        
        private static float ExtractFloatValue(string json, string key, float defaultVal)
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
    }
}
