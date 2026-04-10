using System;
using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Screens
{
    public interface IScreen
    {
        ScreenState State { get; }
        string ScreenName { get; }
        IReadOnlyList<ILayer> Layers { get; }
        
        // Animation (per-screen)
        float PowerOnTime { get; }
        float GetLayerProgress(int layerOrder);
        
        // Lifecycle
        void OnEnter(ScreenTransitionContext context);
        void OnExit();
        void Update(float deltaTime);
        void Render(Rect displayRect, IntPtr textSystem);
        
        /// <summary>
        /// Set the Layer 3 texture for screens that use single-texture Layer 3.
        /// Called by ScreenManager when screen becomes active.
        /// </summary>
        void SetLayer3Texture(RenderTexture layer3Texture);
    }
}
