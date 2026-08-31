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
        public Vector2? CursorPosition { get; private set; }
        
        private readonly string[] _contentLines;
        private readonly ConsoleCellInstanceNative[] _stagingCells = new ConsoleCellInstanceNative[StarfieldNative.MaxConsoleCells];
        
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

            int remaining = buffer.Length - writeIndex;
            int maxCells = Mathf.Min(_stagingCells.Length, remaining);
            int cellsWritten = StarfieldNative.CR_TextLayoutToCells(
                textSystem, text, fontSize, color, 0f, 0f, 0f, aspectRatio,
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
        private string GetTextForProgress(float typeOnProgress)
        {
            string text = string.Join("\n", _contentLines);
            
            if (typeOnProgress < 1f)
            {
                int endIndex = GetTypeOnEndIndex(text, typeOnProgress);
                
                if (endIndex <= 0)
                    return " ";
                else
                    return text.Substring(0, endIndex);
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
