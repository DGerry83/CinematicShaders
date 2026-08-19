using System;
using System.Collections.Generic;
using System.IO;
using CinematicShaders.UI;
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
        private static JsonAvailability _cachedAvailability = JsonAvailability.None; // Cache for detecting state changes
        
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
            if (_isInitialized)
            {
                Debug.LogWarning("[StarCatalogStateManager] Already initialized, call SetCatalog() to change catalog");
                return;
            }
            
            SetCatalog(catalogPath);
            _isInitialized = true;
        }
        
        /// <summary>
        /// Change the active catalog. Fires OnCatalogChanged, OnJsonStateChanged, OnStarDataChanged.
        /// </summary>
        public static void SetCatalog(string catalogPath)
        {
            if (string.IsNullOrEmpty(catalogPath))
            {
                Debug.LogWarning("[StarCatalogStateManager] Cannot set null/empty catalog path");
                return;
            }
            
            string absolutePath = Path.IsPathRooted(catalogPath) 
                ? catalogPath 
                : Path.Combine(KSPUtil.ApplicationRootPath, catalogPath);
            
            // Normalize path for comparison
            absolutePath = Path.GetFullPath(absolutePath);
            
            // Normalize both paths for comparison to prevent false positives
            string normalizedCurrent = string.IsNullOrEmpty(_currentCatalogPath) ? "" : Path.GetFullPath(_currentCatalogPath);
            
            // Check if actually changing
            if (normalizedCurrent == absolutePath)
            {
                Debug.Log($"[StarCatalogStateManager] Catalog already set to: {absolutePath}");
                return;
            }
            
            // Store old state for event
            var oldCatalogPath = _currentCatalogPath;
            var oldCatalogInfo = _currentCatalogInfo;
            var oldJsonPaths = _currentJsonPaths;
            var oldAvailability = oldJsonPaths.GetAvailability();
            var oldActiveJsonPath = oldJsonPaths.GetActiveJsonPath();
            
            // Update catalog state
            _currentCatalogPath = absolutePath;
            _currentCatalogInfo = StarCatalogManager.ReadCatalogHeader(absolutePath);
            _currentJsonPaths = GetJsonPathsForCatalog(absolutePath);
            
            var newAvailability = _currentJsonPaths.GetAvailability();
            var newActiveJsonPath = _currentJsonPaths.GetActiveJsonPath();
            
            // Fire catalog changed event
            OnCatalogChanged?.Invoke(new CatalogChangedEventArgs
            {
                OldCatalogPath = oldCatalogPath,
                NewCatalogPath = _currentCatalogPath,
                OldCatalogInfo = oldCatalogInfo,
                NewCatalogInfo = _currentCatalogInfo,
                OldJsonPaths = oldJsonPaths,
                NewJsonPaths = _currentJsonPaths
            });
            
            // Fire JSON state changed event if availability changed
            if (oldAvailability != newAvailability)
            {
                OnJsonStateChanged?.Invoke(new JsonStateChangedEventArgs
                {
                    CatalogPath = _currentCatalogPath,
                    JsonPaths = _currentJsonPaths,
                    OldAvailability = oldAvailability,
                    NewAvailability = newAvailability,
                    OldActiveJsonPath = oldActiveJsonPath,
                    NewActiveJsonPath = newActiveJsonPath
                });
            }
            
            // Load star data from new JSON
            LoadStarData();
            
            // Fire general state changed
            OnStateChanged?.Invoke();
            
            // Update cache to reflect new state
            _cachedAvailability = newAvailability;
        }
        
        /// <summary>
        /// Refresh JSON state (check if files exist, reload if needed)
        /// </summary>
        public static void RefreshJsonState()
        {
            if (string.IsNullOrEmpty(_currentCatalogPath))
            {
                Debug.LogWarning("[StarCatalogStateManager] Cannot refresh - no catalog set");
                return;
            }
            
            // Use cached availability as the "before" state - this allows detection of changes
            // that happened between calls (e.g., file created by external operation like scan)
            var oldAvailability = _cachedAvailability;
            var oldActiveJsonPath = _currentJsonPaths.GetActiveJsonPath();
            
            // Re-check file existence
            _currentJsonPaths = GetJsonPathsForCatalog(_currentCatalogPath);
            
            var newAvailability = _currentJsonPaths.GetAvailability();
            var newActiveJsonPath = _currentJsonPaths.GetActiveJsonPath();
            
            bool stateChanged = oldAvailability != newAvailability || oldActiveJsonPath != newActiveJsonPath;
            
            if (stateChanged)
            {
                Debug.Log($"[StarCatalogStateManager] JSON state changed: {oldAvailability} -> {newAvailability}");
                
                OnJsonStateChanged?.Invoke(new JsonStateChangedEventArgs
                {
                    CatalogPath = _currentCatalogPath,
                    JsonPaths = _currentJsonPaths,
                    OldAvailability = oldAvailability,
                    NewAvailability = newAvailability,
                    OldActiveJsonPath = oldActiveJsonPath,
                    NewActiveJsonPath = newActiveJsonPath
                });
                
                // Reload star data if active JSON changed
                if (oldActiveJsonPath != newActiveJsonPath)
                {
                    LoadStarData();
                }
                
                OnStateChanged?.Invoke();
                
                // Update cache to reflect new state
                _cachedAvailability = newAvailability;
            }
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
            
            try
            {
                var root = Json.Deserialize(json) as Dictionary<string, object>;
                if (root == null) return stars;
                
                if (!root.TryGetValue("stars", out object starsObj) || !(starsObj is Dictionary<string, object> starDict))
                    return stars;
                
                foreach (var kvp in starDict)
                {
                    if (!int.TryParse(kvp.Key, out int hipId)) continue;
                    if (!(kvp.Value is Dictionary<string, object> starData)) continue;
                    
                    NamedStar star = ParseStarEntry(hipId, starData);
                    if (star != null)
                    {
                        stars.Add(star);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StarCatalogStateManager] Failed to parse JSON stars: {ex.Message}");
            }
            
            return stars;
        }
        
        private static NamedStar ParseStarEntry(int hipId, Dictionary<string, object> starData)
        {
            string rawName = null;
            if (starData.TryGetValue("proper", out object properObj) && properObj is string properStr)
                rawName = properStr;
            else if (starData.TryGetValue("full_designation", out object desigObj) && desigObj is string desigStr)
                rawName = desigStr;
            
            var star = new NamedStar
            {
                HipparcosID = hipId,
                Name = StripDirectionalSuffix(rawName) ?? string.Format(CinematicShadersUIStrings.Kartographer.HipIdFormat, hipId),
                SpectralType = starData.TryGetValue("spectral", out object spectralObj) && spectralObj is string spectralStr ? spectralStr : CinematicShadersUIStrings.Common.UnknownValueSentinel,
                Magnitude = GetFloat(starData, "magnitude", 99f),
                DistanceLy = GetFloat(starData, "distance_ly", 0f),
                Constellation = starData.TryGetValue("constellation", out object constObj) && constObj is string constStr ? constStr : CinematicShadersUIStrings.Common.UnknownValueSentinel
            };
            
            float x = GetFloat(starData, "x", 0f);
            float y = GetFloat(starData, "y", 0f);
            float z = GetFloat(starData, "z", 0f);
            star.Direction = new Vector3(x, y, z).normalized;
            
            if (star.Direction.sqrMagnitude > 0.001f)
                return star;
            
            return null;
        }
        
        private static float GetFloat(Dictionary<string, object> data, string key, float defaultVal)
        {
            if (!data.TryGetValue(key, out object val))
                return defaultVal;
            
            if (val is float f) return f;
            if (val is double d) return (float)d;
            if (val is long l) return l;
            if (val is int i) return i;
            
            if (float.TryParse(val.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result))
            {
                return result;
            }
            
            return defaultVal;
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
    }
}
