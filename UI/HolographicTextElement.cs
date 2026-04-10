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
        Input           // Search input field
    }

    /// <summary>
    /// A text element for the holographic display with dirty-flag rendering.
    /// Supports type-on animation and selection states.
    /// </summary>
    public class HolographicTextElement
    {
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
        public RenderTexture TextTexture;
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
        /// Release the render texture if allocated
        /// </summary>
        public void ReleaseTexture()
        {
            if (TextTexture != null)
            {
                TextTexture.Release();
                Object.Destroy(TextTexture);
                TextTexture = null;
            }
        }
    }
}
