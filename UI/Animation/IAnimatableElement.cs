using System;

namespace CinematicShaders.UI.Animation
{
    /// <summary>
    /// Interface for elements that can have type-on animation.
    /// </summary>
    public interface IAnimatableElement
    {
        /// <summary>
        /// Unique identifier for this element (e.g., "hip_value", "save_button")
        /// </summary>
        string ElementId { get; }
        
        /// <summary>
        /// Current text content to display
        /// </summary>
        string CurrentText { get; }
        
        /// <summary>
        /// Whether this element is currently visible
        /// </summary>
        bool IsVisible { get; }
        
        /// <summary>
        /// How long the type-on animation should take (in seconds)
        /// </summary>
        float TypeOnDuration { get; }
        
        /// <summary>
        /// Set the type-on progress (0.0 = empty, 1.0 = complete)
        /// </summary>
        void SetTypeOnProgress(float progress);
        
        /// <summary>
        /// Check if this element has content to animate (not empty/null)
        /// </summary>
        bool HasContent();
        
        /// <summary>
        /// Check if this element should animate (content changed since last animation).
        /// </summary>
        bool ShouldAnimate();
        
        /// <summary>
        /// Reset animation tracking state for a fresh screen start.
        /// </summary>
        void ResetAnimationState();
    }
}
