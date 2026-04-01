using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Reusable "flicker-on type-on" animation controller for holographic UI labels.
    /// 
    /// Animation sequence:
    /// 1. Circle (0.4s): Circle flickers, no text visible
    /// 2. Box (instant): Box snaps on, shows cursor only
    /// 3. Text (1.5s): Text types on progressively with cursor
    /// 4. Complete: Full text with 2Hz blinking cursor
    /// 
    /// Does NOT manage rendering resources - callers own their textures and native slots.
    /// </summary>
    public class TypeOnAnimationController
    {
        public enum Phase
        {
            Circle,     // 0-0.4s: Circle flickers, no text
            Box,        // 0.4s: Box snaps on, cursor only
            Text,       // 0.4s-1.9s: Text types on
            Complete    // 1.9s+: Cursor blinks, dynamic updates allowed
        }

        // Animation timing constants
        private const float CIRCLE_DURATION = 0.4f;
        private const float TEXT_TYPE_DURATION = 1.5f;
        private const float CURSOR_BLINK_HZ = 2.0f;

        // State
        private Phase _currentPhase = Phase.Complete;
        private float _circleT = 1.0f;      // 0-1 progress through Circle
        private float _textTypeT = 0.0f;    // 0-1 progress through Text
        private string _fullText = "";      // Full text content (shown in Text/Complete phases)
        private string _displayText = "";   // Current display (with cursor)
        private bool _hasTextContent = false;  // True if SetFullText has been called with non-empty

        /// <summary>
        /// Current animation phase
        /// </summary>
        public Phase CurrentPhase => _currentPhase;

        /// <summary>
        /// Current display text with cursor applied.
        /// Use this to render to your texture each frame.
        /// </summary>
        public string DisplayText => _displayText;

        /// <summary>
        /// True if animation is still running (not in Complete phase)
        /// </summary>
        public bool IsAnimating => _currentPhase != Phase.Complete;

        /// <summary>
        /// Circle phase progress (0-1). Use for flicker intensity.
        /// 0 = just started, 1 = circle complete, box should appear
        /// </summary>
        public float CircleProgress => _currentPhase == Phase.Circle ? _circleT : 1.0f;

        /// <summary>
        /// Start a new animation from the beginning (Circle phase).
        /// Call this when acquiring a new target or selecting a new star.
        /// 
        /// NOTE: For animated labels with dynamic content, call SetFullText() 
        /// AFTER the Box phase (e.g., when Text phase starts) to prevent 
        /// content from appearing before the box is visible.
        /// </summary>
        public void Start()
        {
            _currentPhase = Phase.Circle;
            _circleT = 0.0f;
            _textTypeT = 0.0f;
            _fullText = "";
            _displayText = "";
            _hasTextContent = false;
        }

        /// <summary>
        /// Set the full text content to be animated.
        /// For target tracker: Call this when entering Text phase to prevent
        /// text from appearing during Circle/Box phases.
        /// </summary>
        public void SetFullText(string text)
        {
            _fullText = text ?? "";
            _hasTextContent = !string.IsNullOrEmpty(_fullText);
            
            // Rebuild display text immediately if in Text or Complete phase
            if (_currentPhase == Phase.Text || _currentPhase == Phase.Complete)
            {
                RebuildDisplayText();
            }
        }

        /// <summary>
        /// Skip animation and jump immediately to Complete phase.
        /// Use for "same target reselected" case.
        /// </summary>
        public void ForceComplete()
        {
            _currentPhase = Phase.Complete;
            _circleT = 1.0f;
            _textTypeT = 1.0f;
            RebuildDisplayText();
        }

        /// <summary>
        /// Update animation state. Call this every frame.
        /// </summary>
        /// <param name="deltaTime">Time since last update (typically Time.deltaTime)</param>
        public void Update(float deltaTime)
        {
            switch (_currentPhase)
            {
                case Phase.Circle:
                    UpdateCirclePhase(deltaTime);
                    break;

                case Phase.Box:
                    // Box is instant transition
                    _currentPhase = Phase.Text;
                    RebuildDisplayText();
                    break;

                case Phase.Text:
                    UpdateTextPhase(deltaTime);
                    break;

                case Phase.Complete:
                    RebuildDisplayText();  // Handle cursor blink
                    break;
            }
        }

        /// <summary>
        /// Update the underlying text content without restarting animation.
        /// CRITICAL for target tracker: allows distance/velocity updates while 
        /// maintaining Complete phase with blinking cursor.
        /// 
        /// Only has effect in Complete phase. Other phases use frozen snapshot
        /// to avoid visual glitches during type-on animation.
        /// </summary>
        public void UpdateFullText(string newText)
        {
            _fullText = newText ?? "";
            
            // Only rebuild immediately if in Complete phase
            // Other phases will pick up the new text when they reach Complete
            if (_currentPhase == Phase.Complete)
            {
                RebuildDisplayText();
            }
        }

        private void UpdateCirclePhase(float deltaTime)
        {
            _circleT += deltaTime / CIRCLE_DURATION;
            
            if (_circleT >= 1.0f)
            {
                _circleT = 1.0f;
                _currentPhase = Phase.Box;
            }
            
            // No text during Circle phase
            _displayText = "";
        }

        private void UpdateTextPhase(float deltaTime)
        {
            _textTypeT += deltaTime / TEXT_TYPE_DURATION;
            
            if (_textTypeT >= 1.0f)
            {
                _textTypeT = 1.0f;
                _currentPhase = Phase.Complete;
            }
            
            RebuildDisplayText();
        }

        private void RebuildDisplayText()
        {
            switch (_currentPhase)
            {
                case Phase.Circle:
                    _displayText = "";
                    break;

                case Phase.Box:
                    _displayText = "^|";  // Cursor only
                    break;

                case Phase.Text:
                    // Progressive reveal (only if content has been set)
                    if (!_hasTextContent)
                    {
                        _displayText = "^|";  // Cursor only until content arrives
                    }
                    else
                    {
                        int visibleChars = (int)(_fullText.Length * _textTypeT);
                        visibleChars = Mathf.Clamp(visibleChars, 0, _fullText.Length);
                        _displayText = _fullText.Substring(0, visibleChars) + "^|";
                    }
                    break;

                case Phase.Complete:
                    // Full text with 2Hz blinking cursor (only if content has been set)
                    if (!_hasTextContent)
                    {
                        _displayText = "^|";
                    }
                    else
                    {
                        bool cursorVisible = ((Time.time * CURSOR_BLINK_HZ) % 2.0f) < 1.0f;
                        _displayText = _fullText + (cursorVisible ? "^|" : " ");
                    }
                    break;
            }
        }
    }
}
