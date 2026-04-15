using System;
using System.Runtime.InteropServices;
using UnityEngine;
using CinematicShaders.Native;
using CinematicShaders.Native.Structs;

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
        
        public BorderLayer(string[] borderLines)
        {
            _borderLines = borderLines;
        }
        
        /// <summary>
        /// Render the border with type-on effect
        /// </summary>
        public void Render(float typeOnProgress)
        {
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
            
            // Note: Actual rendering happens in FillCellData with native text system
            // This method just prepares the text content for interface compliance
        }
        
        /// <summary>
        /// Legacy no-op stub for screen compatibility during refactor.
        /// </summary>
        public void RenderToTexture(IntPtr textSystem, uint color, float fontSize, float aspectRatio, float typeOnProgress = 1f)
        {
        }
        
        /// <summary>
        /// Legacy no-op stub for screen compatibility during refactor.
        /// </summary>
        public void SetTargetTexture(RenderTexture texture)
        {
        }
        
        public void MarkDirty()
        {
            IsDirty = true;
        }
        
        public void FillCellData(
            IntPtr textSystem,
            ConsoleCellInstanceNative[] buffer,
            ref int writeIndex,
            float typeOnProgress,
            uint color,
            float fontSize,
            float aspectRatio)
        {
            if (textSystem == IntPtr.Zero || buffer == null || writeIndex >= buffer.Length)
                return;

            string borderText = GetTextForProgress(typeOnProgress);
            if (string.IsNullOrEmpty(borderText))
                return;

            int glyphCount = StarfieldNative.CR_TextLayoutEx(textSystem, borderText, fontSize,
                color, 0f, 0f, 0f, aspectRatio);

            if (glyphCount <= 0)
                return;

            IntPtr glyphPtr = StarfieldNative.CR_TextGetGlyphPtr(textSystem);
            int glyphSize = Marshal.SizeOf<StarfieldNative.GlyphData>();
            int glyphIndex = 0;

            string[] lines = borderText.Split('\n');
            for (int y = 0; y < lines.Length && writeIndex < buffer.Length; y++)
            {
                string line = lines[y];
                for (int x = 0; x < line.Length && writeIndex < buffer.Length; x++)
                {
                    char c = line[x];
                    if (c == ' ')
                        continue;

                    if (glyphIndex >= glyphCount)
                        break;

                    var glyph = Marshal.PtrToStructure<StarfieldNative.GlyphData>(
                        IntPtr.Add(glyphPtr, glyphIndex * glyphSize));

                    ushort glyphID = StarfieldNative.CR_TextGetGlyphID(textSystem, c);

                    buffer[writeIndex] = new ConsoleCellInstanceNative
                    {
                        GridX = (ushort)x,
                        GridY = (ushort)y,
                        GlyphID = glyphID,
                        Color = color,
                        U0 = glyph.UvX,
                        V0 = glyph.UvY,
                        U1 = glyph.UvX + glyph.UvW,
                        V1 = glyph.UvY + glyph.UvH
                    };
                    writeIndex++;
                    glyphIndex++;
                }
            }
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
