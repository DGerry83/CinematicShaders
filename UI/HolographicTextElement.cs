using System;
using UnityEngine;

namespace CinematicShaders.UI
{
    /// <summary>
    /// Types of text elements in the holographic display
    /// </summary>
    public enum TextElementType
    {
        Label,          // Static label ("HIP:", "NAME:")
        Value,          // Read-only value
        Editable,       // Click to edit (NAME field)
        SearchResult,   // Clickable result row
        Header,         // "SEARCH RESULTS"
        Border,         // ASCII art elements
        Input,          // Search input field
        Button          // Clickable button
    }

    /// <summary>
    /// A text element for the holographic display with dirty-flag rendering.
    /// Supports type-on animation and selection states.
    /// </summary>
    public class HolographicTextElement
    {
        /// <summary>
        /// Default constructor for object initializer syntax.
        /// </summary>
        public HolographicTextElement()
        {
        }

        // Identification
        public string ElementId;
        public TextElementType Type;

        // Content - ALL CAPS
        public string StaticText;      // Label portion
        public string DynamicText;     // Value portion

        /// <summary>
        /// Full display text combining static and dynamic portions
        /// </summary>
        public string FullDisplayText => $"{StaticText} {DynamicText}".Trim().ToUpper();

        // Position (4K reference coordinates)
        public Rect Position4K;

        /// <summary>
        /// Get scaled position based on scale factor
        /// </summary>
        public Rect ScaledPosition(float scaleFactor) => ScaleRect(Position4K, scaleFactor);

        // Rendering
        public bool IsDirty = true;    // Needs re-render

        // Selection state (for editable)
        public bool IsSelected;
        public bool IsSelecting { get; set; }
        public bool IsEditing { get; set; }  // True when user is actively editing this element

        // Animation
        public float TypeOnProgress = 1.0f;  // 0.0 to 1.0 (1.0 = fully typed)
        public float TypeOnDelay = 0f;       // Seconds before starting (relative to layer start)
        public float TypeOnDuration = 0.5f;  // How long the type-on takes (default 0.5s)

        // Cursor
        public bool ShowCursor;
        public float CursorBlinkTime;

        // Associated data for search results
        public object AssociatedData;
        public bool IsVisible = true;

        // Animation priority order (lower = earlier)
        public int Priority;

        /// <summary>
        /// Helper to scale a rect from 4K reference to target resolution
        /// </summary>
        private static Rect ScaleRect(Rect rect4K, float scaleFactor)
        {
            return new Rect(
                rect4K.x * scaleFactor,
                rect4K.y * scaleFactor,
                rect4K.width * scaleFactor,
                rect4K.height * scaleFactor
            );
        }

        /// <summary>
        /// Mark this element as needing re-render
        /// </summary>
        public void MarkDirty()
        {
            IsDirty = true;
        }

        /// <summary>
        /// Update dynamic text and mark dirty if changed
        /// </summary>
        public void SetDynamicText(string text)
        {
            string newText = text?.ToUpper() ?? "";
            if (DynamicText != newText)
            {
                DynamicText = newText;
                IsDirty = true;
            }
        }

        /// <summary>
        /// Get the text with type-on animation applied
        /// </summary>
        public string GetTypeOnText()
        {
            string fullText = FullDisplayText;
            
            if (TypeOnProgress >= 1f || string.IsNullOrEmpty(fullText))
                return fullText;

            int visibleChars = Mathf.RoundToInt(fullText.Length * TypeOnProgress);
            visibleChars = Mathf.Clamp(visibleChars, 0, fullText.Length);
            return fullText.Substring(0, visibleChars);
        }

        /// <summary>
        /// Creates a HolographicTextElement from a unified grid element definition.
        /// Calculates pixel position dynamically using current display size.
        /// </summary>
        /// <param name="definition">Grid element definition</param>
        public HolographicTextElement(GridElementDefinition definition)
        {
            ElementId = definition.ElementId;
            StaticText = "";
            DynamicText = "";
            Type = ConvertElementType(definition.Type);
            
            // Store grid position for reference
            GridPos = definition.Position;
            GridWidth = definition.Width;
            
            // Calculate pixel position from grid coordinates using current display size
            Position4K = definition.GetPixelRect();
            
            Priority = definition.Priority;
            IsVisible = definition.VisibleByDefault;
            
            // Initialize other fields to defaults
            IsDirty = true;
            TypeOnProgress = 1.0f;
            TypeOnDelay = 0f;
            TypeOnDuration = 0.5f;
        }

        /// <summary>
        /// Legacy constructor for backward compatibility.
        /// </summary>
        [Obsolete("Use constructor without display dimensions instead")]
        public HolographicTextElement(GridElementDefinition definition, float displayWidth, float displayHeight)
            : this(definition)
        {
        }

        /// <summary>
        /// Converts ElementType to TextElementType.
        /// </summary>
        private static TextElementType ConvertElementType(ElementType type)
        {
            switch (type)
            {
                case ElementType.Editable:
                    return TextElementType.Editable;
                case ElementType.Button:
                    return TextElementType.Button;
                case ElementType.Input:
                    return TextElementType.Input;
                case ElementType.SearchResult:
                    return TextElementType.SearchResult;
                case ElementType.Label:
                    return TextElementType.Label;
                default:
                    return TextElementType.Value;
            }
        }

        /// <summary>
        /// Factory method to create a HolographicTextElement from a grid definition.
        /// Uses current display size for glyph-based calculations.
        /// </summary>
        public static HolographicTextElement FromDefinition(GridElementDefinition definition)
        {
            return new HolographicTextElement(definition);
        }

        /// <summary>
        /// Legacy factory method for backward compatibility.
        /// </summary>
        [Obsolete("Use FromDefinition(definition) instead")]
        public static HolographicTextElement FromDefinition(GridElementDefinition definition, float displayWidth, float displayHeight)
        {
            return FromDefinition(definition);
        }


        // ============================================================================
        // NEW: Grid-based positioning (primary) - Added at end per Scope A contract
        // ============================================================================

        /// <summary>
        /// Grid position for this element (59×13 terminal grid).
        /// When GridWidth > 0, this takes precedence over Position4K.
        /// </summary>
        public GridPosition GridPos { get; set; }

        /// <summary>
        /// Width of this element in grid columns.
        /// Set to 0 to use Position4K fallback (legacy positioning).
        /// </summary>
        public int GridWidth { get; set; }

        /// <summary>
        /// Calculate pixel Rect from grid position (for rendering).
        /// Falls back to Position4K if GridWidth is 0.
        /// Uses glyph-based calculations with the current display size.
        /// </summary>
        /// <returns>Pixel rectangle for rendering</returns>
        public Rect GetPixelRect()
        {
            if (GridWidth > 0)
            {
                // Use glyph-based coordinate conversion with current display size
                Vector2 pixelPos = TerminalGridConfig.GridToPixel(
                    GridPos.Column, 
                    GridPos.Row, 
                    TerminalGridConfig.CurrentDisplaySize
                );
                
                // Calculate width based on glyph width
                var (glyphWidth, glyphHeight) = TerminalGridConfig.GlyphMetrics.GetGlyphMetrics(
                    TerminalGridConfig.CurrentDisplaySize
                );
                float width = GridWidth * glyphWidth;
                float height = glyphHeight;
                
                return new Rect(pixelPos.x, pixelPos.y, width, height);
            }
            else
            {
                // Fallback to legacy system
                return Position4K;
            }
        }

        /// <summary>
        /// Legacy method for backward compatibility - uses CurrentDisplaySize internally.
        /// </summary>
        [Obsolete("Use parameterless GetPixelRect() instead")]
        public Rect GetPixelRect(float displayWidth, float displayHeight)
        {
            return GetPixelRect();
        }
    }
}
