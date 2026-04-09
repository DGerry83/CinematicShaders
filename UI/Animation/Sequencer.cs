using System;
using System.Collections.Generic;
using UnityEngine;

namespace CinematicShaders.UI.Animation
{
    /// <summary>
    /// Manages animation sequence for a screen.
    /// Controls order of element animation via priority list.
    /// </summary>
    public class Sequencer
    {
        #region State
        private List<string> _priorityOrder;
        private int _currentIndex = 0;
        private Dictionary<string, IAnimatableElement> _elements;
        private bool _isRunning = false;
        #endregion
        
        #region Events
        /// <summary>
        /// Fired when the entire sequence completes (all elements done)
        /// </summary>
        public event Action OnSequenceComplete;
        #endregion
        
        #region Public API
        /// <summary>
        /// Create a new sequencer with the given priority order.
        /// </summary>
        public Sequencer(List<string> priorityOrder)
        {
            _priorityOrder = priorityOrder ?? new List<string>();
            _elements = new Dictionary<string, IAnimatableElement>();
        }
        
        /// <summary>
        /// Register an element with this sequencer.
        /// </summary>
        public void RegisterElement(IAnimatableElement element)
        {
            if (element == null) return;
            _elements[element.ElementId] = element;
        }
        
        /// <summary>
        /// Unregister an element.
        /// </summary>
        public void UnregisterElement(string elementId)
        {
            _elements.Remove(elementId);
        }
        
        /// <summary>
        /// Start the animation sequence from the beginning.
        /// </summary>
        public void StartSequence()
        {
            _currentIndex = 0;
            _isRunning = true;
            
            // Subscribe to animation completion events
            AnimationController.Instance.OnAnimationComplete -= OnElementAnimationComplete;
            AnimationController.Instance.OnAnimationComplete += OnElementAnimationComplete;
            
            // Start the first visible/populated element
            AdvanceToNextElement();
        }
        
        /// <summary>
        /// Stop the sequence.
        /// </summary>
        public void StopSequence()
        {
            _isRunning = false;
            AnimationController.Instance.OnAnimationComplete -= OnElementAnimationComplete;
        }
        
        /// <summary>
        /// Reset the sequence to the beginning (doesn't clear elements)
        /// </summary>
        public void ResetSequence()
        {
            _currentIndex = 0;
            _isRunning = false;
        }
        
        /// <summary>
        /// Notify the sequencer that specific elements have new content.
        /// Used for post-startup changes (e.g., star selected).
        /// </summary>
        public void OnElementsChanged(List<string> elementIds)
        {
            // If sequence is already running, new changes will be picked up
            // automatically when current animation completes
            // If sequence is complete, restart from first changed element
            
            if (!_isRunning && _currentIndex >= _priorityOrder.Count)
            {
                // Sequence was complete, restart from first changed element
                int restartIndex = int.MaxValue;
                foreach (var elementId in elementIds)
                {
                    int index = _priorityOrder.IndexOf(elementId);
                    if (index >= 0 && index < restartIndex)
                        restartIndex = index;
                }
                
                if (restartIndex < int.MaxValue)
                {
                    _currentIndex = restartIndex;
                    StartSequence();
                }
            }
        }
        
        /// <summary>
        /// Check if the sequence has completed.
        /// </summary>
        public bool IsComplete => _currentIndex >= _priorityOrder.Count;
        
        /// <summary>
        /// Current position in the priority order.
        /// </summary>
        public int CurrentIndex => _currentIndex;
        #endregion
        
        #region Private Methods
        private void OnElementAnimationComplete(string elementId)
        {
            if (!_isRunning) return;
            
            // Advance to next element
            _currentIndex++;
            AdvanceToNextElement();
        }
        
        private void AdvanceToNextElement()
        {
            // Find the next visible/populated element in priority order
            while (_currentIndex < _priorityOrder.Count)
            {
                string elementId = _priorityOrder[_currentIndex];
                
                if (_elements.TryGetValue(elementId, out var element))
                {
                    if (element.IsVisible && element.HasContent())
                    {
                        // Found the next element to animate - start it
                        AnimationController.Instance.StartAnimation(element);
                        return;
                    }
                }
                
                // Element not found, not visible, or empty - skip it
                _currentIndex++;
            }
            
            // No more elements to animate - sequence complete
            _isRunning = false;
            AnimationController.Instance.OnAnimationComplete -= OnElementAnimationComplete;
            OnSequenceComplete?.Invoke();
        }
        #endregion
    }
}
