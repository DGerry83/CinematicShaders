using System;
using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.UI.Animation;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Abstract base class for screens providing common animation and lifecycle functionality.
    /// </summary>
    public abstract class BaseScreen : IScreen
    {
        public ScreenState State { get; protected set; }
        public string ScreenName { get; protected set; }
        public List<ILayer> Layers { get; protected set; } = new List<ILayer>();
        
        // Animation state - per screen (customizable)
        public float PowerOnTime { get; protected set; }
        public float Layer1Progress { get; protected set; }
        public float Layer2Progress { get; protected set; }
        public float Layer3Progress { get; protected set; }
        
        // Animation timing (virtual for per-screen customization)
        protected virtual float Layer1Duration => 2.0f;      // 0-2s: Layer 1 types on
        protected virtual float Layer2Delay => 2.0f;         // Layer 2 starts after Layer 1
        protected virtual float Layer2Duration => 1.5f;      // 2-3.5s: Layer 2 types on
        protected virtual float Layer3Delay => 3.5f;         // Layer 3 starts after Layer 2
        protected virtual float Layer3Duration => 1.0f;      // 3.5s+: Layer 3 types on
        
        // Layer 1/2 completion tracking
        public bool IsLayer1Complete => Layer1Progress >= 1.0f;
        public bool IsLayer2Complete => Layer2Progress >= 1.0f;
        
        // Event for when Layer 2 completes (triggers Layer 3 start)
        public event Action OnLayer2Complete;
        
        // Track if we already fired the event (so it only fires once)
        private bool _layer2CompleteFired = false;
        
        // Sequencer for this screen (if it has Layer 3 elements)
        protected Sequencer _sequencer;
        
        // Priority order for Layer 3 elements (override in derived classes)
        protected virtual List<string> Layer3PriorityOrder => new List<string>();
        
        // Expose as IReadOnlyList for interface
        IReadOnlyList<ILayer> IScreen.Layers => Layers;
        
        /// <summary>
        /// Called when entering this screen
        /// </summary>
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
        /// Called when exiting this screen
        /// </summary>
        public virtual void OnExit()
        {
            // Override in derived classes if cleanup needed
        }
        
        /// <summary>
        /// Update animation timing
        /// </summary>
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
                Layer3Progress = Mathf.Clamp01((PowerOnTime - Layer3Delay) / Layer3Duration);
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
        /// Get the type-on progress for a specific layer order
        /// </summary>
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
        /// Render the screen. Override in derived classes.
        /// </summary>
        public abstract void Render(Rect displayRect, IntPtr textSystem);
        
        /// <summary>
        /// Set the Layer 3 texture for single-texture rendering.
        /// Override in derived classes that use Layer 3.
        /// </summary>
        public virtual void SetLayer3Texture(RenderTexture layer3Texture)
        {
            // Default: no-op. Override in screens that use Layer 3.
        }
        
        /// <summary>
        /// Helper to add a layer and keep list sorted by Order
        /// </summary>
        protected void AddLayer(ILayer layer)
        {
            Layers.Add(layer);
            Layers.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }
}
