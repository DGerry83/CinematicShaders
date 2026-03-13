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
            public float MinMagnitude;
            public float MaxMagnitude;
            public float MagnitudeBias;
            public int HeroCount;  // 16-1024
            public float Clustering;
            public float PopulationBias;
            public float MainSequenceStrength;
            public float RedGiantFrequency;
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
            public float ColorSaturation;  // 0.0-2.0: 0.5=realistic, 1.0=natural, 2.0=vivid
            
            // HYG Catalog Coordinate Rotation (degrees)
            public float RotationX;
            public float RotationY;
            public float RotationZ;
            
            // Galactic plane orientation
            public float GalacticPlaneNormalX;
            public float GalacticPlaneNormalY;
            public float GalacticPlaneNormalZ;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 48)]
        public struct StarDataNative
        {
            public int HipparcosID;    // Hipparcos catalog ID (0 if procedural)
            public float DistancePc;   // Distance in parsecs (0 if unknown)
            public int SpectralType;   // 0=O,1=B,2=A,3=F,4=G,5=K,6=M,7=L,255=Unknown
            public uint Flags;         // Bit 0=IsHero (can be named/important)
            
            public float DirectionX;
            public float DirectionY;
            public float DirectionZ;
            public float Magnitude;

            public float ColorR;
            public float ColorG;
            public float ColorB;
            public float Temperature;

            // Flag constants
            public const uint FLAG_IS_HERO = 1;  // Bit 0: Star can be named/is important

            public StarDataNative(int hipparcosID, float distancePc, int spectralType, uint flags, Vector3 direction, float magnitude, Color color, float temperature)
            {
                HipparcosID = hipparcosID;
                DistancePc = distancePc;
                SpectralType = spectralType;
                Flags = flags;
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
            public bool IsHero => (Flags & FLAG_IS_HERO) != 0;
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
            Vector3 cameraForward,
            // Atmospheric extinction parameters (per-frame)
            float extinctionZenith,     // Visibility at zenith (0-1)
            float extinctionHorizon,    // Visibility at horizon (0-1)
            Vector3 atmosphereUp        // World-space up vector
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldSetSettings(ref StarfieldSettingsNative settings);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr CR_GetStarfieldRenderEventFunc();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldShutdown();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldGenerateCatalog(int seed, int count);

        // Catalog save/load exports
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_StarfieldGetCatalogData([Out] StarDataNative[] outBuffer, int maxCount);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldLoadCatalog([In] StarDataNative[] buffer, int count, int heroCount);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_StarfieldGetCatalogSize();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_StarfieldGetHeroCount();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte CR_StarfieldIsDeviceReady();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte CR_StarfieldCatalogNeedsReload();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldInvalidateResources();

        /// <summary>
        /// Check if the D3D11 device is initialized and ready
        /// </summary>
        public static bool IsDeviceReady()
        {
            try
            {
                return CR_StarfieldIsDeviceReady() != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if catalog needs reload (device was acquired but catalog empty). Resets flag after reading.
        /// </summary>
        public static bool CatalogNeedsReload()
        {
            try
            {
                return CR_StarfieldCatalogNeedsReload() != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Invalidate GPU resources (call on scene change to force recreation, preserves catalog)
        /// </summary>
        public static void InvalidateResources()
        {
            try
            {
                CR_StarfieldInvalidateResources();
            }
            catch
            {
                // Ignore if DLL not loaded
            }
        }

        /// <summary>
        /// Get current catalog data from native plugin
        /// </summary>
        public static StarDataNative[] GetCatalogData(int count)
        {
            if (count <= 0) return new StarDataNative[0];
            
            var buffer = new StarDataNative[count];
            int actualCount = CR_StarfieldGetCatalogData(buffer, count);
            
            if (actualCount != count)
            {
                Debug.LogWarning($"[StarfieldNative] Catalog size mismatch: expected {count}, got {actualCount}");
                // Resize array to actual count
                if (actualCount > 0)
                {
                    var actual = new StarDataNative[actualCount];
                    Array.Copy(buffer, actual, actualCount);
                    return actual;
                }
                return new StarDataNative[0];
            }
            
            return buffer;
        }

        /// <summary>
        /// Load a catalog into the native plugin
        /// </summary>
        public static void LoadCatalog(StarDataNative[] stars, int heroCount)
        {
            if (stars == null || stars.Length == 0)
            {
                Debug.LogWarning("[StarfieldNative] Cannot load null or empty catalog");
                return;
            }
            
            CR_StarfieldLoadCatalog(stars, stars.Length, heroCount);
        }

        /// <summary>
        /// Get the number of stars in the current catalog
        /// </summary>
        public static int GetCatalogSize()
        {
            return CR_StarfieldGetCatalogSize();
        }

        /// <summary>
        /// Get the number of hero stars in the current catalog
        /// </summary>
        public static int GetHeroCount()
        {
            return CR_StarfieldGetHeroCount();
        }

        
    }
}