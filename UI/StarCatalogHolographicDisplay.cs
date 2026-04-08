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
        
        // DEBUG: Instance tracking
        private static int s_instanceCount = 0;
        private int _instanceId;
        #endregion

        #region State
        private bool _isVisible = false;
        private bool _displayPowered = false;
        private float _powerOnTime = 0f;
        private float _borderTypeOnProgress = 0f;
        private const float BORDER_TYPE_ON_DURATION = 2.0f;  // 2.0s for Layer 1 & 2 (border + labels) - slower to match visible typing speed
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

        #region Screen State
        private enum ScreenState { Main, Scan, ConfirmRescan }
        private ScreenState _currentScreen = ScreenState.Main;
        #endregion

        #region Layer 2 Textures (Border + Labels)
        // Layer 2: Combined border + labels textures per screen
        private RenderTexture _mainBorderLabelsTexture;
        private RenderTexture _scanBorderLabelsTexture;
        private RenderTexture _confirmBorderLabelsTexture;
        private bool _mainBorderLabelsDirty = true;
        private bool _scanBorderLabelsDirty = true;
        private bool _confirmBorderLabelsDirty = true;
        #endregion

        #region Layer 2 Content Strings
        // Main screen Layer 2 content (border + labels)
        private static readonly string[] MAIN_LAYER2_LINES = new string[]
        {
            "                                                           ",
            "                                                           ",
            "  HIP:                                                     ",
            "  NAME:                                                    ",
            "  DISTANCE:                                                ",
            "  SPECTRAL:                                                ",
            "  MAG:                                                     ",
            "  CONST:                                                   ",
            "                                                           ",
            "                                                           ",
            "  SEARCH                                                   ",
            "  ►                                                        ",
            "                                                           "
        };

        // SCAN screen Layer 2 content (border + SCAN ASCII art)
        private static readonly string[] SCAN_LAYER2_LINES = new string[]
        {
            "                                                           ",
            "                                                           ",
            "                                                           ",
            "          ╔════════════════════════════════════╗           ",
            "          ║ ███████╗ ██████╗ █████╗ ███╗   ██╗ ║           ",
            "          ║ ██╔════╝██╔════╝██╔══██╗████╗  ██║ ║           ",
            "          ║ ███████╗██║     ███████║██╔██╗ ██║ ║           ",
            "          ║ ╚════██║██║     ██╔══██║██║╚██╗██║ ║           ",
            "          ║ ███████║╚██████╗██║  ██║██║ ╚████║ ║           ",
            "          ║ ╚══════╝ ╚═════╝╚═╝  ╚═╝╚═╝  ╚═══╝ ║           ",
            "          ╚════════════════════════════════════╝           ",
            "                                                           ",
            "                                                           "
        };

        // Confirm screen Layer 2 content (border + text)
        private static readonly string[] CONFIRM_LAYER2_LINES = new string[]
        {
            "                                                           ",
            "                                                           ",
            "                                                           ",
            "                !STAR NAMES WILL BE RESET!                 ",
            "                                                           ",
            "                                                           ",
            "                                                           ",
            "                                                           ",
            "                                                           ",
            "                                                           ",
            "   [YES]                                            [NO]   ",
            "                                                           ",
            "                                                           "
        };
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
            // DEBUG: Assign instance ID
            _instanceId = ++s_instanceCount;
            Debug.Log($"[HolographicDisplay] Instance #{_instanceId} initialized");
            
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
            // NOTE: Labels are now rendered in Layer 2 (combined border+labels texture)
            // Only value fields and interactive elements are created here (Layer 3)

            AddElement("hip_value", TextElementType.Value, "", "", HolographicLayoutConfig.HIP_VALUE_POS, 0.1f);
            AddElement("name_value", TextElementType.Editable, "", "", HolographicLayoutConfig.NAME_VALUE_POS, 0.3f);
            AddElement("distance_value", TextElementType.Value, "", "", HolographicLayoutConfig.DISTANCE_VALUE_POS, 0.5f);
            AddElement("spectral_value", TextElementType.Value, "", "", HolographicLayoutConfig.SPECTRAL_VALUE_POS, 0.7f);
            AddElement("mag_value", TextElementType.Value, "", "", HolographicLayoutConfig.MAG_VALUE_POS, 0.9f);
            AddElement("const_value", TextElementType.Value, "", "", HolographicLayoutConfig.CONST_VALUE_POS, 1.1f);

            // Search elements (Layer 3 - interactive)
            AddElement("search_input", TextElementType.Input, "", "...", HolographicLayoutConfig.SEARCH_INPUT_POS, 1.6f);
            AddElement("rescan_button", TextElementType.Label, "", "[RESCAN]", HolographicLayoutConfig.RESCAN_BUTTON_POS, 1.7f);
            AddElement("selected_star", TextElementType.Value, "", "", HolographicLayoutConfig.SELECTED_STAR_POS, 1.8f);

            // Add SAVE and RESET buttons (if not already present)
            if (!_elements.ContainsKey("save_button"))
            {
                AddElement("save_button", TextElementType.Label, "", "[SAVE]",
                    HolographicLayoutConfig.SAVE_BUTTON_POS, 1.4f);
            }
            if (!_elements.ContainsKey("reset_button"))
            {
                AddElement("reset_button", TextElementType.Label, "", "[RESET]",
                    HolographicLayoutConfig.RESET_BUTTON_POS, 1.45f);
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

            // Release Layer 2 textures
            ReleaseLayer2Texture(ref _mainBorderLabelsTexture, ref _mainBorderLabelsDirty);
            ReleaseLayer2Texture(ref _scanBorderLabelsTexture, ref _scanBorderLabelsDirty);
            ReleaseLayer2Texture(ref _confirmBorderLabelsTexture, ref _confirmBorderLabelsDirty);
        }

        /// <summary>
        /// Release a single Layer 2 texture
        /// </summary>
        private void ReleaseLayer2Texture(ref RenderTexture texture, ref bool dirtyFlag)
        {
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
                texture = null;
                dirtyFlag = true;
            }
        }
        #endregion

        #region IMGUI Window Rendering
        
        private void OnGUI()
        {
            // DEBUG: ModFileLogger.Log($"[DRAW-FLOW] OnGUI called, _isVisible={_isVisible}, instance={_instanceId}");
            if (!_isVisible) return;
            
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
            // Consume mouse events to prevent click-through to game world
            ConsumeMouseEventsIfOverWindow();
            
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
        
        /// <summary>
        /// Consume mouse events when over the window to prevent click-through to game.
        /// This matches the behavior of GUILayout.Window which consumes events automatically.
        /// </summary>
        private void ConsumeMouseEventsIfOverWindow()
        {
            EventType eventType = Event.current.type;
            bool isMouseEvent = (eventType == EventType.MouseDown || 
                                 eventType == EventType.MouseUp || 
                                 eventType == EventType.MouseDrag || 
                                 eventType == EventType.ScrollWheel);
            
            if (!isMouseEvent) return;
            
            // Event.mousePosition is window-relative inside GUI.Window
            Vector2 mousePos = Event.current.mousePosition;
            
            // Check against window rect (window-relative coordinates)
            // _windowRect inside GUI.Window is effectively (0, 0, width, height)
            if (mousePos.x >= 0 && mousePos.x <= _windowRect.width &&
                mousePos.y >= 0 && mousePos.y <= _windowRect.height)
            {
                Event.current.Use();
            }
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
            // Draw black background for CRT area (Layer 1)
            GUI.color = Color.black;
            Rect crtRect = new Rect(
                BORDER_THICKNESS, 
                TITLE_BAR_HEIGHT + BORDER_THICKNESS,
                _windowRect.width - BORDER_THICKNESS * 2,
                _windowRect.height - TITLE_BAR_HEIGHT - BORDER_THICKNESS * 2
            );
            GUI.DrawTexture(crtRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            
            // 3-Layer rendering based on current screen state
            switch (_currentScreen)
            {
                case ScreenState.Scan:
                    if (_displayPowered)
                    {
                        // Layer 2: Border + SCAN ASCII art
                        DrawLayer2(_scanBorderLabelsTexture, SCAN_LAYER2_LINES, ref _scanBorderLabelsDirty);
                        
                        // Handle click detection on SCAN art during Layout event
                        HandleScanScreenClick();
                    }
                    break;
                    
                case ScreenState.ConfirmRescan:
                    if (_displayPowered)
                    {
                        // Layer 2: Border + labels
                        DrawLayer2(_confirmBorderLabelsTexture, CONFIRM_LAYER2_LINES, ref _confirmBorderLabelsDirty);
                        // Layer 3: YES/NO buttons with highlight
                        RenderConfirmButtons();
                        HandleConfirmScreenInteraction();
                    }
                    break;
                    
                default: // ScreenState.Main
                    if (_displayPowered)
                    {
                        // Layer 1: Border only (from ASCII_BORDER_LINES)
                        DrawASCIIBorder();
                        
                        // Layer 2: Labels only (from MAIN_LAYER2_LINES)
                        DrawLayer2(_mainBorderLabelsTexture, MAIN_LAYER2_LINES, ref _mainBorderLabelsDirty);
                        
                        // Layer 3: Value fields (existing elements)
                        UpdateElements();
                        DrawElements();
                    }
                    break;
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
                // Also invalidate all Layer 2 textures as they use the same type-on progress
                InvalidateLayer2();
            }

            foreach (var element in _elements.Values)
            {
                // DEBUG: ModFileLogger.Log($"[DIAG] Element {element.ElementId}: IsDirty={element.IsDirty}, IsVisible={element.IsVisible}, TypeOnProgress={element.TypeOnProgress}");

                // Update type-on animation (only for visible elements)
                if (element.IsVisible && _powerOnTime >= element.TypeOnDelay && element.TypeOnProgress < 1f)
                {
                    float localTime = _powerOnTime - element.TypeOnDelay;
                    element.TypeOnProgress = Mathf.Clamp01(localTime / TYPE_ON_DURATION);
                    element.IsDirty = true;
                }

                // Re-render if dirty (only during Repaint to avoid GPU sync issues)
                if (element.IsDirty && element.IsVisible && Event.current.type == EventType.Repaint)
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
            int glyphCount = StarfieldNative.CR_TextLayoutEx(_textSystem, text, _fontSize, 
                color, 0f, 0f, 0f, 0.667f);  // 0.667f = 2:3 aspect ratio
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

            // Apply type-on truncation (spaces skip - they appear immediately)
            if (element.TypeOnProgress < 1f && !string.IsNullOrEmpty(fullText))
            {
                int endIndex = GetTypeOnEndIndex(fullText, element.TypeOnProgress);
                
                // FIX: Return space when no characters visible, cursor only when text has started
                if (endIndex <= 0)
                    return " ";  // Space = nothing visible
                else
                    return fullText.Substring(0, endIndex) + "^|";
            }

            return fullText;
        }

        /// <summary>
        /// Calculate the end index for type-on animation, counting only non-space characters.
        /// Spaces are included in the result but don't consume type-on time.
        /// </summary>
        private int GetTypeOnEndIndex(string text, float progress)
        {
            if (progress <= 0f) return 0;
            if (progress >= 1f || string.IsNullOrEmpty(text)) return text?.Length ?? 0;
            
            // Count non-space characters
            int totalNonSpace = 0;
            for (int i = 0; i < text.Length; i++)
                if (text[i] != ' ') totalNonSpace++;
            
            // All spaces = show all immediately
            if (totalNonSpace == 0) return text.Length;
            
            // How many non-space chars should be visible?
            int targetNonSpace = Mathf.Max(1, Mathf.RoundToInt(totalNonSpace * progress));
            
            // Find the index that includes targetNonSpace non-space characters
            int seenNonSpace = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ')
                {
                    seenNonSpace++;
                    if (seenNonSpace >= targetNonSpace)
                        return i + 1; // Include this character
                }
            }
            
            return text.Length;
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
                // Only draw during Repaint event
                if (Event.current.type == EventType.Repaint)
                {
                    Graphics.DrawTexture(
                        screenPos,              // dest rect
                        element.TextTexture,    // source texture (already has Kartographer color baked in)
                        new Rect(0, 1, 1, -1),  // source UVs: flip Y
                        0, 0, 0, 0,             // border widths
                        Color.white,            // Full color - texture has grid color baked in
                        null                    // material
                    );
                }
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

        /// <summary>
        /// Check if the custom JSON catalog file exists
        /// </summary>
        private bool HasJsonCatalog()
        {
            return !string.IsNullOrEmpty(_customJsonPath) && File.Exists(_customJsonPath);
        }

        private void PowerOn()
        {
            // DEBUG: ModFileLogger.Log("[DIAG] PowerOn() called");
            _displayPowered = true;
            
            // Check if JSON catalog exists - if not, show SCAN screen
            if (!HasJsonCatalog())
            {
                TransitionToScreen(ScreenState.Scan);
                Debug.Log("[HolographicDisplay] Power ON - No JSON catalog found, showing SCAN screen");
                return;
            }
            
            // Transition to Main screen with animation reset
            TransitionToScreen(ScreenState.Main);

            // Reset type-on animation with proper sequence:
            // 1. Border first (lowest delay)
            // 2. Labels second
            // 3. Values third (only if star selected)
            
            float currentDelay = 0f;
            
            // First: Border (if we had it as an element - currently it's a separate texture)
            // Border renders immediately when powered on
            
            // Second: Labels (HIP, NAME, DISTANCE, etc.) are now in Layer 2 texture
            // They type on as part of the combined border+labels texture
            // No individual element animation needed
            
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
            string[] searchIds = { "search_input", "rescan_button" };
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
            SetElementText("selected_star", $"{star.Name}");
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

            // Release Layer 2 textures
            if (_mainBorderLabelsTexture != null)
            {
                _mainBorderLabelsTexture.Release();
                Destroy(_mainBorderLabelsTexture);
                _mainBorderLabelsTexture = null;
            }
            if (_scanBorderLabelsTexture != null)
            {
                _scanBorderLabelsTexture.Release();
                Destroy(_scanBorderLabelsTexture);
                _scanBorderLabelsTexture = null;
            }
            if (_confirmBorderLabelsTexture != null)
            {
                _confirmBorderLabelsTexture.Release();
                Destroy(_confirmBorderLabelsTexture);
                _confirmBorderLabelsTexture = null;
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

            int glyphCount = StarfieldNative.CR_TextLayoutEx(_textSystem, text, _fontSize, 
                blackColor, 0f, 0f, 0f, 0.667f);  // 0.667f = 2:3 aspect ratio
            if (glyphCount <= 0) return;

            // Clear element texture
            RenderTexture.active = element.TextTexture;
            GL.Clear(true, true, Color.clear);
            // REMOVED: RenderTexture.active = null;  // Keep active for compositing

            // First draw the highlight background (now renders to active RT) - only during Repaint
            if (Event.current.type == EventType.Repaint)
            {
                Graphics.DrawTexture(
                    new Rect(0, 0, element.TextTexture.width, element.TextTexture.height),
                    highlightTex,
                    new Rect(0, 0, 1, 1),
                    0, 0, 0, 0,
                    new Color(1, 1, 1, 1));
            }

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
            "╔════[STAR DATA]═══════════════════╦╦═════[RESULTS]═══════╗",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "╟──────────────────────────────────╢║                     ║",
            "║                                  ║║                     ║",
            "║                                  ║║                     ║",
            "╚══════════════════════════════════╩╩═════════════════════╝"
        };

        // Render texture for the border - uses native text system
        private RenderTexture _borderTexture = null;
        private bool _borderDirty = true;

        /// <summary>
        /// Initialize the border render texture and Layer 2 textures
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

            // Initialize Layer 2 textures for all screens
            InitializeLayer2Texture(ref _mainBorderLabelsTexture, width, height, ref _mainBorderLabelsDirty);
            InitializeLayer2Texture(ref _scanBorderLabelsTexture, width, height, ref _scanBorderLabelsDirty);
            InitializeLayer2Texture(ref _confirmBorderLabelsTexture, width, height, ref _confirmBorderLabelsDirty);
        }

        /// <summary>
        /// Initialize a single Layer 2 texture
        /// </summary>
        private void InitializeLayer2Texture(ref RenderTexture texture, int width, int height, ref bool dirtyFlag)
        {
            if (texture != null) return;

            texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            texture.enableRandomWrite = true;
            texture.Create();
            dirtyFlag = true;
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

            // Apply type-on: only show portion based on progress (with cursor)
            // Spaces skip - they appear immediately without consuming type-on time
            if (_borderTypeOnProgress < 1f)
            {
                int endIndex = GetTypeOnEndIndex(borderText, _borderTypeOnProgress);
                
                // Add cursor when typing is in progress (like text elements)
                if (endIndex <= 0)
                    borderText = " ";  // Space when nothing visible yet
                else
                    borderText = borderText.Substring(0, endIndex) + "^|";
            }

            uint color = GetGridColorUint();
            float fontSize = _fontSize;

            // Layout the border text
            int glyphCount = StarfieldNative.CR_TextLayoutEx(_textSystem, borderText, fontSize, 
                color, 0f, 0f, 0f, 0.667f);  // 0.667f = 2:3 aspect ratio
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
            // Ensure border is rendered (only during Repaint to avoid GPU sync issues)
            if (_borderDirty && Event.current.type == EventType.Repaint)
            {
                RenderBorderTexture();
            }

            // Draw the border texture - type-on effect is in the text content itself
            if (_borderTexture != null && Event.current.type == EventType.Repaint)
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

        /// <summary>
        /// Mark all Layer 2 textures as dirty (e.g., on color change)
        /// </summary>
        public void InvalidateLayer2()
        {
            _mainBorderLabelsDirty = true;
            _scanBorderLabelsDirty = true;
            _confirmBorderLabelsDirty = true;
        }

        #endregion

        #region Layer 2 Rendering Methods

        /// <summary>
        /// Render Layer 2 texture (border + labels) for a specific screen
        /// </summary>
        private void RenderLayer2Texture(string[] textLines, RenderTexture targetTexture)
        {
            if (_textSystem == IntPtr.Zero) return;
            if (targetTexture == null) return;

            // Join lines with newlines
            string text = string.Join("\n", textLines);

            // Apply type-on: only show portion based on progress (with cursor)
            // Spaces skip - they appear immediately without consuming type-on time
            if (_borderTypeOnProgress < 1f)
            {
                int endIndex = GetTypeOnEndIndex(text, _borderTypeOnProgress);
                
                // Add cursor when typing is in progress
                if (endIndex <= 0)
                    text = " ";  // Space when nothing visible yet
                else
                    text = text.Substring(0, endIndex) + "^|";
            }

            uint color = GetGridColorUint();

            // Layout the text
            int glyphCount = StarfieldNative.CR_TextLayoutEx(_textSystem, text, _fontSize, 
                color, 0f, 0f, 0f, 0.667f);  // 0.667f = 2:3 aspect ratio
            if (glyphCount <= 0) return;

            // Clear texture
            RenderTexture.active = targetTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;

            // Dispatch to render
            StarfieldNative.CR_TextDispatch(
                _textSystem,
                targetTexture.GetNativeTexturePtr(),
                glyphCount,
                targetTexture.width,
                targetTexture.height);
        }

        /// <summary>
        /// Draw Layer 2 texture (border + labels) for the current screen
        /// </summary>
        private void DrawLayer2(RenderTexture layer2Texture, string[] contentLines, ref bool dirtyFlag)
        {
            // Only draw during Repaint event
            if (Event.current.type != EventType.Repaint) return;

            // Re-render if dirty
            if (dirtyFlag && layer2Texture != null && contentLines != null)
            {
                RenderLayer2Texture(contentLines, layer2Texture);
                dirtyFlag = false;
            }

            // Draw the texture with UV flip for correct orientation
            if (layer2Texture != null)
            {
                Graphics.DrawTexture(
                    _displayRect,           // dest rect (screen position)
                    layer2Texture,          // source texture
                    new Rect(0, 1, 1, -1),  // source UVs: flip Y
                    0, 0, 0, 0,             // border widths
                    Color.white,            // Full color - texture has grid color baked in
                    null                    // material
                );
            }
        }

        #endregion

        #region Screen Transition

        /// <summary>
        /// Transition to a new screen with proper animation reset.
        /// This is the ONLY way to change screens - never set _currentScreen directly.
        /// </summary>
        private void TransitionToScreen(ScreenState newScreen)
        {
            // 1. Hide current screen elements
            HideCurrentScreenElements();
            
            // 2. Switch screen state
            _currentScreen = newScreen;
            
            // 3. Reset animation timers
            _powerOnTime = 0f;
            _borderTypeOnProgress = 0f;
            
            // 4. Reset element animations
            ResetAllElementAnimations();
            
            // 5. Mark new screen's Layer 2 dirty and set visibility flags
            switch (newScreen)
            {
                case ScreenState.Main:
                    _mainBorderLabelsDirty = true;
                    _showingScanScreen = false;
                    _showingConfirmation = false;
                    // Show main elements
                    foreach (var element in _elements.Values)
                    {
                        element.IsVisible = true;
                    }
                    break;
                case ScreenState.Scan:
                    _scanBorderLabelsDirty = true;
                    _showingScanScreen = true;
                    _showingConfirmation = false;
                    // Hide all main elements for scan screen
                    foreach (var element in _elements.Values)
                    {
                        element.IsVisible = false;
                    }
                    break;
                case ScreenState.ConfirmRescan:
                    _confirmBorderLabelsDirty = true;
                    _showingConfirmation = true;
                    // Scan screen may be showing underneath confirmation
                    // Don't change _showingScanScreen - confirmation is a dialog overlay
                    break;
            }
        }

        /// <summary>
        /// Hide current screen elements before transitioning
        /// </summary>
        private void HideCurrentScreenElements()
        {
            switch (_currentScreen)
            {
                case ScreenState.Main:
                    // Hide all main screen value elements
                    foreach (var element in _elements.Values)
                    {
                        element.IsVisible = false;
                        element.TypeOnProgress = 0f;
                    }
                    break;
                case ScreenState.Scan:
                    // Scan screen has no value elements to hide
                    // Just reset the pressed state
                    _scanPressed = false;
                    break;
                case ScreenState.ConfirmRescan:
                    // Hide confirm buttons
                    _confirmYesSelected = false;
                    _confirmNoSelected = false;
                    break;
            }
        }

        /// <summary>
        /// Reset all element animations for fresh type-on effect
        /// </summary>
        private void ResetAllElementAnimations()
        {
            // Reset main screen elements
            foreach (var element in _elements.Values)
            {
                element.TypeOnProgress = 0f;
                element.IsDirty = true;
            }
            
            // Reset confirm screen state
            _confirmYesSelected = false;
            _confirmNoSelected = false;
        }

        #endregion

        #region SCAN Screen

        // State
        private bool _showingScanScreen = false;
        private HolographicTextElement[] _scanScreenElements;
        private bool _scanPressed = false;  // Track mouse press state for SCAN screen click

        // ASCII art for SCAN
        private static readonly string[] SCAN_ASCII_ART = new string[]
        {
            "╔═════════════════════[NO DATA]═══════════════════════════╗",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "╚═════════════════════════════════════════════════════════╝"
        };

        /// <summary>
        /// Show the SCAN screen with ASCII art
        /// </summary>
        public void ShowScanScreen()
        {
            TransitionToScreen(ScreenState.Scan);
            Debug.Log("[HolographicDisplay] Showing SCAN screen with animation reset");
        }

        /// <summary>
        /// Hide SCAN screen and return to main display
        /// </summary>
        public void HideScanScreen()
        {
            TransitionToScreen(ScreenState.Main);
            Debug.Log("[HolographicDisplay] Hiding SCAN screen, returning to Main");
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

        /// <summary>
        /// Handle click detection on SCAN screen - triggers rescan when SCAN art is clicked
        /// </summary>
        private void HandleScanScreenClick()
        {
            // Calculate SCAN box bounds (centered 38x8 character box)
            float lineHeight = _lineSpacing;
            float charWidth = 14f;  // Approximate monospace char width
            int scanWidthChars = 38;  // Width of SCAN_LAYER2_LINES[0]
            int scanHeightLines = 8;  // Number of lines in SCAN_LAYER2_LINES
            
            float artWidth = scanWidthChars * charWidth;
            float artHeight = scanHeightLines * lineHeight;

            float startX = _displayRect.x + (_displayRect.width - artWidth) * 0.5f;
            float startY = _displayRect.y + (_displayRect.height - artHeight) * 0.5f;

            Rect scanBoxRect = new Rect(startX, startY, artWidth, artHeight);
            
            // Check if mouse is within SCAN box bounds (during non-repaint events for responsiveness)
            Vector2 mousePos = Event.current.mousePosition;
            if (scanBoxRect.Contains(mousePos))
            {
                // Handle mouse down/up for click detection
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    _scanPressed = true;
                }
                else if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
                {
                    if (_scanPressed)
                    {
                        // Click detected on SCAN art - trigger rescan and transition to Main
                        Debug.Log("[HolographicDisplay] SCAN art clicked - triggering rescan");
                        OnRescanConfirmed?.Invoke();
                        // Transition to Main screen after triggering rescan
                        TransitionToScreen(ScreenState.Main);
                    }
                    _scanPressed = false;
                }
            }
            else
            {
                // Mouse outside SCAN box - cancel press
                if (Event.current.type == EventType.MouseUp)
                {
                    _scanPressed = false;
                }
            }
        }

        #endregion

        #region Confirm Screen Interaction

        // Confirm box dimensions (54 chars wide x 13 lines tall)
        private const int CONFIRM_BOX_WIDTH_CHARS = 54;
        private const int CONFIRM_BOX_HEIGHT_LINES = 13;
        private const float CONFIRM_CHAR_WIDTH = 14f;  // Approximate monospace char width
        
        /// <summary>
        /// Calculate the centered confirm box rectangle in screen coordinates
        /// </summary>
        private Rect GetConfirmBoxRect()
        {
            float lineHeight = _lineSpacing;
            float charWidth = CONFIRM_CHAR_WIDTH;
            
            float boxWidth = CONFIRM_BOX_WIDTH_CHARS * charWidth;
            float boxHeight = CONFIRM_BOX_HEIGHT_LINES * lineHeight;
            
            float startX = _displayRect.x + (_displayRect.width - boxWidth) * 0.5f;
            float startY = _displayRect.y + (_displayRect.height - boxHeight) * 0.5f;
            
            return new Rect(startX, startY, boxWidth, boxHeight);
        }
        
        /// <summary>
        /// Handle YES/NO button interaction on Confirm screen
        /// </summary>
        private void HandleConfirmScreenInteraction()
        {
            Rect confirmBoxRect = GetConfirmBoxRect();
            Vector2 mousePos = Event.current.mousePosition;
            
            // Calculate button positions within the confirm box
            float lineHeight = _lineSpacing;
            float charWidth = CONFIRM_CHAR_WIDTH;
            
            // YES button at character position (3, 10) within the box
            float yesX = confirmBoxRect.x + (charWidth * 3);
            float yesY = confirmBoxRect.y + (lineHeight * 10);
            float buttonWidth = charWidth * 6;  // "[YES]" is 6 chars
            float buttonHeight = lineHeight;
            
            Rect yesRect = new Rect(yesX, yesY, buttonWidth, buttonHeight);
            
            // NO button at character position (47, 10) within the box
            float noX = confirmBoxRect.x + (charWidth * 47);
            float noY = confirmBoxRect.y + (lineHeight * 10);
            Rect noRect = new Rect(noX, noY, buttonWidth, buttonHeight);
            
            // Check hover states
            bool wasYesSelected = _confirmYesSelected;
            bool wasNoSelected = _confirmNoSelected;
            
            _confirmYesSelected = yesRect.Contains(mousePos);
            _confirmNoSelected = noRect.Contains(mousePos);
            
            // Mark for re-render if hover state changed
            if (_confirmYesSelected != wasYesSelected || _confirmNoSelected != wasNoSelected)
            {
                // Buttons are rendered as part of Layer 3 (handled separately)
            }
            
            // Handle mouse clicks
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                if (_confirmYesSelected)
                {
                    ConfirmRescan();
                }
                else if (_confirmNoSelected)
                {
                    HideRescanConfirmation();
                }
            }
        }
        
        /// <summary>
        /// Render YES/NO buttons with highlight state (Layer 3)
        /// </summary>
        private void RenderConfirmButtons()
        {
            // Only render during Repaint event
            if (Event.current.type != EventType.Repaint) return;
            
            Rect confirmBoxRect = GetConfirmBoxRect();
            float lineHeight = _lineSpacing;
            float charWidth = CONFIRM_CHAR_WIDTH;
            
            // YES button position
            float yesX = confirmBoxRect.x + (charWidth * 3);
            float yesY = confirmBoxRect.y + (lineHeight * 10);
            float buttonWidth = charWidth * 6;
            float buttonHeight = lineHeight;
            
            Rect yesRect = new Rect(yesX, yesY, buttonWidth, buttonHeight);
            
            // NO button position
            float noX = confirmBoxRect.x + (charWidth * 47);
            float noY = confirmBoxRect.y + (lineHeight * 10);
            Rect noRect = new Rect(noX, noY, buttonWidth, buttonHeight);
            
            // Render YES button with highlight if selected
            if (_confirmYesSelected)
            {
                RenderConfirmButtonHighlighted(yesRect, "[YES]");
            }
            else
            {
                RenderConfirmButtonNormal(yesRect, "[YES]");
            }
            
            // Render NO button with highlight if selected
            if (_confirmNoSelected)
            {
                RenderConfirmButtonHighlighted(noRect, "[NO]");
            }
            else
            {
                RenderConfirmButtonNormal(noRect, "[NO]");
            }
        }
        
        /// <summary>
        /// Render a confirmation button in normal state
        /// </summary>
        private void RenderConfirmButtonNormal(Rect rect, string text)
        {
            if (_textSystem == IntPtr.Zero) return;
            
            // Use temporary texture for button
            RenderTexture buttonTexture = RenderTexture.GetTemporary(
                Mathf.RoundToInt(rect.width), 
                Mathf.RoundToInt(rect.height), 
                0, 
                RenderTextureFormat.ARGB32);
            buttonTexture.enableRandomWrite = true;
            
            uint color = GetGridColorUint();
            int glyphCount = StarfieldNative.CR_TextLayoutEx(_textSystem, text, _fontSize, 
                color, 0f, 0f, 0f, 0.667f);  // 0.667f = 2:3 aspect ratio
            if (glyphCount > 0)
            {
                RenderTexture.active = buttonTexture;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = null;
                
                StarfieldNative.CR_TextDispatch(
                    _textSystem,
                    buttonTexture.GetNativeTexturePtr(),
                    glyphCount,
                    buttonTexture.width,
                    buttonTexture.height);
                
                Graphics.DrawTexture(
                    rect,
                    buttonTexture,
                    new Rect(0, 1, 1, -1),
                    0, 0, 0, 0,
                    Color.white,
                    null);
            }
            
            RenderTexture.ReleaseTemporary(buttonTexture);
        }
        
        /// <summary>
        /// Render a confirmation button with highlight background (2-pass selection rendering)
        /// </summary>
        private void RenderConfirmButtonHighlighted(Rect rect, string text)
        {
            if (_textSystem == IntPtr.Zero) return;
            
            // Use temporary texture for button
            RenderTexture buttonTexture = RenderTexture.GetTemporary(
                Mathf.RoundToInt(rect.width), 
                Mathf.RoundToInt(rect.height), 
                0, 
                RenderTextureFormat.ARGB32);
            buttonTexture.enableRandomWrite = true;
            
            // Pass 1: Render highlight background
            RenderTexture.active = buttonTexture;
            Color highlightColor = GetGridColor();
            highlightColor.a = 0.3f;
            GL.Clear(true, true, highlightColor);
            RenderTexture.active = null;
            
            // Pass 2: Render black text on top
            uint blackColor = 0xFF000000;  // ARGB black
            int glyphCount = StarfieldNative.CR_TextLayoutEx(_textSystem, text, _fontSize, 
                blackColor, 0f, 0f, 0f, 0.667f);  // 0.667f = 2:3 aspect ratio
            if (glyphCount > 0)
            {
                StarfieldNative.CR_TextDispatch(
                    _textSystem,
                    buttonTexture.GetNativeTexturePtr(),
                    glyphCount,
                    buttonTexture.width,
                    buttonTexture.height);
            }
            
            // Draw to screen
            Graphics.DrawTexture(
                rect,
                buttonTexture,
                new Rect(0, 1, 1, -1),
                0, 0, 0, 0,
                Color.white,
                null);
            
            RenderTexture.ReleaseTemporary(buttonTexture);
        }
        
        #endregion

        #region Rescan Confirmation

        // State
        private bool _showingConfirmation = false;
        
        // Confirm screen state
        private bool _confirmYesSelected = false;
        private bool _confirmNoSelected = false;

        // ASCII art for confirmation dialog
        private static readonly string[] CONFIRM_ASCII_ART = new string[]
        {
            "╔════════════════════[ARE YOU SURE?]══════════════════════╗",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "║                                                         ║",
            "╚═════════════════════════════════════════════════════════╝"
        };

        /// <summary>
        /// Show rescan confirmation dialog
        /// </summary>
        private void ShowRescanConfirmation()
        {
            TransitionToScreen(ScreenState.ConfirmRescan);
            Debug.Log("[HolographicDisplay] Showing rescan confirmation dialog");
        }

        /// <summary>
        /// Hide rescan confirmation dialog
        /// </summary>
        private void HideRescanConfirmation()
        {
            TransitionToScreen(ScreenState.Main);
            Debug.Log("[HolographicDisplay] Hiding confirmation dialog, returning to Main");
        }

        /// <summary>
        /// Confirm rescan action
        /// </summary>
        private void ConfirmRescan()
        {
            // Trigger rescan
            OnRescanConfirmed?.Invoke();
            // Transition to Main screen with animation reset
            TransitionToScreen(ScreenState.Main);
            Debug.Log("[HolographicDisplay] Rescan confirmed - transitioning to Main");
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
