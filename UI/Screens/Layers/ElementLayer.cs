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
    /// Refactored to use a single RenderTexture for all elements (single-texture Layer 3).
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
        
        // Single texture for entire Layer 3
        private RenderTexture _layer3Texture;
        private bool _isTextureDirty = true;

        // Layer 3 content strings (rebuilt each frame if dirty)
        private string[] _layer3ContentLines;
        private const int LAYER_3_LINE_COUNT = 17;  // Matches grid rows
        
        /// <summary>
        /// Adapter that wraps HolographicTextElement to make it animatable.
        /// </summary>
        private class ElementAdapter : IAnimatableElement
        {
            private readonly HolographicTextElement _element;
            private readonly ElementLayer _layer;
            private string _lastAnimatedContent = null;
            private bool _hasAnimatedOnce = false;
            
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
                _layer.MarkLayer3Dirty();
            }
            
            public bool HasContent()
            {
                string text = _element.FullDisplayText;
                return !string.IsNullOrWhiteSpace(text) && text != "...";
            }
            
            public bool ShouldAnimate()
            {
                string current = CurrentText;
                
                // First time seeing this element with content
                if (!_hasAnimatedOnce)
                {
                    _hasAnimatedOnce = true;
                    _lastAnimatedContent = current;
                    return true;  // Always animate first time
                }
                
                // Content changed from last time we animated
                if (current != _lastAnimatedContent)
                {
                    _lastAnimatedContent = current;
                    return true;  // Animate because content changed
                }
                
                // Same content as last time, skip animation
                return false;
            }
            
            public void ResetAnimationState()
            {
                _hasAnimatedOnce = false;
                _lastAnimatedContent = null;
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
            
            // Initialize content lines array
            _layer3ContentLines = new string[LAYER_3_LINE_COUNT];
        }
        
        public void SetTextSystem(IntPtr textSystem)
        {
            _textSystem = textSystem;
        }
        
        /// <summary>
        /// Set the shared Layer 3 texture from ScreenManager
        /// </summary>
        public void SetLayer3Texture(RenderTexture texture)
        {
            _layer3Texture = texture;
        }
        
        /// <summary>
        /// Mark the single Layer 3 texture as dirty (needs re-render)
        /// </summary>
        public void MarkLayer3Dirty()
        {
            _isTextureDirty = true;
        }
        
        // Base delay for Layer 3 (when elements start animating)
        private float _layer3Delay = 3.5f;
        
        public void Render(float typeOnProgress)
        {
            // Element rendering is done via single texture in RenderToTexture
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
        /// Render all Layer 3 content to single texture
        /// </summary>
        private void RenderLayer3ToTexture()
        {
            if (_layer3Texture == null || _textSystem == IntPtr.Zero) return;
            
            BuildLayer3Content();
            
            // Combine all lines into single string with newlines
            string fullText = string.Join("\n", _layer3ContentLines);
            
            // Layout text
            uint color = GetGridColorUint();
            int glyphCount = StarfieldNative.CR_TextLayoutEx(
                _textSystem, 
                fullText, 
                _fontSize, 
                color, 
                0f, 0f, 0f, 0.667f
            );
            
            if (glyphCount <= 0) return;
            
            // Render to single texture
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = _layer3Texture;
                GL.Clear(true, true, Color.clear);
                
                StarfieldNative.CR_TextDispatch(
                    _textSystem,
                    _layer3Texture.GetNativeTexturePtr(),
                    glyphCount,
                    _layer3Texture.width,
                    _layer3Texture.height
                );
            }
            finally
            {
                RenderTexture.active = prevActive;
            }
            
            _isTextureDirty = false;
        }
        
        /// <summary>
        /// Build Layer 3 content with current element values
        /// Format: leading spaces + value, with adjustable spacing per line
        /// </summary>
        private void BuildLayer3Content()
        {
            // Rows 0-1: Empty (border area)
            _layer3ContentLines[0] = "";
            _layer3ContentLines[1] = "";
            
            // Row 2: HIP value (6 leading spaces)
            string hipValue = GetElementValue("hip_value");
            _layer3ContentLines[2] = "      " + hipValue;
            
            // Row 3: NAME value (6 leading spaces)
            string nameValue = GetElementValue("name_value");
            _layer3ContentLines[3] = "      " + nameValue;
            
            // Row 4: DISTANCE value (11 leading spaces)
            string distValue = GetElementValue("distance_value");
            _layer3ContentLines[4] = "           " + distValue;
            
            // Row 5: SPECTRAL value (15 leading spaces - aligned with DISTANCE)
            string specValue = GetElementValue("spectral_value");
            _layer3ContentLines[5] = "               " + specValue;
            
            // Row 6: MAG value (11 leading spaces)
            string magValue = GetElementValue("mag_value");
            _layer3ContentLines[6] = "           " + magValue;
            
            // Row 7: CONST value (6 leading spaces)
            string constValue = GetElementValue("const_value");
            _layer3ContentLines[7] = "      " + constValue;
            
            // Row 8: Empty (buttons in Layer 2)
            _layer3ContentLines[8] = "";
            
            // Row 9: Empty (border)
            _layer3ContentLines[9] = "";
            
            // Row 10: Empty (SEARCH/RESCAN in Layer 2)
            _layer3ContentLines[10] = "";
            
            // Row 11: Search input with cursor (4 leading spaces + "► " + input + cursor)
            string searchInput = GetElementValue("search_input");
            bool showCursor = IsElementEditing("search_input") && IsCursorVisible();
            _layer3ContentLines[11] = "    ► " + searchInput + (showCursor ? "▌" : "");
            
            // Rows 12-16: Not used (results are overlaid on rows 2-11 in right column)
            // Actually, let's put results in right column by using spacing
            // Row 2 right: Result 0 (32 leading spaces from right column start)
            string result0 = GetResultValue(0);
            if (!string.IsNullOrEmpty(result0))
            {
                _layer3ContentLines[2] = _layer3ContentLines[2].PadRight(52) + "• " + result0;
            }
            
            // Continue pattern for results 1-9
            for (int i = 1; i < 10; i++)
            {
                string result = GetResultValue(i);
                int row = 2 + i;
                if (!string.IsNullOrEmpty(result) && row < LAYER_3_LINE_COUNT)
                {
                    _layer3ContentLines[row] = "".PadRight(52) + "• " + result;
                }
            }
            
            // Fill remaining rows
            for (int i = 12; i < LAYER_3_LINE_COUNT; i++)
            {
                if (_layer3ContentLines[i] == null)
                    _layer3ContentLines[i] = "";
            }
        }
        
        /// <summary>
        /// Get element value by ID, respecting type-on animation
        /// </summary>
        private string GetElementValue(string elementId)
        {
            var element = _elements.Find(e => e.ElementId == elementId);
            if (element == null || !element.IsVisible) return "";
            
            return GetDisplayText(element);
        }
        
        /// <summary>
        /// Get search result value by index
        /// </summary>
        private string GetResultValue(int index)
        {
            string resultId = "result_" + index;
            return GetElementValue(resultId);
        }
        
        /// <summary>
        /// Check if element is currently in editing mode
        /// </summary>
        private bool IsElementEditing(string elementId)
        {
            // Check both element flag and external editing ID
            var element = _elements.Find(e => e.ElementId == elementId);
            if (element?.IsEditing ?? false) return true;
            return _editingElementId == elementId;
        }
        
        // Cursor state managed by ElementLayer
        private bool _cursorVisible = true;
        private float _cursorTimer = 0f;
        private const float CURSOR_BLINK_INTERVAL = 0.5f;
        
        /// <summary>
        /// Check if cursor should be visible (blink)
        /// </summary>
        private bool IsCursorVisible()
        {
            return _cursorVisible;
        }
        
        /// <summary>
        /// Update cursor blink. Call this from Update().
        /// </summary>
        public void UpdateCursor(float deltaTime)
        {
            _cursorTimer += deltaTime;
            if (_cursorTimer >= CURSOR_BLINK_INTERVAL)
            {
                _cursorVisible = !_cursorVisible;
                _cursorTimer = 0f;
                MarkLayer3Dirty();  // Trigger redraw for cursor blink
            }
        }
        
        // Track which element has editing focus and cursor visibility
        private string _editingElementId = null;
        private bool _editingCursorVisible = false;
        
        /// <summary>
        /// Set the cursor state from external source (e.g., StarCatalogHolographicDisplay).
        /// </summary>
        public void SetCursorState(string editingElementId, bool cursorVisible)
        {
            _editingElementId = editingElementId;
            _editingCursorVisible = cursorVisible;
            MarkLayer3Dirty();
        }
        
        /// <summary>
        /// Get the current editing element ID.
        /// </summary>
        public string GetEditingElementId() => _editingElementId;
        
        /// <summary>
        /// Render all visible elements to their textures and draw them to the screen.
        /// Refactored to use single Layer 3 texture.
        /// </summary>
        public void RenderToTexture(IntPtr textSystem, Rect displayRect, float powerOnTime)
        {
            if (textSystem == IntPtr.Zero) return;
            _textSystem = textSystem;
            
            // Only render during Repaint
            if (Event.current?.type != EventType.Repaint) return;
            
            // Re-render to texture if dirty
            if (_isTextureDirty)
            {
                RenderLayer3ToTexture();
            }
            
            // Draw the Layer 3 texture to screen
            if (_layer3Texture != null && _layer3Texture.IsCreated())
            {
                Graphics.DrawTexture(
                    displayRect,
                    _layer3Texture,
                    new Rect(0, 1, 1, -1),  // Flip Y
                    0, 0, 0, 0,
                    Color.white,
                    null
                );
            }
        }
        
        /// <summary>
        /// Draw an element's texture to the screen.
        /// Kept for backward compatibility during transition.
        /// </summary>
        private void DrawElement(HolographicTextElement element, Rect displayRect)
        {
            // Drawing is now done via single Layer 3 texture in RenderToTexture
            // This method is kept for compatibility during transition
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
            MarkLayer3Dirty();
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
            MarkLayer3Dirty();
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
                MarkLayer3Dirty();
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
                MarkLayer3Dirty();
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
            
            MarkLayer3Dirty();
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
            
            MarkLayer3Dirty();
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
            UpdateCursor(deltaTime);
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
            MarkLayer3Dirty();
        }
        
        /// <summary>
        /// Reset animation state for all elements (call on screen transition).
        /// </summary>
        public void ResetAllAnimationStates()
        {
            foreach (var adapter in _elementAdapters.Values)
            {
                adapter.ResetAnimationState();
            }
        }
        
        /// <summary>
        /// Cleanup resources.
        /// </summary>
        public void Cleanup()
        {
            // Texture is managed by ScreenManager, not here
            _layer3Texture = null;
        }
    }
}
