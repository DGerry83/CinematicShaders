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
    /// Scan screen displayed when no star catalog JSON data is available.
    /// Shows a large ASCII art "SCAN" prompt that the user can click to trigger rescan.
    /// </summary>
    /// <remarks>
    /// <para><b>Layer Configuration:</b></para>
    /// - Layer 1: Border frame
    /// - Layer 2: Large "SCAN" ASCII art
    /// - Layer 3: Not used (reserved for future interactive elements)
    /// 
    /// <para><b>Purpose:</b></para>
    /// This screen appears when the star catalog metadata JSON file is missing
    /// or cannot be loaded. It prompts the user to click the SCAN area to
    /// regenerate the catalog data.
    /// 
    /// <para><b>Interactions:</b></para>
    /// - Hover over SCAN art: Box outline appears
    /// - Click SCAN art: Triggers OnScanClicked event
    /// 
    /// Unlike other screens, ScanScreen does not use Layer 3 because it has
    /// no dynamic value fields or buttons - just the clickable SCAN art.
    /// </remarks>
    public class ScanScreen : BaseScreen, IClickHandler
    {
        private readonly float _fontSize;
        private readonly float _aspectRatio;
        private readonly string[] _contentLines;
        
        /// <summary>
        /// Content lines used to render the SCAN ASCII art.
        /// Exposed for click-zone bounding-box calculation.
        /// </summary>
        public string[] ContentLines => _contentLines;
        
        // NEW: Simple click handler
        public ScanScreenClickHandler ClickHandler { get; private set; }
        public ClickZoneManager ZoneManager => ClickHandler?.ZoneManager;
        
        // NEW: Handler for controller-based click routing
        public ScanScreenHandler Handler { get; set; }
        
        /// <summary>
        /// Layer 3 priority order (reserved for future interactive elements).
        /// Currently only contains scan_prompt for potential future use.
        /// </summary>
        protected override List<string> Layer3PriorityOrder => new List<string>
        {
            "scan_prompt" // If we add interactive elements later
        };
        

        /// <summary>
        /// Initializes a new ScanScreen with the specified content and styling.
        /// </summary>
        /// <param name="borderLines">ASCII art lines for the border frame</param>
        /// <param name="artLines">ASCII art lines for the SCAN graphic</param>
        /// <param name="fontSize">Font size for text rendering</param>
        /// <param name="aspectRatio">Aspect ratio for layout (default 0.667 = 2:3)</param>
        public ScanScreen(string[] borderLines, string[] artLines, float fontSize, float aspectRatio = 0.667f)
            : this(new CustomContent(borderLines, artLines), fontSize, aspectRatio)
        {
        }

        /// <summary>
        /// Initializes a new ScanScreen using an IScreenContent provider.
        /// </summary>
        /// <param name="content">Content provider for border and content lines</param>
        /// <param name="fontSize">Font size for text rendering</param>
        /// <param name="aspectRatio">Aspect ratio for layout (default 0.667 = 2:3)</param>
        public ScanScreen(IScreenContent content, float fontSize, float aspectRatio = 0.667f)
        {
            ScreenName = "Scan";
            _fontSize = fontSize;
            _aspectRatio = aspectRatio;
            _contentLines = content.ContentLines;
            
            // Add layers using content
            AddLayer(new BorderLayer(content.BorderLines));
            AddLayer(new ContentLayer(content.ContentLines));
        }

        // Private helper class for backward compatibility
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
        

        // Constraint-based layout for this screen
        private ScanScreenLayout _layout;
        
        /// <summary>
        /// Gets the constraint-based layout for this screen.
        /// </summary>
        public ScanScreenLayout Layout
        {
            get
            {
                if (_layout == null)
                {
                    _layout = new ScanScreenLayout();
                    var engine = new LayoutEngine();
                    Vector2 dims = TerminalGridConfig.GetDisplayDimensions(TerminalGridConfig.CurrentDisplaySize);
                    _layout.Build(engine, new Rect(0, 0, dims.x, dims.y));
                }
                return _layout;
            }
        }
        
        /// <summary>
        /// Called when entering this screen. Initializes animations and click zone.
        /// </summary>
        /// <param name="context">Transition context</param>
        public override void OnEnter(ScreenTransitionContext context)
        {
            base.OnEnter(context);
            
            OnLayer2Complete += StartLayer3Animation;
            
            // NEW: Create and setup click handler
            ClickHandler = new ScanScreenClickHandler(this);
            ClickHandler.SetupZones();
            
        }
        
        /// <summary>
        /// Called when exiting this screen. Cleans up animations and hover state.
        /// </summary>
        public override void OnExit()
        {
            base.OnExit();
            
            OnLayer2Complete -= StartLayer3Animation;
        }
        
        // Callback methods invoked by ScanScreenClickHandler
        
        public void OnScanAreaClicked()
        {
            ModFileLogger.Log("[ScanScreen] OnScanAreaClicked");
            Handler?.OnScanClicked();
        }
        
        public void OnElementHoverEnter(string elementId)
        {
            _hoveredElementId = elementId;
        }
        
        public void OnElementHoverExit(string elementId)
        {
            _hoveredElementId = null;
        }
        
        /// <summary>
        /// IClickHandler implementation - delegates to ClickHandler.
        /// </summary>
        public void HandleInput(Rect displayRect)
        {
            ClickHandler?.HandleInput(displayRect);
        }
        
        /// <summary>
        /// Starts Layer 3 animation when Layer 2 completes.
        /// </summary>
        private void StartLayer3Animation()
        {
            Debug.Log("[ScanScreen] Layer 2 complete, starting Layer 3");
        }
        
        /// <summary>
        /// Renders this screen.
        /// </summary>
        /// <param name="displayRect">Screen rectangle for rendering</param>
        /// <param name="textSystem">Native text system pointer</param>
        /// <remarks>
        /// Renders Layer 1 (border) and Layer 2 (SCAN art).
        /// Layer 2 only renders once its progress is greater than 0.
        /// Also handles mouse interaction during repaint events.
        /// </remarks>
        public override void Render(Rect displayRect, IntPtr textSystem)
        {
            if (textSystem == IntPtr.Zero) return;
            
            if (Event.current != null)
                HandleInput(displayRect);
            
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            
            uint color = CinematicShadersUIResources.Colors.CRTColors.GetColorUint(StarfieldSettings.KartographerGridColor);
            
            var cells = new ConsoleCellInstanceNative[StarfieldNative.MaxConsoleCells];
            int writeIndex = 0;
            
            var borderLayer = Layers[0] as BorderLayer;
            if (borderLayer != null && Layer1Progress > 0)
                borderLayer.FillCellData(textSystem, cells, ref writeIndex, Layer1Progress, color, _fontSize, _aspectRatio);
            
            var contentLayer = Layers[1] as ContentLayer;
            if (contentLayer != null && Layer2Progress > 0)
                contentLayer.FillCellData(textSystem, cells, ref writeIndex, Layer2Progress, color, _fontSize, _aspectRatio);
            
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

                Color cursorColor = CinematicShadersUIResources.Colors.CRTColors.GetColor(StarfieldSettings.KartographerGridColor);
                Vector2? cursorPos = null;
                if (contentLayer != null && Layer2Progress > 0f && Layer2Progress < 1f)
                    cursorPos = contentLayer.CursorPosition;
                else if (borderLayer != null && Layer1Progress > 0f && Layer1Progress < 1f)
                    cursorPos = borderLayer.CursorPosition;

                if (cursorPos.HasValue)
                {
                    DrawCursorOverlay(displayRect, cursorPos, cursorColor);
                }

                DrawHoverOverlay(displayRect, ClickHandler?.ZoneManager);
            }
        }
        
    }
}
