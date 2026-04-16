using System;
using UnityEngine;
using CinematicShaders.Native.Structs;

namespace CinematicShaders.UI.Screens
{
    public interface ILayer
    {
        int Order { get; }  // 1, 2, 3...
        string LayerName { get; }
        bool IsDirty { get; set; }
        
        void Render(float typeOnProgress);
        void MarkDirty();

        // NEW: cursor overlay support
        Vector2? CursorPosition { get; }

        void FillCellData(
            IntPtr textSystem,
            ConsoleCellInstanceNative[] buffer,
            ref int writeIndex,
            float typeOnProgress,
            uint color,
            float fontSize,
            float aspectRatio);
    }
}
