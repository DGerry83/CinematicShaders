using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CinematicShaders.Native
{
    /// <summary>
    /// P/Invoke declarations for CinematicShadersNative.dll
    /// </summary>
    public static class GTAONative
    {
        private const string DllName = "CinematicShadersNative.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_GTAODebugSetInput(
            IntPtr depthTex,
            IntPtr normalTex,
            int width,
            int height,
            [In] float[] invProj,
            [In] float[] worldToView,
            float nearPlane,
            float farPlane,
            int frameIndex);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_GTAOSetOutputMode(int mode);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr CR_GetGTAORenderEventFunc();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_GTAOShutdown();
    }
}