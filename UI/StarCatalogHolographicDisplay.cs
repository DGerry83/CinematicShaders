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
using static CinematicShaders.UI.UnifiedGridConfig;
using static CinematicShaders.UI.UnifiedGridRegistry;

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
        private const float LAYER_1_DURATION = 1.0f;   // 1.0s for border (halved)
        private const float LAYER_2_DURATION = 1.0f;   // 1.0s for labels (halved)
        private const float LAYER_2_DELAY = 1.0f;      // Start after Layer 1 (halved)
        private const float LAYER_3_DELAY = 4.0f;      // Start after Layer 2
        
        
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

        // Screen manager for screen state handling
        private ScreenManager _screenManager;
        #endregion

        // Note: ScreenState enum removed - now using string ScreenName ("Main", "Scan", "ConfirmRescan")
        // Screen state is managed by ScreenManager



        // Layer 2 content strings (for reference)
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
            
            // Initialize ScreenManager
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
            
            // Splash screen (boot logo)
            var splashScreen = new SplashScreen(_fontSize, aspectRatio);
            splashScreen.OnSplashComplete += HandleSplashComplete;
            _screenManager.RegisterScreen(splashScreen);
            
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
            
            Debug.Log($"[HolographicDisplay] Size changed to: {size}: {dimensions.x}x{dimensions.y}");
        }

        private void CreateElements()
        {
            _elements.Clear();
            _resultElements.Clear();
            
            // Unified grid path (Phase 4)
            if (UnifiedGridConfig.USE_UNIFIED_GRID)
            {
                CreateElementsUnified();
                return;
            }
            
            // Legacy path (existing implementation continues...)
            // FIELD ORDER: HIP, NAME, DISTANCE, SPECTRAL, MAGNITUDE, CONSTELLATION
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

            // Add SAVE and RESET buttons
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

        /// <summary>
        /// Creates elements using unified 59×13 grid system.
        /// Calculates pixel positions dynamically based on display size.
        /// </summary>
        private void CreateElementsUnified()
        {
            // Get current display dimensions
            Vector2 dimensions = HolographicLayoutConfig.GetDisplayDimensions(_displaySize);
            float displayWidth = dimensions.x;
            float displayHeight = dimensions.y;
            
            // Create main screen elements from unified registry
            foreach (var kvp in UnifiedGridRegistry.MainScreenElements)
            {
                var definition = kvp.Value;
                
                // Skip buttons - they are drawn in Layer 2
                if (definition.Type == ElementType.Button)
                    continue;
                
                // Create element using unified definition
                var element = HolographicTextElement.FromDefinition(definition, displayWidth, displayHeight);
                
                // Set initial values based on element type
                switch (definition.ElementId)
                {
                    case "hip_value":
                        element.StaticText = "HIP:";
                        break;
                    case "name_value":
                        element.StaticText = "NAME:";
                        element.Type = TextElementType.Editable;
                        break;
                    case "distance_value":
                        element.StaticText = "DISTANCE:";
                        break;
                    case "spectral_value":
                        element.StaticText = "TYPE:";
                        break;
                    case "mag_value":
                        element.StaticText = "MAG:";
                        break;
                    case "const_value":
                        element.StaticText = "CONST:";
                        break;
                    case "search_input":
                        element.StaticText = "SEARCH:";
                        element.Type = TextElementType.Input;
                        break;
                    case "selected_star":
                        element.StaticText = "SELECTED:";
                        break;
                }
                
                _elements[definition.ElementId] = element;
            }
            
            // Create search result elements dynamically
            for (int i = 0; i < 10; i++)
            {
                var definition = UnifiedGridRegistry.GetSearchResultElement(i);
                var element = HolographicTextElement.FromDefinition(definition, displayWidth, displayHeight);
                element.IsVisible = false; // Hidden by default
                _resultElements.Add(element);
                _elements[element.ElementId] = element;
            }
            
            Debug.Log($"[StarCatalogHolographicDisplay] Created {_elements.Count} main elements and {_resultElements.Count} result elements (unified grid)");
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
            // Textures are now managed by ScreenManager
            // This method is kept for future texture initialization if needed
        }
        
        /// <summary>
        /// Clean up all render textures before recreating them
        /// </summary>
        private void CleanupRenderTextures()
        {
            // Textures are now managed by ScreenManager
            // This method is kept for future cleanup if needed
        }
        #endregion

        #region IMGUI Window Rendering
        
        private void OnGUI()
        {

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
            
            // Render current screen via ScreenManager (SplashScreen, MainScreen, or ScanScreen)
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
                    scanScreen?.HandleMouse(mousePos, _displayRect, mouseDown, mouseUp);
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

        /// <summary>
        /// Gets the CRT display color based on Kartographer settings.
        /// These are custom-tuned colors for the CRT text display that may differ
        /// from the actual Kartographer grid colors for visual consistency.
        /// Note: This is a local copy since HolographicDisplay doesn't inherit from BaseScreen.
        /// </summary>
        private Color GetCRTColor()
        {
            int colorIndex = StarfieldSettings.KartographerGridColor;
            switch (colorIndex)
            {
                case 0: return new Color(1.0f, 0.0f, 0.0f);  // Seafoam -> RED (test)
                case 1: return new Color(0.0f, 0.0f, 1.0f);  // Amber -> BLUE (test)
                case 2: return new Color(0.0f, 1.0f, 0.0f);  // White -> GREEN (test)
                case 3: return new Color(1.0f, 0.0f, 1.0f);  // Green -> MAGENTA (test)
                default: return new Color(1.0f, 0.0f, 0.0f);  // Default -> RED
            }
        }

        /// <summary>
        /// Gets the CRT display color as a uint in ARGB format for native rendering.
        /// Note: This is a local copy since HolographicDisplay doesn't inherit from BaseScreen.
        /// </summary>
        private uint GetCRTColorUint()
        {
            Color c = GetCRTColor();
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
            ModFileLogger.Log("[SearchDebug] HolographicDisplay.Show() called");
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
            
            // Determine target screen based on JSON availability
            bool hasJson = HasJsonCatalog();
            string targetScreen = hasJson ? "Main" : "Scan";
            
            // Transition to Splash screen first - it will auto-transition to target
            var context = new ScreenTransitionContext 
            { 
                IsInitialStartup = true,
                HasStarSelected = _selectedStar != null,
                TargetScreenName = targetScreen
            };
            _screenManager?.TransitionTo("Splash", context);
            
            Debug.Log($"[HolographicDisplay] Power ON - Splash screen, will transition to {targetScreen}");
        }
        
        /// <summary>
        /// Called when SplashScreen completes its animation.
        /// Transitions to the target screen (Main or Scan based on JSON availability).
        /// </summary>
        private void HandleSplashComplete(string targetScreenName)
        {
            if (!_displayPowered || _screenManager == null)
                return;
            
            var context = new ScreenTransitionContext 
            { 
                IsInitialStartup = true,
                HasStarSelected = _selectedStar != null 
            };
            
            if (targetScreenName == "Main")
            {
                _screenManager.TransitionTo("Main", context);
                
                // Initialize click zones for MainScreen
                var mainScreen = _screenManager.CurrentScreen as MainScreen;
                mainScreen?.SetClickZones();
                
                // Notify subscribers that we're powered on
                OnPoweredOn?.Invoke();
                
                Debug.Log("[HolographicDisplay] Splash complete - transitioned to Main");
            }
            else
            {
                _screenManager.TransitionTo("Scan");
                Debug.Log("[HolographicDisplay] Splash complete - transitioned to Scan");
            }
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
            
            // Shutdown ScreenManager
            _screenManager?.Shutdown();
            _screenManager = null;

            // Note: We don't shut down _textSystem here because it's shared
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
        public event Action OnPoweredOn;



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
                
                // Select the star (this syncs to selector/Kartographer)
                SelectStar(star);
                Debug.Log($"[HolographicDisplay] Selected star from search result: {star.Name}");
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

        // Note: Border rendering is now handled by BorderLayer in ScreenManager

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
            
            ModFileLogger.Log($"[SearchDebug] SetStarList called with {_allStars?.Count ?? 0} stars");
            if (_allStars.Count > 0)
            {
                ModFileLogger.Log($"[SearchDebug] First star: HIP {_allStars[0].HipparcosID}, Name: '{_allStars[0].Name}'");
            }
            
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
            ModFileLogger.Log($"[SearchDebug] UpdateSearch called with query='{query}', lastSearchTime={_lastSearchTime:F2}, time={Time.time:F2}");
            
            // Debounce rapid updates
            if (Time.time - _lastSearchTime < SEARCH_DEBOUNCE)
            {
                ModFileLogger.Log($"[SearchDebug] Search debounced - too soon");
                return;
            }
            _lastSearchTime = Time.time;
            
            _searchQuery = query?.ToUpper() ?? "";
            ModFileLogger.Log($"[SearchDebug] Setting searchQuery='{_searchQuery}'");
            
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
            
            ModFileLogger.Log($"[SearchDebug] UpdateSearchResults called. Query='{_searchQuery}', _allStars.Count={_allStars?.Count ?? 0}");
            
            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                // Show empty state message in results
                ShowEmptyResultsState();
                return;
            }
            
            string query = _searchQuery.ToLowerInvariant();
            
            // Filter: match name or HIP ID
            int checkCount = 0;
            foreach (var star in _allStars)
            {
                if (_filteredResults.Count >= MAX_SEARCH_RESULTS)
                    break;
                
                bool nameMatch = star.Name.ToLowerInvariant().Contains(query);
                bool hipMatch = star.HipparcosID.ToString().Contains(query);
                
                checkCount++;
                if (nameMatch || hipMatch)
                {
                    ModFileLogger.Log($"[SearchDebug] Match found: HIP {star.HipparcosID}, Name='{star.Name}'");
                    _filteredResults.Add(star);
                }
            }
            
            ModFileLogger.Log($"[SearchDebug] Searched {checkCount} stars, found {_filteredResults.Count} matches");
            
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
            Event e = Event.current;
            
            // Edit mode has priority
            if (!string.IsNullOrEmpty(_editingElementId))
            {
                HandleEditInput();
                return;
            }
            
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
                ModFileLogger.Log($"[SearchDebug] Typing: char='{e.character}', adding to buffer");
                _inputBuffer += char.ToUpper(e.character);
                ModFileLogger.Log($"[SearchDebug] Buffer now: '{_inputBuffer}'");
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
