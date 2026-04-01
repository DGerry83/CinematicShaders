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
        public static extern int CR_TextLayoutEx(
            IntPtr textSystem,
            [MarshalAs(UnmanagedType.LPStr)] string text,
            float fontSize,
            uint color,
            float originX,
            float originY,
            float lineSpacing);

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
        // Total size: 608 bytes (16 × 38)
        // Generated from ReferenceNotes/tools/generate_struct.py
        // ============================================================================
        
        [StructLayout(LayoutKind.Sequential, Pack = 16)]
        public struct KartographerParamsNative
        {
            // Field offsets (bytes):
            //     0: ResolutionX (float)
            //     4: ResolutionY (float)
            //     8: Time (float)
            //    12: GridIntensity (float)
            //    16: GridThickness (float)
            //    20: ChromaticAberrationStrength (float)
            //    24: VignetteStrength (float)
            //    28: VignetteStart (float)
            //    32: VignetteEnd (float)
            //    36: PreRotationYaw (float)
            //    40: PreRotationPitch (float)
            //    44: GridSizePreset (int)
            //    48: GridColorIndex (int)
            //    52: _pad1 (float)
            //    56: _pad2 (float)
            //    60: _padAlignCamera (float)
            //    64: CameraRightX (float)
            //    68: CameraRightY (float)
            //    72: CameraRightZ (float)
            //    76: _pad3 (float)
            //    80: CameraUpX (float)
            //    84: CameraUpY (float)
            //    88: CameraUpZ (float)
            //    92: _pad4 (float)
            //    96: CameraForwardX (float)
            //   100: CameraForwardY (float)
            //   104: CameraForwardZ (float)
            //   108: _pad5 (float)
            //   112: DebugShapesEnabled (int)
            //   116: _pad6 (float)
            //   120: _pad7 (float)
            //   124: _pad8 (float)
            //   128: DebugCircleCenterX (float)
            //   132: DebugCircleCenterY (float)
            //   136: DebugCircleRadius (float)
            //   140: DebugCircleThickness (float)
            //   144: DebugBoxTopLeftX (float)
            //   148: DebugBoxTopLeftY (float)
            //   152: DebugBoxSizeX (float)
            //   156: DebugBoxSizeY (float)
            //   160: DebugBoxThickness (float)
            //   164: DebugShapeIntensity (float)
            //   168: _pad9 (float)
            //   172: FocalLength (float)
            //   176: SelectionCircleEnabled (int)
            //   180: SelectionStarHash (float)
            //   184: _padSelection2 (float)
            //   188: _padSelection3 (float)
            //   192: SelectionCircleCenterX (float)
            //   196: SelectionCircleCenterY (float)
            //   200: SelectionCircleT (float)
            //   204: SelectionCircleIntensity (float)
            //   208: SelectionCircleThickness (float)
            //   212: SelectionCircleRadius (float)
            //   216: _padSelection4 (float)
            //   220: _padSelection5 (float)
            //   224: _padSelection6 (float)
            //   228: _padSelection7 (float)
            //   232: TextOriginX (float)
            //   236: TextOriginY (float)
            //   240: TextAreaSizeX (float)
            //   244: TextAreaSizeY (float)
            //   248: SelectionTextT (float)
            //   252: GridLabelEnabledMask (uint)
            //   256: _padGridMask1 (float)
            //   260: _padGridMask2 (float)
            //   264: _padGridMask3 (float)
            //   268: _padGridMask4 (float)
            //   272: GridLabel0_PosTangentX (float4)
            //   288: GridLabel0_TangentY (float4)
            //   304: GridLabel1_PosTangentX (float4)
            //   320: GridLabel1_TangentY (float4)
            //   336: GridLabel2_PosTangentX (float4)
            //   352: GridLabel2_TangentY (float4)
            //   368: GridLabel3_PosTangentX (float4)
            //   384: GridLabel3_TangentY (float4)
            //   400: GridLabel4_PosTangentX (float4)
            //   416: GridLabel4_TangentY (float4)
            //   432: GridLabel5_PosTangentX (float4)
            //   448: GridLabel5_TangentY (float4)
            //   464: GridLabel6_PosTangentX (float4)
            //   480: GridLabel6_TangentY (float4)
            //   496: GridLabel7_PosTangentX (float4)
            //   512: GridLabel7_TangentY (float4)
            //   528: GridLabelDebugMask (uint)
            //   532: LabelIntensity0 (float)
            //   536: LabelIntensity1 (float)
            //   540: LabelIntensity2 (float)
            //   544: LabelIntensity3 (float)
            //   548: LabelIntensity4 (float)
            //   552: LabelIntensity5 (float)
            //   556: LabelIntensity6 (float)
            //   560: LabelIntensity7 (float)
            //   564: LabelColor0 (uint)
            //   568: LabelColor1 (uint)
            //   572: LabelColor2 (uint)
            //   576: LabelColor3 (uint)
            //   580: LabelColor4 (uint)
            //   584: LabelColor5 (uint)
            //   588: LabelColor6 (uint)
            //   592: LabelColor7 (uint)

            public float ResolutionX;
            public float ResolutionY;
            public float Time;
            public float GridIntensity;
            public float GridThickness;
            public float ChromaticAberrationStrength;
            public float VignetteStrength;
            public float VignetteStart;
            public float VignetteEnd;
            public float PreRotationYaw;
            public float PreRotationPitch;
            public int GridSizePreset;
            public int GridColorIndex;
            public float _pad1;
            public float _pad2;
            public float _padAlignCamera;
            public float CameraRightX;
            public float CameraRightY;
            public float CameraRightZ;
            public float _pad3;
            public float CameraUpX;
            public float CameraUpY;
            public float CameraUpZ;
            public float _pad4;
            public float CameraForwardX;
            public float CameraForwardY;
            public float CameraForwardZ;
            public float _pad5;
            public int DebugShapesEnabled;
            public float _pad6;
            public float _pad7;
            public float _pad8;
            public float DebugCircleCenterX;
            public float DebugCircleCenterY;
            public float DebugCircleRadius;
            public float DebugCircleThickness;
            public float DebugBoxTopLeftX;
            public float DebugBoxTopLeftY;
            public float DebugBoxSizeX;
            public float DebugBoxSizeY;
            public float DebugBoxThickness;
            public float DebugShapeIntensity;
            public float _pad9;
            public float FocalLength;
            public int SelectionCircleEnabled;
            public float SelectionStarHash;
            public float _padSelection2;
            public float _padSelection3;
            public float SelectionCircleCenterX;
            public float SelectionCircleCenterY;
            public float SelectionCircleT;
            public float SelectionCircleIntensity;
            public float SelectionCircleThickness;
            public float SelectionCircleRadius;
            public float _padSelection4;
            public float _padSelection5;
            public float _padSelection6;
            public float _padSelection7;
            public float TextOriginX;
            public float TextOriginY;
            public float TextAreaSizeX;
            public float TextAreaSizeY;
            public float SelectionTextT;
            public uint GridLabelEnabledMask;
            public float _padGridMask1;
            public float _padGridMask2;
            public float _padGridMask3;
            public float _padGridMask4;
            public Vector4 GridLabel0_PosTangentX;
            public Vector4 GridLabel0_TangentY;
            public Vector4 GridLabel1_PosTangentX;
            public Vector4 GridLabel1_TangentY;
            public Vector4 GridLabel2_PosTangentX;
            public Vector4 GridLabel2_TangentY;
            public Vector4 GridLabel3_PosTangentX;
            public Vector4 GridLabel3_TangentY;
            public Vector4 GridLabel4_PosTangentX;
            public Vector4 GridLabel4_TangentY;
            public Vector4 GridLabel5_PosTangentX;
            public Vector4 GridLabel5_TangentY;
            public Vector4 GridLabel6_PosTangentX;
            public Vector4 GridLabel6_TangentY;
            public Vector4 GridLabel7_PosTangentX;
            public Vector4 GridLabel7_TangentY;
            public uint GridLabelDebugMask;
            public float LabelIntensity0;
            public float LabelIntensity1;
            public float LabelIntensity2;
            public float LabelIntensity3;
            public float LabelIntensity4;
            public float LabelIntensity5;
            public float LabelIntensity6;
            public float LabelIntensity7;
            public uint LabelColor0;
            public uint LabelColor1;
            public uint LabelColor2;
            public uint LabelColor3;
            public uint LabelColor4;
            public uint LabelColor5;
            public uint LabelColor6;
            public uint LabelColor7;
            
            // Vessel Target Selector - separate from Star Selector (96 bytes)
            public int VesselTargetEnabled;
            public float VesselTargetHash;
            public float _padVessel1;
            public float _padVessel2;
            public float VesselTargetCircleCenterX;
            public float VesselTargetCircleCenterY;
            public float VesselTargetCircleT;
            public float VesselTargetCircleIntensity;
            public float VesselTargetCircleThickness;
            public float VesselTargetCircleRadius;
            public float _padVessel3;
            public float _padVessel4;
            public float _padVessel5;
            public float _padVessel6;
            public float VesselTargetBoxTopLeftX;
            public float VesselTargetBoxTopLeftY;
            public float VesselTargetBoxSizeX;
            public float VesselTargetBoxSizeY;
            public float VesselTargetBoxThickness;
            public float _padVessel7;
            public float VesselTargetTextOriginX;
            public float VesselTargetTextOriginY;
            public float VesselTargetTextAreaSizeX;
            public float VesselTargetTextAreaSizeY;
            public float VesselTargetTextT;
            
            // Animated label intensity for type-on animation systems
            public float AnimatedLabelIntensity;
            public float _padAnimated1;
            public float _padAnimated2;
            public float _padAnimated3;
        }

        // Last params cache for incremental updates
        public static KartographerParamsNative LastKartographerParams;

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void CR_StarfieldSetKartographerParams(ref KartographerParamsNative parameters);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int CR_RenderStarfieldCubemap(IntPtr[] targetTextures, int faceSize);
    }
}
