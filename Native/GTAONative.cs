using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CinematicShaders.Native
{
    /// <summary>
    /// P/Invoke declarations for CinematicShadersNative.dll
    /// Explicitly loads the DLL to handle GameData/PluginData subdirectory paths
    /// </summary>
    public static class GTAONative
    {
        private const string DllName = "CinematicShadersNative.dll";
        private static IntPtr _dllHandle = IntPtr.Zero;
        private static bool _loaded = false;

        #region Explicit DLL Loading
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

                UnityEngine.Debug.Log($"[CinematicShaders] Loading native DLL from: {dllPath}");

                if (!File.Exists(dllPath))
                {
                    UnityEngine.Debug.LogError($"[CinematicShaders] Native DLL not found at: {dllPath}");
                    return;
                }

                _dllHandle = LoadLibrary(dllPath);

                if (_dllHandle == IntPtr.Zero)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    UnityEngine.Debug.LogError($"[CinematicShaders] LoadLibrary failed with error {errorCode}: {new System.ComponentModel.Win32Exception(errorCode).Message}");
                    return;
                }

                if (GetProcAddress(_dllHandle, "CR_GTAOSetSettings") == IntPtr.Zero)
                {
                    UnityEngine.Debug.LogError("[CinematicShaders] DLL loaded but CR_GTAOSetSettings export not found!");
                    return;
                }

                _loaded = true;
                UnityEngine.Debug.Log("[CinematicShaders] Native DLL loaded successfully");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[CinematicShaders] Failed to load native DLL: {ex}");
            }
        }

        public static bool IsLoaded => _loaded;
        #endregion

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

        [StructLayout(LayoutKind.Sequential)]
        public struct GTAOSettings
        {
            public float EffectRadius;        // Default: 2.0f
            public float Intensity;           // Default: 0.8f
            public int SliceCount;           // Default: 2
            public int StepsPerSlice;        // Default: 4
            public float SampleDistributionPower; // Default: 2.0f
            public float NormalPower;        // Default: 32.0f
            public float DepthSigma;         // Default: 0.5f
            public float MaxPixelRadius;     // Default: 50.0f
            public float FadeStartDistance;  // Default: 0.0f
            public float FadeEndDistance;    // Default: 500.0f
            public float FadeCurve;          // Default: 1.0f
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_GTAOSetSettings(ref GTAOSettings settings);
    }
}