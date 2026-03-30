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
        public static extern int CR_TextLayout(IntPtr textSystem, [MarshalAs(UnmanagedType.LPStr)] string text, float fontSize, uint color);
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_TextGetBounds(IntPtr textSystem, out float outWidth, out float outHeight);
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_TextMeasure(IntPtr textSystem, [MarshalAs(UnmanagedType.LPStr)] string text, float fontSize, out float outWidth, out float outHeight);
        
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
        public static extern void CR_SetTextTexture(IntPtr texture);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_SetGridLabelTexture(int slot, IntPtr texture);

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

        // ============================================================================
        // Kartographer Parameters - 8 Label Support (Phase 2)
        // Total size: 544 bytes (34 × 16)
        // ============================================================================
        
        [StructLayout(LayoutKind.Sequential)]
        public struct KartographerParamsNative
        {
            // Base grid params (64 bytes) - offsets 0-63
            public float ResolutionX;              // offset 0
            public float ResolutionY;              // offset 4
            public float Time;                     // offset 8
            public float GridIntensity;            // offset 12
            public float GridThickness;            // offset 16
            public float ChromaticAberrationStrength;  // offset 20
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
            
            // Camera basis (48 bytes) - offsets 64-111
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
            
            // Debug shapes (32 bytes) - offsets 112-143
            public int DebugShapesEnabled;         // offset 112
            public float _pad6;                    // offset 116
            public float _pad7;                    // offset 120
            public float _pad8;                    // offset 124
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
            public float _pad9;                    // offset 168
            public float FocalLength;              // offset 172
            
            // Selection circle (32 bytes) - offsets 176-207
            public int SelectionCircleEnabled;     // offset 176
            public float SelectionStarHash;        // offset 180
            public float _padSelection2;           // offset 184
            public float _padSelection3;           // offset 188
            public float SelectionCircleCenterX;   // offset 192
            public float SelectionCircleCenterY;   // offset 196
            public float SelectionCircleT;         // offset 200
            public float SelectionCircleIntensity; // offset 204
            public float SelectionCircleThickness; // offset 208
            public float SelectionCircleRadius;    // offset 212
            public float _padSelection4;           // offset 216
            public float _padSelection5;           // offset 220
            public float _padSelection6;           // offset 224
            public float _padSelection7;           // offset 228
            
            // Text stub (16 bytes) - offsets 232-247
            public float TextOriginX;              // offset 232
            public float TextOriginY;              // offset 236
            public float TextAreaSizeX;            // offset 240
            public float TextAreaSizeY;            // offset 244
            public float SelectionTextT;           // offset 248
            
            // Grid Labels (8 labels) - offsets 252-543
            // Bitmask for enabled labels (bit 0 = label 0, bit 1 = label 1, etc.)
            public uint GridLabelEnabledMask;      // offset 252
            public float _padGridMask1;            // offset 256
            public float _padGridMask2;            // offset 260
            public float _padGridMask3;            // offset 264
            
            // Label 0 (32 bytes) - offsets 268-299
            public float GridLabel0_PosX;          // offset 268
            public float GridLabel0_PosY;          // offset 272
            public float GridLabel0_PosZ;          // offset 276
            public float GridLabel0_SizeX;         // offset 280
            public float GridLabel0_TangentX;      // offset 284
            public float GridLabel0_TangentY;      // offset 288
            public float GridLabel0_TangentZ;      // offset 292
            public float GridLabel0_SizeY;         // offset 296
            
            // Label 1 (32 bytes) - offsets 300-331
            public float GridLabel1_PosX;          // offset 300
            public float GridLabel1_PosY;          // offset 304
            public float GridLabel1_PosZ;          // offset 308
            public float GridLabel1_SizeX;         // offset 312
            public float GridLabel1_TangentX;      // offset 316
            public float GridLabel1_TangentY;      // offset 320
            public float GridLabel1_TangentZ;      // offset 324
            public float GridLabel1_SizeY;         // offset 328
            
            // Label 2 (32 bytes) - offsets 332-363
            public float GridLabel2_PosX;          // offset 332
            public float GridLabel2_PosY;          // offset 336
            public float GridLabel2_PosZ;          // offset 340
            public float GridLabel2_SizeX;         // offset 344
            public float GridLabel2_TangentX;      // offset 348
            public float GridLabel2_TangentY;      // offset 352
            public float GridLabel2_TangentZ;      // offset 356
            public float GridLabel2_SizeY;         // offset 360
            
            // Label 3 (32 bytes) - offsets 364-395
            public float GridLabel3_PosX;          // offset 364
            public float GridLabel3_PosY;          // offset 368
            public float GridLabel3_PosZ;          // offset 372
            public float GridLabel3_SizeX;         // offset 376
            public float GridLabel3_TangentX;      // offset 380
            public float GridLabel3_TangentY;      // offset 384
            public float GridLabel3_TangentZ;      // offset 388
            public float GridLabel3_SizeY;         // offset 392
            
            // Label 4 (32 bytes) - offsets 396-427
            public float GridLabel4_PosX;          // offset 396
            public float GridLabel4_PosY;          // offset 400
            public float GridLabel4_PosZ;          // offset 404
            public float GridLabel4_SizeX;         // offset 408
            public float GridLabel4_TangentX;      // offset 412
            public float GridLabel4_TangentY;      // offset 416
            public float GridLabel4_TangentZ;      // offset 420
            public float GridLabel4_SizeY;         // offset 424
            
            // Label 5 (32 bytes) - offsets 428-459
            public float GridLabel5_PosX;          // offset 428
            public float GridLabel5_PosY;          // offset 432
            public float GridLabel5_PosZ;          // offset 436
            public float GridLabel5_SizeX;         // offset 440
            public float GridLabel5_TangentX;      // offset 444
            public float GridLabel5_TangentY;      // offset 448
            public float GridLabel5_TangentZ;      // offset 452
            public float GridLabel5_SizeY;         // offset 456
            
            // Label 6 (32 bytes) - offsets 460-491
            public float GridLabel6_PosX;          // offset 460
            public float GridLabel6_PosY;          // offset 464
            public float GridLabel6_PosZ;          // offset 468
            public float GridLabel6_SizeX;         // offset 472
            public float GridLabel6_TangentX;      // offset 476
            public float GridLabel6_TangentY;      // offset 480
            public float GridLabel6_TangentZ;      // offset 484
            public float GridLabel6_SizeY;         // offset 488
            
            // Label 7 (32 bytes) - offsets 492-523
            public float GridLabel7_PosX;          // offset 492
            public float GridLabel7_PosY;          // offset 496
            public float GridLabel7_PosZ;          // offset 500
            public float GridLabel7_SizeX;         // offset 504
            public float GridLabel7_TangentX;      // offset 508
            public float GridLabel7_TangentY;      // offset 512
            public float GridLabel7_TangentZ;      // offset 516
            public float GridLabel7_SizeY;         // offset 520
            
            // Final padding to reach 544 bytes (Label 7 ends at 524, need 20 more bytes)
            public float _padEnd1;                 // offset 524
            public float _padEnd2;                 // offset 528
            public float _padEnd3;                 // offset 532
            public float _padEnd4;                 // offset 536
            public float _padEnd5;                 // offset 540
        }

        // Last params cache for incremental updates
        public static KartographerParamsNative LastKartographerParams;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldSetKartographerParams(ref KartographerParamsNative parameters);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_RenderStarfieldCubemap(IntPtr[] targetTextures, int faceSize);
    }
}
