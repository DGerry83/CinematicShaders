using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CinematicShaders.Native;
using CinematicShaders.Core;
using CinematicShaders.UI.Layout;
using CinematicShaders.UI.Layout.ScreenLayouts;
using static CinematicShaders.UI.UnifiedGridConfig;
using static CinematicShaders.UI.UnifiedGridRegistry;


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
        
        // Grid-based content buffer
        private char[,] _gridBuffer = new char[GRID_ROWS, GRID_COLUMNS];
        
        // Constraint-based layout system (dual-path support)
        private MainScreenLayout _mainScreenLayout;
        private ScanScreenLayout _scanScreenLayout;
        private ConfirmRescanScreenLayout _confirmRescanScreenLayout;
        private LayoutEngine _layoutEngine;
        private string _currentScreenName = null;

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
        /// Set element text and trigger animation.
        /// Resets ALL element animations to 0 for global character-based animation.
        /// </summary>
        /// <summary>
        /// Gets the screen area for the specified element from the specified screen.
        /// Uses constraint-based layout when LayoutConfig.UseConstraintLayout is true,
        /// otherwise falls back to legacy UnifiedGridRegistry positions.
        /// </summary>
        public Rect GetElementArea(string screenName, string elementId)
        {
            if (LayoutConfig.UseConstraintLayout)
            {
                EnsureLayoutBuilt(screenName);
                
                switch (screenName)
                {
                    case "Main":
                        return _mainScreenLayout?.GetArea(elementId) ?? Rect.zero;
                    case "Scan":
                        return _scanScreenLayout?.GetArea(elementId) ?? Rect.zero;
                    case "ConfirmRescan":
                        return _confirmRescanScreenLayout?.GetArea(elementId) ?? Rect.zero;
                    default:
                        return Rect.zero;
                }
            }
            
            // Legacy path: Get from UnifiedGridRegistry
            switch (screenName)
            {
                case "Main":
                    if (MainScreenElements.TryGetValue(elementId, out var mainDef))
                        return mainDef.GetPixelRect();
                    // Handle search result elements
                    if (elementId.StartsWith("result_"))
                    {
                        int index;
                        if (int.TryParse(elementId.Substring(7), out index) && index >= 0 && index < 10)
                            return GetSearchResultElement(index).GetPixelRect();
                    }
                    break;
                case "Scan":
                    if (ScanScreenElements.TryGetValue(elementId, out var scanDef))
                        return scanDef.GetPixelRect();
                    break;
                case "ConfirmRescan":
                    if (ConfirmRescanElements.TryGetValue(elementId, out var confirmDef))
                        return confirmDef.GetPixelRect();
                    break;
            }
            
            return Rect.zero;
        }
        
        /// <summary>
        /// Gets the screen area for the specified element (legacy overload for MainScreen).
        /// </summary>
        public Rect GetElementArea(string elementId)
        {
            return GetElementArea("Main", elementId);
        }
        
        /// <summary>
        /// Gets the grid region for the specified element from the specified screen.
        /// Returns grid coordinates directly (column, row, width, height in cells).
        /// </summary>
        public GridRegion GetElementGridRegion(string screenName, string elementId)
        {
            if (LayoutConfig.UseConstraintLayout)
            {
                EnsureLayoutBuilt(screenName);
                
                switch (screenName)
                {
                    case "Main":
                        return _mainScreenLayout?.GetGridArea(elementId) ?? new GridRegion(GridPosition.At(0, 0), 0, 0);
                    case "Scan":
                        return _scanScreenLayout?.GetGridArea(elementId) ?? new GridRegion(GridPosition.At(0, 0), 0, 0);
                    case "ConfirmRescan":
                        return _confirmRescanScreenLayout?.GetGridArea(elementId) ?? new GridRegion(GridPosition.At(0, 0), 0, 0);
                    default:
                        return new GridRegion(GridPosition.At(0, 0), 0, 0);
                }
            }
            
            // Legacy path: Get from UnifiedGridRegistry and convert
            GridElementDefinition definition = null;
            switch (screenName)
            {
                case "Main":
                    if (MainScreenElements.TryGetValue(elementId, out var mainDef))
                        definition = mainDef;
                    else if (elementId.StartsWith("result_"))
                    {
                        int index;
                        if (int.TryParse(elementId.Substring(7), out index) && index >= 0 && index < 10)
                            definition = GetSearchResultElement(index);
                    }
                    break;
                case "Scan":
                    ScanScreenElements.TryGetValue(elementId, out definition);
                    break;
                case "ConfirmRescan":
                    ConfirmRescanElements.TryGetValue(elementId, out definition);
                    break;
            }
            
            if (definition == null)
                return new GridRegion(GridPosition.At(0, 0), 0, 0);
            
            return new GridRegion(definition.Position, definition.Width, definition.Height);
        }
        
        /// <summary>
        /// Gets the grid region for the specified element (overload for MainScreen).
        /// </summary>
        public GridRegion GetElementGridRegion(string elementId)
        {
            return GetElementGridRegion("Main", elementId);
        }
        
        /// <summary>
        /// Gets the grid position for the specified element from the specified screen.
        /// Converts pixel coordinates to grid coordinates.
        /// </summary>
        public GridPosition GetElementGridPosition(string screenName, string elementId)
        {
            GridRegion region = GetElementGridRegion(screenName, elementId);
            return region.TopLeft;
        }
        
        /// <summary>
        /// Gets the grid position for the specified element (legacy overload for MainScreen).
        /// </summary>
        public GridPosition GetElementGridPosition(string elementId)
        {
            return GetElementGridPosition("Main", elementId);
        }
        
        /// <summary>
        /// Ensures the constraint layout for the specified screen is built.
        /// </summary>
        private void EnsureLayoutBuilt(string screenName)
        {
            if (_layoutEngine == null)
            {
                _layoutEngine = new LayoutEngine();
            }
            
            Vector2 displayDims = TerminalGridConfig.GetDisplayDimensions(TerminalGridConfig.CurrentDisplaySize);
            Rect displayArea = new Rect(0, 0, displayDims.x, displayDims.y);
            
            switch (screenName)
            {
                case "Main":
                    if (_mainScreenLayout == null)
                    {
                        _mainScreenLayout = new MainScreenLayout();
                        _mainScreenLayout.Build(_layoutEngine, displayArea);
                    }
                    break;
                case "Scan":
                    if (_scanScreenLayout == null)
                    {
                        _scanScreenLayout = new ScanScreenLayout();
                        _scanScreenLayout.Build(_layoutEngine, displayArea);
                    }
                    break;
                case "ConfirmRescan":
                    if (_confirmRescanScreenLayout == null)
                    {
                        _confirmRescanScreenLayout = new ConfirmRescanScreenLayout();
                        _confirmRescanScreenLayout.Build(_layoutEngine, displayArea);
                    }
                    break;
            }
            
            _currentScreenName = screenName;
        }
        
        /// <summary>
        /// Invalidates all constraint layouts, forcing a rebuild on next access.
        /// Call this when display size changes.
        /// </summary>
        public void InvalidateConstraintLayout()
        {
            _mainScreenLayout = null;
            _scanScreenLayout = null;
            _confirmRescanScreenLayout = null;
            _layoutEngine = null;
            _currentScreenName = null;
        }

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
        public void ResetAllElementAnimations()
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
            
            uint color = CinematicShadersUIResources.Colors.CRTColors.GetColorUint(StarfieldSettings.KartographerGridColor);
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
        /// Initialize grid buffer with spaces.
        /// </summary>
        private void InitializeGridBuffer()
        {
            for (int row = 0; row < GRID_ROWS; row++)
            {
                for (int col = 0; col < GRID_COLUMNS; col++)
                {
                    _gridBuffer[row, col] = ' ';
                }
            }
        }

        /// <summary>
        /// Place text in the grid buffer at specified position.
        /// </summary>
        private void PlaceTextInGrid(string text, int col, int row)
        {
            if (string.IsNullOrEmpty(text) || row < 0 || row >= GRID_ROWS) return;
            
            for (int i = 0; i < text.Length && col + i < GRID_COLUMNS; i++)
            {
                if (col + i >= 0)
                {
                    _gridBuffer[row, col + i] = text[i];
                }
            }
        }

        /// <summary>
        /// Places a single character into the grid buffer.
        /// </summary>
        private void PlaceCharInGrid(char c, int col, int row)
        {
            if (col >= 0 && col < GRID_COLUMNS && row >= 0 && row < GRID_ROWS)
            {
                _gridBuffer[row, col] = c;
            }
        }

        /// <summary>
        /// Build Layer 3 content using grid-based positioning.
        /// Replaces the hardcoded string building with grid placement.
        /// </summary>
        private void BuildLayer3ContentGridBased()
        {
            BuildLayer3ContentUnified();
        }

        /// <summary>
        /// Build Layer 3 content with current element values.
        /// Uses grid-based positioning for alignment.
        /// </summary>
        private void BuildLayer3Content()
        {
            // Use new grid-based layout
            BuildLayer3ContentGridBased();
        }
        
        /// <summary>
        /// Builds Layer 3 content using unified 59×13 grid system.
        /// This is the new unified implementation that replaces legacy grid-based building.
        /// Uses dual-path layout: constraint-based when enabled, legacy registry otherwise.
        /// </summary>
        private void BuildLayer3ContentUnified()
        {
            // At the very start of the method
            // NOTE: Logging disabled to reduce spam - this method is called every frame
            // ModFileLogger.Log("[ElementLayer] BuildLayer3ContentUnified() called");

            // Clear the grid buffer
            Array.Clear(_gridBuffer, 0, _gridBuffer.Length);
            
            // Before getting zones
            // ModFileLogger.Log("[ElementLayer] Getting click zones from registry...");
            
            // Get all main screen elements from unified registry
            var elementDefinitions = MainScreenElements;
            
            // Render each element to the grid buffer
            foreach (var kvp in elementDefinitions)
            {
                var definition = kvp.Value;
                
                // Skip elements that shouldn't be visible by default
                if (!definition.VisibleByDefault)
                    continue;
                
                // Skip buttons - they are drawn in Layer 2, but we need them in registry for click zones
                if (definition.Type == ElementType.Button)
                    continue;
                
                // Find the corresponding HolographicTextElement
                var element = _elements.Find(e => e.ElementId == definition.ElementId);
                if (element != null && element.IsVisible)
                {
                    // Get the display text (respecting type-on animation)
                    string text = GetDisplayText(element);
                    
                    // Get grid position using dual-path method (constraint or legacy)
                    GridPosition gridPos = GetElementGridPosition(definition.ElementId);
                    
                    // Place element text at its grid position
                    if (!string.IsNullOrEmpty(text))
                    {
                        PlaceTextInGrid(text, gridPos.Column, gridPos.Row);
                    }
                    
                    // Update element's grid position for reference
                    element.GridPos = gridPos;
                }
            }
            
            // Handle search results dynamically
            for (int i = 0; i < 10; i++)
            {
                string resultId = $"result_{i}";
                var resultElement = _elements.Find(e => e.ElementId == resultId);
                
                if (resultElement != null && resultElement.IsVisible)
                {
                    string text = GetDisplayText(resultElement);
                    
                    // Get grid position using dual-path method
                    GridPosition gridPos = GetElementGridPosition(resultId);
                    
                    if (!string.IsNullOrEmpty(text))
                    {
                        PlaceTextInGrid(text, gridPos.Column, gridPos.Row);
                    }
                    
                    resultElement.GridPos = gridPos;
                }
            }
            
            // Debug mode: just call GetClickZonesForScreen() without drawing
            if (UnifiedGridConfig.DEBUG_DRAW_CLICK_ZONES)
            {
                // Get zones but don't draw them - test if just calling this makes it work
                var zones = UnifiedGridRegistry.GetClickZonesForScreen(
                    UnifiedGridRegistry.MainScreenElements);
                
                // Add search result zones (also without drawing)
                for (int i = 0; i < 10; i++)
                {
                    var resultDef = UnifiedGridRegistry.GetSearchResultElement(i);
                    zones.Add(new ClickZone(resultDef));
                }
                
                // After getting zones
                // ModFileLogger.Log($"[ElementLayer] Got {zones.Count} zones from registry");
            }

            // Convert grid buffer to string array (same as legacy path)
            _layer3ContentLines = new string[GRID_ROWS];
            for (int row = 0; row < GRID_ROWS; row++)
            {
                var sb = new System.Text.StringBuilder(GRID_COLUMNS);
                for (int col = 0; col < GRID_COLUMNS; col++)
                {
                    sb.Append(_gridBuffer[row, col] == '\0' ? ' ' : _gridBuffer[row, col]);
                }
                _layer3ContentLines[row] = sb.ToString();
            }
        }

/// <summary>
        /// Export grid layout visualization for debugging.
        /// Shows the grid with visible characters and dots for spaces.
        /// </summary>
        public string ExportGridVisualization()
        {
            BuildLayer3ContentGridBased();
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Grid Layout (59×13):");
            sb.AppendLine("Col:" + string.Join("", System.Linq.Enumerable.Range(0, 10).Select(i => i % 10)));
            
            for (int row = 0; row < GRID_ROWS; row++)
            {
                string line = _layer3ContentLines[row];
                string visible = line.Replace(' ', '·');
                sb.AppendLine($"{row:D2} {visible}");
            }
            
            return sb.ToString();
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
                
                // DIAGNOSTIC: (Disabled) Log when animation is starting (progress < 0.5) to verify truncation
                // if (element.TypeOnProgress < 0.5f)
                // {
                //     string result = endIndex <= 0 ? " " : fullText.Substring(0, endIndex) + "^|";
                //     ModFileLogger.Log($"[ElementLayer] GetDisplayText({element.ElementId}): progress={element.TypeOnProgress:F3}, endIndex={endIndex}, result='{result}'");
                //     return result;
                // }
                
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
