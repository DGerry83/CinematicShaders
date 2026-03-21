using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CinematicShaders.Native
{
    public static class GTAONative
    {
        private const string DllName = "CinematicShadersNative.dll";

        static GTAONative()
        {
            DllLoader.EnsureLoaded();
        }

        public static bool IsLoaded => DllLoader.IsLoaded;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_GTAODebugSetInput(
            IntPtr depthTex,
            IntPtr normalTex,
            int width,
            int height,
            [In] float[] worldToView,
            [In] float[] fovParams,
            float nearPlane,
            float farPlane,
            int frameIndex);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_GTAOSetOutputMode(int mode);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr CR_GetGTAORenderEventFunc();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_GTAOShutdown();

        [StructLayout(LayoutKind.Sequential)]
        public struct GTAOSettings
        {
            public float EffectRadius;
            public float Intensity;
            public int SliceCount;
            public int StepsPerSlice;
            public float SampleDistributionPower;
            public float NormalPower;
            public float DepthSigma;
            public float MaxPixelRadius;
            public float FadeStartDistance;
            public float FadeEndDistance;
            public float FadeCurve;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_GTAOSetSettings(ref GTAOSettings settings);
    }
}