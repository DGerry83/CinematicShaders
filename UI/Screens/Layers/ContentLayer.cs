using System;
using System.Runtime.InteropServices;
using UnityEngine;
using CinematicShaders.Native;
using CinematicShaders.Native.Structs;

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
        
        public ContentLayer(string[] contentLines)
        {
            _contentLines = contentLines;
        }
        
        /// <summary>
        /// Render the content with type-on effect
        /// </summary>
        public void Render(float typeOnProgress)
        {
            // Content layer rendering happens in FillCellData
            // This method exists for interface compliance
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

            string text = GetTextForProgress(typeOnProgress);
            if (string.IsNullOrEmpty(text))
                return;

            int glyphCount = StarfieldNative.CR_TextLayoutEx(textSystem, text, fontSize,
                color, 0f, 0f, 0f, aspectRatio);

            if (glyphCount <= 0)
                return;

            IntPtr glyphPtr = StarfieldNative.CR_TextGetGlyphPtr(textSystem);
            int glyphSize = Marshal.SizeOf<StarfieldNative.GlyphData>();
            int glyphIndex = 0;

            string[] lines = text.Split('\n');
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
        private string GetTextForProgress(float typeOnProgress)
        {
            string text = string.Join("\n", _contentLines);
            
            if (typeOnProgress < 1f)
            {
                int endIndex = GetTypeOnEndIndex(text, typeOnProgress);
                
                if (endIndex <= 0)
                    return " ";
                else
                    return text.Substring(0, endIndex) + "^|";
            }
            
            return text;
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
