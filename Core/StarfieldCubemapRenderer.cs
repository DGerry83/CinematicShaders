using System;
using System.IO;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Renders the procedural starfield to a cubemap for reflection purposes.
    /// Uses native C++ rendering for precise control.
    /// </summary>
    public static class StarfieldCubemapRenderer
    {
        // Configuration
        public const int CUBEMAP_SIZE = 1024;

        // Debug output folder (kept for compatibility with old methods)
        private static readonly string DEBUG_FOLDER = Path.Combine(
            KSPUtil.ApplicationRootPath, 
            "GameData", 
            "CinematicShaders", 
            "CubemapDebug");

        /// <summary>
        /// Renders the current starfield directly to KSP skybox using native C++ rendering.
        /// Skips intermediate Cubemap/Texture2D copies for performance.
        /// </summary>
        /// <returns>True if successful, false otherwise.</returns>
        public static bool RenderAndInjectCubemap()
        {
            if (!StarfieldSettings.EnableStarfield)
            {
                Debug.Log("[StarfieldCubemapRenderer] Starfield disabled, skipping cubemap render");
                return false;
            }

            Stopwatch renderTimer = new Stopwatch();

            try
            {
                Debug.Log("[StarfieldCubemapRenderer] Starting native cubemap render...");
                renderTimer.Start();

                // Create 6 RenderTextures for native rendering
                RenderTexture[] renderTextures = new RenderTexture[6];
                IntPtr[] faceTextures = new IntPtr[6];

                for (int i = 0; i < 6; i++)
                {
                    RenderTextureDescriptor rtDesc = new RenderTextureDescriptor(CUBEMAP_SIZE, CUBEMAP_SIZE, RenderTextureFormat.ARGB32, 0);
                    rtDesc.dimension = TextureDimension.Tex2D;
                    rtDesc.msaaSamples = 1;
                    rtDesc.useMipMap = false;
                    rtDesc.autoGenerateMips = false;
                    rtDesc.bindMS = false;
                    
                    renderTextures[i] = new RenderTexture(rtDesc);
                    renderTextures[i].Create();
                    
                    // Clear to ensure texture is created
                    // Using try/finally for consistency with other RT operations
                    RenderTexture prevActive = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = renderTextures[i];
                        GL.Clear(true, true, Color.black);
                    }
                    finally
                    {
                        RenderTexture.active = prevActive;
                    }
                    
                    faceTextures[i] = renderTextures[i].GetNativeTexturePtr();
                }

                // Call native function to render all faces
                int result = Native.StarfieldNative.CR_RenderStarfieldCubemap(faceTextures, CUBEMAP_SIZE);

                renderTimer.Stop();
                long elapsedMs = renderTimer.ElapsedMilliseconds;

                if (result == -2) // Device not initialized
                {
                    Debug.LogWarning("[StarfieldCubemapRenderer] Device not ready, will retry on next trigger");
                    CleanupRenderTextures(renderTextures);
                    return false;
                }
                else if (result != 0)
                {
                    Debug.LogError($"[StarfieldCubemapRenderer] Native render failed with code: {result}");
                    CleanupRenderTextures(renderTextures);
                    return false;
                }

                Debug.Log($"[StarfieldCubemapRenderer] Native render complete: {elapsedMs}ms");

                // Inject directly from RenderTextures (no intermediate copies)
                bool injected = KSPCubemapInjector.InjectFromRenderTextures(renderTextures);
                
                // Cleanup
                CleanupRenderTextures(renderTextures);
                
                if (injected)
                {
                    Debug.Log("[StarfieldCubemapRenderer] Cubemap injected into KSP skybox");
                    return true;
                }
                else
                {
                    Debug.LogWarning("[StarfieldCubemapRenderer] Failed to inject cubemap, will retry on next trigger");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StarfieldCubemapRenderer] Error rendering cubemap: {ex}");
                return false;
            }
        }
        
        private static void CleanupRenderTextures(RenderTexture[] renderTextures)
        {
            for (int i = 0; i < 6; i++)
            {
                if (renderTextures[i] != null)
                {
                    UnityEngine.Object.Destroy(renderTextures[i]);
                }
            }
        }

        /// <summary>
        /// Copies a render texture to a cubemap face.
        /// </summary>
        private static void CopyRenderTextureToCubemapFace(RenderTexture rt, Cubemap cubemap, CubemapFace face)
        {
            RenderTexture.active = rt;
            
            Texture2D tempTex = new Texture2D(CUBEMAP_SIZE, CUBEMAP_SIZE, TextureFormat.RGBA32, false);
            tempTex.ReadPixels(new Rect(0, 0, CUBEMAP_SIZE, CUBEMAP_SIZE), 0, 0, false);
            tempTex.Apply();

            cubemap.SetPixels(tempTex.GetPixels(), face);

            UnityEngine.Object.Destroy(tempTex);
            RenderTexture.active = null;
        }

        /// <summary>
        /// Exports the cubemap faces to disk for debug/verification purposes.
        /// </summary>
        public static void ExportCubemapForDebug(Cubemap cubemap, string label)
        {
            try
            {
                // Ensure debug folder exists
                if (!Directory.Exists(DEBUG_FOLDER))
                {
                    Directory.CreateDirectory(DEBUG_FOLDER);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string baseName = $"{label}_{timestamp}";

                // Export individual faces
                string[] faceNames = { "Right_PX", "Left_NX", "Top_PY", "Bottom_NY", "Front_PZ", "Back_NZ" };
                CubemapFace[] faces = {
                    CubemapFace.PositiveX, CubemapFace.NegativeX,
                    CubemapFace.PositiveY, CubemapFace.NegativeY,
                    CubemapFace.PositiveZ, CubemapFace.NegativeZ
                };

                for (int i = 0; i < 6; i++)
                {
                    Texture2D faceTex = ExtractFaceToTexture2D(cubemap, faces[i]);
                    byte[] png = faceTex.EncodeToPNG();
                    string path = Path.Combine(DEBUG_FOLDER, $"{baseName}_{faceNames[i]}.png");
                    File.WriteAllBytes(path, png);
                    UnityEngine.Object.Destroy(faceTex);

                    Debug.Log($"[StarfieldCubemapRenderer] Exported debug face: {path}");
                }

                // Export cross layout
                Texture2D crossLayout = CreateCrossLayout(cubemap);
                byte[] crossPng = crossLayout.EncodeToPNG();
                string crossPath = Path.Combine(DEBUG_FOLDER, $"{baseName}_cross.png");
                File.WriteAllBytes(crossPath, crossPng);
                UnityEngine.Object.Destroy(crossLayout);

                Debug.Log($"[StarfieldCubemapRenderer] Exported debug cross layout: {crossPath}");
                Debug.Log($"[StarfieldCubemapRenderer] All debug files saved to: {DEBUG_FOLDER}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StarfieldCubemapRenderer] Error exporting debug: {ex}");
            }
        }

        /// <summary>
        /// Extracts a single cubemap face to a Texture2D.
        /// </summary>
        private static Texture2D ExtractFaceToTexture2D(Cubemap cubemap, CubemapFace face)
        {
            Texture2D tex = new Texture2D(cubemap.width, cubemap.height, TextureFormat.RGBA32, false);
            Color[] pixels = cubemap.GetPixels(face);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Creates a cross-shaped layout of all 6 cubemap faces for easy viewing.
        /// Layout:
        ///     [PY]
        /// [NX][PZ][PX][NZ]
        ///     [NY]
        /// </summary>
        private static Texture2D CreateCrossLayout(Cubemap cubemap)
        {
            int size = cubemap.width;
            int crossWidth = size * 4;
            int crossHeight = size * 3;

            Texture2D cross = new Texture2D(crossWidth, crossHeight, TextureFormat.RGBA32, false);

            // Fill with black background
            Color[] blackPixels = new Color[crossWidth * crossHeight];
            for (int i = 0; i < blackPixels.Length; i++) blackPixels[i] = Color.black;
            cross.SetPixels(blackPixels);

            // Copy faces to cross layout
            // Top center: +Y
            CopyFaceToCross(cross, cubemap, CubemapFace.PositiveY, size * 1, size * 2, size);
            // Middle row: -X, +Z, +X, -Z
            CopyFaceToCross(cross, cubemap, CubemapFace.NegativeX, size * 0, size * 1, size);
            CopyFaceToCross(cross, cubemap, CubemapFace.PositiveZ, size * 1, size * 1, size);
            CopyFaceToCross(cross, cubemap, CubemapFace.PositiveX, size * 2, size * 1, size);
            CopyFaceToCross(cross, cubemap, CubemapFace.NegativeZ, size * 3, size * 1, size);
            // Bottom center: -Y
            CopyFaceToCross(cross, cubemap, CubemapFace.NegativeY, size * 1, size * 0, size);

            cross.Apply();
            return cross;
        }

        /// <summary>
        /// Copies a cubemap face to a position in the cross layout texture.
        /// </summary>
        private static void CopyFaceToCross(Texture2D cross, Cubemap cubemap, CubemapFace face, int xOffset, int yOffset, int faceSize)
        {
            Color[] facePixels = cubemap.GetPixels(face);

            for (int y = 0; y < faceSize; y++)
            {
                for (int x = 0; x < faceSize; x++)
                {
                    int faceIndex = y * faceSize + x;
                    int crossX = xOffset + x;
                    int crossY = yOffset + y;
                    cross.SetPixel(crossX, crossY, facePixels[faceIndex]);
                }
            }
        }
    }
}
