using System;
using System.Collections.Generic;
using UnityEngine;

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
        /// Helper to add a layer and keep list sorted by Order
        /// </summary>
        protected void AddLayer(ILayer layer)
        {
            Layers.Add(layer);
            Layers.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }
}
