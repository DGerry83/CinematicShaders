using CinematicShaders.Core;
using CinematicShaders.Native;
using CinematicShaders.UI.Screens;
using CinematicShaders.UI.Screens.Layers;
using CinematicShaders.UI.Content;
using CinematicShaders.UI.State;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static FinePrint.ContractDefs;
using CinematicShaders.UI.Layout;
using CinematicShaders.UI.Layout.ScreenLayouts;

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
        private bool _playJingleOnNextPowerOn = true;
        
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

        // Constraint-based layout (replaces UnifiedGridRegistry)
        private MainScreenLayout _mainScreenLayout;
        private LayoutEngine _layoutEngine;
        
        // NEW: Shared infrastructure for controller architecture
        public StarConsoleServices Services { get; private set; }
        public ScreenRouter Router { get; private set; }
        private MainScreenHandler _mainHandler;
        private ScanScreenHandler _scanHandler;
        private ConfirmRescanHandler _confirmHandler;
        #endregion

        // Note: ScreenState enum removed - now using string ScreenName ("Main", "Scan", "ConfirmRescan")
        // Screen state is managed by ScreenManager



        // Note: Layer content strings are now defined in UI/Content/*ScreenContent.cs
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

        #region Initialization
        public void Initialize(IntPtr sharedTextSystem, float x, float y, 
            HolographicDisplaySize size = HolographicDisplaySize.Medium,
            string customJsonPath = "", string defaultJsonPath = "",
            string catalogPath = "")
        {
            _instanceId = ++s_instanceCount;
            _textSystem = sharedTextSystem;
            
            // Get glyph-based display dimensions
            Vector2 dimensions = TerminalGridConfig.GetDisplayDimensions(size);
            _displayRect = new Rect(x, y, dimensions.x, dimensions.y);
            
            // Calculate window size: display + borders + title bar
            float windowWidth = dimensions.x + 2 * BORDER_THICKNESS;
            float windowHeight = dimensions.y + TITLE_BAR_HEIGHT + 2 * BORDER_THICKNESS;
            _windowRect = new Rect(x, y, windowWidth, windowHeight);
            
            _fontSize = HolographicLayoutConfig.GetFontSize(size);
            _lineSpacing = HolographicLayoutConfig.GetLineSpacing(size);
            _displaySize = size;
            
            // CRITICAL FIX: Set CurrentDisplaySize BEFORE creating elements or zones
            // This ensures TerminalGridConfig.CurrentDisplaySize is correct when
            // MainScreenClickZones.GetAllZones() is called during InitializeScreens()
            TerminalGridConfig.CurrentDisplaySize = size;
            
            // Initialize state manager with catalog path (required for JSON state tracking)
            if (!string.IsNullOrEmpty(catalogPath))
            {
                StarCatalogStateManager.Initialize(catalogPath);
            }

            CreateElements();
            
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
            
            Debug.Log($"[StarCatalogHolographicDisplay] Initialized: window {windowWidth}x{windowHeight} for {size} (display: {dimensions.x}x{dimensions.y})");
        }
        
        private void InitializeScreens()
        {
            float aspectRatio = 0.667f; // 2:3 aspect ratio for text rendering
            
            // Initialize shared infrastructure
            Services = new StarConsoleServices
            {
                Selector = _selector,
                CustomJsonPath = StarCatalogStateManager.CurrentJsonPaths.CustomJsonPath,
                DefaultJsonPath = StarCatalogStateManager.CurrentJsonPaths.DefaultJsonPath
            };
            
            Router = new ScreenRouter(_screenManager, Services);
            
            // Main screen
            var mainScreen = new MainScreen(MainScreenContent.Default, _fontSize, aspectRatio);
            ModFileLogger.Log($"[HolographicDisplay] Creating MainScreen instance {mainScreen.GetHashCode()}");
            
            // Pass elements to MainScreen for Layer 3 rendering
            var mainElements = new List<HolographicTextElement>(_elements.Values);
            mainScreen.SetElements(mainElements);
            ModFileLogger.Log($"[HolographicDisplay] MainScreen elements set, instance {mainScreen.GetHashCode()}");
            
            // Wire controller-based handler
            _mainHandler = new MainScreenHandler(Services, Router, this);
            mainScreen.Handler = _mainHandler;
            
            _screenManager.RegisterScreen(mainScreen);
            
            // Scan screen
            var scanScreen = new ScanScreen(ScanScreenContent.Default, _fontSize, aspectRatio);
            _scanHandler = new ScanScreenHandler(Router, this);
            scanScreen.Handler = _scanHandler;
            _screenManager.RegisterScreen(scanScreen);
            
            // Splash screen (boot logo)
            var splashScreen = new SplashScreen(_fontSize, aspectRatio);
            splashScreen.OnSplashComplete += HandleSplashComplete;
            _screenManager.RegisterScreen(splashScreen);
            
            // Confirm screen
            var confirmScreen = new ConfirmRescanScreen(ConfirmRescanScreenContent.Default, _fontSize, aspectRatio);
            _confirmHandler = new ConfirmRescanHandler(Router, this);
            confirmScreen.Handler = _confirmHandler;
            _screenManager.RegisterScreen(confirmScreen);
        }
        
        /// <summary>
        /// Change the display size (Small/Medium/Large)
        /// </summary>
        public void SetDisplaySize(HolographicDisplaySize size)
        {
            ModFileLogger.Log($"[HolographicDisplay] SetDisplaySize({size}) called");
            ModFileLogger.Log($"[HolographicDisplay] Previous size: {_displaySize}");
            
            // Unified grid supports all display sizes
            
            if (_displaySize == size) return;
            
            _displaySize = size;
            
            // Update the global current display size for glyph-based calculations
            TerminalGridConfig.CurrentDisplaySize = size;

            // NEW: Invalidate constraint layout so it rebuilds with new dimensions
            _mainScreenLayout = null;
            _layoutEngine = null;
            
            // Get glyph-based display dimensions
            Vector2 dimensions = TerminalGridConfig.GetDisplayDimensions(size);
            _fontSize = HolographicLayoutConfig.GetFontSize(size);
            _lineSpacing = HolographicLayoutConfig.GetLineSpacing(size);
            
            // Calculate window size: display + borders + title bar
            float windowWidth = dimensions.x + 2 * BORDER_THICKNESS;
            float windowHeight = dimensions.y + TITLE_BAR_HEIGHT + 2 * BORDER_THICKNESS;
            
            _windowRect = new Rect(_windowRect.x, _windowRect.y, windowWidth, windowHeight);
            
            ModFileLogger.Log($"[HolographicDisplay] New window size: {_windowRect.width}x{_windowRect.height}");
            ModFileLogger.Log($"[HolographicDisplay] CurrentDisplaySize set to: {TerminalGridConfig.CurrentDisplaySize}");
            
            // Recreate elements with new dimensions FIRST so InitializeScreens gets fresh data
            CreateElements();
            
            if (_screenManager != null)
            {
                // Unified grid: Reinitialize ScreenManager textures at new size
                ModFileLogger.Log("[HolographicDisplay] Recreating screens for unified grid");
                _screenManager.Shutdown();
                _screenManager = new ScreenManager(_textSystem);
                _screenManager.InitializeTextures(
                    Mathf.RoundToInt(dimensions.x), 
                    Mathf.RoundToInt(dimensions.y));
                InitializeScreens();
                
                // CRITICAL FIX: Reinitialize click zones after screen reinitialization
                // This ensures zones are calculated for the new display size
                if (_displayPowered)
                {
                    ModFileLogger.Log("[HolographicDisplay] Size change handled by ScreenManager restart");
                }
                
                Debug.Log($"[HolographicDisplay] ScreenManager textures resized to: {dimensions.x}x{dimensions.y}");
            }
            
            // Mark all elements dirty for re-render
            foreach (var element in _elements.Values)
            {
                element.IsDirty = true;
            }
            
            Debug.Log($"[StarCatalogHolographicDisplay] Window size: {windowWidth}x{windowHeight} for {size} (display: {dimensions.x}x{dimensions.y})");
        }

        private void CreateElements()
        {
            _elements.Clear();
            _resultElements.Clear();
            CreateElementsUnified();
        }

        /// <summary>
        /// Ensures the MainScreenLayout is built and ready for use.
        /// Called lazily from CreateElementsUnified().
        /// </summary>
        private void EnsureLayoutBuilt()
        {
            if (_layoutEngine == null)
            {
                _layoutEngine = new LayoutEngine();
            }
            
            if (_mainScreenLayout == null)
            {
                _mainScreenLayout = new MainScreenLayout();
                
                // Get display dimensions for layout
                Vector2 displayDims = TerminalGridConfig.GetDisplayDimensions(
                    TerminalGridConfig.CurrentDisplaySize
                );
                Rect displayArea = new Rect(0, 0, displayDims.x, displayDims.y);
                
                _mainScreenLayout.Build(_layoutEngine, displayArea);
                
                Debug.Log("[StarCatalogHolographicDisplay] MainScreenLayout built successfully");
            }
        }

        /// <summary>
        /// Creates a HolographicTextElement from a GridRegion and metadata.
        /// Replaces HolographicTextElement.FromDefinition().
        /// </summary>
        private HolographicTextElement CreateElementFromGridRegion(
            string elementId, 
            TextElementType type, 
            GridRegion region,
            bool visibleByDefault = true)
        {
            var element = new HolographicTextElement
            {
                ElementId = elementId,
                Type = type,
                StaticText = "",
                DynamicText = "",
                
                // Grid-based positioning (primary)
                GridPos = region.TopLeft,
                GridWidth = region.Width,
                
                // Visibility and animation
                IsVisible = visibleByDefault,
                IsDirty = true,
                TypeOnProgress = 1.0f,
                TypeOnDelay = 0f,
                TypeOnDuration = 0.5f,
                
                // Priority based on type
                Priority = GetPriorityForElement(elementId, type)
            };
            
            return element;
        }

        /// <summary>
        /// Gets animation priority for an element based on its ID and type.
        /// Lower values = earlier in animation sequence.
        /// </summary>
        private int GetPriorityForElement(string elementId, TextElementType type)
        {
            // Match priorities from UnifiedGridRegistry
            switch (elementId)
            {
                case "hip_value": return 0;
                case "name_value": return 1;
                case "distance_value": return 2;
                case "spectral_value": return 3;
                case "mag_value": return 4;
                case "const_value": return 5;
                case "save_button": return 10;
                case "reset_button": return 11;
                case "rescan_button": return 12;
                case "search_input": return 20;
                case "page_number": return 35;
                case var id when id.StartsWith("result_"):
                    // Extract index from "result_N"
                    if (int.TryParse(id.Substring(7), out int idx))
                        return 30 + idx;
                    return 30;
                default:
                    return 100;
            }
        }

        /// <summary>
        /// Determines the TextElementType for a given element ID.
        /// Replaces ElementType from GridElementDefinition.
        /// </summary>
        private TextElementType GetElementType(string elementId)
        {
            switch (elementId)
            {
                case "hip_value":
                case "distance_value":
                case "spectral_value":
                case "mag_value":
                case "const_value":
                    return TextElementType.Value;
                    
                case "name_value":
                    return TextElementType.Editable;
                    
                case "search_input":
                    return TextElementType.Input;
                    
                case "save_button":
                case "reset_button":
                case "rescan_button":
                    return TextElementType.Button;
                    
                case var id when id.StartsWith("result_"):
                    return TextElementType.SearchResult;
                
                case "page_number":
                    return TextElementType.Value;
                    
                default:
                    return TextElementType.Value;
            }
        }

        /// <summary>
        /// Creates elements using constraint-based layout system.
        /// Replaces UnifiedGridRegistry dependency with MainScreenLayout.
        /// </summary>
        private void CreateElementsUnified()
        {
            // Ensure layout is built (lazy initialization)
            EnsureLayoutBuilt();
            
            // List of main screen element IDs (excluding buttons which are drawn in Layer 2)
            string[] mainElementIds = new[]
            {
                "hip_value",
                "name_value", 
                "distance_value",
                "spectral_value",
                "mag_value",
                "const_value",
                "search_input"
                // Note: Buttons (save_button, reset_button, rescan_button) are excluded
                // because they are drawn in Layer 2 and only needed for click zones
            };
            
            // Create main screen elements from layout
            foreach (string elementId in mainElementIds)
            {
                GridRegion region = _mainScreenLayout.GetGridArea(elementId);
                
                // Skip if region is invalid (not found in layout)
                if (region.Width == 0 || region.Height == 0)
                {
                    Debug.LogWarning($"[StarCatalogHolographicDisplay] Layout region not found for: {elementId}");
                    continue;
                }
                
                TextElementType type = GetElementType(elementId);
                var element = CreateElementFromGridRegion(elementId, type, region);
                
                _elements[elementId] = element;
            }
            
            // Create search result elements dynamically (result_0 through result_9)
            for (int i = 0; i < MAX_SEARCH_RESULTS; i++)
            {
                string elementId = $"result_{i}";
                GridRegion region = _mainScreenLayout.GetGridArea(elementId);
                
                if (region.Width == 0 || region.Height == 0)
                {
                    Debug.LogWarning($"[StarCatalogHolographicDisplay] Layout region not found for: {elementId}");
                    continue;
                }
                
                var element = CreateElementFromGridRegion(
                    elementId, 
                    TextElementType.SearchResult, 
                    region,
                    visibleByDefault: false  // Hidden by default
                );
                
                _resultElements.Add(element);
                _elements[elementId] = element;
            }
            
            // Create page number element for pagination display
            GridRegion pageRegion = _mainScreenLayout.GetGridArea("page_number");
            if (pageRegion.Width > 0 && pageRegion.Height > 0)
            {
                var pageElement = CreateElementFromGridRegion(
                    "page_number",
                    TextElementType.Value,
                    pageRegion,
                    visibleByDefault: false  // Hidden by default
                );
                _elements["page_number"] = pageElement;
            }
            
            Debug.Log($"[StarCatalogHolographicDisplay] Created {_elements.Count} elements using constraint layout");
        }

        #endregion

        #region IMGUI Window Rendering
        
        private void OnGUI()
        {

            if (!_isVisible) return;
            
            InitStyles();
            
            // Handle keyboard input (even when window not focused for convenience)
            HandleKeyboardInput();
            
            // Click-away defocus: any mouse click outside the editing element exits edit mode
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && !string.IsNullOrEmpty(_editingElementId))
            {
                bool shouldExit = true;
                if (_mainScreenLayout != null)
                {
                    var region = _mainScreenLayout.GetGridArea(_editingElementId);
                    if (region.Width > 0 && region.Height > 0)
                    {
                        var (glyphW, glyphH) = TerminalGridConfig.GlyphMetrics.GetGlyphMetrics(TerminalGridConfig.CurrentDisplaySize);
                        float displayScreenX = _windowRect.x + BORDER_THICKNESS;
                        float displayScreenY = _windowRect.y + TITLE_BAR_HEIGHT + BORDER_THICKNESS;
                        Rect editScreenRect = new Rect(
                            displayScreenX + region.TopLeft.Column * glyphW,
                            displayScreenY + region.TopLeft.Row * glyphH,
                            region.Width * glyphW,
                            region.Height * glyphH
                        );
                        if (editScreenRect.Contains(Event.current.mousePosition))
                        {
                            shouldExit = false;
                        }
                    }
                }
                if (shouldExit)
                {
                    ExitEditMode(save: true);
                }
            }
            
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
            
            string pwrLabel = _displayPowered ? CinematicShadersUIStrings.StarConsole.PowerOnLabel : CinematicShadersUIStrings.StarConsole.PowerOffLabel;
            if (GUI.Button(pwrRect, pwrLabel, pwrStyle))
            {
                TogglePower();
            }
            
            // Title (center)
            GUIStyle titleStyle = CinematicShadersUIResources.Styles.ConsoleTitle();
            Rect titleRect = new Rect(_windowRect.width * 0.25f, titleY, _windowRect.width * 0.5f, buttonHeight);
            GUI.Label(titleRect, CinematicShadersUIStrings.StarConsole.StarConsoleTitle, titleStyle);
            
            // X Button (right side)
            Rect closeRect = new Rect(_windowRect.width - BORDER_THICKNESS - 30f, titleY, 30f, buttonHeight);
            if (GUI.Button(closeRect, CinematicShadersUIStrings.Common.CloseButton, _closeButtonStyle))
            {
                Hide();
            }
        }
        
        private void DrawWindowBorder()
        {
            // Grey border color (standard KSP UI grey)
            Color borderColor = CinematicShadersUIResources.Colors.CONSOLE_BORDER_GREY;
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
            GUI.color = CinematicShadersUIResources.Colors.CRT_BACKGROUND;
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
            _closeButtonStyle = CinematicShadersUIResources.Styles.ConsoleCloseButton();
            
            // PWR button styles
            _pwrButtonStyle = CinematicShadersUIResources.Styles.ConsolePwrButton();
            
            _pwrButtonActiveStyle = CinematicShadersUIResources.Styles.ConsolePwrButtonActive();
            
            _stylesInitialized = true;
        }
        
        #endregion
        
        // Note: CRT display rendering and color helpers are handled by the screen/layer system.

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
            
            InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT | ControlTypes.PAUSE, "CinematicShaders_StarConsoleEdit");
            
            Debug.Log($"[HolographicDisplay] Entered edit mode for: {elementId}");
        }
        
        /// <summary>
        /// Exit edit mode, optionally saving changes (Workstream C - Layer 3 refactor).
        /// </summary>
        public void ExitEditMode(bool save)
        {
            if (string.IsNullOrEmpty(_editingElementId)) return;
            
            InputLockManager.RemoveControlLock("CinematicShaders_StarConsoleEdit");
            
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
            var element = GetElement(_editingElementId);
            if (element != null)
            {
                string displayText = _editBuffer + (_cursorVisible ? CinematicShadersUIStrings.StarConsole.EditCursorGlyph : "");
                var mainScreen = _screenManager?.CurrentScreen as MainScreen;
                var elementLayer = mainScreen?.GetElementLayer();
                elementLayer?.UpdateElementText(_editingElementId, displayText);
                mainScreen?.ForceRenderTextureReload();
            }
        }
        
        /// <summary>
        /// Update cursor blink animation (Workstream C - Layer 3 refactor).
        /// Passes cursor state to ElementLayer for single-texture rendering.
        /// </summary>
        private void UpdateCursorBlink()
        {
            // Check if we're in edit mode
            if (string.IsNullOrEmpty(_editingElementId)) return;
            
            _cursorBlinkTimer += Time.unscaledDeltaTime;
            
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
        
        #endregion

        #region Persistence
        
        /// <summary>
        /// Save the current star name to _Custom.json
        /// </summary>
        public void SaveStarName(string newName)
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
        public void ResetStarName()
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
        /// Modify star name in JSON file using MiniJSON.
        /// </summary>
        private void ModifyStarNameInJson(int hipId, string newName, string customJsonPath)
        {
            try
            {
                string json = File.ReadAllText(customJsonPath);
                var root = Json.Deserialize(json) as Dictionary<string, object>;
                if (root == null)
                {
                    Debug.LogError($"[HolographicDisplay] Failed to parse JSON: {customJsonPath}");
                    return;
                }

                if (!root.TryGetValue("stars", out object starsObj) || !(starsObj is Dictionary<string, object> stars))
                {
                    Debug.LogError($"[HolographicDisplay] Missing 'stars' object in JSON: {customJsonPath}");
                    return;
                }

                string hipKey = hipId.ToString();
                if (!stars.TryGetValue(hipKey, out object starObj) || !(starObj is Dictionary<string, object> star))
                {
                    // Create minimal star entry if it doesn't exist
                    star = new Dictionary<string, object>();
                    stars[hipKey] = star;
                }

                star["proper"] = newName;

                string newJson = Json.Serialize(root);
                File.WriteAllText(customJsonPath, newJson);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HolographicDisplay] Failed to modify star name in JSON: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get original name from default JSON using MiniJSON.
        /// </summary>
        private string GetOriginalNameFromJson(int hipId, string defaultJsonPath)
        {
            try
            {
                string json = File.ReadAllText(defaultJsonPath);
                var root = Json.Deserialize(json) as Dictionary<string, object>;
                if (root == null) return null;

                if (!root.TryGetValue("stars", out object starsObj) || !(starsObj is Dictionary<string, object> stars))
                    return null;

                if (!stars.TryGetValue(hipId.ToString(), out object starObj) || !(starObj is Dictionary<string, object> star))
                    return null;

                // Try "proper" first, then "full_designation"
                if (star.TryGetValue("proper", out object properObj) && properObj is string proper && !string.IsNullOrEmpty(proper))
                    return proper.ToUpper();

                if (star.TryGetValue("full_designation", out object designationObj) && designationObj is string designation && !string.IsNullOrEmpty(designation))
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
        /// Currently editing element ID (for handlers)
        /// </summary>
        public string EditingElementId => _editingElementId;
        
        /// <summary>
        /// Current search filtered results (for handlers)
        /// </summary>
        public List<NamedStar> FilteredResults => _filteredResults;
        
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
        /// Validates that ScreenManager is ready before powering on.
        /// </summary>
        private bool ValidateBeforePowerOn()
        {
            return _screenManager != null;
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
            
            // Play power-on jingle if enabled
            ModAudioManager.PlayOneShot(AudioGroup.StarConsole, "CinematicShaders/Sounds/StarJingle", _playJingleOnNextPowerOn);
            
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
        
        private void PowerOff()
        {
            ExitEditMode(save: false);
            
            _displayPowered = false;
            
            // Ensure typing sound stops even if animation is mid-type-on
            ModAudioManager.StopLoop("starconsole_typing", 0.025f);
            
            // Clear all element text (don't just hide - clear the data)
            ClearStarData();
            
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
            

        }

        private void SetElementText(string elementId, string text)
        {
            string newText = text?.ToUpper() ?? "";
            if (_elements.TryGetValue(elementId, out var element))
            {
                if (element.DynamicText != newText)
                {
                    element.DynamicText = newText;
                    element.IsDirty = true;
                }
            }
            var mainScreen = _screenManager?.CurrentScreen as MainScreen;
            var elementLayer = mainScreen?.GetElementLayer();
            elementLayer?.UpdateElementText(elementId, newText);
            mainScreen?.ForceRenderTextureReload();
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
            
            // Trigger type-on animation for the clear (elements will type-on empty)
            TriggerValueTypeOnAnimation();
        }
        #endregion

        #region Cleanup
        private void Awake()
        {
            // Subscribe to state manager events once during lifecycle
            StarCatalogStateManager.OnCatalogChanged += HandleCatalogChanged;
            StarCatalogStateManager.OnJsonStateChanged += HandleJsonStateChanged;
        }
        
        private void OnDestroy()
        {
            // Unsubscribe from state manager events
            StarCatalogStateManager.OnCatalogChanged -= HandleCatalogChanged;
            StarCatalogStateManager.OnJsonStateChanged -= HandleJsonStateChanged;
            
            // Shutdown ScreenManager
            _screenManager?.Shutdown();
            _screenManager = null;
            
            // Ensure typing sound is stopped when window is destroyed
            ModAudioManager.StopLoop("starconsole_typing", 0.025f);

            // Note: We don't shut down _textSystem here because it's shared
        }
        #endregion

        

        /// <summary>
        /// Callback events for UI integration
        /// </summary>
        public event Action OnSaveClicked;
        public event Action OnResetClicked;
        public event Action<NamedStar> OnStarSelected;
        public event Action OnRescanConfirmed;
        public event Action OnPoweredOn;

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

        /// <summary>
        /// Trigger catalog scan (wrapper for ScanScreen handler)
        /// </summary>
        public void ScanCatalog()
        {
            OnRescanConfirmed?.Invoke();
        }

        /// <summary>
        /// Confirm rescan action (wrapper for ConfirmRescan handler)
        /// </summary>
        public void ConfirmRescan()
        {
            OnRescanConfirmed?.Invoke();
            _screenManager?.TransitionTo("Main");
            Debug.Log("[HolographicDisplay] Rescan confirmed - transitioning to Main");
        }

        #region Search System

        // State
        private string _searchQuery = "";
        private List<NamedStar> _allStars = new List<NamedStar>();
        private List<NamedStar> _filteredResults = new List<NamedStar>();
        private NamedStar _selectedStar = null;

        // Search debounce
        private float _lastSearchTime = 0f;
        private const float SEARCH_DEBOUNCE = 0.1f;  // 100ms debounce
        
        // Pagination state
        private int _searchPageIndex = 0;
        private List<NamedStar> _allFilteredResults = new List<NamedStar>();

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
            _searchPageIndex = 0;
            _allFilteredResults.Clear();
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
            
            // Reset to first page on any search change
            _searchPageIndex = 0;
            
            // Update search input display
            SetElementText("search_input", string.IsNullOrEmpty(_searchQuery) ? CinematicShadersUIStrings.StarConsole.SearchPlaceholder : _searchQuery);
            
            // Filter results
            UpdateSearchResults();
            
            // Restart animation so the search input types on
            var mainScreen = _screenManager?.CurrentScreen as MainScreen;
            mainScreen?.RestartLayer3Animation();
        }

        /// <summary>
        /// Filter stars based on search query
        /// </summary>
        private void UpdateSearchResults()
        {
            ModFileLogger.Log($"[SearchDebug] UpdateSearchResults called. Query='{_searchQuery}', _allStars.Count={_allStars?.Count ?? 0}");
            
            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                _allFilteredResults.Clear();
                _filteredResults.Clear();
                _searchPageIndex = 0;
                UpdateResultElements();
                UpdatePageNumberDisplay();
                return;
            }
            
            // Get all matching results (unlimited) for pagination
            _allFilteredResults = StarSearchUtility.SearchStars(_allStars, _searchQuery, 0);
            
            ModFileLogger.Log($"[SearchDebug] Found {_allFilteredResults.Count} total matches");
            
            RefreshResultPage();
        }
        
        /// <summary>
        /// Refresh the current page of results from _allFilteredResults based on _searchPageIndex.
        /// </summary>
        private void RefreshResultPage()
        {
            int startIndex = _searchPageIndex * MAX_SEARCH_RESULTS;
            
            // Clamp page index if out of bounds
            int maxPage = _allFilteredResults.Count > 0 ? (_allFilteredResults.Count - 1) / MAX_SEARCH_RESULTS : 0;
            if (_searchPageIndex > maxPage)
            {
                _searchPageIndex = maxPage;
                startIndex = _searchPageIndex * MAX_SEARCH_RESULTS;
            }
            
            _filteredResults.Clear();
            int endIndex = Mathf.Min(startIndex + MAX_SEARCH_RESULTS, _allFilteredResults.Count);
            for (int i = startIndex; i < endIndex; i++)
            {
                _filteredResults.Add(_allFilteredResults[i]);
            }
            
            ModFileLogger.Log($"[SearchDebug] Showing page {_searchPageIndex + 1}/{maxPage + 1} (results {startIndex + 1}-{endIndex})");
            
            UpdateResultElements();
            UpdatePageNumberDisplay();
        }
        
        /// <summary>
        /// Scroll search results by a page delta (+1 or -1).
        /// </summary>
        public void ScrollSearchResults(int delta)
        {
            if (_allFilteredResults.Count == 0) return;
            
            int maxPage = (_allFilteredResults.Count - 1) / MAX_SEARCH_RESULTS;
            int newPage = Mathf.Clamp(_searchPageIndex + delta, 0, maxPage);
            
            if (newPage != _searchPageIndex)
            {
                _searchPageIndex = newPage;
                RefreshResultPage();
            }
        }
        
        /// <summary>
        /// Update the page number display element between the scroll arrows.
        /// </summary>
        private void UpdatePageNumberDisplay()
        {
            int totalPages = Mathf.Max(1, (_allFilteredResults.Count + MAX_SEARCH_RESULTS - 1) / MAX_SEARCH_RESULTS);
            
            if (_allFilteredResults.Count == 0 || totalPages <= 1)
            {
                SetElementText("page_number", "");
                return;
            }
            
            string pageText = string.Format(CinematicShadersUIStrings.StarConsole.PageNumberFormat, _searchPageIndex + 1, totalPages);
            // Pin slash at column 48; left number grows leftward, right number grows rightward
            int regionStartCol = 43;
            int slashIndex = pageText.IndexOf('/');
            int leftPadding = 48 - regionStartCol - slashIndex;
            if (leftPadding > 0)
            {
                pageText = new string(' ', leftPadding) + pageText;
            }
            
            SetElementText("page_number", pageText);
        }

        /// <summary>
        /// Update result elements with filtered stars
        /// </summary>
        private void UpdateResultElements()
        {
            bool anyResultsChanged = false;
            
            for (int i = 0; i < MAX_SEARCH_RESULTS; i++)
            {
                var element = _resultElements[i];
                
                if (i < _filteredResults.Count)
                {
                    var star = _filteredResults[i];
                    
                    // Only flag for animation if the content actually changed
                    bool textChanged = element.DynamicText != star.Name || element.StaticText != CinematicShadersUIStrings.StarConsole.ResultBullet || !element.IsVisible;
                    
                    element.IsVisible = true;
                    element.StaticText = CinematicShadersUIStrings.StarConsole.ResultBullet;
                    element.DynamicText = star.Name;
                    element.AssociatedData = star;
                    element.IsDirty = true;
                    
                    if (textChanged)
                    {
                        element.NeedsTypeOnAnimation = true;
                        element.TypeOnProgress = 0f;
                        anyResultsChanged = true;
                    }
                }
                else
                {
                    string resultId = $"result_{i}";
                    bool wasVisible = element.IsVisible;
                    
                    element.IsVisible = false;
                    element.AssociatedData = null;
                    
                    if (wasVisible)
                    {
                        // Clear the element text in the layer as well
                        (_screenManager?.CurrentScreen as MainScreen)?.GetElementLayer()?.UpdateElementText(resultId, "");
                    }
                }
            }
            
            // Restart Layer 3 animation if any result content changed
            if (anyResultsChanged)
            {
                var mainScreen = _screenManager?.CurrentScreen as MainScreen;
                mainScreen?.RestartLayer3Animation();
            }
            
            var screen = _screenManager?.CurrentScreen as MainScreen;
            screen?.ForceRenderTextureReload();
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
            
            // Sync with shared services (may be called before or after InitializeScreens)
            if (Services != null)
            {
                Services.Selector = selector;
                var jsonPaths = StarCatalogStateManager.CurrentJsonPaths;
                Services.CustomJsonPath = jsonPaths.CustomJsonPath;
                Services.DefaultJsonPath = jsonPaths.DefaultJsonPath;
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
                mainScreen.ForceRenderTextureReload();
            }
            
            _selectedStar = null;
        }

        #endregion

        #region Keyboard Input

        // Input state

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
                    _screenManager?.TransitionTo("Main");
                    Debug.Log("[HolographicDisplay] Hiding confirmation dialog, returning to Main");
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
            
            // Note: Global type-to-search removed. Search only works via
            // explicit edit mode on the search_input field.
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
                _screenManager?.Update(Time.unscaledDeltaTime);
            }
            
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

        #endregion
    }
}
