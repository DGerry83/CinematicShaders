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
        public Vector2? CursorPosition { get; private set; }
        
        private readonly string[] _borderLines;
        private readonly ConsoleCellInstanceNative[] _stagingCells = new ConsoleCellInstanceNative[StarfieldNative.MaxConsoleCells];
        
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
                
                if (endIndex <= 0)
                    borderText = " ";  // Space when nothing visible yet
                else
                    borderText = borderText.Substring(0, endIndex);
            }
            
            // Note: Actual rendering happens in FillCellData with native text system
            // This method just prepares the text content for interface compliance
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

            int remaining = buffer.Length - writeIndex;
            int maxCells = Mathf.Min(_stagingCells.Length, remaining);
            int cellsWritten = StarfieldNative.CR_TextLayoutToCells(
                textSystem, borderText, fontSize, color, 0f, 0f, 0f, aspectRatio,
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
                    return borderText.Substring(0, endIndex);
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
