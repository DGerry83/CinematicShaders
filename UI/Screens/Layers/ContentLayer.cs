using System;
using UnityEngine;
using CinematicShaders.Native;

namespace CinematicShaders.UI.Screens.Layers
{
    /// <summary>
    /// Layer 2: Renders content/labels with type-on animation.
    /// </summary>
    public class ContentLayer : ILayer
    {
        public int Order => 2;
        public string LayerName => "Content";
        public bool IsDirty { get; set; } = true;
        
        private readonly string[] _contentLines;
        private RenderTexture _targetTexture;
        
        // Track last rendered progress and color to avoid redundant renders
        private float _lastRenderedProgress = -1f;
        private uint _lastRenderedColor = 0;
        
        public ContentLayer(string[] contentLines)
        {
            _contentLines = contentLines;
        }
        
        /// <summary>
        /// Render the content with type-on effect
        /// </summary>
        public void Render(float typeOnProgress)
        {
            // Content layer rendering happens in RenderToTexture
            // This method exists for interface compliance
        }
        
        /// <summary>
        /// Render to the target texture using the native text system.
        /// Called by the screen with proper setup.
        /// </summary>
        public void RenderToTexture(IntPtr textSystem, uint color, float fontSize, float aspectRatio, float typeOnProgress)
        {
            if (_targetTexture == null) return;
            
            // Skip if not dirty and progress hasn't changed and color hasn't changed
            // Always render if progress is changing (animation in progress)
            bool progressChanged = Mathf.Abs(typeOnProgress - _lastRenderedProgress) > 0.001f;
            bool colorChanged = color != _lastRenderedColor;
            if (!IsDirty && typeOnProgress >= 1f && !progressChanged && !colorChanged) return;
            
            // Join lines with newlines
            string text = string.Join("\n", _contentLines);
            
            // Apply type-on: only show portion based on progress (with cursor)
            if (typeOnProgress < 1f)
            {
                int endIndex = GetTypeOnEndIndex(text, typeOnProgress);
                
                // Add cursor when typing is in progress
                if (endIndex <= 0)
                    text = " ";  // Space when nothing visible yet
                else
                    text = text.Substring(0, endIndex) + "^|";
            }
            
            // Layout the text
            int glyphCount = StarfieldNative.CR_TextLayoutEx(textSystem, text, fontSize, 
                color, 0f, 0f, 0f, aspectRatio);
            
            if (glyphCount <= 0) return;
            
            // Render to texture with proper active texture handling
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = _targetTexture;
                
                // Clear texture
                GL.Clear(true, true, Color.clear);
                
                // Dispatch to render - texture must be active for this
                StarfieldNative.CR_TextDispatch(
                    textSystem,
                    _targetTexture.GetNativeTexturePtr(),
                    glyphCount,
                    _targetTexture.width,
                    _targetTexture.height);
            }
            finally
            {
                // Always reset active render texture, even if an exception occurred
                RenderTexture.active = prevActive;
            }
            
            // Update tracking state
            IsDirty = false;
            _lastRenderedProgress = typeOnProgress;
            _lastRenderedColor = color;
        }
        
        /// <summary>
        /// Set the target texture for this layer
        /// </summary>
        public void SetTargetTexture(RenderTexture texture)
        {
            _targetTexture = texture;
            IsDirty = true;
            _lastRenderedProgress = -1f;  // Force re-render on texture change
            _lastRenderedColor = 0;       // Force re-render on texture change
        }
        
        public void MarkDirty()
        {
            IsDirty = true;
        }
        
        /// <summary>
        /// Calculate the end index for type-on effect (spaces don't consume progress)
        /// </summary>
        private int GetTypeOnEndIndex(string text, float progress)
        {
            if (string.IsNullOrEmpty(text) || progress <= 0f)
                return 0;
                
            if (progress >= 1f)
                return text.Length;
            
            // Count non-space characters for total progress units
            int nonSpaceCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ' && text[i] != '\n')
                    nonSpaceCount++;
            }
            
            if (nonSpaceCount == 0)
                return text.Length;
            
            // Calculate how many non-space chars to show
            int targetNonSpace = Mathf.RoundToInt(nonSpaceCount * progress);
            
            // Find the index that gives us targetNonSpace non-space characters
            int nonSpaceSeen = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ' && text[i] != '\n')
                    nonSpaceSeen++;
                    
                if (nonSpaceSeen >= targetNonSpace)
                    return i + 1;
            }
            
            return text.Length;
        }
    }
}
