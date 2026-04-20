using System;
using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.Core;
using CinematicShaders.UI;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Central coordinator for screen management in the holographic console system.
    /// Handles screen registration, transitions, and manages the shared texture pool.
    /// </summary>
    /// <remarks>
    /// <para><b>Architecture Overview:</b></para>
    /// The ScreenManager maintains a registry of all available screens and manages
    /// transitions between them.
    /// 
    /// <para><b>Screen Lifecycle:</b></para>
    /// 1. Register screens via RegisterScreen()
    /// 2. Transition between screens via TransitionTo()
    /// 3. Call Update() and Render() each frame for the current screen
    /// 4. Cleanup via Shutdown() when console closes
    /// 
    /// <para><b>Usage Example:</b></para>
    /// <code>
    /// var manager = new ScreenManager(textSystemPtr);
    /// manager.RegisterScreen(new MainScreen(border, labels, fontSize));
    /// manager.RegisterScreen(new ScanScreen(border, art, fontSize));
    /// manager.TransitionTo("Main");
    /// </code>
    /// </remarks>
    public class ScreenManager
    {
        private readonly Dictionary<string, IScreen> _screens = new Dictionary<string, IScreen>();
        private IScreen _currentScreen;
        private readonly IntPtr _textSystem;
        private bool _wasTypingLastFrame = false;
        
        /// <summary>
        /// Gets the currently active screen, or null if no transition has occurred.
        /// </summary>
        public IScreen CurrentScreen => _currentScreen;
        
        /// <summary>
        /// Gets the name of the currently active screen, or null if none.
        /// </summary>
        public string CurrentScreenName => _currentScreen?.ScreenName;
        
        /// <summary>
        /// Gets a registered screen by name.
        /// Returns null if the screen is not registered.
        /// </summary>
        /// <param name="screenName">The name of the screen to get</param>
        /// <returns>The screen if found, null otherwise</returns>
        public IScreen GetScreen(string screenName)
        {
            _screens.TryGetValue(screenName, out var screen);
            return screen;
        }
        
        /// <summary>
        /// Initializes a new ScreenManager with the specified native text system.
        /// </summary>
        /// <param name="textSystem">Native text system pointer for GPU text rendering</param>
        public ScreenManager(IntPtr textSystem)
        {
            _textSystem = textSystem;
        }
        
        /// <summary>
        /// Initializes the shared texture pool for rendering.
        /// </summary>
        /// <param name="width">Requested width (uses actual dimensions for unified grid, Large preset for legacy)</param>
        /// <param name="height">Requested height (uses actual dimensions for unified grid, Large preset for legacy)</param>
        /// <remarks>
        /// For unified grid: Uses passed dimensions to support Small/Medium/Large sizes.
        /// For legacy: Always creates textures at Large preset size (825x450) for 1:1 pixel mapping.
        /// Creates textures for layers 1, 2, and 3.
        /// </remarks>
        public void InitializeTextures(int width, int height)
        {
            // Unified grid: Use actual display dimensions for dynamic sizing
            Debug.Log($"[ScreenManager] Initialized textures at display size: {width}x{height}");
        }
        
        /// <summary>
        /// Registers a screen with this manager.
        /// </summary>
        /// <param name="screen">The screen to register</param>
        /// <exception cref="ArgumentNullException">Thrown if screen is null</exception>
        /// <remarks>
        /// Screens are keyed by their ScreenName property. Registering a screen
        /// with the same name as an existing screen will replace the old one.
        /// </remarks>
        public void RegisterScreen(IScreen screen)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            _screens[screen.ScreenName] = screen;
        }
        
        /// <summary>
        /// Transitions to a new screen with proper lifecycle handling.
        /// </summary>
        /// <param name="screenName">The name of the screen to transition to</param>
        /// <param name="context">Optional transition context (created if null)</param>
        /// <remarks>
        /// This method:
        /// 1. Calls OnExit() on the current screen
        /// 2. Updates the transition context with previous screen info
        /// 3. Switches to the new screen
        /// 4. Calls OnEnter() on the new screen
        /// 
        /// If the screen name is not registered, logs an error and does nothing.
        /// </remarks>
        public void TransitionTo(string screenName, ScreenTransitionContext context = null)
        {
            if (!_screens.ContainsKey(screenName))
            {
                Debug.LogError($"[ScreenManager] Screen '{screenName}' not registered");
                return;
            }
            
            var newScreen = _screens[screenName];
            
            // Exit current screen
            _currentScreen?.OnExit();
            
            // Track previous state for context
            if (context == null)
                context = new ScreenTransitionContext();
            context.PreviousScreen = _currentScreen?.ScreenName ?? screenName;
            
            // Switch to new screen
            _currentScreen = newScreen;
            _currentScreen.OnEnter(context);
            
            Debug.Log($"[ScreenManager] Transitioned to {screenName}");
        }
        
        /// <summary>
        /// Updates the currently active screen.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last frame in seconds</param>
        /// <remarks>
        /// Call this every frame from the console's Update() method.
        /// Does nothing if no screen is currently active.
        /// </remarks>
        public void Update(float deltaTime)
        {
            _currentScreen?.Update(deltaTime);
            
            // Manage typing sound loop based on current screen's type-on animation state
            bool isTyping = (_currentScreen as BaseScreen)?.IsTypeOnAnimationActive ?? false;
            if (isTyping && !_wasTypingLastFrame)
            {
                ModAudioManager.PlayLoop(AudioGroup.StarConsole, "CinematicShaders/Sounds/typingsound", "starconsole_typing");
            }
            else if (!isTyping && _wasTypingLastFrame)
            {
                ModAudioManager.StopLoop("starconsole_typing", 0.025f);
            }
            _wasTypingLastFrame = isTyping;
        }
        
        /// <summary>
        /// Renders the currently active screen.
        /// </summary>
        /// <param name="displayRect">Screen-space rectangle for rendering</param>
        /// <remarks>
        /// Call this during the Repaint event from the console's OnGUI().
        /// </remarks>
        public void Render(Rect displayRect)
        {
            if (_currentScreen == null)
            {
                ModFileLogger.Log("[ScreenManager] Render - EARLY EXIT, _currentScreen is null");
                return;
            }
            
            _currentScreen.Render(displayRect, _textSystem);
        }
        
        /// <summary>
        /// Cleans up all resources used by this manager.
        /// </summary>
        /// <remarks>
        /// Call this when the console is shutting down. This will:
        /// 1. Call OnExit() on the current screen
        /// 2. Clear internal collections
        /// 
        /// The ScreenManager should not be used after Shutdown() is called.
        /// </remarks>
        public void Shutdown()
        {
            _currentScreen?.OnExit();
            _currentScreen = null;
            
            // Ensure typing sound stops when the console shuts down
            ModAudioManager.StopLoop("starconsole_typing", 0.025f);
        }
    }
}
