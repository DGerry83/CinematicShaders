using CinematicShaders.Core;
using CinematicShaders.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CinematicShaders.UI
{
    /// <summary>
    /// Star Catalog Editor window for editing star names.
    /// Allows searching stars and editing their "proper" names in _Custom.json
    /// </summary>
    public class StarCatalogEditorWindow : MonoBehaviour
    {
        #region Constants
        private const int WINDOW_ID = 98766;  // Unique ID
        private const float WINDOW_WIDTH = 350f;
        private const float WINDOW_HEIGHT = 500f;
        private const float SEARCH_HEIGHT = 25f;
        private const float BUTTON_HEIGHT = 25f;
        private const float MARGIN = 10f;
        #endregion

        #region State
        private bool _isVisible = false;
        private bool _stylesInitialized = false;
        private bool _positionInitialized = false;
        private Rect _windowRect = new Rect(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT);
        
        // Search state
        private string _searchText = "";
        private List<NamedStar> _allStars = new List<NamedStar>();
        private List<NamedStar> _filteredStars = new List<NamedStar>();
        private Vector2 _scrollPosition = Vector2.zero;
        
        // Selection state
        private NamedStar _selectedStar = null;
        private string _editNameText = "";
        private string _originalName = "";
        
        // JSON cache
        private Dictionary<int, string> _starJsonSnippets = new Dictionary<int, string>();
        
        // Reference to selector (passed from KartographerTab)
        private KartographerSelector _selector;
        
        // Scan button state
        private bool _hasCheckedForJson = false;
        private bool _jsonExists = false;
        #endregion

        #region Initialization
        public void Initialize(List<NamedStar> stars, KartographerSelector selector, NamedStar preselectedStar = null)
        {
            _allStars = stars.OrderBy(s => s.Name).ToList();
            _filteredStars = new List<NamedStar>(_allStars);
            _selector = selector;
            
            // Subscribe to external selection events (when user clicks star in game world)
            if (_selector != null)
            {
                _selector.OnStarLockedViaClick = OnExternalStarSelected;
            }
            
            // If there's a preselected star (from catalog), select it in the editor
            if (preselectedStar != null)
            {
                SelectStar(preselectedStar);
            }
            
            InitStyles();
        }
        
        /// <summary>
        /// Called when user selects a star via point-and-click in the game world
        /// </summary>
        private void OnExternalStarSelected(NamedStar star)
        {
            if (star == null) return;
            
            // Update our selection to match the externally selected star
            // Don't call _selector.SelectStarByHipId here (would be circular)
            _selectedStar = star;
            _originalName = star.Name;
            _editNameText = star.Name;
            
            Debug.Log($"[StarCatalogEditor] External selection synced: {star.Name} (HIP {star.HipparcosID})");
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            // Cache styles here if needed
            _stylesInitialized = true;
        }
        #endregion

        #region Unity Lifecycle
        private void OnGUI()
        {
            if (!_isVisible) return;
            
            _windowRect = GUILayout.Window(
                WINDOW_ID,
                _windowRect,
                DrawWindow,
                CinematicShadersUIStrings.Kartographer.StarCatalogEditorTitle,
                HighLogic.Skin.window
            );
        }
        #endregion

        #region Position Management
        private void InitializePosition()
        {
            if (_positionInitialized) return;
            if (CinematicShadersWindow.Instance == null) return;
            
            Rect mainRect = CinematicShadersWindow.Instance.WindowRect;
            _windowRect.x = mainRect.x + mainRect.width + 5f;
            _windowRect.y = mainRect.y;
            _positionInitialized = true;
        }
        #endregion

        #region Window Layout
        private void DrawWindow(int id)
        {
            DrawCloseButton();
            GUILayout.Space(5);
            
            DrawScanButton();
            
            DrawSearchBox();
            GUILayout.Space(5);
            
            DrawStarList();
            GUILayout.Space(5);
            
            DrawEditorSection();
            
            // Make window draggable
            GUI.DragWindow();
        }

        private void DrawScanButton()
        {
            // Check if JSON exists (cache result to avoid file check every frame)
            if (!_hasCheckedForJson)
            {
                string customPath = GetCustomJsonPath();
                string defaultPath = GetDefaultJsonPath();
                _jsonExists = File.Exists(customPath) || File.Exists(defaultPath);
                _hasCheckedForJson = true;
            }
            
            // Check if catalog is procedural - only show scan for procedural catalogs
            if (!IsCurrentCatalogProcedural()) return;
            
            // Show Scan button (always available for procedural catalogs)
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button(CinematicShadersUIStrings.Kartographer.ScanButton, 
                GUILayout.Height(30), GUILayout.Width(100)))
            {
                ScanCatalog();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUILayout.Space(5);
            
            // Show appropriate help text based on whether JSON exists
            string helpText = _jsonExists 
                ? CinematicShadersUIStrings.Kartographer.ScanOverwriteHelpText
                : CinematicShadersUIStrings.Kartographer.ScanHelpText;
            GUILayout.Label(helpText, CinematicShadersUIResources.Styles.Help());
            GUILayout.Space(10);
        }

        private bool IsCurrentCatalogProcedural()
        {
            // Get the current catalog path from settings
            string catalogPath = StarfieldSettings.ActiveCatalogPath;
            if (string.IsNullOrEmpty(catalogPath)) return false;
            
            // Convert to absolute path
            string absolutePath = Path.Combine(KSPUtil.ApplicationRootPath, catalogPath);
            if (!File.Exists(absolutePath)) return false;
            
            // Use StarCatalogManager to read header and check IsProcedural flag
            try
            {
                var info = StarCatalogManager.ReadCatalogHeader(absolutePath);
                return info != null && info.IsProcedural;
            }
            catch
            {
                return false;
            }
        }

        private void ScanCatalog()
        {
            try
            {
                string catalogPath = StarfieldSettings.ActiveCatalogPath;
                if (string.IsNullOrEmpty(catalogPath))
                {
                    Debug.LogError("[StarCatalogEditor] No active catalog to scan");
                    return;
                }
                
                string binPath = Path.Combine(KSPUtil.ApplicationRootPath, catalogPath);
                if (!File.Exists(binPath))
                {
                    Debug.LogError($"[StarCatalogEditor] Catalog file not found: {binPath}");
                    return;
                }
                
                // Generate JSON
                if (StarCatalogManager.GenerateJsonForProceduralCatalog(binPath))
                {
                    Debug.Log($"[StarCatalogEditor] Successfully scanned catalog: {binPath}");
                    
                    // Update cached state
                    _jsonExists = true;
                    _hasCheckedForJson = false; // Force recheck on next draw
                    
                    // Force reload JSON from disk (bypasses cache) so selector sees new data
                    if (_selector != null)
                    {
                        _selector.ForceReloadJson();
                    }
                    
                    // Reload to use the new JSON
                    RefreshStarList();
                }
                else
                {
                    Debug.LogWarning($"[StarCatalogEditor] Failed to scan catalog (may not be procedural): {binPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StarCatalogEditor] Error scanning catalog: {ex.Message}");
            }
        }

        private void DrawCloseButton()
        {
            // Manual close button in top-right
            Rect closeRect = new Rect(_windowRect.width - 30, 8, 22, 18);
            if (GUI.Button(closeRect, CinematicShadersUIStrings.Common.CloseButton))
            {
                Hide();
            }
        }

        private void DrawSearchBox()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(CinematicShadersUIStrings.Kartographer.SearchLabel, GUILayout.Width(65));
            
            string newSearch = GUILayout.TextField(_searchText, GUILayout.Height(SEARCH_HEIGHT));
            if (newSearch != _searchText)
            {
                _searchText = newSearch.ToUpper();  // FORCE ALL CAPS
                UpdateFilteredList();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawStarList()
        {
            // Scrollable list of filtered stars
            float listHeight = 200f;
            
            // SHOW EMPTY STATE MESSAGES
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                GUILayout.Label(CinematicShadersUIStrings.Kartographer.EnterTermsMessage, 
                    CinematicShadersUIResources.Styles.Help());
                return;
            }
            
            if (_filteredStars.Count == 0)
            {
                GUILayout.Label(CinematicShadersUIStrings.Kartographer.NoResultMessage, 
                    CinematicShadersUIResources.Styles.Help());
                return;
            }
            
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, 
                GUILayout.Height(listHeight));
            
            foreach (var star in _filteredStars)
            {
                bool isSelected = (_selectedStar == star);
                GUIStyle style = isSelected ? HighLogic.Skin.button : HighLogic.Skin.label;
                
                string displayText = string.Format(CinematicShadersUIStrings.Kartographer.SearchResultFormat, star.HipparcosID, star.Name).ToUpper();
                if (GUILayout.Button(displayText, style))
                {
                    SelectStar(star);
                }
            }
            
            GUILayout.EndScrollView();
        }

        private void DrawEditorSection()
        {
            if (_selectedStar == null)
            {
                GUILayout.Label(CinematicShadersUIStrings.Kartographer.SelectStarPrompt);
                return;
            }
            
            // FIELD ORDER: HIP, NAME, DISTANCE, SPECTRAL, MAGNITUDE, CONSTELLATION
            GUILayout.Label($"{CinematicShadersUIStrings.Kartographer.HipLabel} {_selectedStar.HipparcosID}");
            GUILayout.Label($"{CinematicShadersUIStrings.Kartographer.NameLabel} {_selectedStar.Name}");
            GUILayout.Label($"{CinematicShadersUIStrings.Kartographer.DistanceLabel} " + string.Format(CinematicShadersUIStrings.Kartographer.DistanceLyFormat, _selectedStar.DistanceLy));
            GUILayout.Label($"{CinematicShadersUIStrings.Kartographer.SpectralLabel} {_selectedStar.SpectralType}");
            GUILayout.Label($"{CinematicShadersUIStrings.Kartographer.MagnitudeLabel} {_selectedStar.Magnitude:F2}");
            GUILayout.Label($"{CinematicShadersUIStrings.Kartographer.ConstellationLabel} {_selectedStar.Constellation}");
            
            GUILayout.Space(10);
            GUILayout.Label(CinematicShadersUIStrings.Kartographer.EditNamePrompt);
            
            string newName = GUILayout.TextField(_editNameText, GUILayout.Height(25));
            if (newName != _editNameText)
            {
                _editNameText = newName.ToUpper();  // FORCE ALL CAPS
            }
            
            GUILayout.Space(10);
            
            GUILayout.BeginHorizontal();
            
            // Save button - only enabled when changes made
            bool hasChanges = (_editNameText != _originalName);
            GUI.enabled = hasChanges;
            
            if (GUILayout.Button(CinematicShadersUIStrings.Kartographer.SaveButton, GUILayout.Height(BUTTON_HEIGHT)))
            {
                SaveStarName();
            }
            
            GUI.enabled = true;
            
            // Reset button - reverts to original name from default JSON
            if (GUILayout.Button(CinematicShadersUIStrings.Kartographer.ResetNameButton, GUILayout.Height(BUTTON_HEIGHT)))
            {
                ResetStarName();
            }
            
            GUILayout.EndHorizontal();
        }
        #endregion

        #region Logic
        private void UpdateFilteredList()
        {
            _filteredStars = StarSearchUtility.SearchStars(_allStars, _searchText);
            _scrollPosition = Vector2.zero;  // Reset scroll to top
        }

        private void SelectStar(NamedStar star)
        {
            _selectedStar = star;
            _originalName = star.Name;
            _editNameText = star.Name;
            
            // Also select this star in the Kartographer display
            _selector?.SelectStarByHipId(star.HipparcosID);
        }

        private void SaveStarName()
        {
            if (_selectedStar == null) return;
            
            try
            {
                // Ensure custom JSON exists
                string customPath = GetCustomJsonPath();
                if (string.IsNullOrEmpty(customPath))
                {
                    Debug.LogError("[StarCatalogEditor] Cannot save - no custom JSON path available");
                    return;
                }
                
                if (!File.Exists(customPath))
                {
                    CreateCustomJson(customPath);
                }
                
                // Modify the JSON
                ModifyStarNameInJson(customPath, _selectedStar.HipparcosID, _editNameText);
                
                // Restart Kartographer to force fresh load of _Custom.json
                // This causes a brief "hitch" but guarantees correct state
                RestartKartographer();
                
                Debug.Log($"[StarCatalogEditor] Saved name for HIP {_selectedStar.HipparcosID}: {_editNameText}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StarCatalogEditor] Failed to save: {ex.Message}");
            }
        }

        private void ResetStarName()
        {
            if (_selectedStar == null) return;
            
            try
            {
                // Get the default JSON path (without _Custom suffix)
                string defaultPath = GetDefaultJsonPath();
                if (string.IsNullOrEmpty(defaultPath) || !File.Exists(defaultPath))
                {
                    Debug.LogError("[StarCatalogEditor] Cannot reset - default JSON not found");
                    return;
                }
                
                // Read the original name from the default JSON
                string originalName = GetOriginalNameFromJson(defaultPath, _selectedStar.HipparcosID);
                if (string.IsNullOrEmpty(originalName))
                {
                    Debug.LogWarning($"[StarCatalogEditor] Could not find original name for HIP {_selectedStar.HipparcosID}, using designation");
                    originalName = string.Format(CinematicShadersUIStrings.Kartographer.HipIdFormat, _selectedStar.HipparcosID);
                }
                
                // Ensure custom JSON exists
                string customPath = GetCustomJsonPath();
                if (string.IsNullOrEmpty(customPath))
                {
                    Debug.LogError("[StarCatalogEditor] Cannot reset - no custom JSON path available");
                    return;
                }
                
                if (!File.Exists(customPath))
                {
                    CreateCustomJson(customPath);
                }
                
                // Modify the JSON with the original name
                ModifyStarNameInJson(customPath, _selectedStar.HipparcosID, originalName);
                
                // Restart Kartographer to force fresh load
                RestartKartographer();
                
                Debug.Log($"[StarCatalogEditor] Reset name for HIP {_selectedStar.HipparcosID} to: {originalName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StarCatalogEditor] Failed to reset: {ex.Message}");
            }
        }

        private string GetOriginalNameFromJson(string jsonPath, int hipId)
        {
            try
            {
                string json = File.ReadAllText(jsonPath);
                var root = Json.Deserialize(json) as Dictionary<string, object>;
                if (root == null) return null;

                if (!root.TryGetValue("stars", out object starsObj) || !(starsObj is Dictionary<string, object> stars))
                    return null;

                if (!stars.TryGetValue(hipId.ToString(), out object starObj) || !(starObj is Dictionary<string, object> star))
                    return null;

                if (star.TryGetValue("proper", out object properObj) && properObj is string proper && !string.IsNullOrEmpty(proper))
                    return proper.ToUpper();

                if (star.TryGetValue("full_designation", out object designationObj) && designationObj is string designation && !string.IsNullOrEmpty(designation))
                    return KartographerSelector.StripDirectionalSuffix(designation);

                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StarCatalogEditor] Failed to read original name: {ex.Message}");
                return null;
            }
        }

        private string GetCustomJsonPath()
        {
            // Get from KartographerSelector
            return _selector?.CustomJsonPath ?? "";
        }

        private string GetDefaultJsonPath()
        {
            return _selector?.DefaultJsonPath ?? "";
        }

        private void CreateCustomJson(string customPath)
        {
            string defaultPath = GetDefaultJsonPath();
            if (File.Exists(defaultPath))
            {
                File.Copy(defaultPath, customPath);
                Debug.Log($"[StarCatalogEditor] Created _Custom.json from default: {customPath}");
            }
            else
            {
                // Create minimal JSON structure if no default exists
                string minimalJson = "{\"metadata\":{\"version\":1,\"source_catalog\":\"Custom\",\"generated\":\"" + 
                    DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "\"},\"stars\":{}}";
                File.WriteAllText(customPath, minimalJson);
                Debug.Log($"[StarCatalogEditor] Created minimal _Custom.json: {customPath}");
            }
        }

        private void ModifyStarNameInJson(string jsonPath, int hipId, string newName)
        {
            try
            {
                string json = File.ReadAllText(jsonPath);
                var root = Json.Deserialize(json) as Dictionary<string, object>;
                if (root == null)
                {
                    Debug.LogError($"[StarCatalogEditor] Failed to parse JSON: {jsonPath}");
                    return;
                }

                if (!root.TryGetValue("stars", out object starsObj) || !(starsObj is Dictionary<string, object> stars))
                {
                    Debug.LogError($"[StarCatalogEditor] Missing 'stars' object in JSON: {jsonPath}");
                    return;
                }

                string hipKey = hipId.ToString();
                if (!stars.TryGetValue(hipKey, out object starObj) || !(starObj is Dictionary<string, object> star))
                {
                    star = new Dictionary<string, object>();
                    stars[hipKey] = star;
                }

                star["proper"] = newName;

                string newJson = Json.Serialize(root);
                File.WriteAllText(jsonPath, newJson);

                Debug.Log($"[StarCatalogEditor] Updated HIP {hipId} name to \"{newName}\" in {jsonPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StarCatalogEditor] Failed to modify star name in JSON: {ex.Message}");
            }
        }

        private void RestartKartographer()
        {
            // Programmatic toggle: Disable then re-enable Kartographer
            // This forces a complete reload of the catalog from _Custom.json
            // Brief hitch is acceptable trade-off for guaranteed correctness
            
            if (!StarfieldSettings.EnableKartographer)
                return; // Can't restart if not enabled
            
            // Remember which star we were editing
            int editedHipId = _selectedStar?.HipparcosID ?? 0;
            string newName = _editNameText; // The name we just saved
            
            Debug.Log("[StarCatalogEditor] Restarting Kartographer to reload _Custom.json...");
            
            // Disable
            StarfieldSettings.EnableKartographer = false;
            if (StarfieldNative.IsLoaded)
            {
                StarfieldNative.CR_StarfieldSetKartographerEnabled(0);
            }
            
            // Re-enable
            StarfieldSettings.EnableKartographer = true;
            if (StarfieldNative.IsLoaded)
            {
                StarfieldNative.CR_StarfieldSetKartographerEnabled(1);
            }
            
            StarfieldSettings.Save();
            
            // Find the new selector instance (recreated by KartographerTab)
            // and refresh our data
            StartCoroutine(RefreshAfterRestart(editedHipId, newName));
            
            Debug.Log("[StarCatalogEditor] Kartographer restarted");
        }
        
        private System.Collections.IEnumerator RefreshAfterRestart(int hipId, string savedName)
        {
            // Wait one frame for KartographerTab to process the restart
            yield return null;
            
            // Get the selector from KartographerTab
            KartographerSelector selector = null;
            if (CinematicShadersWindow.Instance != null && 
                CinematicShadersWindow.Instance.KartographerTab != null)
            {
                selector = CinematicShadersWindow.Instance.KartographerTab.Selector;
            }
            
            if (selector != null)
            {
                _selector = selector;
                
                // Subscribe to external selections
                _selector.OnStarLockedViaClick = OnExternalStarSelected;
                
                // FORCE RELOAD the JSON from disk (bypasses cache)
                _selector.ForceReloadJson();
                
                // Now refresh our star list from the reloaded data
                var namedStars = StarCatalogStateManager.NamedStars;
                if (namedStars != null)
                {
                    _allStars = namedStars.Values.OrderBy(s => s.Name).ToList();
                    UpdateFilteredList();
                    
                    // Update the editor with the newly loaded star data
                    if (hipId > 0)
                    {
                        var updatedStar = _allStars.FirstOrDefault(s => s.HipparcosID == hipId);
                        if (updatedStar != null)
                        {
                            _selectedStar = updatedStar;
                            _originalName = updatedStar.Name;
                            _editNameText = updatedStar.Name;
                            
                            Debug.Log($"[StarCatalogEditor] Refreshed after save: {updatedStar.Name} (HIP {hipId})");
                        
                            // Re-select the star to trigger selection animation with new name
                            _selector.SelectStarByHipId(hipId);
                        }
                        else
                        {
                            // Star not found in reloaded data - use saved name as fallback
                            Debug.LogWarning($"[StarCatalogEditor] Star HIP {hipId} not found after reload, using saved name");
                            _editNameText = savedName;
                            _originalName = savedName;
                        }
                    }
                }
            }
        }

        private void RefreshStarList()
        {
            // Get stars from StarCatalogStateManager
            var namedStars = StarCatalogStateManager.NamedStars;
            if (namedStars != null)
            {
                _allStars = namedStars.Values.OrderBy(s => s.Name).ToList();
                UpdateFilteredList();
            }
        }
        #endregion

        #region Public API
        public void Show()
        {
            _isVisible = true;
            InitializePosition();  // Set initial docked position
            _hasCheckedForJson = false;  // Recheck JSON status
            _jsonExists = false;
            UpdateFilteredList();  // Refresh in case data changed
        }

        public void Hide()
        {
            _isVisible = false;
            _selectedStar = null;
            _positionInitialized = false;  // Reset so next Show() can re-dock
        }

        public bool IsVisible => _isVisible;
        public Rect WindowRect => _windowRect;
        #endregion
    }
}
