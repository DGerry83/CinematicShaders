using System;
using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Screens
{
    public interface IScreen
    {
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
        /// Set the shared textures for rendering. All screens implement this.
        /// Each screen decides which layers to use (l1, l2, l3).
        /// </summary>
        void SetTextures(RenderTexture l1, RenderTexture l2, RenderTexture l3);
    }
}
