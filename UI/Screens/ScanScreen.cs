using System;
using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.UI.Screens.Layers;
using CinematicShaders.Native;
using CinematicShaders.Core;
using CinematicShaders.UI.Animation;
using CinematicShaders.UI.Content;

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
    public class ScanScreen : BaseScreen
    {
        private readonly float _fontSize;
        private readonly float _aspectRatio;
        private RenderTexture _layer1Texture;
        private RenderTexture _layer2Texture;
        private Sequencer _sequencer;
        
        // Click zone for SCAN area
        private ClickZone _scanZone;
        private bool _scanHovered = false;
        
        /// <summary>
        /// Layer 3 priority order (reserved for future interactive elements).
        /// Currently only contains scan_prompt for potential future use.
        /// </summary>
        protected override List<string> Layer3PriorityOrder => new List<string>
        {
            "scan_prompt" // If we add interactive elements later
        };
        
        /// <summary>
        /// Event fired when the SCAN art is clicked.
        /// Subscribe to this to trigger catalog rescan.
        /// </summary>
        public event System.Action OnScanClicked;
        
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
        /// Sets the shared textures for rendering.
        /// ScanScreen uses l1 and l2, ignores l3.
        /// </summary>
        /// <param name="l1">Layer 1 texture (border)</param>
        /// <param name="l2">Layer 2 texture (SCAN art)</param>
        /// <param name="l3">Layer 3 texture (ignored)</param>
        /// <remarks>
        /// This is an example of a two-layer screen that ignores the third texture.
        /// The unused l3 parameter is documented to show the design pattern.
        /// </remarks>
        public override void SetTextures(RenderTexture l1, RenderTexture l2, RenderTexture l3)
        {
            _layer1Texture = l1;
            _layer2Texture = l2;
            // Ignore l3 - this screen doesn't use Layer 3
            
            if (Layers.Count > 0 && Layers[0] is BorderLayer bl)
                bl.SetTargetTexture(l1);
            if (Layers.Count > 1 && Layers[1] is ContentLayer cl)
                cl.SetTargetTexture(l2);
        }
        
        /// <summary>
        /// Called when entering this screen. Initializes animations and click zone.
        /// </summary>
        /// <param name="context">Transition context</param>
        public override void OnEnter(ScreenTransitionContext context)
        {
            base.OnEnter(context);
            
            _sequencer = new Sequencer(Layer3PriorityOrder);
            OnLayer2Complete += StartLayer3Animation;
            
            // Initialize single large click zone for SCAN area
            _scanZone = new ClickZone("scan_area", HolographicLayoutConfig.ZONE_SCAN_AREA, true);
            _scanHovered = false;
        }
        
        /// <summary>
        /// Called when exiting this screen. Cleans up animations and hover state.
        /// </summary>
        public override void OnExit()
        {
            base.OnExit();
            
            OnLayer2Complete -= StartLayer3Animation;
            _sequencer?.StopSequence();
            _sequencer = null;
            
            // Clear hover state
            _scanHovered = false;
            StarfieldNative.CR_SetBoxOutline(0, 0, 0, 0, 0);
        }
        
        /// <summary>
        /// Handles mouse interaction for the SCAN area.
        /// </summary>
        /// <param name="mousePos">Current mouse position in screen coordinates</param>
        /// <param name="displayRect">Display rectangle in screen coordinates</param>
        /// <param name="mouseDown">True if left mouse button was pressed this frame</param>
        /// <param name="mouseUp">True if left mouse button was released this frame</param>
        /// <remarks>
        /// Detects hover over the SCAN zone, updates the box outline visual,
        /// and fires OnScanClicked when clicked.
        /// </remarks>
        public void HandleMouse(Vector2 mousePos, Rect displayRect, bool mouseDown, bool mouseUp)
        {
            Vector2 gridPos = MouseToGrid(mousePos, displayRect);
            
            bool wasHovered = _scanHovered;
            _scanHovered = _scanZone.Contains(gridPos);
            
            // Update box outline on hover change
            if (_scanHovered != wasHovered)
            {
                if (_scanHovered)
                {
                    Rect uvRect = _scanZone.GetUVRect();
                    StarfieldNative.CR_SetBoxOutline(1, uvRect.xMin, uvRect.yMin, uvRect.xMax, uvRect.yMax);
                }
                else
                {
                    StarfieldNative.CR_SetBoxOutline(0, 0, 0, 0, 0);
                }
            }
            
            // Handle click
            if (mouseUp && _scanHovered)
            {
                OnScanClicked?.Invoke();
            }
        }
        
        /// <summary>
        /// Starts Layer 3 animation when Layer 2 completes.
        /// </summary>
        private void StartLayer3Animation()
        {
            Debug.Log("[ScanScreen] Layer 2 complete, starting Layer 3");
            _sequencer?.StartSequence();
        }
        
        /// <summary>
        /// Updates this screen's animations.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last frame</param>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _sequencer?.Update();
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
            
            // Only render during Repaint events and when Event.current is valid
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            
            uint color = CinematicShadersUIResources.Colors.CRTColors.GetColorUint(StarfieldSettings.KartographerGridColor);
            
            // Render Layer 1: Border
            var borderLayer = Layers[0] as BorderLayer;
            if (borderLayer != null && _layer1Texture != null && _layer1Texture.IsCreated())
            {
                borderLayer.RenderToTexture(textSystem, color, _fontSize, _aspectRatio, Layer1Progress);
                
                // Draw the texture to screen
                Graphics.DrawTexture(
                    displayRect,
                    _layer1Texture,
                    new Rect(0, 1, 1, -1),  // Flip Y
                    0, 0, 0, 0,
                    Color.white,
                    null
                );
            }
            
            // Render Layer 2: SCAN art
            var contentLayer = Layers[1] as ContentLayer;
            if (contentLayer != null && _layer2Texture != null && _layer2Texture.IsCreated() && Layer2Progress > 0)
            {
                contentLayer.RenderToTexture(textSystem, color, _fontSize, _aspectRatio, Layer2Progress);
                
                // Draw the texture to screen
                Graphics.DrawTexture(
                    displayRect,
                    _layer2Texture,
                    new Rect(0, 1, 1, -1),  // Flip Y
                    0, 0, 0, 0,
                    Color.white,
                    null
                );
            }
            
            // Handle mouse interaction
            if (Event.current != null)
            {
                Vector2 mousePos = Event.current.mousePosition;
                bool mouseDown = Event.current.type == EventType.MouseDown && Event.current.button == 0;
                bool mouseUp = Event.current.type == EventType.MouseUp && Event.current.button == 0;
                HandleMouse(mousePos, displayRect, mouseDown, mouseUp);
            }
        }
        
    }
}
