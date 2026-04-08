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
        
        public void Render(float typeOnProgress)
        {
            // Element rendering is done per-element in RenderToTexture
        }
        
        /// <summary>
        /// Render all visible elements to their textures and draw them to the screen.
        /// </summary>
        public void RenderToTexture(IntPtr textSystem, Rect displayRect, float typeOnProgress)
        {
            if (textSystem == IntPtr.Zero) return;
            _textSystem = textSystem;
            
            // Render each visible element
            foreach (var element in _elements)
            {
                if (!element.IsVisible) continue;
                if (element.TextTexture == null) continue;
                
                // Update type-on animation
                UpdateElementTypeOn(element, typeOnProgress);
                
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
        /// Update type-on animation for an element based on layer progress.
        /// </summary>
        private void UpdateElementTypeOn(HolographicTextElement element, float layerProgress)
        {
            // Element type-on is relative to layer progress
            // element.TypeOnDelay is in seconds from power-on
            // We need to map this to the layer progress
            
            if (layerProgress >= 1f)
            {
                element.TypeOnProgress = 1f;
                return;
            }
            
            // Calculate element's relative progress within the layer animation
            // This is simplified - the actual delays are set up by SetupMainScreenAnimation
            float relativeProgress = Mathf.Clamp01(layerProgress * 2f); // Speed up for visual effect
            element.TypeOnProgress = relativeProgress;
            element.IsDirty = true;
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
        /// Set up type-on delays for Main screen elements.
        /// Called when powering on the display.
        /// </summary>
        public void SetupMainScreenAnimation(float baseDelay, bool hasStarSelected)
        {
            float currentDelay = baseDelay;
            
            // Value fields (only if star selected)
            if (hasStarSelected)
            {
                string[] valueIds = { "hip_value", "name_value", "distance_value", 
                                      "spectral_value", "mag_value", "const_value" };
                foreach (var id in valueIds)
                {
                    var elem = _elements.Find(e => e.ElementId == id);
                    if (elem != null)
                    {
                        elem.TypeOnDelay = currentDelay;
                        elem.TypeOnProgress = 0f;
                        elem.IsDirty = true;
                        currentDelay += 0.15f;
                    }
                }
                
                // Selected star indicator last
                var selElem = _elements.Find(e => e.ElementId == "selected_star");
                if (selElem != null)
                {
                    selElem.TypeOnDelay = currentDelay;
                    selElem.TypeOnProgress = 0f;
                    selElem.IsDirty = true;
                }
                
                currentDelay += 0.3f;
            }
            
            // Search elements
            string[] searchIds = { "search_input", "rescan_button" };
            foreach (var id in searchIds)
            {
                var elem = _elements.Find(e => e.ElementId == id);
                if (elem != null)
                {
                    elem.TypeOnDelay = currentDelay;
                    elem.TypeOnProgress = 0f;
                    elem.IsVisible = true;
                    elem.IsDirty = true;
                    currentDelay += 0.1f;
                }
            }
            
            // Buttons (always visible)
            string[] buttonIds = { "save_button", "reset_button" };
            foreach (var id in buttonIds)
            {
                var elem = _elements.Find(e => e.ElementId == id);
                if (elem != null)
                {
                    elem.TypeOnDelay = currentDelay;
                    elem.TypeOnProgress = 0f;
                    elem.IsVisible = true;
                    elem.IsDirty = true;
                    currentDelay += 0.1f;
                }
            }
            
            // Result rows are hidden initially
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
