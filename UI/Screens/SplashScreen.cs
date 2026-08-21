using System;
using UnityEngine;
using CinematicShaders.UI.Screens.Layers;
using CinematicShaders.Native;
using CinematicShaders.Native.Structs;
using CinematicShaders.Core;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Boot splash screen displayed immediately when PWR is clicked.
    /// Shows the STAR console ASCII art logo for 0.75s, then fades out over 0.75s
    /// before automatically transitioning to MainScreen or ScanScreen.
    /// </summary>
    /// <remarks>
    /// <para><b>Layer Configuration:</b></para>
    /// - Layer 1: Not used (no border)
    /// - Layer 2: ASCII art logo and "System for Tabulation of Astrometric Records"
    /// - Layer 3: Not used
    /// 
    /// <para><b>Animation Timing:</b></para>
    /// - 0.0s - 0.75s: Logo displayed at full opacity
    /// - 0.75s - 1.5s: Logo fades to transparent
    /// - 1.5s: Auto-transition to target screen
    /// 
    /// <para><b>Transition Behavior:</b></para>
    /// SplashScreen reads TargetScreenName from the transition context to determine
    /// which screen to transition to after the animation completes. This is set by
    /// HolographicDisplay based on JSON availability check.
    /// </remarks>
    public class SplashScreen : BaseScreen
    {
        private readonly float _fontSize;
        private readonly float _aspectRatio;
        
        // Splash-specific timing (pop-on then fade)
        private const float VISIBLE_DURATION = 0.75f;
        private const float FADE_DURATION = 0.75f;
        private const float TOTAL_DURATION = 1.5f;
        
        /// <summary>
        /// Target screen to transition to after splash completes.
        /// Set via transition context.
        /// </summary>
        private string _targetScreenName = "Main";
        
        /// <summary>
        /// Flag to ensure we only transition once.
        /// </summary>
        private bool _hasTransitioned = false;
        
        /// <summary>
        /// Event fired when splash animation completes and we're about to transition.
        /// Subscribe to this to trigger the screen transition.
        /// </summary>
        public event Action<string> OnSplashComplete;
        
        /// <summary>
        /// Initializes a new SplashScreen with the specified font styling.
        /// </summary>
        /// <param name="fontSize">Font size for text rendering</param>
        /// <param name="aspectRatio">Aspect ratio for layout (default 0.667 = 2:3)</param>
        public SplashScreen(float fontSize, float aspectRatio = 0.667f)
        {
            ScreenName = "Splash";
            _fontSize = fontSize;
            _aspectRatio = aspectRatio;
            
            // Only use Layer 2 for the logo
            AddLayer(new ContentLayer(CinematicShadersUIStrings.StarConsole.SplashLines));
        }
        

        /// <summary>
        /// Called when entering this screen.
        /// Reads target screen from context and resets animation state.
        /// </summary>
        public override void OnEnter(ScreenTransitionContext context)
        {
            base.OnEnter(context);
            
            _hasTransitioned = false;
            
            // Get target screen from context (set by HolographicDisplay based on JSON check)
            if (context != null && !string.IsNullOrEmpty(context.TargetScreenName))
            {
                _targetScreenName = context.TargetScreenName;
            }
            else
            {
                _targetScreenName = "Main"; // Default fallback
            }
            
            Debug.Log($"[SplashScreen] Entered, will transition to: {_targetScreenName}");
        }
        
        /// <summary>
        /// Called when exiting this screen.
        /// </summary>
        public override void OnExit()
        {
            base.OnExit();
            // Clean up transition flag
            _hasTransitioned = false;
        }
        
        /// <summary>
        /// Updates the splash animation and triggers transition when complete.
        /// </summary>
        public override void Update(float deltaTime)
        {
            // Use custom timing instead of base layer timing
            PowerOnTime += deltaTime;
            
            // Check for animation completion and transition
            if (!_hasTransitioned && PowerOnTime >= TOTAL_DURATION)
            {
                _hasTransitioned = true;
                Debug.Log($"[SplashScreen] Animation complete, transitioning to {_targetScreenName}");
                OnSplashComplete?.Invoke(_targetScreenName);
            }
        }
        
        /// <summary>
        /// Renders the splash screen with fade-out effect.
        /// </summary>
        public override void Render(Rect displayRect, IntPtr textSystem)
        {
            if (textSystem == IntPtr.Zero) return;
            
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            
            // Calculate alpha: 1.0 for first 0.75s, then fade to 0
            float alpha = 1.0f;
            if (PowerOnTime > VISIBLE_DURATION)
            {
                float fadeProgress = (PowerOnTime - VISIBLE_DURATION) / FADE_DURATION;
                alpha = Mathf.Clamp01(1.0f - fadeProgress);
            }
            
            if (alpha <= 0.01f)
                return;
            
            uint baseColor = CinematicShadersUIResources.Colors.CRTColors.GetColorUint(StarfieldSettings.KartographerGridColor);
            uint color = (baseColor & 0x00FFFFFF) | ((uint)(alpha * 255) << 24);
            
            var cells = new ConsoleCellInstanceNative[767];
            int writeIndex = 0;
            
            var contentLayer = Layers[0] as ContentLayer;
            if (contentLayer != null)
                contentLayer.FillCellData(textSystem, cells, ref writeIndex, 1.0f, color, _fontSize, _aspectRatio);
            
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
            }
        }
        
    }
}
