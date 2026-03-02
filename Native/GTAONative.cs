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
        private static IntPtr _dllHandle = IntPtr.Zero;
        private static bool _loaded = false;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        static GTAONative()
        {
            try
            {
                string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string pluginDataPath = Path.GetFullPath(Path.Combine(assemblyPath, "..", "PluginData"));
                string dllPath = Path.Combine(pluginDataPath, DllName);

                Debug.Log($"[CinematicShaders] Loading native DLL from: {dllPath}");

                if (!File.Exists(dllPath))
                {
                    Debug.LogError($"[CinematicShaders] Native DLL not found at: {dllPath}");
                    return;
                }

                _dllHandle = LoadLibrary(dllPath);

                if (_dllHandle == IntPtr.Zero)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    Debug.LogError($"[CinematicShaders] LoadLibrary failed with error {errorCode}: {new System.ComponentModel.Win32Exception(errorCode).Message}");
                    return;
                }

                if (GetProcAddress(_dllHandle, "CR_GTAOSetSettings") == IntPtr.Zero)
                {
                    Debug.LogError("[CinematicShaders] DLL loaded but CR_GTAOSetSettings export not found!");
                    return;
                }

                _loaded = true;
                Debug.Log("[CinematicShaders] Native DLL loaded successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to load native DLL: {ex}");
            }
        }

        public static bool IsLoaded => _loaded;

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