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

        // Position is grid-based only; legacy 4K fallback removed in Phase 1 cleanup

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
        /// When true, this element participates in the Layer 3 type-on animation.
        /// Auto-cleared when TypeOnProgress reaches 1.0.
        /// </summary>
        public bool NeedsTypeOnAnimation = true;

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



        // ============================================================================
        // Grid-based positioning (primary)
        // ============================================================================

        /// <summary>
        /// Grid position for this element (59×13 terminal grid).
        /// </summary>
        public GridPosition GridPos { get; set; }

        /// <summary>
        /// Width of this element in grid columns.
        /// </summary>
        public int GridWidth { get; set; }

        /// <summary>
        /// Calculate pixel Rect from grid position (for rendering).
        /// Uses glyph-based calculations with the current display size.
        /// </summary>
        /// <returns>Pixel rectangle for rendering</returns>
        public Rect GetPixelRect()
        {
            if (GridWidth > 0)
            {
                Vector2 pixelPos = TerminalGridConfig.GridToPixel(
                    GridPos.Column,
                    GridPos.Row,
                    TerminalGridConfig.CurrentDisplaySize
                );

                var (glyphWidth, glyphHeight) = TerminalGridConfig.GlyphMetrics.GetGlyphMetrics(
                    TerminalGridConfig.CurrentDisplaySize
                );
                float width = GridWidth * glyphWidth;
                float height = glyphHeight;

                return new Rect(pixelPos.x, pixelPos.y, width, height);
            }

            return Rect.zero;
        }

        /// <summary>
        /// Legacy method for backward compatibility - uses CurrentDisplaySize internally.
        /// </summary>
    }
}
