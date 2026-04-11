using CinematicShaders.Core;
using CinematicShaders.Native;
using CinematicShaders.UI.Screens;
using CinematicShaders.UI.Screens.Layers;
using CinematicShaders.UI.Animation;
using CinematicShaders.UI.Content;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static FinePrint.ContractDefs;

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
        
        // Layer animation progress (sequential type-on)
        private float _layer1TypeOnProgress = 0f;  // Border (Layer 1)
        private float _layer2TypeOnProgress = 0f;  // Labels (Layer 2)
        private const float LAYER_1_DURATION = 2.0f;   // 2.0s for border
        private const float LAYER_2_DURATION = 2.0f;   // 2.0s for labels
        private const float LAYER_2_DELAY = 2.0f;      // Start after Layer 1
        private const float LAYER_3_DELAY = 4.0f;      // Start after Layer 2
        
        // Legacy variable - keep for compatibility but use _layer1TypeOnProgress
        private float _borderTypeOnProgress = 0f;
        private const float BORDER_TYPE_ON_DURATION = 2.0f;
        
        // Cursor state for edit mode (Workstream C - Layer 3 refactor)
        private float _cursorBlinkTimer = 0f;
        private bool _cursorVisible = true;
        private const float CURSOR_BLINK_INTERVAL = 0.5f; // 500ms
        
        // Track which element is being edited
        private string _editingElementId = null;
        private string _editBuffer = "";
        
        private HolographicDisplaySize _displaySize = HolographicDisplaySize.Medium;
        private float _fontSize = 24f;
        private float _lineSpacing = 32f;
        
        // IMGUI Window
        private Rect _windowRect = new Rect(0, 0, 616, 746);  // Will be set based on display size
        private bool _stylesInitialized = false;
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

        // Display position (set by parent)
        private Rect _displayRect;

        // Render textures for composite output
        private RenderTexture _displayTexture = null;
        
        // Screen manager for screen state handling
        private ScreenManager _screenManager;
        #endregion

        // Note: ScreenState enum removed - now using string ScreenName ("Main", "Scan", "ConfirmRescan")
        // Screen state is managed by ScreenManager

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
            "  HIP:                                                     ",
            "  NAME:                                                    ",
            "  DISTANCE:                                                ",
            "  SPECTRAL:                                                ",
            "  MAG:                                                     ",
            "  CONST:                                                   ",
            "                                                           ",
            "                 [SAVE]   [RESET]                          ",
            "                                                           ",
            "  SEARCH                  [RESCAN]                         ",
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

        /// <summary>
        /// LAYER 3 DUMMY CONTENT - For layout calibration in design software
        /// 
        /// INSTRUCTIONS FOR MANUAL FILL-IN:
        /// 1. Replace the placeholder values below with data from a known star
        /// 2. Build and run the mod
        /// 3. Open the Star Console and click "Export Textures (Debug)"
        /// 4. Find DummyLayer3_{timestamp}.png in TextureExports folder
        /// 5. Load this PNG in your design software along with Layer1 and Layer2
        /// 6. Position Layer 3 elements to match the text positions
        /// 7. Record the top-left pixel coordinates of each element
        /// 8. Provide coordinates for conversion to UV/screen space
        /// </summary>
        private static readonly string[] LAYER3_DUMMY_LINES = new string[]
        {
            "╔════[STAR DATA]═══════════════════╦╦═════[RESULTS]═══════╗",
            "║ HIP:      32349                  ║║ •Star 1             ║",
            "║ NAME:     Sirius                 ║║ •Star 2             ║",
            "║ DISTANCE: 8.6 LY                 ║║ •Star 3             ║",
            "║ SPECTRAL: A                      ║║ •Star 4             ║",
            "║ MAG:      -1.44                  ║║ •Star 5             ║",
            "║ CONST:    Canis Major            ║║ •Star 6             ║",
            "║                                  ║║ •Star 7             ║",
            "║                [SAVE]   [RESET]  ║║ •Star 8             ║",
            "╟──────────────────────────────────╢║ •Star 9             ║",
            "║ SEARCH                  [RESCAN] ║║ •Star 10            ║",
            "║ ► Sirius                         ║║ •Star 11            ║",
            "╚══════════════════════════════════╩╩═════════════════════╝"
        };
        #endregion

        #region JSON Paths (DEPRECATED - Now managed by StarCatalogStateManager)
        
        public void SetJsonPaths(string customPath, string defaultPath)
        {
            // DEPRECATED: Paths now managed by StarCatalogStateManager
            // This method kept for backward compatibility but does nothing
            Debug.Log("[HolographicDisplay] SetJsonPaths is deprecated, using StarCatalogStateManager");
        }
        
        /// <summary>
        /// Event handler for catalog changed events from StarCatalogStateManager
        /// </summary>
        private void HandleCatalogChanged(CatalogChangedEventArgs args)
        {
            Debug.Log($"[HolographicDisplay] Catalog changed event: {args.NewCatalogPath}");
            // Screen transition is handled by OnCatalogChanged which is called from KartographerTab
        }
        
        /// <summary>
        /// Event handler for JSON state changed events from StarCatalogStateManager
        /// </summary>
        private void HandleJsonStateChanged(JsonStateChangedEventArgs args)
        {
            if (_screenManager == null) return;
            
            var currentScreenName = _screenManager.CurrentScreenName;
            
            // React to JSON becoming available
            if (args.NewAvailability != JsonAvailability.None && currentScreenName == "Scan")
            {
                var context = new ScreenTransitionContext 
                { 
                    HasStarSelected = _selectedStar != null 
                };
                _screenManager.TransitionTo("Main", context);
            }
            // React to JSON becoming unavailable
            else if (args.NewAvailability == JsonAvailability.None && currentScreenName == "Main")
            {
                _screenManager.TransitionTo("Scan");
            }
        }
        #endregion

        #region Initialization
        public void Initialize(IntPtr sharedTextSystem, float x, float y, 
            HolographicDisplaySize size = HolographicDisplaySize.Medium,
            string customJsonPath = "", string defaultJsonPath = "",
            string catalogPath = "")
        {
            _instanceId = ++s_instanceCount;
            _textSystem = sharedTextSystem;
            
            // Calculate display dimensions based on size
            Vector2 dimensions = HolographicLayoutConfig.GetDisplayDimensions(size);
            _displayRect = new Rect(x, y, dimensions.x, dimensions.y);
            
            // Set window size including border
            _windowRect = new Rect(
                x, y,
                dimensions.x + BORDER_THICKNESS * 2,
                dimensions.y + TITLE_BAR_HEIGHT + BORDER_THICKNESS * 2
            );
            
            _fontSize = HolographicLayoutConfig.GetFontSize(size);
            _lineSpacing = HolographicLayoutConfig.GetLineSpacing(size);
            _displaySize = size;
            
            // DEPRECATED: Paths now managed by StarCatalogStateManager
            // Subscribe to events for reactive updates
            StarCatalogStateManager.OnCatalogChanged += HandleCatalogChanged;
            StarCatalogStateManager.OnJsonStateChanged += HandleJsonStateChanged;
            
            // Initialize state manager with catalog path (required for JSON state tracking)
            if (!string.IsNullOrEmpty(catalogPath))
            {
                StarCatalogStateManager.Initialize(catalogPath);
            }

            CreateElements();
            InitializeTextures();
            InitializeBorderTexture();
            
            // NEW: Initialize ScreenManager
            _screenManager = new ScreenManager(_textSystem);
            _screenManager.InitializeTextures(
                Mathf.RoundToInt(dimensions.x), 
                Mathf.RoundToInt(dimensions.y));
            InitializeScreens();
            
            // NEW: Detect JSON using centralized state manager
            bool hasValidData = StarCatalogStateManager.HasValidJson();
            string initialScreen = hasValidData ? "Main" : "Scan";
            _screenManager.TransitionTo(initialScreen, new ScreenTransitionContext { 
                IsInitialStartup = true 
            });
            
            Debug.Log($"[HolographicDisplay] Initialized at ({x}, {y}), size: {size}, " +
                      $"dataAvailable: {hasValidData}, initialScreen: {initialScreen}");
        }
        
        private void InitializeScreens()
        {
            float aspectRatio = 0.667f; // 2:3 aspect ratio for text rendering
            
            // Main screen
            var mainScreen = new MainScreen(MainScreenContent.Default, _fontSize, aspectRatio);
            ModFileLogger.Log($"[HolographicDisplay] Creating MainScreen instance {mainScreen.GetHashCode()}");
            
            // Pass elements to MainScreen for Layer 3 rendering
            var mainElements = new List<HolographicTextElement>(_elements.Values);
            mainScreen.SetElements(mainElements);
            ModFileLogger.Log($"[HolographicDisplay] MainScreen elements set, instance {mainScreen.GetHashCode()}");
            
            // Subscribe to MainScreen click events (NEW: ClickHandler-based)
            mainScreen.OnElementClicked += OnMainScreenElementClicked;
            
            _screenManager.RegisterScreen(mainScreen);
            
            // Scan screen
            var scanScreen = new ScanScreen(ScanScreenContent.Default, _fontSize, aspectRatio);
            scanScreen.OnScanClicked += () => {
                OnRescanConfirmed?.Invoke();
            };
            _screenManager.RegisterScreen(scanScreen);
            
            // Confirm screen
            var confirmScreen = new ConfirmRescanScreen(ConfirmRescanScreenContent.Default, _fontSize, aspectRatio);
            confirmScreen.OnYesClicked += () => {
                OnRescanConfirmed?.Invoke();
                _screenManager.TransitionTo("Main");
            };
            confirmScreen.OnNoClicked += () => {
                _screenManager.TransitionTo("Main");
            };
            _screenManager.RegisterScreen(confirmScreen);
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
            
            // ScreenManager textures stay at Large size (825x450)
            // Font size changes provide the "scaling" for different presets
            if (_screenManager != null)
            {
                // Just mark layers dirty so they re-render with new font size
                _screenManager.MarkAllLayersDirty();
                
                // Re-initialize screens with new font size
                InitializeScreens();
            }
            
            // Mark all elements dirty for re-render
            foreach (var element in _elements.Values)
            {
                element.IsDirty = true;
            }
            _borderDirty = true;
            
            Debug.Log($"[HolographicDisplay] Size changed to: {size}: {dimensions.x}x{dimensions.y}");
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
            // Draw title bar with PWR button and X
            DrawTitleBar();
            
            // Draw grey border area
            DrawWindowBorder();
            
            // Update display rect based on window position
            UpdateDisplayRect();
            
            // Handle mouse interaction for CRT area
            // DISABLED: Legacy pixel-based click detection - now handled by ClickHandler grid-based system
            // UpdateMouseInteraction();
            
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
            // Draw black background for CRT area (Layer 0)
            GUI.color = Color.black;
            Rect crtRect = new Rect(
                BORDER_THICKNESS, 
                TITLE_BAR_HEIGHT + BORDER_THICKNESS,
                _windowRect.width - BORDER_THICKNESS * 2,
                _windowRect.height - TITLE_BAR_HEIGHT - BORDER_THICKNESS * 2
            );
            GUI.DrawTexture(crtRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            
            // Render current screen via ScreenManager (Layers 1, 2, and 3)
            if (_displayPowered && _screenManager != null)
            {
                _screenManager.Render(_displayRect);
            }
            
            // Handle screen-specific interactions (Layer 3 interactions are handled by MainScreen)
            if (_displayPowered && _screenManager?.CurrentScreen?.ScreenName != "Main")
            {
                HandleScreenInteractions();
            }
        }
        
        private void HandleScreenInteractions()
        {
            if (_screenManager?.CurrentScreen == null) return;
            
            var currentScreenName = _screenManager.CurrentScreen.ScreenName;
            Vector2 mousePos = Event.current.mousePosition;
            bool mouseDown = Event.current.type == EventType.MouseDown && Event.current.button == 0;
            bool mouseUp = Event.current.type == EventType.MouseUp && Event.current.button == 0;
            
            switch (currentScreenName)
            {
                case "Scan":
                    var scanScreen = _screenManager.CurrentScreen as ScanScreen;
                    scanScreen?.HandleClick(mousePos, _displayRect, mouseDown);
                    break;
                    
                case "ConfirmRescan":
                    var confirmScreen = _screenManager.CurrentScreen as ConfirmRescanScreen;
                    confirmScreen?.UpdateInteraction(mousePos, _displayRect, mouseDown, mouseUp);
                    break;
                    
                case "Main":
                    // Main screen interactions handled separately via element system
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
            
            // Update Layer 1 (Border) type-on animation
            if (_layer1TypeOnProgress < 1f)
            {
                _layer1TypeOnProgress = Mathf.Clamp01(_powerOnTime / LAYER_1_DURATION);
                _borderTypeOnProgress = _layer1TypeOnProgress;  // Keep legacy in sync
                InvalidateBorder();  // Mark border dirty to re-render
            }
            
            // Update Layer 2 (Labels) type-on animation - starts after Layer 1
            if (_powerOnTime >= LAYER_2_DELAY && _layer2TypeOnProgress < 1f)
            {
                float layer2LocalTime = _powerOnTime - LAYER_2_DELAY;
                _layer2TypeOnProgress = Mathf.Clamp01(layer2LocalTime / LAYER_2_DURATION);
                InvalidateLayer2();  // Mark Layer 2 dirty to re-render
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

            // Render to texture with proper active texture handling
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = element.TextTexture;
                
                // Clear texture
                GL.Clear(true, true, Color.clear);
                
                // Dispatch to render - texture must be active for this
                StarfieldNative.CR_TextDispatch(
                    _textSystem,
                    element.TextTexture.GetNativeTexturePtr(),
                    glyphCount,
                    element.TextTexture.width,
                    element.TextTexture.height);
            }
            finally
            {
                RenderTexture.active = prevActive;
            }
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
        // NOTE: GetGridColor() and GetGridColorUint() have been consolidated to BaseScreen
        // as protected methods. All screens now inherit these methods from BaseScreen.
        // HolographicDisplay uses the StarfieldSettings directly for color lookup.
        
        /// <summary>
        /// Get the grid color based on Kartographer settings.
        /// Note: This is a local copy since HolographicDisplay doesn't inherit from BaseScreen.
        /// </summary>
        private Color GetGridColor()
        {
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

        /// <summary>
        /// Get the grid color as a uint in ARGB format for native rendering.
        /// Note: This is a local copy since HolographicDisplay doesn't inherit from BaseScreen.
        /// </summary>
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
        
        // Edit state - single source of truth via _editingElementId
        private string _originalName = "";
        
        /// <summary>
        /// Enter edit mode for a specific element (Workstream C - Layer 3 refactor).
        /// Supports both name_value and search_input.
        /// </summary>
        public void EnterEditMode(string elementId)
        {
            if (_editingElementId == elementId) return;
            
            // Exit previous edit mode without saving
            if (!string.IsNullOrEmpty(_editingElementId))
            {
                ExitEditMode(save: false);
            }
            
            _editingElementId = elementId;
            _cursorVisible = true;
            _cursorBlinkTimer = 0f;
            
            // Get current value as edit buffer
            var element = GetElement(elementId);
            if (element != null)
            {
                _editBuffer = element.DynamicText;
                element.IsEditing = true;
                element.IsDirty = true;
                
                // Set original name for potential revert on cancel
                if (elementId == "name_value")
                {
                    _originalName = _editBuffer;
                    element.IsSelecting = true;
                    element.ShowCursor = true;
                }
            }
            
            // Pass cursor state to ElementLayer via public method
            var mainScreen = _screenManager?.CurrentScreen as MainScreen;
            mainScreen?.SetCursorState(_editingElementId, _cursorVisible);
            
            Debug.Log($"[HolographicDisplay] Entered edit mode for: {elementId}");
        }
        
        /// <summary>
        /// Legacy EnterEditMode for NAME field (backward compatibility)
        /// </summary>
        private void EnterEditMode()
        {
            if (_selectedStar == null) return;
            EnterEditMode("name_value");
        }
        
        /// <summary>
        /// Exit edit mode, optionally saving changes (Workstream C - Layer 3 refactor).
        /// </summary>
        public void ExitEditMode(bool save)
        {
            if (string.IsNullOrEmpty(_editingElementId)) return;
            
            var element = GetElement(_editingElementId);
            if (element != null)
            {
                element.IsEditing = false;
                element.ShowCursor = false;
                
                if (save)
                {
                    element.DynamicText = _editBuffer.ToUpper();
                    
                    // Save based on element type
                    if (_editingElementId == "name_value")
                    {
                        SaveStarName(_editBuffer);
                    }
                    else if (_editingElementId == "search_input")
                    {
                        _searchQuery = _editBuffer.ToUpper();
                        UpdateSearch(_editBuffer);
                    }
                }
                else
                {
                    // Revert to original
                    if (_editingElementId == "name_value")
                    {
                        SetElementText("name_value", _originalName);
                    }
                }
                
                element.IsDirty = true;
            }
            
            Debug.Log($"[HolographicDisplay] Exited edit mode for {_editingElementId} (saved: {save})");
            
            _editingElementId = null;
            _editBuffer = "";
            _cursorVisible = false;
            
            // Clear cursor state in ElementLayer via public method
            var mainScreen = _screenManager?.CurrentScreen as MainScreen;
            mainScreen?.SetCursorState(null, false);
        }
        
        /// <summary>
        /// Handle edit mode keyboard input (Workstream C - Layer 3 refactor).
        /// Supports both name_value and search_input fields.
        /// </summary>
        private void HandleEditInput()
        {
            // Use _editingElementId as single source of truth for edit mode
            if (string.IsNullOrEmpty(_editingElementId)) return;
            
            string effectiveElementId = _editingElementId;
            
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;
            
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
                    UpdateEditDisplay();
                }
                e.Use();
                return;
            }
            
            // Delete to clear entire field
            if (e.keyCode == KeyCode.Delete)
            {
                _editBuffer = "";
                UpdateEditDisplay();
                e.Use();
                return;
            }
            
            // Regular character input (forced uppercase)
            if (e.character != '\0' && !char.IsControl(e.character))
            {
                _editBuffer += char.ToUpper(e.character);
                UpdateEditDisplay();
                e.Use();
                return;
            }
        }
        
        /// <summary>
        /// Update the element display with current edit buffer and cursor state.
        /// </summary>
        private void UpdateEditDisplay()
        {
            // Update the element display with edit buffer + cursor
            var element = GetElement(_editingElementId);
            if (element != null)
            {
                // Append cursor character if visible
                string displayText = _editBuffer + (_cursorVisible ? "▌" : "");
                element.DynamicText = displayText;
                element.IsDirty = true;
            }
            
            // Trigger Layer 3 redraw via ElementLayer public method
            var mainScreen = _screenManager?.CurrentScreen as MainScreen;
            mainScreen?.MarkElementLayerDirty();
        }
        
        /// <summary>
        /// Update cursor blink animation (Workstream C - Layer 3 refactor).
        /// Passes cursor state to ElementLayer for single-texture rendering.
        /// </summary>
        private void UpdateCursorBlink()
        {
            // Check if we're in edit mode
            if (string.IsNullOrEmpty(_editingElementId)) return;
            
            _cursorBlinkTimer += Time.deltaTime;
            
            if (_cursorBlinkTimer >= CURSOR_BLINK_INTERVAL)
            {
                _cursorBlinkTimer = 0f;
                _cursorVisible = !_cursorVisible;
                
                // Update display with new cursor state
                UpdateEditDisplay();
                
                // Pass cursor state to ElementLayer via public method
                var mainScreen = _screenManager?.CurrentScreen as MainScreen;
                mainScreen?.SetCursorState(_editingElementId, _cursorVisible);
            }
        }
        
        /// <summary>
        /// Pass cursor state to ElementLayer. Call from Update() when in edit mode.
        /// </summary>
        private void UpdateElementLayerCursor()
        {
            if (!string.IsNullOrEmpty(_editingElementId))
            {
                var mainScreen = _screenManager?.CurrentScreen as MainScreen;
                mainScreen?.SetCursorState(_editingElementId, _cursorVisible);
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
            
            var jsonPaths = StarCatalogStateManager.CurrentJsonPaths;
            string customJsonPath = jsonPaths.CustomJsonPath;
            
            if (string.IsNullOrEmpty(customJsonPath)) return;
            
            try
            {
                // Ensure custom JSON exists
                if (!File.Exists(customJsonPath))
                {
                    CreateCustomJson();
                }
                
                // Modify the JSON
                ModifyStarNameInJson(_selectedStar.HipparcosID, newName, customJsonPath);
                
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
            
            var jsonPaths = StarCatalogStateManager.CurrentJsonPaths;
            string defaultJsonPath = jsonPaths.DefaultJsonPath;
            string customJsonPath = jsonPaths.CustomJsonPath;
            
            if (string.IsNullOrEmpty(defaultJsonPath) || !File.Exists(defaultJsonPath))
            {
                Debug.LogError("[HolographicDisplay] Cannot reset - default JSON not found");
                return;
            }
            
            try
            {
                // Read original name from default JSON
                string originalName = GetOriginalNameFromJson(_selectedStar.HipparcosID, defaultJsonPath);
                if (string.IsNullOrEmpty(originalName))
                {
                    originalName = $"HIP {_selectedStar.HipparcosID}";
                }
                
                // Ensure custom JSON exists
                if (!File.Exists(customJsonPath))
                {
                    CreateCustomJson();
                }
                
                // Modify the JSON with original name
                ModifyStarNameInJson(_selectedStar.HipparcosID, originalName, customJsonPath);
                
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
            var jsonPaths = StarCatalogStateManager.CurrentJsonPaths;
            string defaultJsonPath = jsonPaths.DefaultJsonPath;
            string customJsonPath = jsonPaths.CustomJsonPath;
            
            if (string.IsNullOrEmpty(customJsonPath)) return;
            
            if (File.Exists(defaultJsonPath))
            {
                File.Copy(defaultJsonPath, customJsonPath);
                Debug.Log($"[HolographicDisplay] Created _Custom.json from default");
            }
            else
            {
                string minimalJson = "{\"metadata\":{\"version\":1,\"source_catalog\":\"Custom\",\"generated\":\"" + 
                    DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "\"},\"stars\":{}}";
                File.WriteAllText(customJsonPath, minimalJson);
                Debug.Log($"[HolographicDisplay] Created minimal _Custom.json");
            }
        }
        
        /// <summary>
        /// Modify star name in JSON file
        /// </summary>
        private void ModifyStarNameInJson(int hipId, string newName, string customJsonPath)
        {
            string json = File.ReadAllText(customJsonPath);
            
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
            File.WriteAllText(customJsonPath, newJson);
        }
        
        /// <summary>
        /// Get original name from default JSON
        /// </summary>
        private string GetOriginalNameFromJson(int hipId, string defaultJsonPath)
        {
            try
            {
                string json = File.ReadAllText(defaultJsonPath);
                
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
        /// Check if a valid JSON catalog exists
        /// Uses centralized StarCatalogStateManager
        /// </summary>
        private bool HasJsonCatalog()
        {
            // Use centralized state manager
            return StarCatalogStateManager.HasValidJson();
        }

        /// <summary>
        /// Validates that ScreenManager textures are ready before powering on.
        /// </summary>
        private bool ValidateBeforePowerOn()
        {
            if (_screenManager == null) return false;
            
            // Validate textures are ready
            _screenManager.ValidateTextures();
            return true;
        }

        private void PowerOn()
        {
            // Validate textures before powering on (defensive against device loss)
            if (!ValidateBeforePowerOn())
            {
                Debug.LogWarning("[HolographicDisplay] PowerOn aborted - ScreenManager not ready");
                return;
            }
            
            _displayPowered = true;
            _powerOnTime = 0f; // Reset power on time
            
            // Reset AnimationController for fresh animations
            AnimationController.Instance.Reset();
            
            // Check if JSON catalog exists
            if (!HasJsonCatalog())
            {
                _screenManager?.TransitionTo("Scan");
                Debug.Log("[HolographicDisplay] Power ON - No JSON catalog found, showing SCAN screen");
                return;
            }
            
            // Transition to Main screen
            var context = new ScreenTransitionContext 
            { 
                IsInitialStartup = true,
                HasStarSelected = _selectedStar != null 
            };
            _screenManager?.TransitionTo("Main", context);
            
            // Initialize click zones for MainScreen
            var mainScreen = _screenManager?.CurrentScreen as MainScreen;
            mainScreen?.SetClickZones();
            
            Debug.Log("[HolographicDisplay] Power ON - Main screen with animation");
        }
        
        public void OnCatalogChanged()
        {
            if (_screenManager == null) return;
            
            // Use centralized state manager
            bool hasValidData = StarCatalogStateManager.HasValidJson();
            var currentScreenName = _screenManager.CurrentScreenName;
            
            // If we have JSON but are on SCAN screen, transition to Main
            if (hasValidData && currentScreenName == "Scan")
            {
                var context = new ScreenTransitionContext 
                { 
                    HasStarSelected = _selectedStar != null 
                };
                _screenManager.TransitionTo("Main", context);
                Debug.Log("[HolographicDisplay] Catalog changed - transitioning to Main (JSON found)");
            }
            // If we don't have JSON but are on Main screen, transition to SCAN
            else if (!hasValidData && currentScreenName == "Main")
            {
                _screenManager.TransitionTo("Scan");
                Debug.Log("[HolographicDisplay] Catalog changed - transitioning to Scan (no JSON)");
            }
        }

        private void PowerOff()
        {
            _displayPowered = false;
            
            // Clear all element text (don't just hide - clear the data)
            ClearStarData();
            
            // Reset AnimationController
            AnimationController.Instance.Reset();
            
            // Hide all elements
            foreach (var element in _elements.Values)
            {
                element.IsVisible = false;
                element.TypeOnProgress = 0f;
                element.IsDirty = true;
            }
            
            // Clear click zones to prevent detection when powered off
            var mainScreen = _screenManager?.CurrentScreen as MainScreen;
            mainScreen?.ClearClickZones();
            
            Debug.Log("[HolographicDisplay] Power OFF");
        }

        public void SetStarData(NamedStar star)
        {
            ModFileLogger.Log($"[HolographicDisplay] SetStarData called for HIP {star.HipparcosID}");
            if (star == null) return;

            SetElementText("hip_value", star.HipparcosID.ToString());
            SetElementText("name_value", star.Name);
            SetElementText("distance_value", $"{star.DistanceLy:F1} LY");
            SetElementText("spectral_value", star.SpectralType);
            SetElementText("mag_value", star.Magnitude.ToString("F2"));
            SetElementText("const_value", star.Constellation);
            SetElementText("selected_star", $"{star.Name}");
            
            // Notify MainScreen of star selection for animation
            if (_screenManager?.CurrentScreen is MainScreen mainScreen)
            {
                ModFileLogger.Log($"[HolographicDisplay] Calling mainScreen.OnStarSelected(), _screenManager.CurrentScreen is {(_screenManager?.CurrentScreen?.GetType().Name ?? "NULL")}");
                mainScreen.OnStarSelected(star);
            }
        }
        
        /// <summary>
        /// Trigger type-on animation for value fields when star data changes.
        /// Sequential timing: 0.5s per element, no overlap.
        /// </summary>
        private void TriggerValueTypeOnAnimation()
        {
            if (!_displayPowered) return;
            
            // Delays are relative to _powerOnTime, so we need to add _powerOnTime
            // to make the animation start "now" rather than at time 0
            float startTime = _powerOnTime;
            float currentDelay = 0f;
            string[] valueIds = { "hip_value", "name_value", "distance_value", 
                                  "spectral_value", "mag_value", "const_value" };
            
            foreach (var id in valueIds)
            {
                if (_elements.TryGetValue(id, out var elem))
                {
                    elem.TypeOnDelay = startTime + currentDelay;  // Delay relative to "now"
                    elem.TypeOnDuration = 0.5f;
                    elem.TypeOnProgress = 0f;  // Reset to start
                    elem.IsVisible = true;
                    elem.IsDirty = true;
                    currentDelay += 0.5f;  // Next element starts after this one finishes
                }
            }
            
            // Selected star indicator last
            if (_elements.TryGetValue("selected_star", out var selElem))
            {
                selElem.TypeOnDelay = startTime + currentDelay;
                selElem.TypeOnDuration = 0.5f;
                selElem.TypeOnProgress = 0f;
                selElem.IsVisible = true;
                selElem.IsDirty = true;
            }
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
            // Clear all value fields
            SetElementText("hip_value", "");
            SetElementText("name_value", "");
            SetElementText("distance_value", "");
            SetElementText("spectral_value", "");
            SetElementText("mag_value", "");
            SetElementText("const_value", "");
            SetElementText("selected_star", "");
            
            // Trigger type-on animation for the clear (elements will type-on empty)
            TriggerValueTypeOnAnimation();
        }
        #endregion

        #region Cleanup
        private void OnDestroy()
        {
            // Unsubscribe from state manager events
            StarCatalogStateManager.OnCatalogChanged -= HandleCatalogChanged;
            StarCatalogStateManager.OnJsonStateChanged -= HandleJsonStateChanged;
            
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

            // Shutdown ScreenManager
            _screenManager?.Shutdown();
            _screenManager = null;

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
        /// Handle element click (legacy - from HolographicDisplay's own hit detection)
        /// </summary>
        private void OnElementClicked(HolographicTextElement element)
        {
            Debug.Log($"[HolographicDisplay] Clicked: {element.ElementId}");

            switch (element.ElementId)
            {
                case "name_value":
                    EnterEditMode("name_value");
                    break;
                case "search_input":
                    EnterEditMode("search_input");
                    break;
                case "rescan_button":
                    ShowRescanConfirmation();
                    break;
                case "save_button":
                    if (!string.IsNullOrEmpty(_editingElementId))
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
                    // Exit edit mode without saving before resetting
                    if (!string.IsNullOrEmpty(_editingElementId))
                    {
                        ExitEditMode(save: false);
                    }
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
        /// Handle element click from MainScreen's ClickHandler (NEW: Contract 7)
        /// </summary>
        private void OnMainScreenElementClicked(string elementId)
        {
            Debug.Log($"[HolographicDisplay] MainScreen clicked: {elementId}");
            
            switch (elementId)
            {
                case "name_value":
                    EnterEditMode("name_value");
                    break;
                case "hip_value":
                case "distance_value":
                case "spectral_value":
                case "mag_value":
                case "const_value":
                    // Value fields - could implement copy-to-clipboard or other actions
                    Debug.Log($"[HolographicDisplay] Value field clicked: {elementId}");
                    break;
                case "search_input":
                    EnterEditMode("search_input");
                    break;
                case "rescan_button":
                    ShowRescanConfirmation();
                    break;
                case "save_button":
                    if (!string.IsNullOrEmpty(_editingElementId))
                    {
                        ExitEditMode(save: true);
                    }
                    else
                    {
                        SaveStarName(_selectedStar?.Name);
                    }
                    break;
                case "reset_button":
                    ResetStarName();
                    break;
                default:
                    // Check for result row clicks
                    if (elementId.StartsWith("result_"))
                    {
                        int resultIndex = int.Parse(elementId.Substring(7));
                        var resultElement = _resultElements.Find(r => r.ElementId == elementId);
                        if (resultElement != null)
                        {
                            OnSearchResultClicked(resultElement);
                        }
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
                // CRITICAL: Exit edit mode WITHOUT saving before switching stars
                // This ensures the old edit text doesn't persist in the field
                if (!string.IsNullOrEmpty(_editingElementId))
                {
                    ExitEditMode(save: false);
                }
                
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

        // CONFIRM layer 1
        private static readonly string[] ASCII_BORDER_LINES_CONFIRM = new string[]
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
        // SCAN layer 1
        private static readonly string[] ASCII_BORDER_LINES_SCAN = new string[]
        {
            "╔═══════════════════════[NO DATA]═════════════════════════╗",
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
        private void RenderBorderTexture(string[] borderLines)
        {
            if (_textSystem == IntPtr.Zero) return;
            if (_borderTexture == null) InitializeBorderTexture();
            if (!_borderDirty) return;

            _borderDirty = false;

            // Build border text from lines
            string borderText = string.Join("\n", borderLines);

            // Apply type-on: only show portion based on progress (with cursor)
            // Spaces skip - they appear immediately without consuming type-on time
            if (_layer1TypeOnProgress < 1f)
            {
                int endIndex = GetTypeOnEndIndex(borderText, _layer1TypeOnProgress);
                
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

            // Render to texture with proper active texture handling
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = _borderTexture;
                
                // Clear texture
                GL.Clear(true, true, Color.clear);

                // Dispatch to render - texture must be active for this
                StarfieldNative.CR_TextDispatch(
                    _textSystem,
                    _borderTexture.GetNativeTexturePtr(),
                    glyphCount,
                    _borderTexture.width,
                    _borderTexture.height);
            }
            finally
            {
                // Always reset active render texture, even if an exception occurred
                RenderTexture.active = prevActive;
            }
        }

        /// <summary>
        /// Draw the full ASCII border with native text rendering
        /// </summary>
        private void DrawASCIIBorder(string[] borderLines)
        {
            // Ensure border is rendered (only during Repaint to avoid GPU sync issues)
            if (_borderDirty && Event.current.type == EventType.Repaint)
            {
                RenderBorderTexture(borderLines);
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
            if (_layer2TypeOnProgress < 1f)
            {
                int endIndex = GetTypeOnEndIndex(text, _layer2TypeOnProgress);
                
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

            // Render to texture with proper active texture handling
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = targetTexture;
                
                // Clear texture
                GL.Clear(true, true, Color.clear);

                // Dispatch to render - texture must be active for this
                StarfieldNative.CR_TextDispatch(
                    _textSystem,
                    targetTexture.GetNativeTexturePtr(),
                    glyphCount,
                    targetTexture.width,
                    targetTexture.height);
            }
            finally
            {
                // Always reset active render texture, even if an exception occurred
                RenderTexture.active = prevActive;
            }
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

        /// <summary>
        /// Render the dummy Layer 3 content to a texture for layout calibration.
        /// This allows exporting a reference image showing where values should appear.
        /// </summary>
        private void RenderDummyLayer3ToTexture(RenderTexture targetTexture)
        {
            if (_textSystem == IntPtr.Zero) return;
            if (targetTexture == null) return;

            // Join lines with newlines
            string text = string.Join("\n", LAYER3_DUMMY_LINES);

            uint color = GetGridColorUint();

            // Layout the text
            int glyphCount = StarfieldNative.CR_TextLayoutEx(_textSystem, text, _fontSize, 
                color, 0f, 0f, 0f, 0.667f);  // 0.667f = 2:3 aspect ratio
            if (glyphCount <= 0) return;

            // Render to texture with proper active texture handling
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = targetTexture;
                
                // Clear texture
                GL.Clear(true, true, Color.clear);

                // Dispatch to render - texture must be active for this
                StarfieldNative.CR_TextDispatch(
                    _textSystem,
                    targetTexture.GetNativeTexturePtr(),
                    glyphCount,
                    targetTexture.width,
                    targetTexture.height);
            }
            finally
            {
                // Always reset active render texture, even if an exception occurred
                RenderTexture.active = prevActive;
            }
        }

        #endregion

        #region Screen Transition

        /*
         * NOTE: Screen transition logic is now handled by ScreenManager.
         * The following methods have been replaced:
         * - TransitionToScreen() -> _screenManager.TransitionTo()
         * - HideCurrentScreenElements() -> Handled in individual screen OnExit()
         * - ResetAllElementAnimations() -> Handled in BaseScreen.OnEnter()
         * 
         * Obsolete fields removed:
         * - _currentScreen -> Use _screenManager.CurrentScreenName
         * - _showingScanScreen -> Check _screenManager.CurrentScreenName == "Scan"
         * - _showingConfirmation -> Check _screenManager.CurrentScreenName == "ConfirmRescan"
         */
        
        /// <summary>
        /// Reset all element animations for fresh type-on effect (kept for PowerOn)
        /// </summary>
        private void ResetAllElementAnimations()
        {
            // Reset main screen elements
            foreach (var element in _elements.Values)
            {
                element.TypeOnProgress = 0f;
                element.IsDirty = true;
            }
        }

        #endregion

        #region SCAN Screen

        // Scan screen ASCII art

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
            _screenManager?.TransitionTo("Scan");
            Debug.Log("[HolographicDisplay] Showing SCAN screen with animation reset");
        }

        /// <summary>
        /// Hide SCAN screen and return to main display
        /// </summary>
        public void HideScanScreen()
        {
            _screenManager?.TransitionTo("Main");
            Debug.Log("[HolographicDisplay] Hiding SCAN screen, returning to Main");
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
        

        
        #endregion

        #region Rescan Confirmation

        // Confirmation dialog ASCII art

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
            _screenManager?.TransitionTo("ConfirmRescan");
            Debug.Log("[HolographicDisplay] Showing rescan confirmation dialog");
        }

        /// <summary>
        /// Hide rescan confirmation dialog
        /// </summary>
        private void HideRescanConfirmation()
        {
            _screenManager?.TransitionTo("Main");
            Debug.Log("[HolographicDisplay] Hiding confirmation dialog, returning to Main");
        }

        /// <summary>
        /// Confirm rescan action
        /// </summary>
        private void ConfirmRescan()
        {
            OnRescanConfirmed?.Invoke();
            _screenManager?.TransitionTo("Main");
            Debug.Log("[HolographicDisplay] Rescan confirmed - transitioning to Main");
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
                _selector.OnStarUnlocked = OnExternalStarCleared;
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
            
            // Update visibility - we have a star selected
            if (_screenManager?.CurrentScreen is MainScreen mainScreen)
            {
                mainScreen.UpdateElementVisibility(hasStarSelected: true);
            }
            
            Debug.Log($"[HolographicDisplay] External selection synced: {star.Name} (HIP {star.HipparcosID})");
        }

        private void OnExternalStarCleared()
        {
            // Clear our selection to match
            ClearSelection();
            
            Debug.Log("[HolographicDisplay] External deselection synced - star cleared");
        }

        /// <summary>
        /// Get currently selected star
        /// </summary>
        public NamedStar GetSelectedStar()
        {
            return _selectedStar;
        }

        public void ClearSelection()
        {
            // Notify MainScreen of deselection before clearing
            if (_screenManager?.CurrentScreen is MainScreen mainScreen)
            {
                mainScreen.OnStarDeselected();
            }
            
            _selectedStar = null;
            // ClearStarData called by OnStarDeselected
        }

        #endregion

        #region Keyboard Input

        // Input state
        private bool _capturingInput = false;
        private string _inputBuffer = "";

        /// <summary>
        /// Process keyboard events (updated for edit mode)
        /// </summary>
        private void HandleKeyboardInput()
        {
            // Edit mode has priority
            if (!string.IsNullOrEmpty(_editingElementId))
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
                if (_screenManager?.CurrentScreenName == "ConfirmRescan")
                {
                    HideRescanConfirmation();
                    e.Use();
                    return;
                }
                
                if (_screenManager?.CurrentScreenName == "Scan")
                {
                    _screenManager?.TransitionTo("Main");
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
        
        private void Update()
        {
            if (!_isVisible) return;
            
            // Update cursor blink in edit mode
            UpdateCursorBlink();
            
            // Update screen manager animations ONLY when powered on
            if (_displayPowered)
            {
                _screenManager?.Update(Time.deltaTime);
            }
            
            // Update AnimationController for type-on animations
            AnimationController.Instance.Update(Time.deltaTime);
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

        /// <summary>
        /// Export all display textures to PNG files for debugging/layout.
        /// Files are saved to PluginData/TextureExports/
        /// </summary>
        public void ExportAllTexturesToPng()
        {
            try
            {
                string exportDir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "CinematicShaders", "PluginData", "TextureExports");
                if (!Directory.Exists(exportDir))
                    Directory.CreateDirectory(exportDir);
                
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                int exportedCount = 0;
                
                // Export ScreenManager layers (1, 2, 3)
                if (_screenManager != null)
                {
                    var layerTextures = _screenManager.GetAllLayerTextures();
                    foreach (var kvp in layerTextures)
                    {
                        if (kvp.Value != null)
                        {
                            string layerName = kvp.Key == 1 ? "Layer1" : kvp.Key == 2 ? "Layer2" : "Layer3";
                            ExportRenderTextureToPng(kvp.Value, Path.Combine(exportDir, $"ScreenManager_{layerName}_{timestamp}.png"));
                            exportedCount++;
                        }
                    }
                }
                
                // Export legacy border texture (Layer 1)
                if (_borderTexture != null)
                {
                    ExportRenderTextureToPng(_borderTexture, Path.Combine(exportDir, $"Legacy_Layer1_Border_{timestamp}.png"));
                    exportedCount++;
                }
                
                // Export per-screen Layer 2 textures
                if (_mainBorderLabelsTexture != null)
                {
                    ExportRenderTextureToPng(_mainBorderLabelsTexture, Path.Combine(exportDir, $"Legacy_Layer2_MainLabels_{timestamp}.png"));
                    exportedCount++;
                }
                if (_scanBorderLabelsTexture != null)
                {
                    ExportRenderTextureToPng(_scanBorderLabelsTexture, Path.Combine(exportDir, $"Legacy_Layer2_ScanLabels_{timestamp}.png"));
                    exportedCount++;
                }
                if (_confirmBorderLabelsTexture != null)
                {
                    ExportRenderTextureToPng(_confirmBorderLabelsTexture, Path.Combine(exportDir, $"Legacy_Layer2_ConfirmLabels_{timestamp}.png"));
                    exportedCount++;
                }
                
                // REMOVED: Per-element texture export loop (old system)
                // The single Layer 3 texture is now exported via ScreenManager above
                
                // Export dummy Layer 3 texture (for layout calibration reference)
                RenderTexture dummyLayer3Texture = new RenderTexture(
                    Mathf.RoundToInt(HolographicLayoutConfig.DISPLAY_WIDTH_LARGE),
                    Mathf.RoundToInt(HolographicLayoutConfig.DISPLAY_HEIGHT_LARGE),
                    0, RenderTextureFormat.ARGB32);
                dummyLayer3Texture.enableRandomWrite = true;
                dummyLayer3Texture.Create();
                
                RenderDummyLayer3ToTexture(dummyLayer3Texture);
                
                ExportRenderTextureToPng(dummyLayer3Texture, Path.Combine(exportDir, $"DummyLayer3_{timestamp}.png"));
                exportedCount++;
                
                dummyLayer3Texture.Release();
                Destroy(dummyLayer3Texture);
                
                // Export display texture (composite)
                if (_displayTexture != null)
                {
                    ExportRenderTextureToPng(_displayTexture, Path.Combine(exportDir, $"DisplayTexture_{timestamp}.png"));
                    exportedCount++;
                }
                
                Debug.Log($"[HolographicDisplay] Exported {exportedCount} textures to: {exportDir}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HolographicDisplay] Failed to export textures: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Helper to export a single RenderTexture to PNG
        /// </summary>
        private void ExportRenderTextureToPng(RenderTexture rt, string filePath)
        {
            if (rt == null) return;
            
            // Create temporary Texture2D to read the RenderTexture
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            
            // Encode to PNG and save
            byte[] pngData = tex.EncodeToPNG();
            File.WriteAllBytes(filePath, pngData);
            
            UnityEngine.Object.Destroy(tex);
        }

        #endregion
    }
}
