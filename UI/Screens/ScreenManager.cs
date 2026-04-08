using System;
using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Central coordinator for screen management.
    /// Manages screen registry, transitions, and shared texture pool.
    /// </summary>
    public class ScreenManager
    {
        private readonly Dictionary<ScreenState, IScreen> _screens = new Dictionary<ScreenState, IScreen>();
        private IScreen _currentScreen;
        private readonly IntPtr _textSystem;
        
        // Shared textures - one per layer order (1, 2, 3)
        private readonly Dictionary<int, RenderTexture> _layerTextures = new Dictionary<int, RenderTexture>();
        private int _textureWidth;
        private int _textureHeight;
        
        // Track which screen has textures assigned to avoid redundant SetTextures calls
        private IScreen _screenWithAssignedTextures;
        
        public IScreen CurrentScreen => _currentScreen;
        public ScreenState? CurrentState => _currentScreen?.State;
        
        public ScreenManager(IntPtr textSystem)
        {
            _textSystem = textSystem;
        }
        
        /// <summary>
        /// Initialize the shared texture pool for the given display size
        /// </summary>
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
        /// Get or create a shared texture for the specified layer order
        /// </summary>
        public RenderTexture GetLayerTexture(int layerOrder)
        {
            if (!_layerTextures.ContainsKey(layerOrder))
            {
                EnsureTexture(layerOrder);
            }
            return _layerTextures[layerOrder];
        }
        
        /// <summary>
        /// Mark all layer textures as dirty (e.g., on color change)
        /// </summary>
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
        /// Register a screen with the manager
        /// </summary>
        public void RegisterScreen(IScreen screen)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            _screens[screen.State] = screen;
        }
        
        /// <summary>
        /// Transition to a new screen with proper lifecycle handling
        /// </summary>
        public void TransitionTo(ScreenState state, ScreenTransitionContext context = null)
        {
            if (!_screens.ContainsKey(state))
            {
                Debug.LogError($"[ScreenManager] Screen state {state} not registered");
                return;
            }
            
            var newScreen = _screens[state];
            
            // Exit current screen
            _currentScreen?.OnExit();
            
            // Track previous state for context
            if (context == null)
                context = new ScreenTransitionContext();
            context.PreviousScreen = _currentScreen?.State ?? state;
            
            // Switch to new screen
            _currentScreen = newScreen;
            _screenWithAssignedTextures = null;  // Force texture reassignment for new screen
            _currentScreen.OnEnter(context);
            
            Debug.Log($"[ScreenManager] Transitioned to {state}");
        }
        
        /// <summary>
        /// Update the current screen
        /// </summary>
        public void Update(float deltaTime)
        {
            _currentScreen?.Update(deltaTime);
        }
        
        /// <summary>
        /// Render the current screen
        /// </summary>
        public void Render(Rect displayRect)
        {
            if (_currentScreen == null) return;
            
            // Only assign textures when screen changes, not every frame
            if (_screenWithAssignedTextures != _currentScreen)
            {
                AssignTexturesToCurrentScreen();
                _screenWithAssignedTextures = _currentScreen;
            }
            
            _currentScreen.Render(displayRect, _textSystem);
        }
        
        /// <summary>
        /// Assign shared textures to the current screen.
        /// Called only when screen changes.
        /// </summary>
        private void AssignTexturesToCurrentScreen()
        {
            var layer1Texture = GetLayerTexture(1);
            var layer2Texture = GetLayerTexture(2);
            
            switch (_currentScreen.State)
            {
                case ScreenState.Main:
                    (_currentScreen as MainScreen)?.SetTextures(layer1Texture, layer2Texture);
                    break;
                case ScreenState.Scan:
                    (_currentScreen as ScanScreen)?.SetTextures(layer1Texture, layer2Texture);
                    break;
                case ScreenState.ConfirmRescan:
                    (_currentScreen as ConfirmRescanScreen)?.SetTextures(layer1Texture, layer2Texture);
                    break;
            }
        }
        
        /// <summary>
        /// Get all layer textures for debugging/export
        /// </summary>
        public Dictionary<int, RenderTexture> GetAllLayerTextures()
        {
            return new Dictionary<int, RenderTexture>(_layerTextures);
        }
        
        /// <summary>
        /// Clean up all resources
        /// </summary>
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
