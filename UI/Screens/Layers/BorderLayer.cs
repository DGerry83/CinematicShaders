using System;
using UnityEngine;
using CinematicShaders.Native;

namespace CinematicShaders.UI.Screens.Layers
{
    /// <summary>
    /// Layer 1: Renders the ASCII border frame with type-on animation.
    /// </summary>
    public class BorderLayer : ILayer
    {
        public int Order => 1;
        public string LayerName => "Border";
        public bool IsDirty { get; set; } = true;
        
        private readonly string[] _borderLines;
        private RenderTexture _targetTexture;
        
        public BorderLayer(string[] borderLines)
        {
            _borderLines = borderLines;
        }
        
        /// <summary>
        /// Render the border with type-on effect
        /// </summary>
        public void Render(float typeOnProgress)
        {
            if (_targetTexture == null) return;
            
            // Build border text from lines
            string borderText = string.Join("\n", _borderLines);
            
            // Apply type-on: only show portion based on progress (with cursor)
            // Spaces skip - they appear immediately without consuming type-on time
            if (typeOnProgress < 1f)
            {
                int endIndex = GetTypeOnEndIndex(borderText, typeOnProgress);
                
                // Add cursor when typing is in progress
                if (endIndex <= 0)
                    borderText = " ";  // Space when nothing visible yet
                else
                    borderText = borderText.Substring(0, endIndex) + "^|";
            }
            
            // Note: Actual rendering happens in RenderToTexture with native text system
            // This method just prepares the text content
        }
        
        /// <summary>
        /// Render to the target texture using the native text system.
        /// Called by the screen with proper setup.
        /// </summary>
        public void RenderToTexture(IntPtr textSystem, uint color, float fontSize, float aspectRatio, float typeOnProgress = 1f)
        {
            if (_targetTexture == null) return;
            if (!IsDirty && typeOnProgress >= 1f) return;
            
            IsDirty = false;
            
            // Build border text from lines and apply type-on
            string borderText = GetTextForProgress(typeOnProgress);
            
            // Layout the border text using native text system
            int glyphCount = StarfieldNative.CR_TextLayoutEx(textSystem, borderText, fontSize, 
                color, 0f, 0f, 0f, aspectRatio);
            
            if (glyphCount <= 0) return;
            
            // Clear texture
            RenderTexture.active = _targetTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = null;
            
            // Dispatch to render
            StarfieldNative.CR_TextDispatch(
                textSystem,
                _targetTexture.GetNativeTexturePtr(),
                glyphCount,
                _targetTexture.width,
                _targetTexture.height);
        }
        
        /// <summary>
        /// Set the target texture for this layer
        /// </summary>
        public void SetTargetTexture(RenderTexture texture)
        {
            _targetTexture = texture;
            IsDirty = true;
        }
        
        public void MarkDirty()
        {
            IsDirty = true;
        }
        
        /// <summary>
        /// Get the current text content for type-on rendering
        /// </summary>
        public string GetTextForProgress(float typeOnProgress)
        {
            string borderText = string.Join("\n", _borderLines);
            
            if (typeOnProgress < 1f)
            {
                int endIndex = GetTypeOnEndIndex(borderText, typeOnProgress);
                
                if (endIndex <= 0)
                    return " ";
                else
                    return borderText.Substring(0, endIndex) + "^|";
            }
            
            return borderText;
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
