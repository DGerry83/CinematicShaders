using System;
using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.Native;
using CinematicShaders.Core;

namespace CinematicShaders.UI.Screens.Layers
{
    /// <summary>
    /// Layer 3: Renders interactive elements (buttons, value fields).
    /// </summary>
    public class ElementLayer : ILayer
    {
        public int Order => 3;
        public string LayerName => "Elements";
        public bool IsDirty { get; set; } = true;
        
        private readonly List<HolographicTextElement> _elements;
        private readonly float _fontSize;
        private IntPtr _textSystem;
        
        // Cache for highlight textures (avoid per-frame allocation)
        private RenderTexture _cachedHighlightTexture = null;
        private Vector2 _cachedHighlightSize = Vector2.zero;
        
        public ElementLayer(List<HolographicTextElement> elements, float fontSize)
        {
            _elements = elements ?? new List<HolographicTextElement>();
            _fontSize = fontSize;
        }
        
        public void SetTextSystem(IntPtr textSystem)
        {
            _textSystem = textSystem;
        }
        
        // Base delay for Layer 3 (when elements start animating)
        private float _layer3Delay = 3.5f;
        
        /// <summary>
        /// Tracks sequential animation state for Layer 3 elements.
        /// Elements animate one at a time, each waiting for the previous to complete.
        /// </summary>
        private class SequentialAnimationState
        {
            public List<HolographicTextElement> ElementQueue { get; } = new List<HolographicTextElement>();
            public int CurrentIndex { get; set; } = 0;
            public float ElementDuration { get; set; } = 0.5f;
            public bool IsRunning => CurrentIndex < ElementQueue.Count;
            
            public void Reset()
            {
                ElementQueue.Clear();
                CurrentIndex = 0;
            }
            
            public HolographicTextElement CurrentElement => 
                IsRunning ? ElementQueue[CurrentIndex] : null;
        }

        private SequentialAnimationState _sequentialAnimation = new SequentialAnimationState();
        
        public void Render(float typeOnProgress)
        {
            // Element rendering is done per-element in RenderToTexture
        }
        
        /// <summary>
        /// Set the Layer 3 delay for calculating element start times.
        /// </summary>
        public void SetLayer3Delay(float delay)
        {
            _layer3Delay = delay;
        }
        
        /// <summary>
        /// Render all visible elements to their textures and draw them to the screen.
        /// </summary>
        public void RenderToTexture(IntPtr textSystem, Rect displayRect, float powerOnTime)
        {
            if (textSystem == IntPtr.Zero) return;
            _textSystem = textSystem;
            
            // Render each visible element
            foreach (var element in _elements)
            {
                if (!element.IsVisible) continue;
                if (element.TextTexture == null) continue;
                
                // Check if content changed and trigger re-type-on
                CheckContentChangedAndRetype(element, powerOnTime);
                
                // Update type-on animation
                if (_sequentialAnimation.ElementQueue.Contains(element))
                {
                    // This element is in the sequential queue - use sequential animation
                    UpdateElementTypeOnSequential(element, powerOnTime);
                }
                else
                {
                    // This element animates independently (e.g., search results)
                    UpdateElementTypeOnIndependent(element, powerOnTime);
                }
                
                // Re-render if dirty (only during Repaint to avoid GPU sync issues)
                if (element.IsDirty && Event.current.type == EventType.Repaint)
                {
                    if (element.IsSelected)
                    {
                        RenderSelectedElement(element);
                    }
                    else
                    {
                        RenderElement(element);
                    }
                    element.IsDirty = false;
                }
                
                // Draw the element texture to screen
                DrawElement(element, displayRect);
            }
        }
        
        /// <summary>
        /// Check if element content changed and reset type-on animation if so.
        /// This makes elements "re-type" when their content changes.
        /// </summary>
        private void CheckContentChangedAndRetype(HolographicTextElement element, float powerOnTime)
        {
            string currentText = element.FullDisplayText;
            
            // Initialize tracking for this element
            if (!_lastRenderedTexts.ContainsKey(element.ElementId))
            {
                _lastRenderedTexts[element.ElementId] = currentText;
                return;
            }
            
            string lastText = _lastRenderedTexts[element.ElementId];
            
            // If content changed (and not just the cursor), trigger re-type-on
            if (lastText != currentText)
            {
                // Don't re-type for cursor blink changes (^| added/removed)
                string lastWithoutCursor = lastText.Replace("^|", "").Trim();
                string currentWithoutCursor = currentText.Replace("^|", "").Trim();
                
                if (lastWithoutCursor != currentWithoutCursor)
                {
                    // Content actually changed - reset type-on animation
                    element.TypeOnDelay = powerOnTime - _layer3Delay;  // Start "now"
                    element.TypeOnProgress = 0f;
                    element.IsDirty = true;
                }
                
                // Update tracking
                _lastRenderedTexts[element.ElementId] = currentText;
            }
        }
        
        /// <summary>
        /// Update type-on animation for elements sequentially.
        /// Only the current element in the queue animates; when it completes, we advance to the next.
        /// </summary>
        private void UpdateElementTypeOnSequential(HolographicTextElement element, float powerOnTime)
        {
            // Find this element's index in the queue
            int elementIndex = _sequentialAnimation.ElementQueue.IndexOf(element);
            if (elementIndex < 0) return; // Not in queue
            
            float elementDuration = _sequentialAnimation.ElementDuration;
            // Element type-on starts at: _layer3Delay + (index * duration)
            float elementStartTime = _layer3Delay + (elementIndex * elementDuration);
            
            // Calculate progress for this specific element
            if (powerOnTime >= elementStartTime + elementDuration)
            {
                // Element has finished typing - mark complete and advance queue
                element.TypeOnProgress = 1f;
                
                // Advance to next element if this is the current one
                if (_sequentialAnimation.CurrentIndex == elementIndex && _sequentialAnimation.IsRunning)
                {
                    _sequentialAnimation.CurrentIndex++;
                }
            }
            else if (powerOnTime >= elementStartTime)
            {
                // Element is currently typing
                float localTime = powerOnTime - elementStartTime;
                element.TypeOnProgress = Mathf.Clamp01(localTime / elementDuration);
                element.IsDirty = true;
            }
            else
            {
                // Element hasn't started yet
                element.TypeOnProgress = 0f;
            }
        }
        
        /// <summary>
        /// Update type-on animation for an element independently (not part of sequential queue).
        /// Used for search results and dynamically added elements.
        /// </summary>
        private void UpdateElementTypeOnIndependent(HolographicTextElement element, float powerOnTime)
        {
            // Element type-on starts at: _layer3Delay + element.TypeOnDelay
            float elementStartTime = _layer3Delay + element.TypeOnDelay;
            float elementDuration = element.TypeOnDuration;
            
            // Calculate progress for this specific element
            if (powerOnTime >= elementStartTime + elementDuration)
            {
                // Element has finished typing
                element.TypeOnProgress = 1f;
            }
            else if (powerOnTime >= elementStartTime)
            {
                // Element is currently typing
                float localTime = powerOnTime - elementStartTime;
                element.TypeOnProgress = Mathf.Clamp01(localTime / elementDuration);
                element.IsDirty = true;
            }
            else
            {
                // Element hasn't started yet
                element.TypeOnProgress = 0f;
            }
        }
        
        /// <summary>
        /// Render a single element to its texture.
        /// Adapted from StarCatalogHolographicDisplay.RenderElement()
        /// </summary>
        private void RenderElement(HolographicTextElement element)
        {
            if (_textSystem == IntPtr.Zero) return;
            if (element.TextTexture == null) return;
            
            // Get text to render (with type-on truncation)
            string text = GetDisplayText(element);
            if (string.IsNullOrEmpty(text)) return;
            
            // Get grid color
            uint color = GetGridColorUint();
            
            // Layout text in native system
            int glyphCount = StarfieldNative.CR_TextLayoutEx(_textSystem, text, _fontSize, 
                color, 0f, 0f, 0f, 0.667f);  // 0.667f = 2:3 aspect ratio
            
            if (glyphCount <= 0) return;
            
            // Render to texture with proper active texture handling (try/finally)
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = element.TextTexture;
                
                // Clear texture
                GL.Clear(true, true, Color.clear);
                
                // Dispatch to render - texture must be active for this
                StarfieldNative.CR_TextDispatch(
                    _textSystem,
                    element.TextTexture.GetNativeTexturePtr(),
                    glyphCount,
                    element.TextTexture.width,
                    element.TextTexture.height);
            }
            finally
            {
                RenderTexture.active = prevActive;
            }
        }
        
        /// <summary>
        /// Render an element with selection highlight (two-pass: highlight background + black text)
        /// </summary>
        private void RenderSelectedElement(HolographicTextElement element)
        {
            if (_textSystem == IntPtr.Zero) return;
            if (element.TextTexture == null) return;
            
            // Get text to render
            string text = GetDisplayText(element);
            if (string.IsNullOrEmpty(text)) return;
            
            // Pass 1: Draw highlight background to a temp texture
            RenderTexture highlightTex = GetHighlightTexture(element);
            RenderHighlightBackground(highlightTex, element);
            
            // Pass 2: Render text in BLACK color
            uint blackColor = 0xFF000000;  // ARGB black
            
            int glyphCount = StarfieldNative.CR_TextLayoutEx(_textSystem, text, _fontSize, 
                blackColor, 0f, 0f, 0f, 0.667f);
            
            if (glyphCount <= 0) return;
            
            // Clear element texture and composite
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = element.TextTexture;
                GL.Clear(true, true, Color.clear);
                
                // Draw highlight background first
                if (Event.current.type == EventType.Repaint)
                {
                    Graphics.DrawTexture(
                        new Rect(0, 0, element.TextTexture.width, element.TextTexture.height),
                        highlightTex,
                        new Rect(0, 0, 1, 1),
                        0, 0, 0, 0,
                        Color.white);
                }
                
                // Then render black text on top
                StarfieldNative.CR_TextDispatch(
                    _textSystem,
                    element.TextTexture.GetNativeTexturePtr(),
                    glyphCount,
                    element.TextTexture.width,
                    element.TextTexture.height);
            }
            finally
            {
                RenderTexture.active = prevActive;
            }
        }
        
        /// <summary>
        /// Draw an element's texture to the screen.
        /// </summary>
        private void DrawElement(HolographicTextElement element, Rect displayRect)
        {
            if (element.TextTexture == null) return;
            if (Event.current.type != EventType.Repaint) return;
            
            // Calculate screen position
            Rect screenPos = new Rect(
                displayRect.x + element.Position4K.x,
                displayRect.y + element.Position4K.y,
                element.Position4K.width,
                element.Position4K.height
            );
            
            // Flip texture vertically via UV coordinates
            Graphics.DrawTexture(
                screenPos,
                element.TextTexture,
                new Rect(0, 1, 1, -1),  // Flip Y
                0, 0, 0, 0,
                Color.white,
                null
            );
        }
        
        /// <summary>
        /// Get display text with type-on animation applied.
        /// </summary>
        private string GetDisplayText(HolographicTextElement element)
        {
            string fullText = element.FullDisplayText;
            
            // Apply type-on truncation (spaces skip - they appear immediately)
            if (element.TypeOnProgress < 1f && !string.IsNullOrEmpty(fullText))
            {
                int endIndex = GetTypeOnEndIndex(fullText, element.TypeOnProgress);
                
                if (endIndex <= 0)
                    return " ";
                else
                    return fullText.Substring(0, endIndex) + "^|";
            }
            
            return fullText;
        }
        
        /// <summary>
        /// Calculate the end index for type-on animation.
        /// Spaces don't consume type-on time.
        /// </summary>
        private int GetTypeOnEndIndex(string text, float progress)
        {
            if (progress <= 0f) return 0;
            if (progress >= 1f || string.IsNullOrEmpty(text)) return text?.Length ?? 0;
            
            // Count non-space characters
            int totalNonSpace = 0;
            for (int i = 0; i < text.Length; i++)
                if (text[i] != ' ') totalNonSpace++;
            
            if (totalNonSpace == 0) return text.Length;
            
            // How many non-space chars should be visible?
            int targetNonSpace = Mathf.Max(1, Mathf.RoundToInt(totalNonSpace * progress));
            
            // Find the index that includes targetNonSpace non-space characters
            int seenNonSpace = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ')
                {
                    seenNonSpace++;
                    if (seenNonSpace >= targetNonSpace)
                        return i + 1;
                }
            }
            
            return text.Length;
        }
        
        /// <summary>
        /// Create or get a temporary render texture for highlight background.
        /// </summary>
        private RenderTexture GetHighlightTexture(HolographicTextElement element)
        {
            int width = Mathf.Max(64, Mathf.RoundToInt(element.Position4K.width));
            int height = Mathf.Max(32, Mathf.RoundToInt(element.Position4K.height));
            
            // Check if we can reuse cached texture
            if (_cachedHighlightTexture != null &&
                _cachedHighlightSize.x == width &&
                _cachedHighlightSize.y == height)
            {
                return _cachedHighlightTexture;
            }
            
            // Release old cached texture if size changed
            ReleaseHighlightCache();
            
            // Create new texture
            _cachedHighlightTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _cachedHighlightTexture.enableRandomWrite = true;
            _cachedHighlightTexture.Create();
            _cachedHighlightSize = new Vector2(width, height);
            
            return _cachedHighlightTexture;
        }
        
        /// <summary>
        /// Render the colored highlight background.
        /// </summary>
        private void RenderHighlightBackground(RenderTexture target, HolographicTextElement element)
        {
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                
                // Clear to highlight color (grid color at 30% opacity)
                Color highlightColor = GetGridColor();
                highlightColor.a = 0.3f;
                GL.Clear(true, true, highlightColor);
            }
            finally
            {
                RenderTexture.active = prevActive;
            }
        }
        
        private void ReleaseHighlightCache()
        {
            if (_cachedHighlightTexture != null)
            {
                _cachedHighlightTexture.Release();
                UnityEngine.Object.Destroy(_cachedHighlightTexture);
                _cachedHighlightTexture = null;
                _cachedHighlightSize = Vector2.zero;
            }
        }
        
        /// <summary>
        /// Get the grid color from StarfieldSettings.
        /// </summary>
        private Color GetGridColor()
        {
            int colorIndex = StarfieldSettings.KartographerGridColor;
            switch (colorIndex)
            {
                case 0: return new Color(0.1f, 0.9f, 0.7f);  // Seafoam
                case 1: return new Color(1.0f, 0.65f, 0.0f); // Amber
                case 2: return new Color(0.85f, 0.95f, 1.0f); // White
                case 3: return new Color(0.25f, 1.0f, 0.0f);  // Green
                default: return new Color(0.1f, 0.9f, 0.7f);
            }
        }
        
        private uint GetGridColorUint()
        {
            Color c = GetGridColor();
            uint r = (uint)(c.r * 255) & 0xFF;
            uint g = (uint)(c.g * 255) & 0xFF;
            uint b = (uint)(c.b * 255) & 0xFF;
            return 0xFF000000 | (r << 16) | (g << 8) | b;
        }
        
        public void MarkDirty()
        {
            IsDirty = true;
            foreach (var element in _elements)
            {
                element.IsDirty = true;
            }
        }
        
        /// <summary>
        /// Set visibility for all elements.
        /// </summary>
        public void SetElementVisibility(bool visible)
        {
            foreach (var element in _elements)
            {
                element.IsVisible = visible;
            }
        }
        
        /// <summary>
        /// Set visibility for a specific element.
        /// </summary>
        public void SetElementVisibility(string elementId, bool visible)
        {
            var element = _elements.Find(e => e.ElementId == elementId);
            if (element != null)
            {
                element.IsVisible = visible;
            }
        }
        
        // Track previous text for content change detection
        private Dictionary<string, string> _lastRenderedTexts = new Dictionary<string, string>();
        
        /// <summary>
        /// Set up sequential type-on animation for Main screen elements.
        /// Elements animate one at a time in sequence.
        /// </summary>
        public void SetupMainScreenAnimation(float baseDelay, bool hasStarSelected)
        {
            // Reset any previous animation state
            _sequentialAnimation.Reset();
            
            // Build the queue of elements to animate in order
            var queue = _sequentialAnimation.ElementQueue;
            
            // Value fields (only if star selected) - added to queue first
            if (hasStarSelected)
            {
                string[] valueIds = { "hip_value", "name_value", "distance_value", 
                                      "spectral_value", "mag_value", "const_value" };
                foreach (var id in valueIds)
                {
                    var elem = _elements.Find(e => e.ElementId == id);
                    if (elem != null)
                    {
                        elem.TypeOnProgress = 0f;
                        elem.IsDirty = true;
                        elem.IsVisible = true;
                        queue.Add(elem);
                    }
                }
                
                // Selected star indicator
                var selElem = _elements.Find(e => e.ElementId == "selected_star");
                if (selElem != null)
                {
                    selElem.TypeOnProgress = 0f;
                    selElem.IsDirty = true;
                    selElem.IsVisible = true;
                    queue.Add(selElem);
                }
            }
            
            // Search elements (sequential after value fields)
            string[] searchIds = { "search_input", "rescan_button" };
            foreach (var id in searchIds)
            {
                var elem = _elements.Find(e => e.ElementId == id);
                if (elem != null)
                {
                    elem.TypeOnProgress = 0f;
                    elem.IsDirty = true;
                    elem.IsVisible = true;
                    queue.Add(elem);
                }
            }
            
            // Buttons (always visible, animate last)
            string[] buttonIds = { "save_button", "reset_button" };
            foreach (var id in buttonIds)
            {
                var elem = _elements.Find(e => e.ElementId == id);
                if (elem != null)
                {
                    elem.TypeOnProgress = 0f;
                    elem.IsDirty = true;
                    elem.IsVisible = true;
                    queue.Add(elem);
                }
            }
            
            // Initialize sequential animation state
            _sequentialAnimation.CurrentIndex = 0;
            _sequentialAnimation.ElementDuration = 0.5f;
            
            // Hide result rows initially
            foreach (var elem in _elements)
            {
                if (elem.ElementId.StartsWith("result_"))
                {
                    elem.IsVisible = false;
                    elem.TypeOnProgress = 0f;
                }
            }
        }
        
        /// <summary>
        /// Check if the sequential animation has completed all elements.
        /// </summary>
        public bool IsSequentialAnimationComplete => !_sequentialAnimation.IsRunning;

        /// <summary>
        /// Reset sequential animation state.
        /// </summary>
        public void ResetSequentialAnimation()
        {
            _sequentialAnimation.Reset();
        }
        
        /// <summary>
        /// Reset all element animations for fresh type-on effect.
        /// </summary>
        public void ResetAllAnimations()
        {
            foreach (var element in _elements)
            {
                element.TypeOnProgress = 0f;
                element.IsDirty = true;
            }
            
            // Also reset sequential animation queue
            _sequentialAnimation.Reset();
        }
        
        /// <summary>
        /// Cleanup resources.
        /// </summary>
        public void Cleanup()
        {
            ReleaseHighlightCache();
        }
    }
}
