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

        static StarfieldNative()
        {
            DllLoader.EnsureLoaded();
        }

        public static bool IsLoaded => DllLoader.IsLoaded;

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

            // Rendering style transitions 
            public int UseSoftBloom;  // 0 = Classic, 1 = Soft HDR

            // HYG Catalog Coordinate Rotation (degrees)
            public float RotationX;
            public float RotationY;
            public float RotationZ;

            // Galactic plane orientation
            public float GalacticPlaneNormalX;
            public float GalacticPlaneNormalY;
            public float GalacticPlaneNormalZ;

            // Global scene dimming factors
            public float SunGlareDimming;      // 1.0 = full brightness, 0.0 = fully dimmed
            public float PlanetaryDimming;     // 1.0 = full brightness, 0.0 = fully dimmed  
            public float GlobalDimming;        // min(Sun, Planetary)
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
            Vector3 atmosphereUp,       // World-space up vector
            // Optional: explicit render target for cubemap rendering (IntPtr.Zero = use current)
            IntPtr explicitRenderTarget = default(IntPtr)
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
        public static extern void CR_StarfieldSetDimming(float sunGlareDimming, float planetaryDimming);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldSetKartographerEnabled(byte enabled);

        // Kartographer visual parameters struct (Phase 1 expanded - must match native exactly)
        // Total size: 256 bytes
        [StructLayout(LayoutKind.Sequential)]
        public struct KartographerParamsNative
        {
            // Base grid params (64 bytes)
            public float ResolutionX;              // offset 0
            public float ResolutionY;              // offset 4
            public float Time;                     // offset 8
            public float GridIntensity;            // offset 12
            public float GridThickness;            // offset 16
            public float ChromaticAberrationStrength; // offset 20
            public float VignetteStrength;         // offset 24
            public float VignetteStart;            // offset 28
            public float VignetteEnd;              // offset 32
            public float PreRotationYaw;           // offset 36
            public float PreRotationPitch;         // offset 40
            public int GridSizePreset;             // offset 44
            public int GridColorIndex;             // offset 48
            public float _pad1;                    // offset 52
            public float _pad2;                    // offset 56
            public float _padAlignCamera;          // offset 60
            
            // Camera basis (48 bytes)
            public float CameraRightX;             // offset 64
            public float CameraRightY;             // offset 68
            public float CameraRightZ;             // offset 72
            public float _pad3;                    // offset 76
            public float CameraUpX;                // offset 80
            public float CameraUpY;                // offset 84
            public float CameraUpZ;                // offset 88
            public float _pad4;                    // offset 92
            public float CameraForwardX;           // offset 96
            public float CameraForwardY;           // offset 100
            public float CameraForwardZ;           // offset 104
            public float _pad5;                    // offset 108
            
            // Debug shapes (32 bytes)
            public int DebugShapesEnabled;         // offset 112
            public float _pad6;
            public float _pad7;
            public float _pad8;
            public float DebugCircleCenterX;       // offset 128
            public float DebugCircleCenterY;       // offset 132
            public float DebugCircleRadius;        // offset 136
            public float DebugCircleThickness;     // offset 140
            public float DebugBoxTopLeftX;         // offset 144
            public float DebugBoxTopLeftY;         // offset 148
            public float DebugBoxSizeX;            // offset 152
            public float DebugBoxSizeY;            // offset 156
            public float DebugBoxThickness;        // offset 160
            public float DebugShapeIntensity;      // offset 164
            public float _pad9;
            public float _pad10;
            
            // Selection animation (32 bytes) - reserved for future phases
            public float SelectionCircleCenterX;   // offset 176
            public float SelectionCircleCenterY;   // offset 180
            public float SelectionCircleT;         // offset 184
            public float SelectionCircleIntensity; // offset 188
            public float SelectionCircleThickness; // offset 192
            public float SelectionCircleRadius;    // offset 196
            public float BoxCenterX;               // offset 200
            public float BoxCenterY;               // offset 204
            public float BoxHalfSizeX;             // offset 208
            public float BoxHalfSizeY;             // offset 212
            public float BoxCornerRadius;          // offset 216
            public float BoxThickness;             // offset 220
            public float BoxT;                     // offset 224
            public float _pad11;
            
            // Text stub (16 bytes) - reserved for future phases
            public float TextOriginX;              // offset 232
            public float TextOriginY;              // offset 236
            public float TextAreaSizeX;            // offset 240
            public float TextAreaSizeY;            // offset 244
            public float SelectionTextT;           // offset 248
            public float _pad12;                   // offset 252
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldSetKartographerParams(ref KartographerParamsNative kartParams);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_RenderStarfieldCubemap([In] IntPtr[] targetTextures, int faceSize);


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