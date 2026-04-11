using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CinematicShaders.Native;
using CinematicShaders.Core;


namespace CinematicShaders.UI.Screens.Layers
{
    /// <summary>
    /// Layer 3: Renders interactive elements (buttons, value fields).
    /// Simplified architecture - text rendering only, no complex state machine.
    /// Animation handled globally - all elements animate as ONE continuous character stream.
    /// </summary>
    public class ElementLayer : ILayer
    {
        public int Order => 3;
        public string LayerName => "Elements";
        public bool IsDirty { get; set; } = true;
        
        private readonly List<HolographicTextElement> _elements;
        private readonly float _fontSize;
        private IntPtr _textSystem;
        
        // Single texture for entire Layer 3
        private RenderTexture _layer3Texture;
        private bool _isTextureDirty = true;

        // Layer 3 content strings
        private string[] _layer3ContentLines;
        private const int LAYER_3_LINE_COUNT = 17;
        
        // Character-based animation constants
        private const float CHARS_PER_SECOND = 60f;
        private const float MIN_TYPEON_DURATION = 0.5f;
        
        // Cursor state
        private bool _cursorVisible = true;
        private float _cursorTimer = 0f;
        private const float CURSOR_BLINK_INTERVAL = 0.5f;
        private string _editingElementId = null;
        
        // Priority order for element animation sequence
        private List<string> _priorityOrder = new List<string>
        {
            "hip_value", "name_value", "distance_value",
            "spectral_value", "mag_value", "const_value",
            "selected_star", "search_input",
            "result_0", "result_1", "result_2", "result_3", "result_4",
            "result_5", "result_6", "result_7", "result_8", "result_9"
        };
        
        public ElementLayer(List<HolographicTextElement> elements, float fontSize)
        {
            _elements = elements ?? new List<HolographicTextElement>();
            _fontSize = fontSize;
            _layer3ContentLines = new string[LAYER_3_LINE_COUNT];
        }
        
        public void SetTextSystem(IntPtr textSystem)
        {
            _textSystem = textSystem;
        }
        
        /// <summary>
        /// Set the shared Layer 3 texture from ScreenManager.
        /// </summary>
        public void SetLayer3Texture(RenderTexture texture)
        {
            _layer3Texture = texture;
        }
        
        /// <summary>
        /// Mark the single Layer 3 texture as dirty (needs re-render).
        /// </summary>
        public void MarkLayer3Dirty()
        {
            _isTextureDirty = true;
        }
        
        /// <summary>
        /// Mark this layer as dirty (ILayer implementation).
        /// </summary>
        public void MarkDirty()
        {
            IsDirty = true;
            MarkLayer3Dirty();
        }
        
        public void Render(float typeOnProgress)
        {
            // Element rendering is done via single texture in RenderToTexture.
        }
        
        /// <summary>
        /// Set the Layer 3 delay for calculating element start times.
        /// Deprecated: Now using normalized Layer3Progress from BaseScreen.
        /// </summary>
        public void SetLayer3Delay(float delay)
        {
            // No longer used - progress is now passed directly from BaseScreen.Layer3Progress
        }
        
        /// <summary>
        /// Set element text and trigger animation.
        /// Resets ALL element animations to 0 for global character-based animation.
        /// </summary>
        public void SetElementText(string elementId, string text)
        {
            var element = _elements.Find(e => e.ElementId == elementId);
            if (element != null && element.DynamicText != text)
            {
                ModFileLogger.Log($"[ElementLayer] SetElementText({elementId}): '{element.DynamicText}' -> '{text}', resetting ALL animations");
                element.DynamicText = text;
                element.IsVisible = !string.IsNullOrEmpty(text);
                element.IsDirty = true;
                
                // Reset ALL element animations to 0 for global character-based animation
                ResetAllElementAnimations();
                
                MarkLayer3Dirty();
            }
        }
        
        /// <summary>
        /// Reset TypeOnProgress for ALL elements to 0.
        /// Called when any element text changes to restart the global animation.
        /// </summary>
        private void ResetAllElementAnimations()
        {
            foreach (var e in _elements)
            {
                e.TypeOnProgress = 0f;
                e.IsDirty = true;
            }
            ModFileLogger.Log("[ElementLayer] All element animations reset to 0");
        }
        
        /// <summary>
        /// Set the priority order for element animation.
        /// Elements animate in this order as one continuous character stream.
        /// </summary>
        public void SetPriorityOrder(List<string> priorityOrder)
        {
            _priorityOrder = priorityOrder ?? _priorityOrder;
        }
        
        /// <summary>
        /// Clear all value fields (on deselect).
        /// </summary>
        public void ClearValueFields()
        {
            string[] valueIds = { "hip_value", "name_value", "distance_value",
                                  "spectral_value", "mag_value", "const_value", "selected_star" };
            foreach (var id in valueIds)
            {
                SetElementText(id, "");
            }
        }
        
        /// <summary>
        /// Count total visible non-space characters in all elements
        /// </summary>
        public int GetVisibleCharacterCount()
        {
            int count = 0;
            foreach (var element in _elements)
            {
                if (element.IsVisible)
                {
                    string text = element.FullDisplayText;
                    foreach (char c in text)
                    {
                        if (c != ' ') count++;
                    }
                }
            }
            return count;
        }
        
        /// <summary>
        /// Calculate type-on duration based on total character count across all visible elements.
        /// This ensures consistent animation speed regardless of how many elements are visible.
        /// </summary>
        public float CalculateTypeOnDuration()
        {
            int totalCharCount = GetTotalVisibleCharacterCount();
            float duration = totalCharCount / CHARS_PER_SECOND;
            return Mathf.Max(MIN_TYPEON_DURATION, duration);
        }
        
        /// <summary>
        /// Get total visible character count across ALL visible elements.
        /// Used for global character-based animation.
        /// </summary>
        private int GetTotalVisibleCharacterCount()
        {
            int total = 0;
            var visibleElements = GetSortedVisibleElements();
            
            foreach (var element in visibleElements)
            {
                string text = element.FullDisplayText;
                foreach (char c in text)
                {
                    if (c != ' ' && c != '\n' && c != '\r' && c != '\t')
                        total++;
                }
            }
            return total;
        }
        
        /// <summary>
        /// Get visible elements sorted by priority order.
        /// </summary>
        private List<HolographicTextElement> GetSortedVisibleElements()
        {
            var visibleElements = _elements.FindAll(e => e.IsVisible);
            
            // Sort by priority order
            visibleElements.Sort((a, b) =>
            {
                int indexA = _priorityOrder.IndexOf(a.ElementId);
                int indexB = _priorityOrder.IndexOf(b.ElementId);
                
                // Elements not in priority list go at the end
                if (indexA < 0) indexA = int.MaxValue;
                if (indexB < 0) indexB = int.MaxValue;
                
                return indexA.CompareTo(indexB);
            });
            
            return visibleElements;
        }
        
        /// <summary>
        /// Render all visible elements.
        /// Uses Layer3Progress (0-1) as global character position across ALL elements.
        /// Elements animate as ONE continuous character stream (like reading a book).
        /// </summary>
        public void RenderToTexture(IntPtr textSystem, Rect displayRect, float layer3Progress)
        {
            if (textSystem == IntPtr.Zero || _layer3Texture == null) return;
            if (Event.current?.type != EventType.Repaint) return;
            
            _textSystem = textSystem;
            
            // Global character-based animation
            // Layer3Progress (0-1) represents position through the entire character stream
            if (layer3Progress > 0)
            {
                DistributeGlobalProgressAcrossElements(layer3Progress);
            }
            
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
                    new Rect(0, 1, 1, -1),
                    0, 0, 0, 0,
                    Color.white,
                    null
                );
            }
        }
        
        /// <summary>
        /// Distribute global Layer3Progress across all visible elements.
        /// Treats all elements as one continuous character stream.
        /// </summary>
        private void DistributeGlobalProgressAcrossElements(float globalProgress)
        {
            var sortedElements = GetSortedVisibleElements();
            if (sortedElements.Count == 0) return;
            
            // Calculate total character count
            int totalChars = 0;
            var elementCharCounts = new List<int>();
            foreach (var element in sortedElements)
            {
                int charCount = CountNonSpaceChars(element.FullDisplayText);
                elementCharCounts.Add(charCount);
                totalChars += charCount;
            }
            
            if (totalChars == 0) return;
            
            // Calculate how many characters should be visible at this progress
            int visibleCharCount = Mathf.FloorToInt(globalProgress * totalChars);
            
            // Distribute across elements
            int charsAssigned = 0;
            bool hasChanges = false;
            
            for (int i = 0; i < sortedElements.Count; i++)
            {
                var element = sortedElements[i];
                int elementCharCount = elementCharCounts[i];
                float prevProgress = element.TypeOnProgress;
                
                if (elementCharCount == 0)
                {
                    // Element has no characters (e.g., empty text) - show it fully
                    element.TypeOnProgress = 1.0f;
                }
                else if (charsAssigned + elementCharCount <= visibleCharCount)
                {
                    // Entire element visible
                    element.TypeOnProgress = 1.0f;
                    charsAssigned += elementCharCount;
                }
                else if (charsAssigned >= visibleCharCount)
                {
                    // Element not started yet
                    element.TypeOnProgress = 0.0f;
                }
                else
                {
                    // Partially visible - calculate exact progress within this element
                    int charsIntoElement = visibleCharCount - charsAssigned;
                    element.TypeOnProgress = (float)charsIntoElement / elementCharCount;
                    charsAssigned += elementCharCount;
                }
                
                // Mark dirty if progress changed
                if (Mathf.Abs(element.TypeOnProgress - prevProgress) > 0.001f)
                {
                    element.IsDirty = true;
                    _isTextureDirty = true;
                    hasChanges = true;
                }
            }
            
            if (hasChanges && globalProgress < 1.0f)
            {
                ModFileLogger.Log($"[ElementLayer] Global progress {globalProgress:F3}: {visibleCharCount}/{totalChars} chars visible");
            }
        }
        
        /// <summary>
        /// Count non-space characters in text.
        /// </summary>
        private int CountNonSpaceChars(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            
            int count = 0;
            foreach (char c in text)
            {
                if (c != ' ' && c != '\n' && c != '\r' && c != '\t')
                    count++;
            }
            return count;
        }
        
        /// <summary>
        /// Render all Layer 3 content to single texture.
        /// </summary>
        private void RenderLayer3ToTexture()
        {
            if (_layer3Texture == null || _textSystem == IntPtr.Zero) return;
            
            BuildLayer3Content();
            
            string fullText = string.Join("\n", _layer3ContentLines);
            
            uint color = GetGridColorUint();
            int glyphCount = StarfieldNative.CR_TextLayoutEx(
                _textSystem, 
                fullText, 
                _fontSize, 
                color, 
                0f, 0f, 0f, 0.667f
            );
            
            if (glyphCount <= 0) return;
            
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
        /// Build Layer 3 content with current element values.
        /// </summary>
        private void BuildLayer3Content()
        {
            _layer3ContentLines = new string[LAYER_3_LINE_COUNT];
            
            // Rows 0-1: Empty (border area)
            _layer3ContentLines[0] = "";
            _layer3ContentLines[1] = "";
            
            // Row 2: HIP value + optional result 0
            string hipValue = GetElementValue("hip_value");
            string result0 = GetResultValue(0);
            if (!string.IsNullOrEmpty(result0))
            {
                int spacesAfter = 38 - hipValue.Length;
                _layer3ContentLines[2] = "            " + hipValue + new string(' ', spacesAfter) + "• " + result0;
            }
            else
            {
                _layer3ContentLines[2] = "            " + hipValue;
            }
            
            // Row 3: NAME value + optional result 1
            string nameValue = GetElementValue("name_value");
            string result1 = GetResultValue(1);
            if (!string.IsNullOrEmpty(result1))
            {
                int spacesAfter = 38 - nameValue.Length;
                _layer3ContentLines[3] = "            " + nameValue + new string(' ', spacesAfter) + "• " + result1;
            }
            else
            {
                _layer3ContentLines[3] = "            " + nameValue;
            }
            
            // Row 4: DISTANCE value + optional result 2
            string distValue = GetElementValue("distance_value");
            string result2 = GetResultValue(2);
            if (!string.IsNullOrEmpty(result2))
            {
                int spacesAfter = 38 - distValue.Length;
                _layer3ContentLines[4] = "            " + distValue + new string(' ', spacesAfter) + "• " + result2;
            }
            else
            {
                _layer3ContentLines[4] = "            " + distValue;
            }
            
            // Row 5: SPECTRAL value + optional result 3
            string specValue = GetElementValue("spectral_value");
            string result3 = GetResultValue(3);
            if (!string.IsNullOrEmpty(result3))
            {
                int spacesAfter = 38 - specValue.Length;
                _layer3ContentLines[5] = "            " + specValue + new string(' ', spacesAfter) + "• " + result3;
            }
            else
            {
                _layer3ContentLines[5] = "            " + specValue;
            }
            
            // Row 6: MAG value + optional result 4
            string magValue = GetElementValue("mag_value");
            string result4 = GetResultValue(4);
            if (!string.IsNullOrEmpty(result4))
            {
                int spacesAfter = 38 - magValue.Length;
                _layer3ContentLines[6] = "            " + magValue + new string(' ', spacesAfter) + "• " + result4;
            }
            else
            {
                _layer3ContentLines[6] = "            " + magValue;
            }
            
            // Row 7: CONST value + optional result 5
            string constValue = GetElementValue("const_value");
            string result5 = GetResultValue(5);
            if (!string.IsNullOrEmpty(result5))
            {
                int spacesAfter = 38 - constValue.Length;
                _layer3ContentLines[7] = "            " + constValue + new string(' ', spacesAfter) + "• " + result5;
            }
            else
            {
                _layer3ContentLines[7] = "            " + constValue;
            }
            
            // Rows 8-10: Empty
            _layer3ContentLines[8] = "";
            _layer3ContentLines[9] = "";
            _layer3ContentLines[10] = "";
            
            // Row 11: Search input with cursor + optional result 9
            string searchInput = GetElementValue("search_input");
            bool showCursor = (_editingElementId == "search_input") && _cursorVisible;
            string result9 = GetResultValue(9);
            if (!string.IsNullOrEmpty(result9))
            {
                string inputWithCursor = searchInput + (showCursor ? "▌" : "");
                int spacesAfter = 38 - inputWithCursor.Length - 3;
                _layer3ContentLines[11] = "    ► " + inputWithCursor + new string(' ', spacesAfter) + "• " + result9;
            }
            else
            {
                _layer3ContentLines[11] = "    ► " + searchInput + (showCursor ? "▌" : "");
            }
            
            // Rows 12-16: Additional results
            for (int i = 6; i < 10; i++)
            {
                string result = GetResultValue(i);
                int row = 12 + (i - 6);
                if (!string.IsNullOrEmpty(result) && row < LAYER_3_LINE_COUNT)
                {
                    _layer3ContentLines[row] = "                                      " + result;
                }
                else if (row < LAYER_3_LINE_COUNT)
                {
                    _layer3ContentLines[row] = "";
                }
            }
        }
        
        private string GetElementValue(string elementId)
        {
            var element = _elements.Find(e => e.ElementId == elementId);
            if (element == null || !element.IsVisible) return "";
            return GetDisplayText(element);
        }
        
        private string GetResultValue(int index)
        {
            return GetElementValue("result_" + index);
        }
        
        private string GetDisplayText(HolographicTextElement element)
        {
            string fullText = element.FullDisplayText;
            
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
        
        private int GetTypeOnEndIndex(string text, float progress)
        {
            if (progress <= 0f) return 0;
            if (progress >= 1f || string.IsNullOrEmpty(text)) return text?.Length ?? 0;
            
            int totalNonSpace = 0;
            for (int i = 0; i < text.Length; i++)
                if (text[i] != ' ') totalNonSpace++;
            
            if (totalNonSpace == 0) return text.Length;
            
            int targetNonSpace = Mathf.Max(1, Mathf.RoundToInt(totalNonSpace * progress));
            
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
        
        private Color GetGridColor()
        {
            int colorIndex = StarfieldSettings.KartographerGridColor;
            switch (colorIndex)
            {
                case 0: return new Color(0.1f, 0.9f, 0.7f);
                case 1: return new Color(1.0f, 0.65f, 0.0f);
                case 2: return new Color(0.85f, 0.95f, 1.0f);
                case 3: return new Color(0.25f, 1.0f, 0.0f);
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
                MarkLayer3Dirty();
            }
        }
        
        /// <summary>
        /// Set the cursor state from external source.
        /// </summary>
        public void SetCursorState(string editingElementId, bool cursorVisible)
        {
            _editingElementId = editingElementId;
            _cursorVisible = cursorVisible;
            MarkLayer3Dirty();
        }
        
        /// <summary>
        /// Get the current editing element ID.
        /// </summary>
        public string GetEditingElementId() => _editingElementId;
        
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
        /// Cleanup resources.
        /// </summary>
        public void Cleanup()
        {
            _layer3Texture = null;
        }
    }
}
