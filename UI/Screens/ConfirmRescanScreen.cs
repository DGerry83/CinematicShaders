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
    /// Confirmation dialog screen for rescan operations.
    /// Displays a warning message with YES/NO buttons for user confirmation.
    /// </summary>
    /// <remarks>
    /// <para><b>Layer Configuration:</b></para>
    /// - Layer 1: Border frame
    /// - Layer 2: Warning text and dialog content
    /// - Layer 3: YES/NO buttons (appear after content)
    /// 
    /// <para><b>Purpose:</b></para>
    /// This screen appears when the user initiates a catalog rescan while
    /// star data already exists. It confirms the user's intention to
    /// overwrite existing data before proceeding.
    /// 
    /// <para><b>Interactions:</b></para>
    /// - Click YES: Triggers OnYesClicked, proceeds with rescan
    /// - Click NO: Triggers OnNoClicked, returns to previous screen
    /// - Hover over buttons: Box outline appears
    /// 
    /// <para><b>Visual Design:</b></para>
    /// The dialog uses the standard border with warning text centered.
    /// Buttons appear in sequence after the warning text types on,
    /// following the Layer3PriorityOrder.
    /// </remarks>
    public class ConfirmRescanScreen : BaseScreen, IClickHandler
    {
        private readonly float _fontSize;
        private readonly float _aspectRatio;
        
        // Constraint-based layout for this screen
        private ConfirmRescanScreenLayout _layout;
        
        // New click handler (Simplified Click System)
        public ConfirmRescanClickHandler ClickHandler { get; private set; }
        public ClickZoneManager ZoneManager => ClickHandler?.ZoneManager;
        
        // NEW: Handler for controller-based click routing
        public ConfirmRescanHandler Handler { get; set; }
        
        /// <summary>
        /// Layer 3 priority order for button appearance sequence.
        /// YES button appears first, followed by NO button.
        /// </summary>
        protected override List<string> Layer3PriorityOrder => new List<string>
        {
            "yes_button",
            "no_button"
        };
        

        // Callback methods invoked by ConfirmRescanClickHandler
        
        public void OnYesButtonClicked()
        {
            ModFileLogger.Log("[ConfirmRescanScreen] OnYesButtonClicked");
            Handler?.OnYesClicked();
        }
        
        public void OnNoButtonClicked()
        {
            ModFileLogger.Log("[ConfirmRescanScreen] OnNoButtonClicked");
            Handler?.OnNoClicked();
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
        /// Initializes a new ConfirmRescanScreen with the specified content and styling.
        /// </summary>
        /// <param name="borderLines">ASCII art lines for the border frame</param>
        /// <param name="textLines">Warning text lines for the dialog</param>
        /// <param name="fontSize">Font size for text rendering</param>
        /// <param name="aspectRatio">Aspect ratio for layout (default 0.667 = 2:3)</param>
        public ConfirmRescanScreen(string[] borderLines, string[] textLines, float fontSize, float aspectRatio = 0.667f)
            : this(new CustomContent(borderLines, textLines), fontSize, aspectRatio)
        {
        }

        /// <summary>
        /// Initializes a new ConfirmRescanScreen using an IScreenContent provider.
        /// </summary>
        /// <param name="content">Content provider for border and content lines</param>
        /// <param name="fontSize">Font size for text rendering</param>
        /// <param name="aspectRatio">Aspect ratio for layout (default 0.667 = 2:3)</param>
        public ConfirmRescanScreen(IScreenContent content, float fontSize, float aspectRatio = 0.667f)
        {
            ScreenName = "ConfirmRescan";
            _fontSize = fontSize;
            _aspectRatio = aspectRatio;
            
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
        

        /// <summary>
        /// Gets the constraint-based layout for this screen.
        /// </summary>
        public ConfirmRescanScreenLayout Layout
        {
            get
            {
                if (_layout == null)
                {
                    _layout = new ConfirmRescanScreenLayout();
                    var engine = new LayoutEngine();
                    Vector2 dims = TerminalGridConfig.GetDisplayDimensions(TerminalGridConfig.CurrentDisplaySize);
                    _layout.Build(engine, new Rect(0, 0, dims.x, dims.y));
                }
                return _layout;
            }
        }
        
        /// <summary>
        /// Called when entering this screen. Initializes animations and click zones.
        /// </summary>
        /// <param name="context">Transition context</param>
        public override void OnEnter(ScreenTransitionContext context)
        {
            base.OnEnter(context);
            
            OnLayer2Complete += StartLayer3Animation;
            
            // NEW: Create and setup click handler (Simplified Click System)
            ClickHandler = new ConfirmRescanClickHandler(this);
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
        
        /// <summary>
        /// Starts Layer 3 animation when Layer 2 completes.
        /// </summary>
        private void StartLayer3Animation()
        {
            Debug.Log("[ConfirmRescanScreen] Layer 2 complete, starting Layer 3");
        }
        
        /// <summary>
        /// Renders this screen.
        /// </summary>
        /// <param name="displayRect">Screen rectangle for rendering</param>
        /// <param name="textSystem">Native text system pointer</param>
        /// <remarks>
        /// Renders Layer 1 (border) and Layer 2 (warning text).
        /// YES/NO buttons are rendered separately by the native system
        /// to support interactive hover states.
        /// 
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
            
            var cells = new ConsoleCellInstanceNative[767];
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
