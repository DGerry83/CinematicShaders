using System;
using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.Core;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Central coordinator for screen management in the holographic console system.
    /// Handles screen registration, transitions, and manages the shared texture pool.
    /// </summary>
    /// <remarks>
    /// <para><b>Architecture Overview:</b></para>
    /// The ScreenManager maintains a registry of all available screens and manages
    /// transitions between them. It owns the shared RenderTexture pool (one texture
    /// per layer) that screens use for rendering.
    /// 
    /// <para><b>Texture Pool:</b></para>
    /// Textures are created once and reused across screen transitions. Each layer
    /// (1, 2, 3) has its own texture. Textures are assigned to screens only when
    /// the screen changes, not every frame, for efficiency.
    /// 
    /// <para><b>Screen Lifecycle:</b></para>
    /// 1. Register screens via RegisterScreen()
    /// 2. Initialize texture pool via InitializeTextures()
    /// 3. Transition between screens via TransitionTo()
    /// 4. Call Update() and Render() each frame for the current screen
    /// 5. Cleanup via Shutdown() when console closes
    /// 
    /// <para><b>Usage Example:</b></para>
    /// <code>
    /// var manager = new ScreenManager(textSystemPtr);
    /// manager.RegisterScreen(new MainScreen(border, labels, fontSize));
    /// manager.RegisterScreen(new ScanScreen(border, art, fontSize));
    /// manager.InitializeTextures(width, height);
    /// manager.TransitionTo("Main");
    /// </code>
    /// </remarks>
    public class ScreenManager
    {
        private readonly Dictionary<string, IScreen> _screens = new Dictionary<string, IScreen>();
        private IScreen _currentScreen;
        private readonly IntPtr _textSystem;
        
        // Shared textures - one per layer order (1, 2, 3)
        private readonly Dictionary<int, RenderTexture> _layerTextures = new Dictionary<int, RenderTexture>();
        private int _textureWidth;
        private int _textureHeight;
        
        // Track which screen has textures assigned to avoid redundant SetTextures calls
        private IScreen _screenWithAssignedTextures;
        
        /// <summary>
        /// Gets the currently active screen, or null if no transition has occurred.
        /// </summary>
        public IScreen CurrentScreen => _currentScreen;
        
        /// <summary>
        /// Gets the name of the currently active screen, or null if none.
        /// </summary>
        public string CurrentScreenName => _currentScreen?.ScreenName;
        
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
        /// <param name="width">Requested width (ignored - always uses Large preset dimensions)</param>
        /// <param name="height">Requested height (ignored - always uses Large preset dimensions)</param>
        /// <remarks>
        /// Always creates textures at Large preset size (825x450) to ensure 1:1 pixel mapping
        /// regardless of the actual display size. This maintains crisp text rendering.
        /// Creates textures for layers 1, 2, and 3.
        /// </remarks>
        public void InitializeTextures(int width, int height)
        {
            // IGNORE passed dimensions - always use Large size
            // This ensures 1:1 pixel mapping at all presets
            _textureWidth = 825;  // Large width
            _textureHeight = 450; // Large height
            
            // Create shared textures for layers 1, 2, and 3
            EnsureTexture(1);
            EnsureTexture(2);
            EnsureTexture(3);
        }
        
        /// <summary>
        /// Gets or creates a shared texture for the specified layer order.
        /// </summary>
        /// <param name="layerOrder">The layer order (1, 2, or 3)</param>
        /// <returns>The RenderTexture for this layer</returns>
        public RenderTexture GetLayerTexture(int layerOrder)
        {
            if (!_layerTextures.ContainsKey(layerOrder))
            {
                EnsureTexture(layerOrder);
            }
            return _layerTextures[layerOrder];
        }
        
        /// <summary>
        /// Marks all layers of the current screen as dirty, forcing a redraw.
        /// </summary>
        /// <remarks>
        /// Call this when visual properties change (e.g., grid color) to ensure
        /// all layers are re-rendered with the new settings.
        /// </remarks>
        public void MarkAllLayersDirty()
        {
            if (_currentScreen != null)
            {
                foreach (var layer in _currentScreen.Layers)
                {
                    layer.MarkDirty();
                }
            }
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
        /// 4. Forces texture reassignment for the new screen
        /// 5. Calls OnEnter() on the new screen
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
            _screenWithAssignedTextures = null;  // Force texture reassignment for new screen
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
        }
        
        /// <summary>
        /// Validates all layer textures and recreates any that are invalid.
        /// </summary>
        /// <remarks>
        /// This is called automatically by Render() as a defensive measure against
        /// device loss (e.g., when the GPU device is reset). It checks each texture
        /// and recreates it if null or not created.
        /// </remarks>
        public void ValidateTextures()
        {
            // Check and recreate layer textures if needed
            for (int i = 1; i <= 3; i++)
            {
                if (!_layerTextures.TryGetValue(i, out var texture) || 
                    texture == null || 
                    !texture.IsCreated())
                {
                    Debug.Log($"[ScreenManager] Layer {i} texture invalid, recreating...");
                    EnsureTexture(i);
                }
            }
        }
        
        /// <summary>
        /// Renders the currently active screen.
        /// </summary>
        /// <param name="displayRect">Screen-space rectangle for rendering</param>
        /// <remarks>
        /// This method:
        /// 1. Validates textures (recreates if needed)
        /// 2. Assigns textures to the current screen (only if screen changed)
        /// 3. Calls Render() on the current screen
        /// 
        /// Call this during the Repaint event from the console's OnGUI().
        /// </remarks>
        public void Render(Rect displayRect)
        {
            if (_currentScreen == null)
            {
                ModFileLogger.Log("[ScreenManager] Render - EARLY EXIT, _currentScreen is null");
                return;
            }
            
            // Validate textures before rendering (defensive against device loss)
            ValidateTextures();
            
            // Only assign textures when screen changes, not every frame
            bool shouldAssignTextures = _screenWithAssignedTextures != _currentScreen;
            
            if (shouldAssignTextures)
            {
                AssignTexturesToCurrentScreen();
                _screenWithAssignedTextures = _currentScreen;
            }
            
            _currentScreen.Render(displayRect, _textSystem);
        }
        
        /// <summary>
        /// Assigns shared textures to the current screen.
        /// Called internally when the screen changes.
        /// </summary>
        private void AssignTexturesToCurrentScreen()
        {
            var layer1Texture = GetLayerTexture(1);
            var layer2Texture = GetLayerTexture(2);
            var layer3Texture = GetLayerTexture(3);
            
            // Assign all textures via unified interface
            _currentScreen?.SetTextures(layer1Texture, layer2Texture, layer3Texture);

        }
        
        /// <summary>
        /// Gets a copy of all layer textures for debugging or export purposes.
        /// </summary>
        /// <returns>Dictionary mapping layer order to RenderTexture</returns>
        public Dictionary<int, RenderTexture> GetAllLayerTextures()
        {
            return new Dictionary<int, RenderTexture>(_layerTextures);
        }
        
        /// <summary>
        /// Cleans up all resources used by this manager.
        /// </summary>
        /// <remarks>
        /// Call this when the console is shutting down. This will:
        /// 1. Call OnExit() on the current screen
        /// 2. Release and destroy all shared textures
        /// 3. Clear internal collections
        /// 
        /// The ScreenManager should not be used after Shutdown() is called.
        /// </remarks>
        public void Shutdown()
        {
            _currentScreen?.OnExit();
            _currentScreen = null;
            _screenWithAssignedTextures = null;
            
            foreach (var texture in _layerTextures.Values)
            {
                if (texture != null)
                {
                    texture.Release();
                    UnityEngine.Object.Destroy(texture);
                }
            }
            _layerTextures.Clear();
        }
        
        /// <summary>
        /// Creates or recreates a texture for the specified layer order.
        /// </summary>
        /// <param name="layerOrder">The layer order (1, 2, or 3)</param>
        private void EnsureTexture(int layerOrder)
        {
            if (_layerTextures.ContainsKey(layerOrder) && _layerTextures[layerOrder] != null)
                return;
                
            var texture = new RenderTexture(_textureWidth, _textureHeight, 0, RenderTextureFormat.ARGB32);
            texture.enableRandomWrite = true;
            texture.Create();
            
            _layerTextures[layerOrder] = texture;
        }
    }
}
