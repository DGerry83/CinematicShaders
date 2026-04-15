using System;
using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Screens
{
    /// <summary>
    /// Defines the contract for all screen implementations in the holographic console system.
    /// </summary>
    /// <remarks>
    /// Screens are organized in a three-layer rendering pipeline:
    /// - Layer 1: Border/frame elements (types on first)
    /// - Layer 2: Content labels and static text (types on after Layer 1)
    /// - Layer 3: Interactive elements and value fields (types on after Layer 2)
    /// 
    /// Each screen receives all three textures via <see cref="SetTextures"/> but decides
    /// which layers to actually use. This allows screens to be simple (1-2 layers) or
    /// complex (all 3 layers) without interface changes.
    /// </remarks>
    public interface IScreen
    {
        /// <summary>
        /// Gets the unique name identifier for this screen.
        /// Used by ScreenManager for registration and transitions.
        /// </summary>
        string ScreenName { get; }
        
        /// <summary>
        /// Gets the read-only collection of layers that make up this screen.
        /// Layers are sorted by their Order property (ascending).
        /// </summary>
        IReadOnlyList<ILayer> Layers { get; }
        
        /// <summary>
        /// Gets the total time since this screen was powered on.
        /// Used for coordinating animation timing across layers.
        /// </summary>
        float PowerOnTime { get; }
        
        /// <summary>
        /// Gets the animation progress (0-1) for a specific layer order.
        /// </summary>
        /// <param name="layerOrder">The layer order (1, 2, or 3)</param>
        /// <returns>Progress value from 0.0 (not started) to 1.0 (complete)</returns>
        float GetLayerProgress(int layerOrder);
        
        /// <summary>
        /// Called when entering this screen from a transition.
        /// Use this to reset animation state, initialize click zones, and subscribe to events.
        /// </summary>
        /// <param name="context">Context information about the transition, including previous screen and star selection state</param>
        void OnEnter(ScreenTransitionContext context);
        
        /// <summary>
        /// Called when exiting this screen for a transition.
        /// Use this to unsubscribe from events, stop animations, and clean up temporary state.
        /// </summary>
        void OnExit();
        
        /// <summary>
        /// Called every frame while this screen is active.
        /// Update animation timing, sequencer state, and element animations here.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last frame in seconds</param>
        void Update(float deltaTime);
        
        /// <summary>
        /// Called during the Repaint event to render this screen.
        /// </summary>
        /// <param name="displayRect">The screen-space rectangle where this screen should be rendered</param>
        /// <param name="textSystem">Native text system pointer for GPU text rendering</param>
        void Render(Rect displayRect, IntPtr textSystem);
        
    }
}
