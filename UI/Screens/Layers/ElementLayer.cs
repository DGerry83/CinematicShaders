using System;
using System.Collections.Generic;
using UnityEngine;
using CinematicShaders.Native;
using CinematicShaders.Core;
using CinematicShaders.UI.Animation;

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
        
        // Adapter storage for animation system
        private Dictionary<string, ElementAdapter> _elementAdapters = 
            new Dictionary<string, ElementAdapter>();
        
        // Cache for highlight textures (avoid per-frame allocation)
        private RenderTexture _cachedHighlightTexture = null;
        private Vector2 _cachedHighlightSize = Vector2.zero;
        
        /// <summary>
        /// Adapter that wraps HolographicTextElement to make it animatable.
        /// </summary>
        private class ElementAdapter : IAnimatableElement
        {
            private readonly HolographicTextElement _element;
            private readonly ElementLayer _layer;
            
            public ElementAdapter(HolographicTextElement element, ElementLayer layer)
            {
                _element = element;
                _layer = layer;
            }
            
            public string ElementId => _element.ElementId;
            
            public string CurrentText => _element.FullDisplayText;
            
            public bool IsVisible => _element.IsVisible;
            
            public float TypeOnDuration => _element.TypeOnDuration;
            
            public void SetTypeOnProgress(float progress)
            {
                _element.TypeOnProgress = progress;
                _element.IsDirty = true;
            }
            
            public bool HasContent()
            {
                string text = _element.FullDisplayText;
                return !string.IsNullOrWhiteSpace(text) && text != "...";
            }
        }
        
        public ElementLayer(List<HolographicTextElement> elements, float fontSize)
        {
            _elements = elements ?? new List<HolographicTextElement>();
            _fontSize = fontSize;
            
            // Create adapters for all elements
            foreach (var element in _elements)
            {
                _elementAdapters[element.ElementId] = new ElementAdapter(element, this);
            }
        }
        
        public void SetTextSystem(IntPtr textSystem)
        {
            _textSystem = textSystem;
        }
        
        // Base delay for Layer 3 (when elements start animating)
        private float _layer3Delay = 3.5f;
        
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
        /// Check if an element should animate or appear immediately.
        /// </summary>
        private bool IsImmediateMode(string elementId)
        {
            // Search input is always immediate
            if (elementId == "search_input")
                return true;
            
            // Add other immediate elements here if needed
            
            return false;
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
                
                // Handle immediate mode elements (search, edit) - no animation
                if (IsImmediateMode(element.ElementId))
                {
                    element.TypeOnProgress = 1.0f;
                }
                // For animated elements, AnimationController updates TypeOnProgress
                // We just read the current value here
                
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
        
        /// <summary>
        /// Set the display text for a specific element.
        /// </summary>
        public void SetElementText(string elementId, string text)
        {
            var element = _elements.Find(e => e.ElementId == elementId);
            if (element != null)
            {
                element.SetDynamicText(text);
            }
        }
        
        /// <summary>
        /// Register all elements with a sequencer.
        /// </summary>
        public void RegisterWithSequencer(Sequencer sequencer)
        {
            foreach (var adapter in _elementAdapters.Values)
            {
                sequencer.RegisterElement(adapter);
            }
        }

        /// <summary>
        /// Unregister all elements from a sequencer.
        /// </summary>
        public void UnregisterFromSequencer(Sequencer sequencer)
        {
            foreach (var adapter in _elementAdapters.Values)
            {
                sequencer.UnregisterElement(adapter.ElementId);
            }
        }

        /// <summary>
        /// Get an element adapter by ID.
        /// </summary>
        public IAnimatableElement GetElement(string elementId)
        {
            _elementAdapters.TryGetValue(elementId, out var adapter);
            return adapter;
        }
        
        /// <summary>
        /// Notify that element content has changed (e.g., star selected).
        /// Returns list of element IDs that changed.
        /// </summary>
        public List<string> OnContentChanged(string[] elementIds)
        {
            var changedIds = new List<string>();
            
            foreach (var id in elementIds)
            {
                if (_elementAdapters.TryGetValue(id, out var adapter))
                {
                    // Reset animation for this element
                    var element = _elements.Find(e => e.ElementId == id);
                    if (element != null)
                    {
                        element.TypeOnProgress = 0f;
                        element.IsDirty = true;
                        element.IsVisible = true;
                        changedIds.Add(id);
                    }
                }
            }
            
            return changedIds;
        }
        
        /// <summary>
        /// Set up elements for Main screen.
        /// Call this when Main screen is activated.
        /// </summary>
        public void SetupMainScreenAnimation(bool hasStarSelected)
        {
            // Reset all elements
            foreach (var element in _elements)
            {
                element.TypeOnProgress = 0f;
                element.IsDirty = true;
                
                // Determine visibility based on element type and star selection
                if (IsValueField(element.ElementId))
                {
                    // Value fields only visible when star selected
                    element.IsVisible = hasStarSelected;
                }
                else if (IsButton(element.ElementId) || IsSearchElement(element.ElementId))
                {
                    // Buttons and search always visible
                    element.IsVisible = true;
                }
                else if (element.ElementId.StartsWith("result_"))
                {
                    // Result rows hidden initially
                    element.IsVisible = false;
                }
            }
        }

        private bool IsValueField(string elementId)
        {
            return elementId == "hip_value" || elementId == "name_value" || 
                   elementId == "distance_value" || elementId == "spectral_value" || 
                   elementId == "mag_value" || elementId == "const_value" ||
                   elementId == "selected_star";
        }

        private bool IsButton(string elementId)
        {
            return elementId == "save_button" || elementId == "reset_button" || 
                   elementId == "rescan_button";
        }

        private bool IsSearchElement(string elementId)
        {
            return elementId == "search_input" || elementId.StartsWith("result_");
        }
        
        /// <summary>
        /// Update animations for all elements. Call this from screen's Update().
        /// </summary>
        public void UpdateAnimations(float deltaTime)
        {
            AnimationController.Instance.Update(deltaTime);
        }
        
        /// <summary>
        /// Check if the sequential animation has completed all elements.
        /// </summary>
        [Obsolete("Use Sequencer.IsComplete instead")]
        public bool IsSequentialAnimationComplete => true;

        /// <summary>
        /// Reset sequential animation state.
        /// </summary>
        [Obsolete("Use Sequencer.ResetSequence instead")]
        public void ResetSequentialAnimation()
        {
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
