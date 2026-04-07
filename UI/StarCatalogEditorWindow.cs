using CinematicShaders.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private string _cachedJson = null;
        private Dictionary<int, string> _starJsonSnippets = new Dictionary<int, string>();
        
        // Reference to selector (passed from KartographerTab)
        private KartographerSelector _selector;
        #endregion

        #region Initialization
        public void Initialize(List<NamedStar> stars, KartographerSelector selector, NamedStar preselectedStar = null)
        {
            _allStars = stars.OrderBy(s => s.Name).ToList();
            _filteredStars = new List<NamedStar>(_allStars);
            _selector = selector;
            
            // If there's a preselected star (from catalog), select it in the editor
            if (preselectedStar != null)
            {
                SelectStar(preselectedStar);
            }
            
            InitStyles();
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
            
            EnforceDockedPosition();
            
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
        private void EnforceDockedPosition()
        {
            if (CinematicShadersWindow.Instance == null) return;
            
            Rect mainRect = CinematicShadersWindow.Instance.WindowRect;
            _windowRect.x = mainRect.x + mainRect.width + 5f;
            _windowRect.y = mainRect.y;
        }
        #endregion

        #region Window Layout
        private void DrawWindow(int id)
        {
            DrawCloseButton();
            GUILayout.Space(5);
            
            DrawSearchBox();
            GUILayout.Space(5);
            
            DrawStarList();
            GUILayout.Space(5);
            
            DrawEditorSection();
            
            // No DragWindow() - docked window
        }

        private void DrawCloseButton()
        {
            // Manual close button in top-right
            Rect closeRect = new Rect(_windowRect.width - 30, 8, 22, 18);
            if (GUI.Button(closeRect, "X"))
            {
                Hide();
            }
        }

        private void DrawSearchBox()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(CinematicShadersUIStrings.Kartographer.SearchLabel, GUILayout.Width(50));
            
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
                
                string displayText = $"HIP {star.HipparcosID}: {star.Name}".ToUpper();
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
            GUILayout.Label($"{CinematicShadersUIStrings.Kartographer.DistanceLabel} {_selectedStar.DistanceLy:F1} LY");
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
            
            bool hasChanges = (_editNameText != _originalName);
            GUI.enabled = hasChanges;
            
            if (GUILayout.Button(CinematicShadersUIStrings.Kartographer.SaveButton, GUILayout.Height(BUTTON_HEIGHT)))
            {
                SaveStarName();
            }
            
            GUI.enabled = true;
        }
        #endregion

        #region Logic
        private void UpdateFilteredList()
        {
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                _filteredStars = new List<NamedStar>(_allStars);
            }
            else
            {
                string query = _searchText.ToLowerInvariant();
                _filteredStars = _allStars.Where(s => 
                    s.Name.ToLowerInvariant().Contains(query) ||
                    s.HipparcosID.ToString().Contains(query)
                ).ToList();
            }
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
                
                // Reload to refresh display
                ReloadCatalogData();
                
                Debug.Log($"[StarCatalogEditor] Saved name for HIP {_selectedStar.HipparcosID}: {_editNameText}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StarCatalogEditor] Failed to save: {ex.Message}");
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
            string json = File.ReadAllText(jsonPath);
            
            // Find the star entry
            string hipKey = $"\"{hipId}\":";
            int starStart = json.IndexOf(hipKey);
            if (starStart < 0) 
            {
                Debug.LogError($"[StarCatalogEditor] HIP {hipId} not found in JSON");
                return;
            }
            
            int braceStart = json.IndexOf('{', starStart);
            int braceEnd = FindMatchingBrace(json, braceStart);
            if (braceEnd < 0) 
            {
                Debug.LogError($"[StarCatalogEditor] Could not find matching brace for HIP {hipId}");
                return;
            }
            
            string starJson = json.Substring(braceStart, braceEnd - braceStart + 1);
            
            // Check if "proper" field exists
            string properPattern = "\"proper\":";
            int properPos = starJson.IndexOf(properPattern);
            
            string newStarJson;
            if (properPos >= 0)
            {
                // Replace existing "proper" value
                int quoteStart = starJson.IndexOf('"', properPos + properPattern.Length);
                int quoteEnd = starJson.IndexOf('"', quoteStart + 1);
                newStarJson = starJson.Substring(0, quoteStart + 1) + 
                             EscapeJsonString(newName) + 
                             starJson.Substring(quoteEnd);
            }
            else
            {
                // Add "proper" field after opening brace
                newStarJson = "{\"proper\":\"" + EscapeJsonString(newName) + "\"," + 
                             starJson.Substring(1);
            }
            
            // Replace in full JSON
            string newJson = json.Substring(0, braceStart) + newStarJson + json.Substring(braceEnd + 1);
            File.WriteAllText(jsonPath, newJson);
            
            Debug.Log($"[StarCatalogEditor] Updated HIP {hipId} name to \"{newName}\" in {jsonPath}");
        }

        private string EscapeJsonString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

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

        private void ReloadCatalogData()
        {
            // Trigger reload in KartographerSelector
            if (_selector != null)
            {
                string catalogPath = StarfieldSettings.ActiveCatalogPath;
                if (!string.IsNullOrEmpty(catalogPath))
                {
                    string absolutePath = Path.Combine(KSPUtil.ApplicationRootPath, catalogPath);
                    _selector.LoadJsonForCatalog(absolutePath);
                    
                    // Refresh our star list
                    RefreshStarList();
                }
            }
        }

        private void RefreshStarList()
        {
            // Use reflection to access private _namedStars field
            if (_selector == null) return;
            
            var field = typeof(KartographerSelector).GetField("_namedStars", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                var namedStars = field.GetValue(_selector) as Dictionary<int, NamedStar>;
                if (namedStars != null)
                {
                    _allStars = namedStars.Values.OrderBy(s => s.Name).ToList();
                    UpdateFilteredList();
                }
            }
        }
        #endregion

        #region Public API
        public void Show()
        {
            _isVisible = true;
            UpdateFilteredList();  // Refresh in case data changed
        }

        public void Hide()
        {
            _isVisible = false;
            _selectedStar = null;
        }

        public bool IsVisible => _isVisible;
        public Rect WindowRect => _windowRect;
        #endregion
    }
}
