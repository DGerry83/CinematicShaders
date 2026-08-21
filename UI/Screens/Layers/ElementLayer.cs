using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CinematicShaders.Native;
using CinematicShaders.Native.Structs;
using CinematicShaders.Core;
using CinematicShaders.UI.Layout;
using CinematicShaders.UI.Layout.ScreenLayouts;
using static CinematicShaders.UI.UnifiedGridConfig;


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
        public Vector2? CursorPosition { get; private set; }
        
        private readonly List<HolographicTextElement> _elements;
        private readonly float _fontSize;
        private IntPtr _textSystem;
        
        // Layer 3 content strings
        private string[] _layer3ContentLines;
        private const int LAYER_3_LINE_COUNT = 17;
        
        // Character-based animation constants
        private const float FIELD_CHARS_PER_SECOND = 30f;
        private const float RESULT_WEIGHT = 0.5f;  // Results animate at 2x speed (60 chars/sec effective)
        
        // Cursor state
        private bool _cursorVisible = true;
        private float _cursorTimer = 0f;
        private const float CURSOR_BLINK_INTERVAL = 0.5f;
        private string _editingElementId = null;
        
        // Animation snapshot — captured at start of cycle so totalChars doesn't shrink
        // as elements complete, which was causing progress to accelerate through later elements.
        private List<HolographicTextElement> _animationSnapshot;
        private int _animationSnapshotTotalChars;
        
        // Priority order for element animation sequence
        private List<string> _priorityOrder = new List<string>
        {
            "hip_value", "name_value", "distance_value",
            "spectral_value", "mag_value", "const_value",
            "search_input",
            "result_0", "result_1", "result_2", "result_3", "result_4",
            "result_5", "result_6", "result_7", "result_8", "result_9",
            "page_number"
        };
        
        // Grid-based content buffer
        private char[,] _gridBuffer = new char[GRID_ROWS, GRID_COLUMNS];
        private readonly ConsoleCellInstanceNative[] _stagingCells = new ConsoleCellInstanceNative[767];
        
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
        /// Mark this layer as dirty (ILayer implementation).
        /// </summary>
        public void MarkDirty()
        {
            IsDirty = true;
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
        /// Gets the pixel rectangle for the specified element from the specified screen.
        /// Uses constraint-based layout.
        /// </summary>
        public Rect GetElementArea(string screenName, string elementId)
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
                element.DynamicText = text;
                element.IsVisible = !string.IsNullOrEmpty(text);
                element.IsDirty = true;
                
                // Don't animate elements that are currently being edited — they must stay fully visible
                if (!element.IsEditing)
                {
                    ModFileLogger.Log($"[ElementLayer] SetElementText({elementId}): '{element.DynamicText}' -> '{text}', flagging for animation");
                    element.NeedsTypeOnAnimation = true;
                    element.TypeOnProgress = 0f;
                }
                else
                {
                    ModFileLogger.Log($"[ElementLayer] SetElementText({elementId}): '{element.DynamicText}' -> '{text}', skipping animation — element is editing");
                }
            }
        }
        
        public void UpdateElementText(string elementId, string text)
        {
            var element = _elements.Find(e => e.ElementId == elementId);
            if (element != null && element.DynamicText != text)
            {
                element.DynamicText = text;
                element.IsVisible = !string.IsNullOrEmpty(text);
                element.IsDirty = true;
                
                // Don't animate elements that are currently being edited — they must stay fully visible
                if (!element.IsEditing)
                {
                    element.NeedsTypeOnAnimation = true;
                    element.TypeOnProgress = 0f;
                }
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
                e.NeedsTypeOnAnimation = true;
                e.TypeOnProgress = 0f;
                e.IsDirty = true;
            }
            ModFileLogger.Log("[ElementLayer] All element animations reset to 0");
        }

        /// <summary>
        /// Returns true if any visible element still needs type-on animation.
        /// </summary>
        public bool HasElementsNeedingAnimation()
        {
            foreach (var element in _elements)
            {
                if (element.IsVisible && element.NeedsTypeOnAnimation)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Flags a single element for type-on animation and resets its progress.
        /// </summary>
        public void ResetAnimationForElement(string elementId)
        {
            var element = _elements.Find(e => e.ElementId == elementId);
            if (element != null && !element.IsEditing)
            {
                element.NeedsTypeOnAnimation = true;
                element.TypeOnProgress = 0f;
                element.IsDirty = true;
            }
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
                                  "spectral_value", "mag_value", "const_value" };
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
            float weightedCharCount = GetWeightedAnimationCharacterCount();
            if (weightedCharCount <= 0f) return 0f;
            float duration = weightedCharCount / FIELD_CHARS_PER_SECOND;
            
            // Snapshot animating elements at start of cycle
            _animationSnapshot = GetSortedVisibleElements().FindAll(e => e.NeedsTypeOnAnimation);
            _animationSnapshotTotalChars = 0;
            foreach (var e in _animationSnapshot)
                _animationSnapshotTotalChars += CountNonSpaceChars(e.FullDisplayText);
            
            ModFileLogger.Log($"[AnimDebug] CalculateTypeOnDuration weightedChars={weightedCharCount:F1} duration={duration:F3}s snapshotChars={_animationSnapshotTotalChars}");
            return duration;
        }
        
        /// <summary>
        /// Get total visible character count across ALL visible elements.
        /// Used for global character-based animation.
        /// </summary>
        private float GetWeightedAnimationCharacterCount()
        {
            float total = 0f;
            var visibleElements = GetSortedVisibleElements();
            
            foreach (var element in visibleElements)
            {
                if (!element.NeedsTypeOnAnimation) continue;
                
                string text = element.FullDisplayText;
                int charCount = 0;
                foreach (char c in text)
                {
                    if (c != ' ' && c != '\n' && c != '\r' && c != '\t')
                        charCount++;
                }
                
                // Search results animate at 2x effective speed (0.5x weight)
                if (element.ElementId.StartsWith("result_") || element.Type == TextElementType.SearchResult)
                    total += charCount * RESULT_WEIGHT;
                else
                    total += charCount;
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

        public void FillCellData(
            IntPtr textSystem,
            ConsoleCellInstanceNative[] buffer,
            ref int writeIndex,
            float typeOnProgress,
            uint color,
            float fontSize,
            float aspectRatio)
        {
            IntPtr ts = textSystem != IntPtr.Zero ? textSystem : _textSystem;
            if (ts == IntPtr.Zero || buffer == null || writeIndex >= buffer.Length)
                return;

            if (typeOnProgress > 0f)
            {
                DistributeGlobalProgressAcrossElements(typeOnProgress);
            }
            
            if (typeOnProgress >= 1f)
            {
                _animationSnapshot = null;
                _animationSnapshotTotalChars = 0;
            }

            BuildLayer3Content();

            var sb = new System.Text.StringBuilder();
            for (int row = 0; row < GRID_ROWS; row++)
            {
                for (int col = 0; col < GRID_COLUMNS; col++)
                {
                    sb.Append(_gridBuffer[row, col] == '\0' ? ' ' : _gridBuffer[row, col]);
                }
                if (row < GRID_ROWS - 1)
                    sb.Append('\n');
            }
            string fullText = sb.ToString();

            if (string.IsNullOrEmpty(fullText))
                return;

            int remaining = buffer.Length - writeIndex;
            int maxCells = Mathf.Min(_stagingCells.Length, remaining);
            int cellsWritten = StarfieldNative.CR_TextLayoutToCells(
                ts, fullText, fontSize, color, 0f, 0f, 0f, aspectRatio,
                _stagingCells, maxCells);

            if (cellsWritten > 0)
            {
                Array.Copy(_stagingCells, 0, buffer, writeIndex, cellsWritten);

                if (typeOnProgress > 0f && typeOnProgress < 1f)
                {
                    var last = buffer[writeIndex + cellsWritten - 1];
                    CursorPosition = new Vector2(last.PosX + last.SizeX, last.PosY);
                }
                else
                {
                    CursorPosition = null;
                }
            }
            else
            {
                CursorPosition = null;
            }

            writeIndex += cellsWritten;
        }
        
        /// <summary>
        /// Distribute global Layer3Progress across all visible elements.
        /// Treats all elements as one continuous character stream.
        /// </summary>
        private void DistributeGlobalProgressAcrossElements(float globalProgress)
        {
            // Use snapshot taken at start of animation cycle so totalChars stays constant
            var elements = _animationSnapshot;
            int totalChars = _animationSnapshotTotalChars;
            
            if (elements == null || elements.Count == 0 || totalChars == 0) return;
            
            // Calculate per-element char counts from snapshot
            var elementCharCounts = new List<int>();
            foreach (var element in elements)
            {
                elementCharCounts.Add(CountNonSpaceChars(element.FullDisplayText));
            }
            
            int visibleCharCount = Mathf.Max(1, Mathf.FloorToInt(globalProgress * totalChars));
            
            // DEBUG: log distribution summary when animation is active
            if (globalProgress > 0f && globalProgress < 1f)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"[AnimDebug] Distribute global={globalProgress:F3} totalChars={totalChars} visibleChars={visibleCharCount} animating={elements.Count}: ");
                for (int i = 0; i < elements.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append($"{elements[i].ElementId}({elementCharCounts[i]})");
                }
                ModFileLogger.Log(sb.ToString());
            }
            
            int charsAssigned = 0;
            bool hasChanges = false;
            
            for (int i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                int elementCharCount = elementCharCounts[i];
                float prevProgress = element.TypeOnProgress;
                
                if (elementCharCount == 0)
                {
                    element.TypeOnProgress = 1.0f;
                }
                else if (charsAssigned + elementCharCount <= visibleCharCount)
                {
                    element.TypeOnProgress = 1.0f;
                    charsAssigned += elementCharCount;
                }
                else if (charsAssigned >= visibleCharCount)
                {
                    element.TypeOnProgress = 0.0f;
                }
                else
                {
                    int charsIntoElement = visibleCharCount - charsAssigned;
                    element.TypeOnProgress = (float)charsIntoElement / elementCharCount;
                    charsAssigned += elementCharCount;
                }
                
                // Auto-clear flag when animation completes
                if (element.TypeOnProgress >= 1.0f)
                {
                    element.NeedsTypeOnAnimation = false;
                }
                
                // DEBUG: log per-element progress when animation is active
                if (globalProgress > 0f && globalProgress < 1f)
                {
                    ModFileLogger.Log($"[AnimDebug]   {element.ElementId}: prev={prevProgress:F3} new={element.TypeOnProgress:F3} charCount={elementCharCount}");
                }
                
                if (Mathf.Abs(element.TypeOnProgress - prevProgress) > 0.001f)
                {
                    element.IsDirty = true;
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
        /// Builds Layer 3 content using constraint-based layout.
        /// Sources all element positions from the constraint layout system.
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
            
            // Main screen element IDs in render order
            // (must match the elements created by StarCatalogHolographicDisplay)
            string[] mainElementIds = new string[]
            {
                "hip_value", "name_value", "distance_value",
                "spectral_value", "mag_value", "const_value",
                "search_input",
                "page_number"
            };
            
            // Render each element to the grid buffer
            foreach (string elementId in mainElementIds)
            {
                // Skip buttons - they are drawn in Layer 2
                if (elementId == "save_button" || elementId == "reset_button" || elementId == "rescan_button")
                    continue;
                
                // Find the corresponding HolographicTextElement
                var element = _elements.Find(e => e.ElementId == elementId);
                if (element != null && element.IsVisible)
                {
                    string text = GetDisplayText(element);
                    
                    GridPosition gridPos = GetElementGridPosition(elementId);
                    
                    if (!string.IsNullOrEmpty(text))
                    {
                        PlaceTextInGrid(text, gridPos.Column, gridPos.Row);
                    }
                    
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
        /// Gets the current display text for the specified element.
        /// Returns null if the element does not exist in this layer.
        /// Returns empty string if the element exists but is invisible.
        /// </summary>
        public string GetElementText(string elementId)
        {
            var element = _elements.Find(e => e.ElementId == elementId);
            if (element == null) return null;
            if (!element.IsVisible) return "";
            return GetDisplayText(element);
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
                {
                    if (element.TypeOnProgress > 0f && element.TypeOnProgress < 1f)
                        ModFileLogger.Log($"[AnimDebug]   Display {element.ElementId}: progress={element.TypeOnProgress:F3} endIndex=0/{fullText.Length} text=' '");
                    return " ";
                }
                else
                {
                    string result = fullText.Substring(0, endIndex);
                    if (element.TypeOnProgress > 0f && element.TypeOnProgress < 1f)
                        ModFileLogger.Log($"[AnimDebug]   Display {element.ElementId}: progress={element.TypeOnProgress:F3} endIndex={endIndex}/{fullText.Length} text='{result}'");
                    return result;
                }
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
            }
        }
        
        /// <summary>
        /// Set the cursor state from external source.
        /// </summary>
        public void SetCursorState(string editingElementId, bool cursorVisible)
        {
            _editingElementId = editingElementId;
            _cursorVisible = cursorVisible;
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
        /// Cleanup resources.
        /// </summary>
        public void Cleanup()
        {
        }
    }
}
