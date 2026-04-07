using CinematicShaders.Core;
using CinematicShaders.Native;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CinematicShaders.UI
{
    /// <summary>
    /// Holographic display for the Star Catalog Editor.
    /// Renders text using native text system on black background with CRT aesthetic.
    /// </summary>
    public class StarCatalogHolographicDisplay : MonoBehaviour
    {
        #region Constants
        private const int MAX_SEARCH_RESULTS = 10;
        private const float TYPE_ON_DURATION = 0.5f;  // Seconds per element
        private const float BORDER_THICKNESS = 8f;    // Grey border around CRT
        private const float TITLE_BAR_HEIGHT = 30f;   // Height for PWR button and X
        private const int WINDOW_ID = 98767;          // Unique window ID
        #endregion

        #region State
        private bool _isVisible = false;
        private bool _displayPowered = false;
        private float _powerOnTime = 0f;
        private float _borderTypeOnProgress = 0f;
        private const float BORDER_TYPE_ON_DURATION = 0.5f;
        private HolographicDisplaySize _displaySize = HolographicDisplaySize.Medium;
        private float _fontSize = 24f;
        private float _lineSpacing = 32f;
        
        // IMGUI Window
        private Rect _windowRect = new Rect(0, 0, 616, 746);  // Will be set based on display size
        private bool _stylesInitialized = false;
        private GUIStyle _titleBarStyle;
        private GUIStyle _closeButtonStyle;
        private GUIStyle _pwrButtonStyle;
        private GUIStyle _pwrButtonActiveStyle;

        // Text elements
        private Dictionary<string, HolographicTextElement> _elements =
            new Dictionary<string, HolographicTextElement>();
        private List<HolographicTextElement> _resultElements =
            new List<HolographicTextElement>();

        // Native text system reference (shared from KartographerSelector)
        private IntPtr _textSystem = IntPtr.Zero;
        private bool _ownsTextSystem = false;

        // Display position (set by parent)
        private Rect _displayRect;

        // Render textures for composite output
        private RenderTexture _displayTexture = null;
        #endregion

        #region JSON Paths
        private string _customJsonPath = "";
        private string _defaultJsonPath = "";
        
        /// <summary>
        /// Set JSON file paths for persistence
        /// </summary>
        public void SetJsonPaths(string customPath, string defaultPath)
        {
            _customJsonPath = customPath ?? "";
            _defaultJsonPath = defaultPath ?? "";
        }
        #endregion

        #region Initialization
        public void Initialize(IntPtr sharedTextSystem, float x, float y, 
            HolographicDisplaySize size = HolographicDisplaySize.Medium,
            string customJsonPath = "", string defaultJsonPath = "")
        {
            _textSystem = sharedTextSystem;
            _displaySize = size;
            
            // Get fixed dimensions for the selected size
            Vector2 dimensions = HolographicLayoutConfig.GetDisplayDimensions(size);
            _fontSize = HolographicLayoutConfig.GetFontSize(size);
            _lineSpacing = HolographicLayoutConfig.GetLineSpacing(size);
            
            // Set window position
            _windowRect.x = x;
            _windowRect.y = y;
            
            // Calculate window size based on display size plus borders
            float displayWidth = dimensions.x;
            float displayHeight = dimensions.y;
            _windowRect.width = displayWidth + BORDER_THICKNESS * 2;
            _windowRect.height = displayHeight + TITLE_BAR_HEIGHT + BORDER_THICKNESS * 2;
            
            // Initial display rect (will be updated in DrawWindow)
            _displayRect = new Rect(x + BORDER_THICKNESS, y + TITLE_BAR_HEIGHT + BORDER_THICKNESS,
                displayWidth, displayHeight);
            
            _customJsonPath = customJsonPath;
            _defaultJsonPath = defaultJsonPath;

            CreateElements();
            InitializeTextures();
            InitializeBorderTexture();

            Debug.Log($"[HolographicDisplay] Initialized at ({x}, {y}), size: {size}, " +
                      $"window: {_windowRect.width}x{_windowRect.height}");
        }
        
        /// <summary>
        /// Change the display size (Small/Medium/Large)
        /// </summary>
        public void SetDisplaySize(HolographicDisplaySize size)
        {
            if (_displaySize == size) return;
            
            _displaySize = size;
            
            // Get new dimensions
            Vector2 dimensions = HolographicLayoutConfig.GetDisplayDimensions(size);
            _fontSize = HolographicLayoutConfig.GetFontSize(size);
            _lineSpacing = HolographicLayoutConfig.GetLineSpacing(size);
            
            // Update window size
            _windowRect.width = dimensions.x + BORDER_THICKNESS * 2;
            _windowRect.height = dimensions.y + TITLE_BAR_HEIGHT + BORDER_THICKNESS * 2;
            
            // Recreate textures for new size
            CleanupRenderTextures();
            InitializeTextures();
            InitializeBorderTexture();
            
            // Mark all elements dirty for re-render
            foreach (var element in _elements.Values)
            {
                element.IsDirty = true;
            }
            _borderDirty = true;
            
            Debug.Log($"[HolographicDisplay] Size changed to: {size}");
        }

        private void CreateElements()
        {
            // FIELD ORDER: HIP, NAME, DISTANCE, SPECTRAL, MAGNITUDE, CONSTELLATION
            // All labels and values in ALL CAPS

            AddElement("hip_label", TextElementType.Label, "HIP:", "", HolographicLayoutConfig.HIP_LABEL_POS, 0f);
            AddElement("hip_value", TextElementType.Value, "", "", HolographicLayoutConfig.HIP_VALUE_POS, 0.1f);

            AddElement("name_label", TextElementType.Label, "NAME:", "", HolographicLayoutConfig.NAME_LABEL_POS, 0.2f);
            AddElement("name_value", TextElementType.Editable, "", "", HolographicLayoutConfig.NAME_VALUE_POS, 0.3f);

            AddElement("distance_label", TextElementType.Label, "DISTANCE:", "", HolographicLayoutConfig.DISTANCE_LABEL_POS, 0.4f);
            AddElement("distance_value", TextElementType.Value, "", "", HolographicLayoutConfig.DISTANCE_VALUE_POS, 0.5f);

            AddElement("spectral_label", TextElementType.Label, "SPECTRAL:", "", HolographicLayoutConfig.SPECTRAL_LABEL_POS, 0.6f);
            AddElement("spectral_value", TextElementType.Value, "", "", HolographicLayoutConfig.SPECTRAL_VALUE_POS, 0.7f);

            AddElement("mag_label", TextElementType.Label, "MAG:", "", HolographicLayoutConfig.MAG_LABEL_POS, 0.8f);
            AddElement("mag_value", TextElementType.Value, "", "", HolographicLayoutConfig.MAG_VALUE_POS, 0.9f);

            AddElement("const_label", TextElementType.Label, "CONST:", "", HolographicLayoutConfig.CONST_LABEL_POS, 1.0f);
            AddElement("const_value", TextElementType.Value, "", "", HolographicLayoutConfig.CONST_VALUE_POS, 1.1f);

            // Search elements
            AddElement("search_label", TextElementType.Label, "SEARCH", "", HolographicLayoutConfig.SEARCH_LABEL_POS, 1.5f);
            AddElement("search_input", TextElementType.Input, "", "...", HolographicLayoutConfig.SEARCH_INPUT_POS, 1.6f);
            AddElement("rescan_button", TextElementType.Label, "", "[RESCAN]", HolographicLayoutConfig.RESCAN_BUTTON_POS, 1.7f);
            AddElement("selected_star", TextElementType.Value, "", "", HolographicLayoutConfig.SELECTED_STAR_POS, 1.8f);

            // Results header
            AddElement("results_header", TextElementType.Header, "", "RESULTS", HolographicLayoutConfig.RESULTS_HEADER_POS, 2.0f);

            // Add SAVE and RESET buttons (if not already present)
            if (!_elements.ContainsKey("save_button"))
            {
                AddElement("save_button", TextElementType.Label, "", "[SAVE]",
                    new Rect(1240, 720, 160, 64), 1.4f);
            }
            if (!_elements.ContainsKey("reset_button"))
            {
                AddElement("reset_button", TextElementType.Label, "", "[RESET]",
                    new Rect(1480, 720, 160, 64), 1.45f);
            }

            // Results rows (10 max)
            for (int i = 0; i < MAX_SEARCH_RESULTS; i++)
            {
                var elem = new HolographicTextElement
                {
                    ElementId = $"result_{i}",
                    Type = TextElementType.SearchResult,
                    StaticText = "",
                    DynamicText = "",
                    Position4K = HolographicLayoutConfig.GetResultRowPos(i),
                    TypeOnDelay = 2.2f + (i * 0.05f),
                    IsVisible = false  // Hidden until populated
                };
                _resultElements.Add(elem);
                _elements[elem.ElementId] = elem;
            }
        }

        private void AddElement(string id, TextElementType type, string staticText, string dynamicText, Rect pos4K, float typeOnDelay)
        {
            _elements[id] = new HolographicTextElement
            {
                ElementId = id,
                Type = type,
                StaticText = staticText.ToUpper(),
                DynamicText = dynamicText.ToUpper(),
                Position4K = pos4K,
                TypeOnDelay = typeOnDelay,
                TypeOnProgress = 0f,  // Start at 0 for type-on animation
                IsDirty = true
            };
        }

        private void InitializeTextures()
        {
            // Create display texture at fixed size
            Vector2 dimensions = HolographicLayoutConfig.GetDisplayDimensions(_displaySize);
            int width = Mathf.RoundToInt(dimensions.x);
            int height = Mathf.RoundToInt(dimensions.y);

            _displayTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _displayTexture.enableRandomWrite = true;
            _displayTexture.Create();

            // Create per-element textures
            foreach (var element in _elements.Values)
            {
                CreateElementTexture(element);
            }
        }

        private void CreateElementTexture(HolographicTextElement element)
        {
            // Element textures at fixed size
            int width = Mathf.Max(64, Mathf.RoundToInt(element.Position4K.width));
            int height = Mathf.Max(32, Mathf.RoundToInt(element.Position4K.height));

            element.TextTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            element.TextTexture.enableRandomWrite = true;
            element.TextTexture.Create();
        }
        
        /// <summary>
        /// Clean up all render textures before recreating them
        /// </summary>
        private void CleanupRenderTextures()
        {
            // Release display texture
            if (_displayTexture != null)
            {
                _displayTexture.Release();
                Destroy(_displayTexture);
                _displayTexture = null;
            }
            
            // Release element textures
            foreach (var element in _elements.Values)
            {
                if (element.TextTexture != null)
                {
                    element.TextTexture.Release();
                    Destroy(element.TextTexture);
                    element.TextTexture = null;
                }
            }
            
            // Release border texture
            if (_borderTexture != null)
            {
                _borderTexture.Release();
                Destroy(_borderTexture);
                _borderTexture = null;
            }
        }
        #endregion

        #region IMGUI Window Rendering
        
        private void OnGUI()
        {
            // DEBUG: ModFileLogger.Log($"[DRAW-FLOW] OnGUI called, _isVisible={_isVisible}");
            if (!_isVisible) return;
            
            // Only draw during Repaint event to avoid duplicates
            if (Event.current.type != EventType.Repaint) return;
            
            InitStyles();
            
            // Handle keyboard input (even when window not focused for convenience)
            HandleKeyboardInput();
            
            // Draw the IMGUI window with title bar and borders
            // Use GUI.Window (not GUILayout.Window) to prevent auto-sizing
            _windowRect = GUI.Window(
                WINDOW_ID,
                _windowRect,
                DrawWindow,
                "",  // No title - we draw our own
                HighLogic.Skin.window
            );
            
            // Make window draggable from edges
            ClampWindowToScreen();
        }
        
        private void DrawWindow(int windowId)
        {
            // Draw title bar with PWR button and X
            DrawTitleBar();
            
            // Draw grey border area
            DrawWindowBorder();
            
            // Update display rect based on window position
            UpdateDisplayRect();
            
            // Handle mouse interaction for CRT area
            UpdateMouseInteraction();
            
            // Draw the CRT display inside the border
            DrawCRTDisplay();
            
            // Make window draggable
            GUI.DragWindow();
        }
        
        private void DrawTitleBar()
        {
            float titleY = 4f;
            float buttonHeight = 22f;
            
            // PWR Button (left side)
            Rect pwrRect = new Rect(BORDER_THICKNESS, titleY, 80f, buttonHeight);
            GUIStyle pwrStyle = _displayPowered ? _pwrButtonActiveStyle : _pwrButtonStyle;
            
            string pwrLabel = _displayPowered ? "[•] PWR" : "[ ] PWR";
            if (GUI.Button(pwrRect, pwrLabel, pwrStyle))
            {
                TogglePower();
            }
            
            // Title (center)
            GUIStyle titleStyle = new GUIStyle(HighLogic.Skin.label);
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontStyle = FontStyle.Bold;
            Rect titleRect = new Rect(_windowRect.width * 0.25f, titleY, _windowRect.width * 0.5f, buttonHeight);
            GUI.Label(titleRect, "STAR CONSOLE", titleStyle);
            
            // X Button (right side)
            Rect closeRect = new Rect(_windowRect.width - BORDER_THICKNESS - 30f, titleY, 30f, buttonHeight);
            if (GUI.Button(closeRect, "X", _closeButtonStyle))
            {
                Hide();
            }
        }
        
        private void DrawWindowBorder()
        {
            // Grey border color (standard KSP UI grey)
            Color borderColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            GUI.color = borderColor;
            
            // Top border (under title bar)
            Rect topBorder = new Rect(0, TITLE_BAR_HEIGHT, _windowRect.width, BORDER_THICKNESS);
            GUI.DrawTexture(topBorder, Texture2D.whiteTexture);
            
            // Left border
            Rect leftBorder = new Rect(0, TITLE_BAR_HEIGHT, BORDER_THICKNESS, _windowRect.height - TITLE_BAR_HEIGHT);
            GUI.DrawTexture(leftBorder, Texture2D.whiteTexture);
            
            // Right border
            Rect rightBorder = new Rect(_windowRect.width - BORDER_THICKNESS, TITLE_BAR_HEIGHT, 
                BORDER_THICKNESS, _windowRect.height - TITLE_BAR_HEIGHT);
            GUI.DrawTexture(rightBorder, Texture2D.whiteTexture);
            
            // Bottom border
            Rect bottomBorder = new Rect(0, _windowRect.height - BORDER_THICKNESS, 
                _windowRect.width, BORDER_THICKNESS);
            GUI.DrawTexture(bottomBorder, Texture2D.whiteTexture);
            
            GUI.color = Color.white;
        }
        
        private void DrawCRTDisplay()
        {
            // DEBUG: ModFileLogger.Log("[DRAW-FLOW] DrawCRTDisplay called");
            // Draw black background for CRT area
            GUI.color = Color.black;
            Rect crtRect = new Rect(
                BORDER_THICKNESS, 
                TITLE_BAR_HEIGHT + BORDER_THICKNESS,
                _windowRect.width - BORDER_THICKNESS * 2,
                _windowRect.height - TITLE_BAR_HEIGHT - BORDER_THICKNESS * 2
            );
            GUI.DrawTexture(crtRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            
            // Draw appropriate screen content
            if (_showingConfirmation)
            {
                DrawConfirmationDialog();
            }
            else if (_showingScanScreen)
            {
                DrawScanScreen();
            }
            else
            {
                // Draw ASCII border
                DrawASCIIBorder();
                
                // Update and draw text elements
                // DEBUG: ModFileLogger.Log("[DRAW-FLOW] About to call UpdateElements and DrawElements");
                UpdateElements();
                DrawElements();
                // DEBUG: ModFileLogger.Log("[DRAW-FLOW] Back from DrawElements");
            }
        }
        
        private void UpdateDisplayRect()
        {
            // Update _displayRect to match the CRT area within the window
            // Window-relative coordinates (0,0 = window top-left) since this is used inside GUI.Window
            _displayRect = new Rect(
                BORDER_THICKNESS,
                TITLE_BAR_HEIGHT + BORDER_THICKNESS,
                _windowRect.width - BORDER_THICKNESS * 2,
                _windowRect.height - TITLE_BAR_HEIGHT - BORDER_THICKNESS * 2
            );
            // DEBUG: ModFileLogger.Log($"[DRAW] UpdateDisplayRect: _windowRect={_windowRect}, _displayRect={_displayRect}");
        }
        
        private void ClampWindowToScreen()
        {
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);
        }
        
        private void InitStyles()
        {
            if (_stylesInitialized) return;
            
            // Close button style
            _closeButtonStyle = new GUIStyle(HighLogic.Skin.button);
            _closeButtonStyle.fontSize = 12;
            _closeButtonStyle.padding = new RectOffset(2, 2, 2, 2);
            
            // PWR button styles
            _pwrButtonStyle = new GUIStyle(HighLogic.Skin.button);
            _pwrButtonStyle.fontSize = 11;
            _pwrButtonStyle.alignment = TextAnchor.MiddleLeft;
            _pwrButtonStyle.padding = new RectOffset(4, 4, 2, 2);
            
            _pwrButtonActiveStyle = new GUIStyle(_pwrButtonStyle);
            _pwrButtonActiveStyle.normal.textColor = new Color(0.2f, 0.9f, 0.3f);  // Green when on
            
            _stylesInitialized = true;
        }
        
        #endregion
        
        #region CRT Display Rendering
        
        private void DrawBackground()
        {
            // Pure black background for CRT area
            GUI.color = Color.black;
            GUI.DrawTexture(_displayRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void UpdateElements()
        {
            // DEBUG: ModFileLogger.Log($"[DIAG] UpdateElements: _displayPowered={_displayPowered}, element count={_elements?.Count}");
            if (!_displayPowered) {
                // DEBUG: ModFileLogger.Log("[DIAG] FAIL: not powered on");
                return;
            }

            _powerOnTime += Time.deltaTime;
            
            // Update border type-on animation
            if (_borderTypeOnProgress < 1f)
            {
                _borderTypeOnProgress = Mathf.Clamp01(_powerOnTime / BORDER_TYPE_ON_DURATION);
                InvalidateBorder();  // Mark border dirty to re-render
            }

            foreach (var element in _elements.Values)
            {
                // DEBUG: ModFileLogger.Log($"[DIAG] Element {element.ElementId}: IsDirty={element.IsDirty}, IsVisible={element.IsVisible}, TypeOnProgress={element.TypeOnProgress}");

                // Update type-on animation
                if (_powerOnTime >= element.TypeOnDelay && element.TypeOnProgress < 1f)
                {
                    float localTime = _powerOnTime - element.TypeOnDelay;
                    element.TypeOnProgress = Mathf.Clamp01(localTime / TYPE_ON_DURATION);
                    element.IsDirty = true;
                }

                // Re-render if dirty
                if (element.IsDirty && element.IsVisible)
                {
                    // Use two-pass selection rendering for selected elements
                    if (element.IsSelected)
                    {
                        RenderSelectedElement(element);
                    }
                    else
                    {
                        RenderElement(element);
                    }
                    element.IsDirty = false;
                }
            }
        }

        private void RenderElement(HolographicTextElement element)
        {
            // DEBUG: ModFileLogger.Log($"[RENDER] RenderElement called for {element.ElementId}");
            // DEBUG: ModFileLogger.Log($"[DIAG] RenderElement {element.ElementId}: _textSystem={_textSystem != IntPtr.Zero}");
            if (_textSystem == IntPtr.Zero) {
                // DEBUG: ModFileLogger.Log($"[DIAG] FAIL: _textSystem is null");
                return;
            }
            
            // DEBUG: ModFileLogger.Log($"[DIAG] {element.ElementId}: TextTexture={element.TextTexture != null}");
            if (element.TextTexture == null) {
                // DEBUG: ModFileLogger.Log($"[DIAG] FAIL: TextTexture is null");
                return;
            }

            // Get text to render (with type-on truncation)
            string text = GetDisplayText(element);
            // DEBUG: ModFileLogger.Log($"[DIAG] {element.ElementId}: text='{text}', length={text?.Length}");
            if (string.IsNullOrEmpty(text)) {
                // DEBUG: ModFileLogger.Log($"[DIAG] FAIL: text is empty");
                return;
            }

            // Get grid color
            uint color = GetGridColorUint();

            // Layout text in native system
            int glyphCount = StarfieldNative.CR_TextLayout(_textSystem, text, _fontSize, color);
            // DEBUG: ModFileLogger.Log($"[DIAG] {element.ElementId}: glyphCount={glyphCount}");
            if (glyphCount <= 0) {
                // DEBUG: ModFileLogger.Log($"[DIAG] FAIL: glyphCount <= 0");
                return;
            }

            // DEBUG: ModFileLogger.Log($"[DIAG] {element.ElementId}: Calling CR_TextDispatch");

            // Clear texture
            RenderTexture.active = element.TextTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;

            // Dispatch to render
            StarfieldNative.CR_TextDispatch(
                _textSystem,
                element.TextTexture.GetNativeTexturePtr(),
                glyphCount,
                element.TextTexture.width,
                element.TextTexture.height);
        }

        private string GetDisplayText(HolographicTextElement element)
        {
            string fullText = element.FullDisplayText;

            // Apply type-on truncation
            if (element.TypeOnProgress < 1f && !string.IsNullOrEmpty(fullText))
            {
                int visibleChars = Mathf.RoundToInt(fullText.Length * element.TypeOnProgress);
                visibleChars = Mathf.Clamp(visibleChars, 0, fullText.Length);
                
                // FIX: Return space when no characters visible, cursor only when text has started
                if (visibleChars == 0)
                    return " ";  // Space = nothing visible
                else
                    return fullText.Substring(0, visibleChars) + "^|";
            }

            return fullText;
        }

        private void DrawElements()
        {
            // DIAGNOSTIC: Log entry point
            // DEBUG: ModFileLogger.Log($"[DRAW] DrawElements called, _elements count={_elements?.Count}, _displayRect={_displayRect}");
            // DEBUG: ModFileLogger.Log($"[DRAW] GUI.matrix={GUI.matrix}");
            
            if (!_displayPowered) {
                // DEBUG: ModFileLogger.Log("[DRAW] DrawElements: not powered, returning");
                return;
            }
            
            int visibleCount = 0;
            foreach (var element in _elements.Values)
            {
                // DIAGNOSTIC: Log element state
                // DEBUG: ModFileLogger.Log($"[DRAW] Element {element.ElementId}: Position4K={element.Position4K}, IsVisible={element.IsVisible}, IsDirty={element.IsDirty}");
                
                if (!element.IsVisible) continue;
                if (element.TextTexture == null) continue;
                
                visibleCount++;

                // Use original Y position - flipping is done via UV coordinates
                Rect screenPos = new Rect(
                    _displayRect.x + element.Position4K.x,   // ADD display offset
                    _displayRect.y + element.Position4K.y,   // ADD display offset
                    element.Position4K.width,
                    element.Position4K.height
                );

                // Calculate what the CORRECT position should be (for comparison)
                Rect correctScreenPos = new Rect(
                    _displayRect.x + element.Position4K.x,
                    _displayRect.y + element.Position4K.y,
                    element.Position4K.width,
                    element.Position4K.height
                );
                
                // DIAGNOSTIC: Log final screen position before draw
                // DEBUG: ModFileLogger.Log($"[DRAW] Drawing {element.ElementId} at screenPos={screenPos}, correctPos SHOULD BE={correctScreenPos}, textureSize={element.TextTexture.width}x{element.TextTexture.height}");
                // DEBUG: ModFileLogger.Log($"[DRAW] _displayRect.x={_displayRect.x}, _displayRect.y={_displayRect.y}, Position4K.x={element.Position4K.x}, Position4K.y={element.Position4K.y}");

                // Flip texture vertically via UV coordinates
                // DEBUG: Tint red to identify this draw call
                Graphics.DrawTexture(
                    screenPos,              // dest rect
                    element.TextTexture,    // source texture
                    new Rect(0, 1, 1, -1),  // source UVs: flip Y
                    0, 0, 0, 0,             // border widths
                    Color.red,              // DEBUG: Tint red to identify
                    null                    // material
                );
            }
            
            // DEBUG: ModFileLogger.Log($"[DRAW] DrawElements complete, drew {visibleCount} visible elements");
        }
        #endregion

        #region Color Helpers
        private Color GetGridColor()
        {
            // Use Kartographer grid colors
            int colorIndex = StarfieldSettings.KartographerGridColor;
            switch (colorIndex)
            {
                case 0: return new Color(0.1f, 0.9f, 0.7f);  // Seafoam
                case 1: return new Color(1.0f, 0.65f, 0.0f); // Amber
                case 2: return new Color(0.85f, 0.95f, 1.0f); // White
                case 3: return new Color(0.25f, 1.0f, 0.0f);  // Green
                default: return new Color(0.1f, 0.9f, 0.7f);  // Default seafoam
            }
        }

        private uint GetGridColorUint()
        {
            Color c = GetGridColor();
            uint r = (uint)(c.r * 255) & 0xFF;
            uint g = (uint)(c.g * 255) & 0xFF;
            uint b = (uint)(c.b * 255) & 0xFF;
            return 0xFF000000 | (r << 16) | (g << 8) | b;  // ARGB format (A=FF)
        }
        #endregion

        #region Edit Mode
        
        // Edit state
        private bool _isEditing = false;
        private string _editBuffer = "";
        private string _originalName = "";
        private float _cursorBlinkTimer = 0f;
        private bool _cursorVisible = true;
        
        /// <summary>
        /// Enter edit mode for NAME field
        /// </summary>
        private void EnterEditMode()
        {
            if (_selectedStar == null) return;
            if (_isEditing) return;
            
            _isEditing = true;
            _originalName = _selectedStar.Name;
            _editBuffer = _selectedStar.Name;
            _cursorBlinkTimer = 0f;
            _cursorVisible = true;
            
            // Mark name element as editing
            var nameElement = GetElement("name_value");
            if (nameElement != null)
            {
                nameElement.IsSelecting = true;
                nameElement.IsDirty = true;
                nameElement.ShowCursor = true;
            }
            
            Debug.Log($"[HolographicDisplay] Entered edit mode for: {_selectedStar.Name}");
        }
        
        /// <summary>
        /// Exit edit mode, optionally saving changes
        /// </summary>
        private void ExitEditMode(bool save)
        {
            if (!_isEditing) return;
            
            _isEditing = false;
            
            var nameElement = GetElement("name_value");
            if (nameElement != null)
            {
                nameElement.IsSelecting = false;
                nameElement.ShowCursor = false;
                nameElement.IsDirty = true;
            }
            
            if (save)
            {
                // Save the edited name
                SaveStarName(_editBuffer);
            }
            else
            {
                // Revert to original
                _editBuffer = _originalName;
                SetElementText("name_value", _originalName);
            }
            
            Debug.Log($"[HolographicDisplay] Exited edit mode (saved: {save})");
        }
        
        /// <summary>
        /// Handle edit mode keyboard input
        /// </summary>
        private void HandleEditInput()
        {
            if (!_isEditing) return;
            
            Event e = Event.current;
            
            if (e.type != EventType.KeyDown)
                return;
            
            // Enter/Return to save
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                ExitEditMode(save: true);
                e.Use();
                return;
            }
            
            // Escape to cancel
            if (e.keyCode == KeyCode.Escape)
            {
                ExitEditMode(save: false);
                e.Use();
                return;
            }
            
            // Backspace to delete last character
            if (e.keyCode == KeyCode.Backspace)
            {
                if (_editBuffer.Length > 0)
                {
                    _editBuffer = _editBuffer.Substring(0, _editBuffer.Length - 1);
                    SetElementText("name_value", _editBuffer + (_cursorVisible ? "^|" : ""));
                }
                e.Use();
                return;
            }
            
            // Delete to clear entire field
            if (e.keyCode == KeyCode.Delete)
            {
                _editBuffer = "";
                SetElementText("name_value", _cursorVisible ? "^|" : "");
                e.Use();
                return;
            }
            
            // Regular character input (forced uppercase)
            if (e.character != '\0' && !char.IsControl(e.character))
            {
                _editBuffer += char.ToUpper(e.character);
                SetElementText("name_value", _editBuffer + (_cursorVisible ? "^|" : ""));
                e.Use();
                return;
            }
        }
        
        /// <summary>
        /// Update cursor blink animation
        /// </summary>
        private void UpdateCursorBlink()
        {
            if (!_isEditing) return;
            
            _cursorBlinkTimer += Time.deltaTime;
            
            // 2Hz blink (0.25s on, 0.25s off)
            bool newVisible = (_cursorBlinkTimer % 0.5f) < 0.25f;
            
            if (newVisible != _cursorVisible)
            {
                _cursorVisible = newVisible;
                SetElementText("name_value", _editBuffer + (_cursorVisible ? "^|" : ""));
            }
        }
        
        #endregion

        #region Persistence
        
        /// <summary>
        /// Save the current star name to _Custom.json
        /// </summary>
        private void SaveStarName(string newName)
        {
            if (_selectedStar == null) return;
            if (string.IsNullOrEmpty(_customJsonPath)) return;
            
            try
            {
                // Ensure custom JSON exists
                if (!File.Exists(_customJsonPath))
                {
                    CreateCustomJson();
                }
                
                // Modify the JSON
                ModifyStarNameInJson(_selectedStar.HipparcosID, newName);
                
                // Update local state
                _selectedStar.Name = newName;
                SetElementText("name_value", newName);
                
                // Refresh the selector to reload _Custom.json
                RefreshSelector();
                
                Debug.Log($"[HolographicDisplay] Saved name for HIP {_selectedStar.HipparcosID}: {newName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HolographicDisplay] Failed to save: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Reset star name to original from default JSON
        /// </summary>
        private void ResetStarName()
        {
            if (_selectedStar == null) return;
            if (string.IsNullOrEmpty(_defaultJsonPath) || !File.Exists(_defaultJsonPath))
            {
                Debug.LogError("[HolographicDisplay] Cannot reset - default JSON not found");
                return;
            }
            
            try
            {
                // Read original name from default JSON
                string originalName = GetOriginalNameFromJson(_selectedStar.HipparcosID);
                if (string.IsNullOrEmpty(originalName))
                {
                    originalName = $"HIP {_selectedStar.HipparcosID}";
                }
                
                // Ensure custom JSON exists
                if (!File.Exists(_customJsonPath))
                {
                    CreateCustomJson();
                }
                
                // Modify the JSON with original name
                ModifyStarNameInJson(_selectedStar.HipparcosID, originalName);
                
                // Update local state
                _selectedStar.Name = originalName;
                SetElementText("name_value", originalName);
                
                // Refresh the selector
                RefreshSelector();
                
                Debug.Log($"[HolographicDisplay] Reset name for HIP {_selectedStar.HipparcosID} to: {originalName}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HolographicDisplay] Failed to reset: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Create _Custom.json from default JSON or minimal structure
        /// </summary>
        private void CreateCustomJson()
        {
            if (File.Exists(_defaultJsonPath))
            {
                File.Copy(_defaultJsonPath, _customJsonPath);
                Debug.Log($"[HolographicDisplay] Created _Custom.json from default");
            }
            else
            {
                string minimalJson = "{\"metadata\":{\"version\":1,\"source_catalog\":\"Custom\",\"generated\":\"" + 
                    DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "\"},\"stars\":{}}";
                File.WriteAllText(_customJsonPath, minimalJson);
                Debug.Log($"[HolographicDisplay] Created minimal _Custom.json");
            }
        }
        
        /// <summary>
        /// Modify star name in JSON file
        /// </summary>
        private void ModifyStarNameInJson(int hipId, string newName)
        {
            string json = File.ReadAllText(_customJsonPath);
            
            // Find the star entry
            string hipKey = $"\"{hipId}\":";
            int starStart = json.IndexOf(hipKey);
            if (starStart < 0)
            {
                Debug.LogError($"[HolographicDisplay] HIP {hipId} not found in JSON");
                return;
            }
            
            int braceStart = json.IndexOf('{', starStart);
            int braceEnd = FindMatchingBrace(json, braceStart);
            if (braceEnd < 0)
            {
                Debug.LogError($"[HolographicDisplay] Could not find matching brace for HIP {hipId}");
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
            File.WriteAllText(_customJsonPath, newJson);
        }
        
        /// <summary>
        /// Get original name from default JSON
        /// </summary>
        private string GetOriginalNameFromJson(int hipId)
        {
            try
            {
                string json = File.ReadAllText(_defaultJsonPath);
                
                string hipKey = $"\"{hipId}\":";
                int starStart = json.IndexOf(hipKey);
                if (starStart < 0) return null;
                
                int braceStart = json.IndexOf('{', starStart);
                int braceEnd = FindMatchingBrace(json, braceStart);
                if (braceEnd < 0) return null;
                
                string starJson = json.Substring(braceStart, braceEnd - braceStart + 1);
                
                // Try "proper" first, then "full_designation"
                string proper = ExtractStringValue(starJson, "proper");
                if (!string.IsNullOrEmpty(proper))
                    return proper.ToUpper();
                
                string designation = ExtractStringValue(starJson, "full_designation");
                if (!string.IsNullOrEmpty(designation))
                    return StripDirectionalSuffix(designation);
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HolographicDisplay] Failed to read original name: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Refresh selector after JSON modification
        /// </summary>
        private void RefreshSelector()
        {
            if (_selector != null)
            {
                // Force reload JSON from disk
                _selector.ForceReloadJson();
                
                // Re-select the current star to trigger animation with new name
                if (_selectedStar != null)
                {
                    _selector.SelectStarByHipId(_selectedStar.HipparcosID);
                }
            }
        }
        
        #endregion
        
        #region JSON Helpers
        
        /// <summary>
        /// Find matching closing brace
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
        /// Extract string value from JSON snippet
        /// </summary>
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
        
        /// <summary>
        /// Escape string for JSON
        /// </summary>
        private string EscapeJsonString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
        
        /// <summary>
        /// Strip directional suffixes from designation
        /// </summary>
        private string StripDirectionalSuffix(string fullDesignation)
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
        
        #endregion

        #region Public API
        public void Show()
        {
            _isVisible = true;
            // Don't auto-power on - let user click PWR button
            // This also allows the window to be positioned before first draw
        }
        
        public void ShowAt(float x, float y)
        {
            _windowRect.x = x;
            _windowRect.y = y;
            Show();
        }

        public void Hide()
        {
            _isVisible = false;
            PowerOff();
            // Notify parent that window closed
            OnWindowClosed?.Invoke();
        }

        public bool IsVisible => _isVisible;
        public Rect DisplayRect => _displayRect;
        public Rect WindowRect => _windowRect;
        
        /// <summary>
        /// Event fired when window is closed via X button
        /// </summary>
        public event Action OnWindowClosed;

        private void TogglePower()
        {
            // DEBUG: ModFileLogger.Log($"[DIAG] TogglePower: current={_displayPowered}");
            if (_displayPowered)
            {
                PowerOff();
            }
            else
            {
                PowerOn();
            }
        }

        private void PowerOn()
        {
            // DEBUG: ModFileLogger.Log("[DIAG] PowerOn() called");
            _displayPowered = true;
            _powerOnTime = 0f;
            _borderTypeOnProgress = 0f;
            
            // Show all elements for type-on animation
            foreach (var element in _elements.Values)
            {
                element.IsVisible = true;
            }
            
            // Mark border as dirty to re-render
            InvalidateBorder();

            // Reset type-on animation with proper sequence:
            // 1. Border first (lowest delay)
            // 2. Labels second
            // 3. Values third (only if star selected)
            
            float currentDelay = 0f;
            
            // First: Border (if we had it as an element - currently it's a separate texture)
            // Border renders immediately when powered on
            
            // Second: Labels (HIP, NAME, DISTANCE, etc.)
            string[] labelIds = { "hip_label", "name_label", "distance_label", 
                                  "spectral_label", "mag_label", "const_label" };
            foreach (var id in labelIds)
            {
                if (_elements.TryGetValue(id, out var elem))
                {
                    elem.TypeOnDelay = currentDelay;
                    elem.TypeOnProgress = 0f;
                    elem.IsDirty = true;
                    currentDelay += 0.15f;  // 150ms between labels
                }
            }
            
            // Third: Values (only if we have a selected star)
            if (_selectedStar != null)
            {
                string[] valueIds = { "hip_value", "name_value", "distance_value", 
                                      "spectral_value", "mag_value", "const_value" };
                foreach (var id in valueIds)
                {
                    if (_elements.TryGetValue(id, out var elem))
                    {
                        elem.TypeOnDelay = currentDelay;
                        elem.TypeOnProgress = 0f;
                        elem.IsDirty = true;
                        currentDelay += 0.15f;
                    }
                }
                
                // Selected star indicator last
                if (_elements.TryGetValue("selected_star", out var selElem))
                {
                    selElem.TypeOnDelay = currentDelay;
                    selElem.TypeOnProgress = 0f;
                    selElem.IsDirty = true;
                }
            }
            
            // Search elements come after
            currentDelay += 0.3f;
            string[] searchIds = { "search_label", "search_input", "rescan_button" };
            foreach (var id in searchIds)
            {
                if (_elements.TryGetValue(id, out var elem))
                {
                    elem.TypeOnDelay = currentDelay;
                    elem.TypeOnProgress = 0f;
                    elem.IsDirty = true;
                    currentDelay += 0.1f;
                }
            }
            
            // Results header and rows
            if (_elements.TryGetValue("results_header", out var headerElem))
            {
                headerElem.TypeOnDelay = currentDelay;
                headerElem.TypeOnProgress = 0f;
                headerElem.IsDirty = true;
            }
            
            Debug.Log("[HolographicDisplay] Power ON - type-on animation started");
        }

        private void PowerOff()
        {
            _displayPowered = false;
            
            // Hide all elements immediately
            foreach (var element in _elements.Values)
            {
                element.IsVisible = false;
                element.TypeOnProgress = 0f;
                element.IsDirty = true;
            }
            
            Debug.Log("[HolographicDisplay] Power OFF");
        }

        /// <summary>
        /// Update display data with star information
        /// </summary>
        public void SetStarData(NamedStar star)
        {
            if (star == null) return;

            SetElementText("hip_value", star.HipparcosID.ToString());
            SetElementText("name_value", star.Name);
            SetElementText("distance_value", $"{star.DistanceLy:F1} LY");
            SetElementText("spectral_value", star.SpectralType);
            SetElementText("mag_value", star.Magnitude.ToString("F2"));
            SetElementText("const_value", star.Constellation);
            SetElementText("selected_star", $"►{star.Name}");
        }

        private void SetElementText(string elementId, string text)
        {
            if (_elements.TryGetValue(elementId, out var element))
            {
                string newText = text?.ToUpper() ?? "";
                if (element.DynamicText != newText)
                {
                    element.DynamicText = newText;
                    element.IsDirty = true;
                }
            }
        }

        /// <summary>
        /// Clear all star data from display
        /// </summary>
        public void ClearStarData()
        {
            SetElementText("hip_value", "");
            SetElementText("name_value", "");
            SetElementText("distance_value", "");
            SetElementText("spectral_value", "");
            SetElementText("mag_value", "");
            SetElementText("const_value", "");
            SetElementText("selected_star", "");
        }
        #endregion

        #region Cleanup
        private void OnDestroy()
        {
            // Release render textures
            if (_displayTexture != null)
            {
                _displayTexture.Release();
                Destroy(_displayTexture);
            }

            foreach (var element in _elements.Values)
            {
                if (element.TextTexture != null)
                {
                    element.TextTexture.Release();
                    Destroy(element.TextTexture);
                }
            }

            // Release highlight texture cache if allocated
            ReleaseHighlightCache();

            // Release border texture
            if (_borderTexture != null)
            {
                _borderTexture.Release();
                Destroy(_borderTexture);
                _borderTexture = null;
            }

            // Note: We don't shut down _textSystem here because it's shared
        }
        #endregion

        #region Selection Rendering

        // Cache for highlight textures (avoid per-frame allocation)
        private RenderTexture _cachedHighlightTexture = null;
        private Vector2 _cachedHighlightSize = Vector2.zero;

        /// <summary>
        /// Render an element with selection highlight (two-pass: highlight background + black text)
        /// </summary>
        private void RenderSelectedElement(HolographicTextElement element)
        {
            if (_textSystem == IntPtr.Zero) return;
            if (element.TextTexture == null) return;

            // Get text to render
            string text = GetDisplayText(element);
            if (string.IsNullOrEmpty(text)) return;

            // Pass 1: Draw highlight background to a temp texture
            RenderTexture highlightTex = GetHighlightTexture(element);
            RenderHighlightBackground(highlightTex, element);

            // Pass 2: Render text in BLACK color
            uint blackColor = 0xFF000000;  // ARGB black

            int glyphCount = StarfieldNative.CR_TextLayout(_textSystem, text, _fontSize, blackColor);
            if (glyphCount <= 0) return;

            // Clear element texture
            RenderTexture.active = element.TextTexture;
            GL.Clear(true, true, Color.clear);
            // REMOVED: RenderTexture.active = null;  // Keep active for compositing

            // First draw the highlight background (now renders to active RT)
            Graphics.DrawTexture(
                new Rect(0, 0, element.TextTexture.width, element.TextTexture.height),
                highlightTex,
                new Rect(0, 0, 1, 1),
                0, 0, 0, 0,
                new Color(1, 1, 1, 1));

            // Then render black text on top (also uses active RT via native UAV)
            StarfieldNative.CR_TextDispatch(
                _textSystem,
                element.TextTexture.GetNativeTexturePtr(),
                glyphCount,
                element.TextTexture.width,
                element.TextTexture.height);

            // NOW clear active RT after all operations complete
            RenderTexture.active = null;

            ReleaseHighlightTexture(highlightTex);
        }

        /// <summary>
        /// Create or get a temporary render texture for highlight background
        /// </summary>
        private RenderTexture GetHighlightTexture(HolographicTextElement element)
        {
            int width = Mathf.Max(64, Mathf.RoundToInt(element.Position4K.width));
            int height = Mathf.Max(32, Mathf.RoundToInt(element.Position4K.height));

            // Check if we can reuse cached texture
            if (_cachedHighlightTexture != null &&
                _cachedHighlightSize.x == width &&
                _cachedHighlightSize.y == height)
            {
                return _cachedHighlightTexture;
            }

            // Release old cached texture if size changed
            ReleaseHighlightCache();

            // Create new texture
            _cachedHighlightTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _cachedHighlightTexture.enableRandomWrite = true;
            _cachedHighlightTexture.Create();
            _cachedHighlightSize = new Vector2(width, height);

            return _cachedHighlightTexture;
        }

        private void ReleaseHighlightTexture(RenderTexture tex)
        {
            // Texture is cached, don't release immediately
            // It will be reused or cleaned up in OnDestroy
        }

        private void ReleaseHighlightCache()
        {
            if (_cachedHighlightTexture != null)
            {
                _cachedHighlightTexture.Release();
                Destroy(_cachedHighlightTexture);
                _cachedHighlightTexture = null;
                _cachedHighlightSize = Vector2.zero;
            }
        }

        /// <summary>
        /// Render the colored highlight background
        /// </summary>
        private void RenderHighlightBackground(RenderTexture target, HolographicTextElement element)
        {
            RenderTexture.active = target;

            // Clear to highlight color (grid color at 30% opacity)
            Color highlightColor = GetGridColor();
            highlightColor.a = 0.3f;
            GL.Clear(true, true, highlightColor);

            RenderTexture.active = null;
        }

        #endregion

        #region Mouse Interaction

        // State
        private HolographicTextElement _hoveredElement = null;
        private HolographicTextElement _pressedElement = null;
        private Vector2 _mousePosition = Vector2.zero;

        /// <summary>
        /// Check if mouse is over a specific element
        /// </summary>
        private bool IsMouseOverElement(HolographicTextElement element)
        {
            if (!element.IsVisible) return false;

            Rect screenPos = new Rect(
                _displayRect.x + element.Position4K.x,
                _displayRect.y + element.Position4K.y,
                element.Position4K.width,
                element.Position4K.height
            );

            return screenPos.Contains(_mousePosition);
        }

        /// <summary>
        /// Update mouse state and handle hover/click
        /// </summary>
        private void UpdateMouseInteraction()
        {
            // Get mouse position (Unity GUI coordinates: top-left origin)
            _mousePosition = Event.current.mousePosition;

            // Find hovered element
            HolographicTextElement newHovered = null;

            foreach (var element in _elements.Values)
            {
                if (IsClickable(element) && IsMouseOverElement(element))
                {
                    newHovered = element;
                    break;
                }
            }

            // Handle hover change
            if (newHovered != _hoveredElement)
            {
                // Clear old hover
                if (_hoveredElement != null)
                {
                    _hoveredElement.IsSelected = false;
                    _hoveredElement.IsDirty = true;
                }

                // Set new hover
                _hoveredElement = newHovered;
                if (_hoveredElement != null)
                {
                    _hoveredElement.IsSelected = true;
                    _hoveredElement.IsDirty = true;
                }
            }

            // Handle mouse down/up for click detection
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                _pressedElement = _hoveredElement;
            }
            else if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                if (_pressedElement != null && _pressedElement == _hoveredElement)
                {
                    // Click detected
                    OnElementClicked(_pressedElement);
                }
                _pressedElement = null;
            }
        }

        /// <summary>
        /// Check if an element is clickable
        /// </summary>
        private bool IsClickable(HolographicTextElement element)
        {
            switch (element.Type)
            {
                case TextElementType.Editable:
                case TextElementType.SearchResult:
                case TextElementType.Input:
                    return true;
                default:
                    // Check for button elements by ID
                    return element.ElementId == "rescan_button" ||
                           element.ElementId == "save_button" ||
                           element.ElementId == "reset_button" ||
                           element.ElementId == "yes_button" ||
                           element.ElementId == "no_button" ||
                           element.ElementId == "scan_ascii";  // ASCII SCAN art
            }
        }

        /// <summary>
        /// Handle element click
        /// </summary>
        private void OnElementClicked(HolographicTextElement element)
        {
            Debug.Log($"[HolographicDisplay] Clicked: {element.ElementId}");

            switch (element.ElementId)
            {
                case "name_value":
                    EnterEditMode();
                    break;
                case "rescan_button":
                case "scan_ascii":
                    ShowRescanConfirmation();
                    break;
                case "save_button":
                    if (_isEditing)
                    {
                        ExitEditMode(save: true);
                    }
                    else
                    {
                        // Save current displayed name (should match selected star)
                        SaveStarName(_selectedStar?.Name);
                    }
                    break;
                case "reset_button":
                    ResetStarName();
                    break;
                case "yes_button":
                    ConfirmRescan();
                    break;
                case "no_button":
                    HideRescanConfirmation();
                    break;
                default:
                    // Check for result row clicks
                    if (element.ElementId.StartsWith("result_"))
                    {
                        OnSearchResultClicked(element);
                    }
                    break;
            }
        }

        /// <summary>
        /// Callback events for UI integration
        /// </summary>
        public event Action OnSaveClicked;
        public event Action OnResetClicked;
        public event Action<NamedStar> OnStarSelected;
        public event Action OnRescanConfirmed;



        /// <summary>
        /// Handle search result click
        /// </summary>
        private void OnSearchResultClicked(HolographicTextElement element)
        {
            if (element.AssociatedData is NamedStar star)
            {
                SetStarData(star);
                OnStarSelected?.Invoke(star);
                Debug.Log($"[HolographicDisplay] Selected star: {star.Name}");
            }
        }

        #endregion

        #region ASCII Border Rendering

        // ASCII art layout strings (4K reference)
        private static readonly string[] ASCII_BORDER_LINES = new string[]
        {
            "\u2554\u2550\u2550\u2550\u2550[STAR DATA]\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2566\u2566\u2550\u2550\u2550\u2550\u2550[RESULTS]\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557",
            "\u2551                                  \u2551\u2551                     \u2551",
            "\u2551                                  \u2551\u2551                     \u2551",
            "\u2551                                  \u2551\u2551                     \u2551",
            "\u2551                                  \u2551\u2551                     \u2551",
            "\u2551                                  \u2551\u2551                     \u2551",
            "\u2551                                  \u2551\u2551                     \u2551",
            "\u2551                                  \u2551\u2551                     \u2551",
            "\u2560\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2563\u2551                     \u2551",
            "\u2551                                  \u2551\u2551                     \u2551",
            "\u2551                                  \u2551\u2551                     \u2551",
            "\u2551                                  \u2551\u2551                     \u2551",
            "\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2569\u2569\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d"
        };

        // Render texture for the border - uses native text system
        private RenderTexture _borderTexture = null;
        private bool _borderDirty = true;

        /// <summary>
        /// Initialize the border render texture
        /// </summary>
        private void InitializeBorderTexture()
        {
            if (_borderTexture != null) return;

            Vector2 dimensions = HolographicLayoutConfig.GetDisplayDimensions(_displaySize);
            int width = Mathf.RoundToInt(dimensions.x);
            int height = Mathf.RoundToInt(dimensions.y);

            _borderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _borderTexture.enableRandomWrite = true;
            _borderTexture.Create();
            _borderDirty = true;
        }

        /// <summary>
        /// Render the ASCII border using native text system
        /// </summary>
        private void RenderBorderTexture()
        {
            if (_textSystem == IntPtr.Zero) return;
            if (_borderTexture == null) InitializeBorderTexture();
            if (!_borderDirty) return;

            _borderDirty = false;

            // Build border text from lines
            string borderText = string.Join("\n", ASCII_BORDER_LINES);

            // Apply type-on: only show portion based on progress
            if (_borderTypeOnProgress < 1f)
            {
                int totalChars = borderText.Length;
                int visibleChars = Mathf.RoundToInt(totalChars * _borderTypeOnProgress);
                visibleChars = Mathf.Clamp(visibleChars, 0, totalChars);
                borderText = borderText.Substring(0, visibleChars);
            }

            uint color = GetGridColorUint();
            float fontSize = _fontSize;

            // Layout the border text
            int glyphCount = StarfieldNative.CR_TextLayout(_textSystem, borderText, fontSize, color);
            if (glyphCount <= 0) return;

            // Clear texture
            RenderTexture.active = _borderTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;

            // Dispatch to render
            StarfieldNative.CR_TextDispatch(
                _textSystem,
                _borderTexture.GetNativeTexturePtr(),
                glyphCount,
                _borderTexture.width,
                _borderTexture.height);
        }

        /// <summary>
        /// Draw the full ASCII border with native text rendering
        /// </summary>
        private void DrawASCIIBorder()
        {
            // Ensure border is rendered
            if (_borderDirty)
            {
                RenderBorderTexture();
            }

            // Draw the border texture - type-on effect is in the text content itself
            if (_borderTexture != null)
            {
                // Remove alpha fade - border types on, doesn't fade
                // Use full color, the type-on effect is in the text content itself
                Graphics.DrawTexture(
                    _displayRect,           // dest rect (screen position)
                    _borderTexture,         // source texture
                    new Rect(0, 1, 1, -1),  // source UVs: flip Y (x, y, width, height in UV space)
                    0, 0, 0, 0,             // border widths
                    Color.white,            // Full color, no alpha fade
                    null                    // material
                );
            }
        }

        /// <summary>
        /// Mark border as needing re-render (e.g., on color change)
        /// </summary>
        public void InvalidateBorder()
        {
            _borderDirty = true;
        }

        #endregion

        #region SCAN Screen

        // State
        private bool _showingScanScreen = false;
        private HolographicTextElement[] _scanScreenElements;

        // ASCII art for SCAN
        private static readonly string[] SCAN_ASCII_ART = new string[]
        {
            "\u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557",
            "\u2551 \u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2557 \u2588\u2588\u2588\u2588\u2588\u2588\u2557 \u2588\u2588\u2588\u2588\u2588\u2557 \u2588\u2588\u2588\u2557   \u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2551",
            "\u2551 \u2593\u2593\u2555\u2550\u2550\u2550\u2550\u2550\u2593\u2593\u2554\u2550\u2550\u2550\u2550\u2550\u2593\u2593\u2554\u2550\u2550\u2593\u2593\u2557\u2593\u2593\u2588\u2588\u2557  \u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2551",
            "\u2551 \u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2557\u2593\u2593\u2551     \u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2593\u2593\u2593\u2593\u2554\u2593\u2593\u2557 \u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2551",
            "\u2551 \u255a\u2550\u2550\u2550\u2550\u2550\u2593\u2593\u2593\u2593\u2551     \u2593\u2593\u2554\u2550\u2550\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2551",
            "\u2551 \u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2593\u2593\u2593\u255a\u2588\u2588\u2588\u2588\u2588\u2588\u2557\u2593\u2593\u2593\u2593  \u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2554\u2588\u2588\u2588\u2588\u2588\u2588\u2588\u2551",
            "\u2551 \u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2593\u2593\u255a\u2550\u2550\u2550\u2550\u2550\u2593\u2593\u2593\u2593\u2593\u2593  \u2593\u2593\u2593\u2593\u2593\u2593\u2593\u2593\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2551",
            "\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d"
        };

        /// <summary>
        /// Show the SCAN screen with ASCII art
        /// </summary>
        public void ShowScanScreen()
        {
            _showingScanScreen = true;

            // Hide main elements
            foreach (var element in _elements.Values)
            {
                element.IsVisible = false;
            }
        }

        /// <summary>
        /// Hide SCAN screen and return to main display
        /// </summary>
        public void HideScanScreen()
        {
            _showingScanScreen = false;

            // Show main elements
            foreach (var element in _elements.Values)
            {
                element.IsVisible = true;
            }
        }

        /// <summary>
        /// Draw SCAN screen if active
        /// </summary>
        private void DrawScanScreen()
        {
            if (!_showingScanScreen) return;

            // Draw centered SCAN ASCII art
            Color borderColor = GetGridColor();
            GUI.color = borderColor;

            float lineHeight = _lineSpacing;
            float charWidth = 14f;  // Approximate monospace char width
            float artWidth = SCAN_ASCII_ART[0].Length * charWidth;
            float artHeight = SCAN_ASCII_ART.Length * lineHeight;

            float startX = _displayRect.x + (_displayRect.width - artWidth) * 0.5f;
            float startY = _displayRect.y + (_displayRect.height - artHeight) * 0.5f;

            GUIStyle scanStyle = new GUIStyle();
            scanStyle.fontSize = Mathf.RoundToInt(_fontSize * 0.9f);
            scanStyle.normal.textColor = borderColor;

            for (int i = 0; i < SCAN_ASCII_ART.Length; i++)
            {
                Rect lineRect = new Rect(
                    startX,
                    startY + (i * lineHeight),
                    artWidth,
                    lineHeight
                );
                GUI.Label(lineRect, SCAN_ASCII_ART[i], scanStyle);
            }

            // Make the ASCII art clickable
            Rect artRect = new Rect(startX, startY, artWidth, artHeight);
            if (GUI.Button(artRect, "", GUIStyle.none))
            {
                ShowRescanConfirmation();
            }

            GUI.color = Color.white;
        }

        #endregion

        #region Rescan Confirmation

        // State
        private bool _showingConfirmation = false;

        // ASCII art for confirmation dialog
        private static readonly string[] CONFIRM_ASCII_ART = new string[]
        {
            "\u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550[ARE YOU SURE?]\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557",
            "\u2551                                                    \u2551",
            "\u2551             !STAR NAMES WILL BE RESET!             \u2551",
            "\u2551                                                    \u2551",
            "\u2551                                                    \u2551",
            "\u2551                                                    \u2551",
            "\u2551  [YES]                                       [NO]  \u2551",
            "\u2551                                                    \u2551",
            "\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d"
        };

        /// <summary>
        /// Show rescan confirmation dialog
        /// </summary>
        private void ShowRescanConfirmation()
        {
            _showingConfirmation = true;
        }

        /// <summary>
        /// Hide rescan confirmation dialog
        /// </summary>
        private void HideRescanConfirmation()
        {
            _showingConfirmation = false;
        }

        /// <summary>
        /// Confirm rescan action
        /// </summary>
        private void ConfirmRescan()
        {
            HideRescanConfirmation();
            OnRescanConfirmed?.Invoke();
        }

        /// <summary>
        /// Draw confirmation dialog if active
        /// </summary>
        private void DrawConfirmationDialog()
        {
            if (!_showingConfirmation) return;

            // Draw semi-transparent overlay
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.DrawTexture(_displayRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Draw confirmation ASCII art
            Color borderColor = GetGridColor();
            GUI.color = borderColor;

            float lineHeight = _lineSpacing;
            float charWidth = 14f;
            float artWidth = CONFIRM_ASCII_ART[0].Length * charWidth;
            float artHeight = CONFIRM_ASCII_ART.Length * lineHeight;

            float startX = _displayRect.x + (_displayRect.width - artWidth) * 0.5f;
            float startY = _displayRect.y + (_displayRect.height - artHeight) * 0.5f;

            GUIStyle confirmStyle = new GUIStyle();
            confirmStyle.fontSize = Mathf.RoundToInt(_fontSize * 0.85f);
            confirmStyle.normal.textColor = borderColor;

            for (int i = 0; i < CONFIRM_ASCII_ART.Length; i++)
            {
                Rect lineRect = new Rect(
                    startX,
                    startY + (i * lineHeight),
                    artWidth,
                    lineHeight
                );
                GUI.Label(lineRect, CONFIRM_ASCII_ART[i], confirmStyle);
            }

            // Draw YES/NO buttons (hit areas over the [YES] and [NO] text)
            float buttonY = startY + (6 * lineHeight);
            float yesX = startX + (charWidth * 3);
            float noX = startX + artWidth - (charWidth * 6);
            float buttonWidth = charWidth * 6;
            float buttonHeight = lineHeight;

            Rect yesRect = new Rect(yesX, buttonY, buttonWidth, buttonHeight);
            Rect noRect = new Rect(noX, buttonY, buttonWidth, buttonHeight);

            // Handle YES click
            if (GUI.Button(yesRect, "", GUIStyle.none))
            {
                ConfirmRescan();
            }

            // Handle NO click  
            if (GUI.Button(noRect, "", GUIStyle.none))
            {
                HideRescanConfirmation();
            }

            GUI.color = Color.white;
        }

        #endregion

        #region Updated OnGUI

        // Original OnGUI replaced with this updated version
        // This is called via the modified OnGUI method below

        #endregion

        #region Search System

        // State
        private string _searchQuery = "";
        private List<NamedStar> _allStars = new List<NamedStar>();
        private List<NamedStar> _filteredResults = new List<NamedStar>();
        private NamedStar _selectedStar = null;

        // Search debounce
        private float _lastSearchTime = 0f;
        private const float SEARCH_DEBOUNCE = 0.1f;  // 100ms debounce

        /// <summary>
        /// Initialize with star list from selector
        /// </summary>
        public void SetStarList(List<NamedStar> stars)
        {
            _allStars = stars ?? new List<NamedStar>();
            _allStars.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            
            // Clear search and show empty state
            _searchQuery = "";
            _inputBuffer = "";
            UpdateSearchResults();
            
            // Update search input display
            SetElementText("search_input", "");
        }

        /// <summary>
        /// Update search query and filter results
        /// </summary>
        public void UpdateSearch(string query)
        {
            // Debounce rapid updates
            if (Time.time - _lastSearchTime < SEARCH_DEBOUNCE)
            {
                return;
            }
            _lastSearchTime = Time.time;
            
            _searchQuery = query?.ToUpper() ?? "";
            
            // Update search input display
            SetElementText("search_input", string.IsNullOrEmpty(_searchQuery) ? "..." : _searchQuery);
            
            // Filter results
            UpdateSearchResults();
        }

        /// <summary>
        /// Filter stars based on search query
        /// </summary>
        private void UpdateSearchResults()
        {
            _filteredResults.Clear();
            
            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                // Show empty state message in results
                ShowEmptyResultsState();
                return;
            }
            
            string query = _searchQuery.ToLowerInvariant();
            
            // Filter: match name or HIP ID
            foreach (var star in _allStars)
            {
                if (_filteredResults.Count >= MAX_SEARCH_RESULTS)
                    break;
                
                bool nameMatch = star.Name.ToLowerInvariant().Contains(query);
                bool hipMatch = star.HipparcosID.ToString().Contains(query);
                
                if (nameMatch || hipMatch)
                {
                    _filteredResults.Add(star);
                }
            }
            
            // Update result elements
            UpdateResultElements();
        }

        /// <summary>
        /// Show empty state (ENTER TERMS or NO RESULT)
        /// </summary>
        private void ShowEmptyResultsState()
        {
            // Hide all result rows
            for (int i = 0; i < MAX_SEARCH_RESULTS; i++)
            {
                var element = _resultElements[i];
                element.IsVisible = false;
                element.IsDirty = true;
            }
            
            // Show message in first row
            if (_resultElements.Count > 0)
            {
                var msgElement = _resultElements[0];
                msgElement.IsVisible = true;
                msgElement.StaticText = "";
                msgElement.DynamicText = string.IsNullOrEmpty(_searchQuery) ? "ENTER TERMS" : "NO RESULT";
                msgElement.AssociatedData = null;
                msgElement.IsDirty = true;
            }
        }

        /// <summary>
        /// Update result elements with filtered stars
        /// </summary>
        private void UpdateResultElements()
        {
            for (int i = 0; i < MAX_SEARCH_RESULTS; i++)
            {
                var element = _resultElements[i];
                
                if (i < _filteredResults.Count)
                {
                    var star = _filteredResults[i];
                    element.IsVisible = true;
                    element.StaticText = "•";
                    element.DynamicText = star.Name;
                    element.AssociatedData = star;
                    element.IsDirty = true;
                }
                else
                {
                    element.IsVisible = false;
                    element.AssociatedData = null;
                }
            }
        }

        #endregion

        #region Star Selection

        // External selector reference
        private KartographerSelector _selector;

        /// <summary>
        /// Set the selector for bidirectional sync
        /// </summary>
        public void SetSelector(KartographerSelector selector)
        {
            _selector = selector;
            
            // Subscribe to external selection events
            if (_selector != null)
            {
                _selector.OnStarLockedViaClick = OnExternalStarSelected;
            }
        }

        /// <summary>
        /// Select a star (internal + external sync)
        /// </summary>
        public void SelectStar(NamedStar star)
        {
            if (star == null) return;
            
            _selectedStar = star;
            
            // Update display
            SetStarData(star);
            
            // Sync to selector (if available)
            if (_selector != null)
            {
                _selector.SelectStarByHipId(star.HipparcosID);
                _selector.SetMouseHoverMode(true);
                _selector.SelectionCircleEnabled = true;
            }
            
            // Notify subscribers
            OnStarSelected?.Invoke(star);
        }

        /// <summary>
        /// Called when user selects a star via point-and-click in the game world
        /// </summary>
        private void OnExternalStarSelected(NamedStar star)
        {
            if (star == null) return;
            
            // Update our selection to match
            _selectedStar = star;
            SetStarData(star);
            
            Debug.Log($"[HolographicDisplay] External selection synced: {star.Name} (HIP {star.HipparcosID})");
        }

        /// <summary>
        /// Get currently selected star
        /// </summary>
        public NamedStar GetSelectedStar()
        {
            return _selectedStar;
        }

        /// <summary>
        /// Clear current selection
        /// </summary>
        public void ClearSelection()
        {
            _selectedStar = null;
            ClearStarData();
        }

        #endregion

        #region Keyboard Input

        // Input state
        private bool _capturingInput = false;
        private string _inputBuffer = "";
        private HolographicTextElement _inputElement = null;

        /// <summary>
        /// Process keyboard events (updated for edit mode)
        /// </summary>
        private void HandleKeyboardInput()
        {
            // Edit mode has priority
            if (_isEditing)
            {
                HandleEditInput();
                return;
            }
            
            Event e = Event.current;
            
            if (e.type != EventType.KeyDown)
                return;
            
            // Handle ESC to clear selection/close dialogs
            if (e.keyCode == KeyCode.Escape)
            {
                if (_showingConfirmation)
                {
                    HideRescanConfirmation();
                    e.Use();
                    return;
                }
                
                if (_showingScanScreen)
                {
                    HideScanScreen();
                    e.Use();
                    return;
                }
                
                // Clear selection
                ClearSelection();
                e.Use();
                return;
            }
            
            // Handle Enter to activate search/selection
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                if (_filteredResults.Count > 0 && !string.IsNullOrEmpty(_searchQuery))
                {
                    // Select first result
                    SelectStar(_filteredResults[0]);
                    e.Use();
                }
                return;
            }
            
            // Handle typing for search input
            if (e.character != '\0' && !char.IsControl(e.character))
            {
                _inputBuffer += char.ToUpper(e.character);
                UpdateSearch(_inputBuffer);
                e.Use();
                return;
            }
            
            // Handle backspace
            if (e.keyCode == KeyCode.Backspace)
            {
                if (_inputBuffer.Length > 0)
                {
                    _inputBuffer = _inputBuffer.Substring(0, _inputBuffer.Length - 1);
                    UpdateSearch(_inputBuffer);
                    e.Use();
                }
                return;
            }
            
            // Handle delete (clear search)
            if (e.keyCode == KeyCode.Delete)
            {
                _inputBuffer = "";
                UpdateSearch("");
                e.Use();
                return;
            }
        }

        /// <summary>
        /// Enable/disable input capture mode
        /// </summary>
        public void SetInputCapture(bool capture)
        {
            _capturingInput = capture;
        }

        #endregion
        
        #region Unity Lifecycle
        
        /// <summary>
        /// Unity Update callback - cursor blink in edit mode
        /// </summary>
        private void Update()
        {
            if (!_isVisible) return;
            
            // Update cursor blink in edit mode
            UpdateCursorBlink();
        }
        
        #endregion

        #region Helper Methods

        /// <summary>
        /// Get element by ID
        /// </summary>
        private HolographicTextElement GetElement(string elementId)
        {
            _elements.TryGetValue(elementId, out var element);
            return element;
        }

        /// <summary>
        /// Check if star list is empty
        /// </summary>
        public bool HasStars()
        {
            return _allStars != null && _allStars.Count > 0;
        }

        /// <summary>
        /// Get count of filtered results
        /// </summary>
        public int GetResultCount()
        {
            return _filteredResults?.Count ?? 0;
        }

        #endregion
    }
}
