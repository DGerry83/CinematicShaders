using System;
using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.UI.Animation;
using CinematicShaders.Core;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Abstract base class for all holographic console screens.
    /// Provides common animation timing, lifecycle management, and utility methods.
    /// </summary>
    /// <remarks>
    /// The animation system uses a sequential three-layer approach:
    /// 1. Layer 1 (0-1s): Border and frame elements type on
    /// 2. Layer 2 (1-1.75s): Labels and static content type on
    /// 3. Layer 3 (1.75s+): Interactive elements and values type on
    /// 
    /// Derived classes can customize timing by overriding the virtual duration properties.
    /// Layer 3 elements use a priority-based sequencer for fine-grained control over
    /// the order in which elements appear.
    /// 
    /// <para><b>Implementation Guide:</b></para>
    /// To create a new screen:
    /// 1. Inherit from BaseScreen
    /// 2. Set ScreenName in constructor
    /// 3. Add layers using AddLayer()
    /// 4. Override SetTextures() to assign textures to your layers
    /// 5. Override OnEnter()/OnExit() for setup/cleanup
    /// 6. Override Render() to implement custom rendering
    /// </remarks>
    public abstract class BaseScreen : IScreen
    {
        /// <summary>
        /// Gets or sets the unique name identifier for this screen.
        /// Must be set in the derived class constructor.
        /// </summary>
        public string ScreenName { get; protected set; }
        
        /// <summary>
        /// Gets the list of layers that make up this screen.
        /// Use AddLayer() to add layers in the correct order.
        /// </summary>
        public List<ILayer> Layers { get; protected set; } = new List<ILayer>();
        
        // Animation state - per screen (customizable)
        
        /// <summary>
        /// Gets the total time since this screen was powered on.
        /// Reset to 0 in OnEnter() when screen becomes active.
        /// </summary>
        public float PowerOnTime { get; protected set; }
        
        /// <summary>
        /// Gets the current animation progress for Layer 1 (0-1).
        /// </summary>
        public float Layer1Progress { get; protected set; }
        
        /// <summary>
        /// Gets the current animation progress for Layer 2 (0-1).
        /// </summary>
        public float Layer2Progress { get; protected set; }
        
        /// <summary>
        /// Gets the current animation progress for Layer 3 (0-1).
        /// </summary>
        public float Layer3Progress { get; protected set; }
        
        // Animation timing (virtual for per-screen customization)
        
        /// <summary>
        /// Gets the duration of Layer 1 animation in seconds.
        /// Override to customize timing. Default: 1.0s (halved for faster boot)
        /// </summary>
        protected virtual float Layer1Duration => 1.0f;
        
        /// <summary>
        /// Gets the delay before Layer 2 animation starts (after Layer 1 completes).
        /// Override to customize timing. Default: 1.0s (halved for faster boot)
        /// </summary>
        protected virtual float Layer2Delay => 1.0f;
        
        /// <summary>
        /// Gets the duration of Layer 2 animation in seconds.
        /// Override to customize timing. Default: 0.75s (halved for faster boot)
        /// </summary>
        protected virtual float Layer2Duration => 0.75f;
        
        /// <summary>
        /// Gets the delay before Layer 3 animation starts (after Layer 2 completes).
        /// Override to customize timing. Default: 1.75s (halved for faster boot)
        /// </summary>
        protected virtual float Layer3Delay => 1.75f;
        
        /// <summary>
        /// Gets the duration of Layer 3 animation in seconds.
        /// Override to customize timing. Default: 1.0s
        /// </summary>
        protected virtual float Layer3Duration => 1.0f;
        
        // Character-based animation constants (Phase 1: Layer 3 only)
        protected const float CHARS_PER_SECOND = 60f;
        protected const float MIN_TYPEON_DURATION = 0.5f;
        
        // Layer 1/2 completion tracking
        
        /// <summary>
        /// Gets whether Layer 1 animation has completed (progress >= 1.0).
        /// </summary>
        public bool IsLayer1Complete => Layer1Progress >= 1.0f;
        
        /// <summary>
        /// Gets whether Layer 2 animation has completed (progress >= 1.0).
        /// </summary>
        public bool IsLayer2Complete => Layer2Progress >= 1.0f;
        
        /// <summary>
        /// Event fired when Layer 2 animation completes.
        /// Subscribe to this to trigger Layer 3 animations.
        /// </summary>
        public event Action OnLayer2Complete;
        
        // Track if we already fired the event (so it only fires once)
        private bool _layer2CompleteFired = false;
        
        /// <summary>
        /// Sequencer for Layer 3 element animations.
        /// Created in OnEnter() if this screen has Layer 3 elements.
        /// </summary>
        protected Sequencer _sequencer;
        
        /// <summary>
        /// Gets the priority order for Layer 3 elements.
        /// Override to specify the sequence in which elements should appear.
        /// Elements not in this list use default priority.
        /// </summary>
        /// <example>
        /// protected override List&lt;string&gt; Layer3PriorityOrder => new List&lt;string&gt;
        /// {
        ///     "value_field_1",  // Appears first
        ///     "value_field_2",  // Appears second
        ///     "button_1"        // Appears third
        /// };
        /// </example>
        protected virtual List<string> Layer3PriorityOrder => new List<string>();
        
        // Expose as IReadOnlyList for interface
        IReadOnlyList<ILayer> IScreen.Layers => Layers;
        
        /// <summary>
        /// Called when entering this screen. Resets animation state and marks layers dirty.
        /// </summary>
        /// <param name="context">Transition context including previous screen and star selection state</param>
        /// <remarks>
        /// Override this method to add custom initialization logic, but always call base.OnEnter()
        /// to ensure proper animation state reset.
        /// </remarks>
        public virtual void OnEnter(ScreenTransitionContext context)
        {
            PowerOnTime = 0f;
            Layer1Progress = 0f;
            Layer2Progress = 0f;
            Layer3Progress = 0f;
            _layer2CompleteFired = false;
            
            // Mark all layers as dirty for fresh render
            foreach (var layer in Layers)
            {
                layer.MarkDirty();
            }
        }
        
        /// <summary>
        /// Called when exiting this screen for a transition.
        /// </summary>
        /// <remarks>
        /// Override this method to clean up resources, unsubscribe from events,
        /// and stop any ongoing animations. No need to call base.OnExit() unless
        /// you need the default behavior (which does nothing).
        /// </remarks>
        public virtual void OnExit()
        {
            // Override in derived classes if cleanup needed
        }
        
        /// <summary>
        /// Updates animation timing for all layers based on PowerOnTime.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last frame in seconds</param>
        /// <remarks>
        /// This method calculates progress values for each layer based on the
        /// configured delays and durations. It also fires OnLayer2Complete when
        /// Layer 2 finishes and updates the sequencer if present.
        /// 
        /// Override to add custom update logic, but always call base.Update()
        /// to maintain proper animation timing.
        /// </remarks>
        public virtual void Update(float deltaTime)
        {
            PowerOnTime += deltaTime;
            
            // Update layer progress based on timing
            Layer1Progress = Mathf.Clamp01(PowerOnTime / Layer1Duration);
            
            if (PowerOnTime >= Layer2Delay)
                Layer2Progress = Mathf.Clamp01((PowerOnTime - Layer2Delay) / Layer2Duration);
            else
                Layer2Progress = 0f;
                
            if (PowerOnTime >= Layer3Delay)
            {
                // Phase 1: Character-based duration for Layer 3
                float layer3Duration = CalculateLayerDuration(3);
                Layer3Progress = Mathf.Clamp01((PowerOnTime - Layer3Delay) / layer3Duration);
            }
            else
                Layer3Progress = 0f;
            
            // Check for Layer 2 completion and fire event
            if (IsLayer2Complete && !_layer2CompleteFired)
            {
                _layer2CompleteFired = true;
                OnLayer2Complete?.Invoke();
            }
            
            // Update sequencer if we have one
            _sequencer?.Update();
        }
        
        /// <summary>
        /// Gets the animation progress for a specific layer order.
        /// </summary>
        /// <param name="layerOrder">The layer order (1, 2, or 3)</param>
        /// <returns>Progress value from 0.0 to 1.0</returns>
        public float GetLayerProgress(int layerOrder)
        {
            switch (layerOrder)
            {
                case 1: return Layer1Progress;
                case 2: return Layer2Progress;
                case 3: return Layer3Progress;
                default: return 1f;
            }
        }
        
        /// <summary>
        /// Calculates the animation duration for a specific layer based on content.
        /// Override to implement character-based timing.
        /// </summary>
        /// <param name="layerOrder">The layer order (1, 2, or 3)</param>
        /// <returns>Duration in seconds</returns>
        protected virtual float CalculateLayerDuration(int layerOrder)
        {
            // Default implementation uses fixed durations
            switch (layerOrder)
            {
                case 1: return Layer1Duration;
                case 2: return Layer2Duration;
                case 3: return Layer3Duration;
                default: return MIN_TYPEON_DURATION;
            }
        }
        
        /// <summary>
        /// Renders this screen to the display.
        /// </summary>
        /// <param name="displayRect">Screen-space rectangle for rendering</param>
        /// <param name="textSystem">Native text system pointer</param>
        /// <remarks>
        /// Must be implemented by derived classes. Typical implementation:
        /// 1. Check for Repaint event
        /// 2. Get grid color via GetGridColorUint()
        /// 3. Render each layer with appropriate progress
        /// 4. Handle mouse interaction
        /// </remarks>
        public abstract void Render(Rect displayRect, IntPtr textSystem);
        
        /// <summary>
        /// Sets the shared textures for this screen's layers.
        /// </summary>
        /// <param name="l1">Layer 1 texture</param>
        /// <param name="l2">Layer 2 texture</param>
        /// <param name="l3">Layer 3 texture</param>
        /// <remarks>
        /// Must be implemented by derived classes. Each screen decides which
        /// texture parameters to actually use based on its layer configuration.
        /// </remarks>
        public abstract void SetTextures(RenderTexture l1, RenderTexture l2, RenderTexture l3);
        
        /// <summary>
        /// Adds a layer to this screen and maintains sorted order by Layer.Order.
        /// </summary>
        /// <param name="layer">The layer to add</param>
        protected void AddLayer(ILayer layer)
        {
            Layers.Add(layer);
            Layers.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
        
        /// <summary>
        /// Gets the grid color based on Kartographer settings.
        /// Supports Seafoam (0), Amber (1), White (2), and Green (3).
        /// </summary>
        /// <returns>The configured grid color as Unity Color</returns>
        protected Color GetGridColor()
        {
            int colorIndex = StarfieldSettings.KartographerGridColor;
            switch (colorIndex)
            {
                case 0: return new Color(0.1f, 0.9f, 0.7f);  // Seafoam
                case 1: return new Color(1.0f, 0.65f, 0.0f); // Amber
                case 2: return new Color(0.85f, 0.95f, 1.0f); // White
                case 3: return new Color(0.25f, 1.0f, 0.0f);  // Green
                default: return new Color(0.1f, 0.9f, 0.7f);  // Default seafoam
            }
        }
        
        /// <summary>
        /// Gets the grid color as a uint in ARGB format for native rendering.
        /// </summary>
        /// <returns>Color packed as 0xFFRRGGBB</returns>
        protected uint GetGridColorUint()
        {
            Color c = GetGridColor();
            uint r = (uint)(c.r * 255) & 0xFF;
            uint g = (uint)(c.g * 255) & 0xFF;
            uint b = (uint)(c.b * 255) & 0xFF;
            return 0xFF000000 | (r << 16) | (g << 8) | b;  // ARGB format (A=FF)
        }
        
        /// <summary>
        /// Converts a screen-space mouse position to grid coordinates for hit detection.
        /// </summary>
        /// <param name="mousePos">Mouse position in screen coordinates</param>
        /// <param name="displayRect">The display rectangle in screen coordinates</param>
        /// <returns>Grid coordinates (grid cells from top-left)</returns>
        /// <remarks>
        /// Grid coordinates are based on HolographicLayoutConfig grid cell dimensions.
        /// Use these coordinates with ClickZone for element hit detection.
        /// </remarks>
        protected Vector2 MouseToGrid(Vector2 mousePos, Rect displayRect)
        {
            float localX = mousePos.x - displayRect.x;
            float localY = mousePos.y - displayRect.y;
            float gridX = localX / HolographicLayoutConfig.GRID_CELL_WIDTH;
            float gridY = localY / HolographicLayoutConfig.GRID_CELL_HEIGHT;
            return new Vector2(gridX, gridY);
        }
    }
}
