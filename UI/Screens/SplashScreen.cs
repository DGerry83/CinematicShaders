using System;
using UnityEngine;
using CinematicShaders.UI.Screens.Layers;
using CinematicShaders.Native;
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
        private RenderTexture _layer2Texture;
        
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
        /// ASCII art lines for the STAR console logo.
        /// </summary>
        private static readonly string[] SPLASH_LINES = new string[]
        {
            @" ________   _________    ________      ________",
            @"|\   ____\ |\___   ___\ |\   __  \    |\   __  \",
            @"\ \  \___|_\|___ \  \_| \ \  \|\  \   \ \  \|\  \",
            @" \ \_____  \    \ \  \   \ \   __  \   \ \   _  _\",
            @"  \|____|\  \  __\ \  \ __\ \  \ \  \ __\ \  \\  \|",
            @"    ____\_\  \|\__\ \__\\__\ \__\ \__\\__\ \__\\ _\|\__\",
            @"   |\_________\|__|\|__\|__|\|__|\|__\|__|\|__|\|__\|__|",
            @"   \|_________|System for Tabulation of Astrometric Records",
            @"                                                       v1.0"
        };
        
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
            AddLayer(new ContentLayer(SPLASH_LINES));
        }
        
        /// <summary>
        /// Sets the shared textures for rendering.
        /// SplashScreen only uses Layer 2 texture for the logo.
        /// </summary>
        public override void SetTextures(RenderTexture l1, RenderTexture l2, RenderTexture l3)
        {
            _layer2Texture = l2;
            // Ignore l1 and l3 - this screen only uses Layer 2
            
            if (Layers.Count > 0 && Layers[0] is ContentLayer cl)
                cl.SetTargetTexture(l2);
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
            
            // Only render during Repaint events
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            
            // Calculate alpha: 1.0 for first 0.75s, then fade to 0
            float alpha = 1.0f;
            if (PowerOnTime > VISIBLE_DURATION)
            {
                float fadeProgress = (PowerOnTime - VISIBLE_DURATION) / FADE_DURATION;
                alpha = Mathf.Clamp01(1.0f - fadeProgress);
            }
            
            // Skip rendering if fully faded
            if (alpha <= 0.01f)
                return;
            
            uint color = CinematicShadersUIResources.Colors.CRTColors.GetColorUint(StarfieldSettings.KartographerGridColor);
            
            // Render Layer 2: Logo
            var contentLayer = Layers[0] as ContentLayer;
            if (contentLayer != null && _layer2Texture != null && _layer2Texture.IsCreated())
            {
                // Render full content (no type-on for splash)
                contentLayer.RenderToTexture(textSystem, color, _fontSize, _aspectRatio, 1.0f);
                
                // Draw with calculated alpha for fade effect
                Color drawColor = new Color(1f, 1f, 1f, alpha);
                Graphics.DrawTexture(
                    displayRect,
                    _layer2Texture,
                    new Rect(0, 1, 1, -1),  // Flip Y
                    0, 0, 0, 0,
                    drawColor,
                    null
                );
                
                // Reset GUI color
                GUI.color = Color.white;
            }
        }
        
        /// <summary>
        /// Gets the grid color from StarfieldSettings.
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
