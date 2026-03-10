using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CinematicShaders.Native
{
    public static class StarfieldNative
    {
        private const string DllName = "CinematicShadersNative.dll";
        private static IntPtr _dllHandle = IntPtr.Zero;
        private static bool _loaded = false;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        static StarfieldNative()
        {
            try
            {
                // Check if DLL is already loaded by GTAONative or previous initialization
                _dllHandle = GetModuleHandle(DllName);

                if (_dllHandle == IntPtr.Zero)
                {
                    // Not loaded yet, load it explicitly
                    string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    string pluginDataPath = Path.GetFullPath(Path.Combine(assemblyPath, "..", "PluginData"));
                    string dllPath = Path.Combine(pluginDataPath, DllName);

                    Debug.Log($"[StarfieldNative] Loading native DLL from: {dllPath}");

                    if (!File.Exists(dllPath))
                    {
                        Debug.LogError($"[StarfieldNative] Native DLL not found at: {dllPath}");
                        return;
                    }

                    _dllHandle = LoadLibrary(dllPath);

                    if (_dllHandle == IntPtr.Zero)
                    {
                        int errorCode = Marshal.GetLastWin32Error();
                        Debug.LogError($"[StarfieldNative] LoadLibrary failed with error {errorCode}: {new System.ComponentModel.Win32Exception(errorCode).Message}");
                        return;
                    }
                }
                else
                {
                    Debug.Log("[StarfieldNative] DLL already loaded (shared with GTAO)");
                }

                if (GetProcAddress(_dllHandle, "CR_StarfieldSetSettings") == IntPtr.Zero)
                {
                    Debug.LogError("[StarfieldNative] DLL loaded but CR_StarfieldSetSettings export not found!");
                    return;
                }

                _loaded = true;
                Debug.Log("[StarfieldNative] Native DLL initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StarfieldNative] Failed to initialize native DLL: {ex}");
            }
        }

        public static bool IsLoaded => _loaded;

        [StructLayout(LayoutKind.Sequential)]
        public struct StarfieldSettingsNative
        {
            public float Exposure;
            public float BlurPixels;
            public float StarDensity;
            public float MinMagnitude;
            public float MaxMagnitude;
            public float MagnitudeBias;
            public float HeroRarity;
            public float Clustering;
            public float StaggerAmount;
            public float PopulationBias;
            public float MainSequenceStrength;
            public float RedGiantRarity;
            public float GalacticFlatness;
            public float GalacticDiscFalloff;
            public float BandCenterBoost;
            public float BandCoreSharpness;
            public float BulgeIntensity;
            public float BulgeWidth;
            public float BulgeHeight;
            public float BulgeSoftness;
            public float BulgeNoiseScale;
            public float BulgeNoiseStrength;
            public float BloomThreshold;
            public float BloomIntensity;
            public float SpikeIntensity;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 32)]
        public struct StarDataNative
        {
            public float DirectionX;
            public float DirectionY;
            public float DirectionZ;
            public float Magnitude;

            public float ColorR;
            public float ColorG;
            public float ColorB;
            public float Temperature;

            public StarDataNative(Vector3 direction, float magnitude, Color color, float temperature)
            {
                DirectionX = direction.x;
                DirectionY = direction.y;
                DirectionZ = direction.z;
                Magnitude = magnitude;
                ColorR = color.r;
                ColorG = color.g;
                ColorB = color.b;
                Temperature = temperature;
            }

            public Vector3 Direction => new Vector3(DirectionX, DirectionY, DirectionZ);
            public Color Color => new Color(ColorR, ColorG, ColorB, 1.0f);
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldSetCameraMatrices(
            IntPtr deviceSourceTexture,  // Pass Texture2D.whiteTexture.GetNativeTexturePtr()
            int width,
            int height,
            float verticalFOV,
            float aspectRatio,
            Vector3 cameraRight,
            Vector3 cameraUp,
            Vector3 cameraForward
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldSetSettings(ref StarfieldSettingsNative settings);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr CR_GetStarfieldRenderEventFunc();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldShutdown();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldGenerateCatalog(int seed, int count);
    }
}