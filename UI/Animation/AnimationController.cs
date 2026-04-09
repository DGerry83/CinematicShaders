using System;
using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Animation
{
    /// <summary>
    /// Singleton controller that manages all active type-on animations.
    /// Fires callbacks when animations complete.
    /// </summary>
    public class AnimationController
    {
        #region Singleton
        private static AnimationController _instance;
        public static AnimationController Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new AnimationController();
                return _instance;
            }
        }
        #endregion
        
        #region Active Animation Tracking
        private class ActiveAnimation
        {
            public IAnimatableElement Element;
            public float Progress;
            public float Duration;
            public bool Completed;
        }
        
        private Dictionary<string, ActiveAnimation> _activeAnimations = 
            new Dictionary<string, ActiveAnimation>();
        #endregion
        
        #region Events
        /// <summary>
        /// Fired when an element's type-on animation completes.
        /// Parameter is the ElementId that completed.
        /// </summary>
        public event Action<string> OnAnimationComplete;
        #endregion
        
        #region Public API
        /// <summary>
        /// Start animating an element.
        /// </summary>
        public void StartAnimation(IAnimatableElement element)
        {
            if (element == null) return;
            if (!element.HasContent()) return; // Skip empty elements
            
            // Reset any existing animation for this element
            StopAnimation(element.ElementId);
            
            var anim = new ActiveAnimation
            {
                Element = element,
                Progress = 0f,
                Duration = element.TypeOnDuration,
                Completed = false
            };
            
            _activeAnimations[element.ElementId] = anim;
            element.SetTypeOnProgress(0f);
        }
        
        /// <summary>
        /// Stop animating an element.
        /// </summary>
        public void StopAnimation(string elementId)
        {
            if (_activeAnimations.ContainsKey(elementId))
            {
                _activeAnimations.Remove(elementId);
            }
        }
        
        /// <summary>
        /// Check if an element is currently animating.
        /// </summary>
        public bool IsAnimating(string elementId)
        {
            return _activeAnimations.ContainsKey(elementId);
        }
        
        /// <summary>
        /// Stop all animations and clear state.
        /// </summary>
        public void Reset()
        {
            _activeAnimations.Clear();
        }
        
        /// <summary>
        /// Update all active animations. Call this every frame.
        /// </summary>
        public void Update(float deltaTime)
        {
            var completedAnimations = new List<string>();
            
            foreach (var kvp in _activeAnimations)
            {
                var anim = kvp.Value;
                if (anim.Completed) continue;
                
                // Advance progress
                anim.Progress += deltaTime / anim.Duration;
                
                if (anim.Progress >= 1.0f)
                {
                    // Animation complete
                    anim.Progress = 1.0f;
                    anim.Completed = true;
                    anim.Element.SetTypeOnProgress(1.0f);
                    completedAnimations.Add(kvp.Key);
                }
                else
                {
                    // Still animating
                    anim.Element.SetTypeOnProgress(anim.Progress);
                }
            }
            
            // Fire completion events after processing all animations
            // (to avoid modifying collection during iteration)
            foreach (var elementId in completedAnimations)
            {
                _activeAnimations.Remove(elementId);
                OnAnimationComplete?.Invoke(elementId);
            }
        }
        #endregion
    }
}
