using System;
using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.UI.Screens.Layers;
using CinematicShaders.Native;
using CinematicShaders.Native.Structs;
using CinematicShaders.Core;
using CinematicShaders.UI.Content;
using CinematicShaders.UI;
using CinematicShaders.UI.Layout;
using CinematicShaders.UI.Layout.ScreenLayouts;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// The main data display screen for the holographic console.
    /// Shows star information including HIP ID, name, distance, spectral type, magnitude, and constellation.
    /// </summary>
    /// <remarks>
    /// <para><b>Layer Configuration:</b></para>
    /// - Layer 1: Border frame and static header elements
    /// - Layer 2: Field labels ("HIP:", "NAME:", "DISTANCE:", etc.)
    /// - Layer 3: Dynamic value fields, buttons, and search input
    /// 
    /// <para><b>Animation Sequence:</b></para>
    /// 1. Border types on (0-2s)
    /// 2. Labels type on (2-3.5s)
    /// 3. Value fields and buttons appear in priority order (3.5s+)
    /// 
    /// <para><b>Interactions:</b></para>
    /// - Click on value fields to edit (name field is editable)
    /// - Click buttons (SAVE, RESET, RESCAN) for actions
    /// - Type in search box to find stars
    /// - Click search results to select stars
    /// 
    /// <para><b>Click Zone System:</b></para>
    /// Uses UV-based hit detection with MainScreenClickHandler. Zones are defined in
    /// MainScreenClickHandler.SetupZones and enabled/disabled based on star selection state.
    /// </remarks>
    public class MainScreen : BaseScreen, IClickHandler
    {
        private readonly float _fontSize;
        private readonly float _aspectRatio;
        private ElementLayer _elementLayer;
        private MainScreenLayout _layout;
        
        /// <summary>
        /// Gets the constraint-based layout for this screen.
        /// </summary>
        public MainScreenLayout Layout
        {
            get
            {
                if (_layout == null)
                {
                    _layout = new MainScreenLayout();
                    var engine = new LayoutEngine();
                    Vector2 dims = TerminalGridConfig.GetDisplayDimensions(TerminalGridConfig.CurrentDisplaySize);
                    _layout.Build(engine, new Rect(0, 0, dims.x, dims.y));
                }
                return _layout;
            }
        }
        
        // NEW: Simple click handler
        public MainScreenClickHandler ClickHandler { get; private set; }
        public ClickZoneManager ZoneManager => ClickHandler?.ZoneManager;
        
        // NEW: Handler for controller-based click routing
        public MainScreenHandler Handler { get; set; }
        
        /// <summary>
        /// Event fired when an interactive element is clicked.
        /// String parameter is the element ID (e.g., "name_value", "save_button").
        /// </summary>
        public event Action<string> OnElementClicked;
        
        /// <summary>
        /// Layer 3 priority order for type-on animation sequence.
        /// </summary>
        protected override List<string> Layer3PriorityOrder => new List<string>
        {
            "hip_value", "name_value", "distance_value",
            "spectral_value", "mag_value", "const_value",
            "selected_star", "search_input",
            "rescan_button", "save_button", "reset_button"
        };
        
        /// <summary>
        /// Initializes a new MainScreen with the specified content and styling.
        /// </summary>
        public MainScreen(string[] borderLines, string[] labelLines, float fontSize, float aspectRatio = 0.667f)
            : this(new CustomContent(borderLines, labelLines), fontSize, aspectRatio)
        {
        }

        /// <summary>
        /// Initializes a new MainScreen using an IScreenContent provider.
        /// </summary>
        public MainScreen(IScreenContent content, float fontSize, float aspectRatio = 0.667f)
        {
            ScreenName = "Main";
            _fontSize = fontSize;
            _aspectRatio = aspectRatio;
            
            AddLayer(new BorderLayer(content.BorderLines));
            AddLayer(new ContentLayer(content.ContentLines));
            
            // Note: ClickHandler is created in OnEnter() for proper initialization timing
        }

        private class CustomContent : IScreenContent
        {
            public string[] BorderLines { get; }
            public string[] ContentLines { get; }
            public CustomContent(string[] border, string[] content)
            {
                BorderLines = border;
                ContentLines = content;
            }
        }
        
        /// <summary>
        /// Sets the elements for Layer 3 rendering.
        /// </summary>
        public void SetElements(List<HolographicTextElement> elements)
        {
            _elementLayer = new ElementLayer(elements, _fontSize);
            _elementLayer.SetPriorityOrder(Layer3PriorityOrder);
            AddLayer(_elementLayer);
        }
        
        /// <summary>
        /// Gets the ElementLayer for external access.
        /// </summary>
        public ElementLayer GetElementLayer() => _elementLayer;
        
        /// <summary>
        /// Sets the cursor visibility state.
        /// </summary>
        public void SetCursorState(string elementId, bool visible)
        {
            _elementLayer?.SetCursorState(elementId, visible);
        }
        
        /// <summary>
        /// Marks the ElementLayer as dirty.
        /// </summary>
        public void MarkElementLayerDirty()
        {
        }
        

        /// <summary>
        /// Called when entering this screen.
        /// </summary>
        public override void OnEnter(ScreenTransitionContext context)
        {
            base.OnEnter(context);
            
            // Subscribe to Layer 2 completion
            OnLayer2Complete += StartLayer3Animation;
            
            // NEW: Create and setup click handler
            ClickHandler = new MainScreenClickHandler(this);
            ClickHandler.SetupZones();
            
            // Enable value field click zones based on star selected
            UpdateClickZoneState(context?.HasStarSelected ?? false);
            
            // Show elements
            if (_elementLayer != null)
            {
                bool hasStar = context?.HasStarSelected ?? false;
                
                _elementLayer.SetElementVisibility(true);
                
                // Buttons and search always visible
                _elementLayer?.SetElementVisibility("save_button", true);
                _elementLayer?.SetElementVisibility("reset_button", true);
                _elementLayer?.SetElementVisibility("rescan_button", true);
                _elementLayer?.SetElementVisibility("search_input", true);
            }
        }
        
        /// <summary>
        /// Called when exiting this screen.
        /// </summary>
        public override void OnExit()
        {
            base.OnExit();
            
            OnLayer2Complete -= StartLayer3Animation;
            
            _elementLayer?.SetElementVisibility(false);
        }
        
        /// <summary>
        /// Initialize click zones. Called when display is powered on.
        /// </summary>
        public void SetClickZones()
        {
            // New system sets up zones automatically in OnEnter
            // This method preserved for compatibility
            ModFileLogger.Log("[MainScreen] SetClickZones() called (compatibility no-op)");
        }
        
        /// <summary>
        /// Clear click zones. Called when display is powered off.
        /// </summary>
        public void ClearClickZones()
        {
            ClickHandler?.ZoneManager?.Clear();
            ModFileLogger.Log("[MainScreen] ClearClickZones() called");
        }
        
        /// <summary>
        /// Enable/disable click zones based on star selection state.
        /// </summary>
        private void UpdateClickZoneState(bool hasStarSelected)
        {
            if (ClickHandler?.ZoneManager == null) return;
            
            foreach (var zone in ClickHandler.ZoneManager.GetAllZones())
            {
                if (zone.Category == "value")
                {
                    zone.IsEnabled = hasStarSelected;
                }
            }
        }
        
        // Callback methods invoked by MainScreenClickHandler
        
        public void OnValueClicked(string elementId)
        {
            ModFileLogger.Log($"[MainScreen] OnValueClicked: {elementId}");
            OnElementClicked?.Invoke(elementId);
        }
        
        public void OnSaveClicked()
        {
            ModFileLogger.Log("[MainScreen] OnSaveClicked");
            OnElementClicked?.Invoke("save_button");
        }
        
        public void OnResetClicked()
        {
            ModFileLogger.Log("[MainScreen] OnResetClicked");
            OnElementClicked?.Invoke("reset_button");
        }
        
        public void OnRescanClicked()
        {
            ModFileLogger.Log("[MainScreen] OnRescanClicked");
            OnElementClicked?.Invoke("rescan_button");
        }
        
        public void OnSearchClicked()
        {
            ModFileLogger.Log("[MainScreen] OnSearchClicked");
            OnElementClicked?.Invoke("search_input");
        }
        
        public void OnSelectedStarClicked()
        {
            ModFileLogger.Log("[MainScreen] OnSelectedStarClicked");
            OnElementClicked?.Invoke("selected_star");
        }
        
        public void OnResultClicked(int index)
        {
            ModFileLogger.Log($"[MainScreen] OnResultClicked: result_{index}");
            OnElementClicked?.Invoke($"result_{index}");
        }
        
        public void OnElementHoverEnter(string elementId)
        {
            // Currently no-op or add highlight logic
            ModFileLogger.Log($"[MainScreen] Hover enter: {elementId}");
        }
        
        public void OnElementHoverExit(string elementId)
        {
            // Currently no-op or remove highlight logic
            ModFileLogger.Log($"[MainScreen] Hover exit: {elementId}");
        }
        
        /// <summary>
        /// IClickHandler implementation - delegates to ClickHandler.
        /// </summary>
        public void HandleInput(Rect displayRect)
        {
            ClickHandler?.HandleInput(displayRect);
        }
        
        /// <summary>
        /// Renders this screen.
        /// </summary>
        public override void Render(Rect displayRect, IntPtr textSystem)
        {
            if (textSystem == IntPtr.Zero) return;
            
            // Handle clicks FIRST - this needs to run for all event types
            HandleInput(displayRect);
            
            // Only render graphics during Repaint event
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            
            uint color = CinematicShadersUIResources.Colors.CRTColors.GetColorUint(StarfieldSettings.KartographerGridColor);
            
            var cells = new ConsoleCellInstanceNative[767];
            int writeIndex = 0;
            
            var borderLayer = Layers[0] as BorderLayer;
            if (borderLayer != null && Layer1Progress > 0)
                borderLayer.FillCellData(textSystem, cells, ref writeIndex, Layer1Progress, color, _fontSize, _aspectRatio);
            
            var contentLayer = Layers[1] as ContentLayer;
            if (contentLayer != null && Layer2Progress > 0)
                contentLayer.FillCellData(textSystem, cells, ref writeIndex, Layer2Progress, color, _fontSize, _aspectRatio);
            
            if (_elementLayer != null && Layer3Progress > 0)
                _elementLayer.FillCellData(textSystem, cells, ref writeIndex, Layer3Progress, color, _fontSize, _aspectRatio);
            
            if (writeIndex > 0)
            {
                var rt = GetOrCreateConsoleRenderTexture(displayRect);
                RenderTexture.active = rt;
                
                StarfieldNative.CR_DrawConsoleGrid(
                    textSystem, rt.GetNativeTexturePtr(), cells, writeIndex,
                    displayRect.x, displayRect.y, displayRect.width, displayRect.height,
                    _fontSize, color);
                
                GL.IssuePluginEvent(StarfieldNative.CR_GetConsoleRenderEventFunc(), 0);
                
                RenderTexture.active = null;
                GUI.DrawTexture(displayRect, rt);

                // Draw cursor for the currently animating layer (Layer 3 > 2 > 1)
                Color cursorColor = GetGridColor();
                Vector2? cursorPos = null;
                if (_elementLayer != null && Layer3Progress > 0f && Layer3Progress < 1f)
                    cursorPos = _elementLayer.CursorPosition;
                else if (contentLayer != null && Layer2Progress > 0f && Layer2Progress < 1f)
                    cursorPos = contentLayer.CursorPosition;
                else if (borderLayer != null && Layer1Progress > 0f && Layer1Progress < 1f)
                    cursorPos = borderLayer.CursorPosition;

                if (cursorPos.HasValue)
                {
                    DrawCursorOverlay(displayRect, cursorPos, _fontSize * 0.5f, _fontSize, cursorColor);
                }
            }
        }
        
        /// <summary>
        /// Updates element visibility based on star selection.
        /// </summary>
        public void UpdateElementVisibility(bool hasStarSelected)
        {
            if (_elementLayer == null) return;
            
            string[] valueIds = { "hip_value", "name_value", "distance_value", 
                                  "spectral_value", "mag_value", "const_value", "selected_star" };
            foreach (var id in valueIds)
            {
                _elementLayer.SetElementVisibility(id, hasStarSelected);
            }
            
            _elementLayer?.SetElementVisibility("save_button", true);
            _elementLayer?.SetElementVisibility("reset_button", true);
            _elementLayer?.SetElementVisibility("rescan_button", true);
            _elementLayer?.SetElementVisibility("search_input", true);
        }
        
        /// <summary>
        /// Starts the Layer 3 animation sequence when Layer 2 completes.
        /// </summary>
        private void StartLayer3Animation()
        {
            Debug.Log("[MainScreen] Layer 2 complete, starting Layer 3 animation");
        }
        
        /// <summary>
        /// Calculate animation duration for a layer based on content.
        /// Phase 1: Character-based timing for Layer 3 only.
        /// </summary>
        protected override float CalculateLayerDuration(int layerOrder)
        {
            switch (layerOrder)
            {
                case 1:
                    return Layer1Duration; // Keep existing fixed duration
                case 2:
                    return Layer2Duration; // Keep existing fixed duration
                case 3:
                    // NEW: Character-based calculation for Layer 3
                    var elementLayer = Layers.Count > 2 ? Layers[2] as ElementLayer : null;
                    return elementLayer?.CalculateTypeOnDuration() ?? MIN_TYPEON_DURATION;
                default:
                    return MIN_TYPEON_DURATION;
            }
        }
        
        /// <summary>
        /// Updates this screen's animations.
        /// </summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _elementLayer?.UpdateCursor(deltaTime);
        }
        
        /// <summary>
        /// Called when a star is selected.
        /// Resets Layer 3 animation for type-on effect when new star data is displayed.
        /// </summary>
        public void OnStarSelected(NamedStar star)
        {
            if (star == null) return;
            
            ModFileLogger.Log($"[MainScreen] OnStarSelected called for HIP {star.HipparcosID} - Resetting Layer 3 animation");
            ModFileLogger.Log($"[MainScreen] BEFORE reset: PowerOnTime={PowerOnTime:F3}, Layer3Progress={Layer3Progress:F3}");
            
            // CRITICAL FIX: Reset Layer 3 animation timing to trigger type-on effect
            // This ensures text animates character-by-character instead of appearing instantly
            PowerOnTime = Layer3Delay;  // Reset to start of Layer 3
            Layer3Progress = 0f;        // Force progress to 0
            
            ModFileLogger.Log($"[MainScreen] AFTER reset: PowerOnTime={PowerOnTime:F3}, Layer3Progress={Layer3Progress:F3}");
            
            // Reset all element animations to 0 (this also happens in SetElementText, but doing it
            // explicitly here ensures all elements are reset even if text doesn't change)
            _elementLayer?.ResetAllElementAnimations();
            
            // Set text values - this will also call ResetAllElementAnimations() internally
            _elementLayer?.SetElementText("hip_value", star.HipparcosID.ToString());
            _elementLayer?.SetElementText("name_value", star.Name);
            _elementLayer?.SetElementText("distance_value", $"{star.DistanceLy:F1} LY");
            _elementLayer?.SetElementText("spectral_value", star.SpectralType);
            _elementLayer?.SetElementText("mag_value", star.Magnitude.ToString("F2"));
            _elementLayer?.SetElementText("const_value", star.Constellation);
            _elementLayer?.SetElementText("selected_star", star.Name);
            
            // Enable value field click zones
            UpdateClickZoneState(true);
            UpdateElementVisibility(true);
            ReleaseConsoleRenderTexture();
            
            ModFileLogger.Log($"[MainScreen] OnStarSelected complete - animation should start from 0");
        }

        /// <summary>
        /// Called when the star selection is cleared.
        /// </summary>
        public void OnStarDeselected()
        {
            _elementLayer?.ClearValueFields();
            UpdateClickZoneState(false);
            UpdateElementVisibility(false);
        }
        
        /// <summary>
        /// Get the grid color from StarfieldSettings.
        /// </summary>
        private Color GetGridColor()
        {
            int colorIndex = StarfieldSettings.KartographerGridColor;
            switch (colorIndex)
            {
                case 0: return new Color(0.1f, 0.9f, 0.7f);
                case 1: return new Color(1.0f, 0.65f, 0.0f);
                case 2: return new Color(0.85f, 0.95f, 1.0f);
                case 3: return new Color(0.25f, 1.0f, 0.0f);
                default: return new Color(0.1f, 0.9f, 0.7f);
            }
        }
        
        private uint GetGridColorUint()
        {
            Color c = GetGridColor();
            uint r = (uint)(c.r * 255) & 0xFF;
            uint g = (uint)(c.g * 255) & 0xFF;
            uint b = (uint)(c.b * 255) & 0xFF;
            return 0xFF000000 | (r << 16) | (g << 8) | b;
        }
        
    }
}
