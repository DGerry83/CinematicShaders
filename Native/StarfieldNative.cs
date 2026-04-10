using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using CinematicShaders.Native.Structs;

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

        // ============================================================================
        // Text System structs and imports (Phase 2 - Font Integration)
        // ============================================================================
        
        [StructLayout(LayoutKind.Sequential)]
        public struct GlyphData
        {
            public float PosX;
            public float PosY;
            public float SizeX;
            public float SizeY;
            public float UvX;
            public float UvY;
            public float UvW;
            public float UvH;
            public uint Color;
            public float Smoothing;
        }
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr CR_TextInit(IntPtr deviceSourceTexture, [MarshalAs(UnmanagedType.LPWStr)] string fontPath);
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_TextShutdown(IntPtr textSystem);
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_TextLayout(IntPtr textSystem, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, float fontSize, uint color);
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_TextGetBounds(IntPtr textSystem, out float outWidth, out float outHeight);
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_TextMeasure(IntPtr textSystem, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, float fontSize, out float outWidth, out float outHeight);
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr CR_TextGetAtlasSRV(IntPtr textSystem);
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr CR_TextGetGlyphPtr(IntPtr textSystem);
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_TextGetGlyphCount(IntPtr textSystem);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_TextExportAtlas(IntPtr textSystem, [MarshalAs(UnmanagedType.LPStr)] string filename);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_TextExportGlyphDebug(IntPtr textSystem, [MarshalAs(UnmanagedType.LPStr)] string baseFilename);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_TextDispatch(
            IntPtr textSystem,
            IntPtr outputTexture,
            int glyphCount,
            int outputWidth,
            int outputHeight);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_TextLayoutEx(
            IntPtr textSystem,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
            float fontSize,
            uint color,
            float originX,
            float originY,
            float lineSpacing,
            float aspectRatio);  // NEW: aspect ratio parameter

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_TextDispatchEx(
            IntPtr textSystem,
            IntPtr outputTexture,
            int glyphCount,
            int outputWidth,
            int outputHeight,
            int clearOutput);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_SetTextTexture(IntPtr texture);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_SetVesselTargetTextTexture(IntPtr texture);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_SetGridLabelTexture(int slot, IntPtr texture);
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_ClearGridLabelSlot(int slot);

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

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_StarfieldGetCatalogData(StarDataNative[] outBuffer, int maxCount);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldLoadCatalog(StarDataNative[] buffer, int count, int heroCount);

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

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern byte CR_NavballTexturesNeedReupload();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldSetDimming(float sunGlareDimming, float planetaryDimming);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldSetKartographerEnabled(byte enabled);

        // ============================================================================
        // Convenience wrappers (C#-friendly versions)
        // ============================================================================
        
        public static bool CatalogNeedsReload()
        {
            return CR_StarfieldCatalogNeedsReload() != 0;
        }
        
        public static void InvalidateResources()
        {
            CR_StarfieldInvalidateResources();
        }
        
        public static int GetCatalogSize()
        {
            return CR_StarfieldGetCatalogSize();
        }
        
        public static int GetHeroCount()
        {
            return CR_StarfieldGetHeroCount();
        }
        
        public static StarDataNative[] GetCatalogData(int count)
        {
            if (count <= 0) return new StarDataNative[0];
            
            var buffer = new StarDataNative[count];
            int actualCount = CR_StarfieldGetCatalogData(buffer, count);
            
            if (actualCount != count)
            {
                Debug.LogWarning($"[StarfieldNative] Catalog size mismatch: expected {count}, got {actualCount}");
                // Resize array to actual count
                System.Array.Resize(ref buffer, actualCount);
            }
            
            return buffer;
        }
        
        public static void LoadCatalog(StarDataNative[] stars, int heroCount)
        {
            if (stars == null || stars.Length == 0)
            {
                Debug.LogWarning("[StarfieldNative] Cannot load null or empty catalog");
                return;
            }
            CR_StarfieldLoadCatalog(stars, stars.Length, heroCount);
        }



        // Last params cache for incremental updates
        public static KartographerParamsNative LastKartographerParams;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldSetKartographerParams(ref KartographerParamsNative parameters);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_RenderStarfieldCubemap(IntPtr[] targetTextures, int faceSize);

        // ============================================================================
        // Navball Icon Texture Array (Phase 4d)
        // ============================================================================
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_SetNavballIconTextures(
            [In] IntPtr[] sourceTextures, int width, int height);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_SetPointingIconTexture(IntPtr texture);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_SetManeuverTextTexture(IntPtr texture);
        
        /// <summary>
        /// STUB: Sets the box outline for click zone highlighting.
        /// This function is not yet implemented in the native plugin.
        /// Calls are silently ignored to prevent EntryPointNotFoundException.
        /// TODO: Implement proper native function in C++ plugin.
        /// See: ReferenceNotes/StarConsoleLayer3Debug/BOX_OUTLINE_FEATURE_SPEC.md
        /// </summary>
        /// <param name="enabled">1 to enable, 0 to disable</param>
        /// <param name="xMin">Left coordinate (UV space)</param>
        /// <param name="yMin">Top coordinate (UV space)</param>
        /// <param name="xMax">Right coordinate (UV space)</param>
        /// <param name="yMax">Bottom coordinate (UV space)</param>
        public static void CR_SetBoxOutline(int enabled, float xMin, float yMin, float xMax, float yMax)
        {
            // STUB IMPLEMENTATION
            // Native function not yet built. Feature deferred to dedicated implementation session.
            // This prevents EntryPointNotFoundException while the feature is pending.
        }
        
        /// <summary>
        /// Uploads 7 navball icon textures to the GPU as a texture array.
        /// </summary>
        /// <param name="textures">Array of 7 Texture2D objects (must be R8G8B8A8 format)</param>
        /// <param name="width">Texture width (must be same for all)</param>
        /// <param name="height">Texture height (must be same for all)</param>
        /// <returns>True if successful</returns>
        public static bool SetNavballIconTextures(Texture2D[] textures, int width, int height)
        {
            if (textures == null || textures.Length != 7) return false;
            
            IntPtr[] nativePtrs = new IntPtr[7];
            for (int i = 0; i < 7; i++) {
                if (textures[i] == null) return false;
                nativePtrs[i] = textures[i].GetNativeTexturePtr();
            }
            
            return CR_SetNavballIconTextures(nativePtrs, width, height) == 0;
        }
    }
}
