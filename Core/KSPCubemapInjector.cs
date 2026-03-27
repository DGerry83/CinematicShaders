using System;
using System.Reflection;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Injects starfield cubemap into KSP's GalaxyCubeControl for Parallax reflection capture.
    /// </summary>
    public static class KSPCubemapInjector
    {
        // Cached reflection info for Parallax SkyboxControl
        private static Type _skyboxControlType;
        private static FieldInfo _alreadyGeneratedField;
        private static bool _reflectionInitialized = false;
        
        // Face name mapping: XP/XN/YP/YN/ZP/ZN to CubemapFace enum
        private static readonly string[] FaceNames = { "XP", "XN", "YP", "YN", "ZP", "ZN" };
        private static readonly CubemapFace[] FaceIndices = 
        {
            CubemapFace.PositiveX,
            CubemapFace.NegativeX, 
            CubemapFace.PositiveY,
            CubemapFace.NegativeY,
            CubemapFace.PositiveZ,
            CubemapFace.NegativeZ
        };
        
        /// <summary>
        /// Injects a cubemap into GalaxyCubeControl's 6 face renderers.
        /// </summary>
        public static bool InjectCubemap(Cubemap cubemap)
        {
            if (cubemap == null)
            {
                Debug.LogError("[KSPCubemapInjector] Cannot inject null cubemap");
                return false;
            }
            
            // Find GalaxyCubeControl
            GalaxyCubeControl galaxyCube = GalaxyCubeControl.Instance;
            if (galaxyCube == null)
            {
                Debug.LogWarning("[KSPCubemapInjector] GalaxyCubeControl.Instance is null, retrying...");
                return false;
            }
            
            Debug.Log($"[KSPCubemapInjector] Injecting cubemap {cubemap.name} into GalaxyCubeControl");
            
            // Extract faces and inject into each renderer
            bool allFacesInjected = true;
            for (int i = 0; i < 6; i++)
            {
                if (!InjectFace(galaxyCube, FaceNames[i], FaceIndices[i], cubemap))
                {
                    allFacesInjected = false;
                }
            }
            
            if (allFacesInjected)
            {
                // Trigger Parallax re-extraction
                TriggerParallaxReextraction();
                
                return true;
            }
            else
            {
                Debug.LogWarning("[KSPCubemapInjector] Some faces failed to inject");
                return false;
            }
        }
        
        /// <summary>
        /// Injects a single cubemap face into the corresponding GalaxyCubeControl child renderer.
        /// </summary>
        private static bool InjectFace(GalaxyCubeControl galaxyCube, string faceName, CubemapFace face, Cubemap cubemap)
        {
            // Find the child renderer
            Transform faceTransform = galaxyCube.transform.Find(faceName);
            if (faceTransform == null)
            {
                Debug.LogError($"[KSPCubemapInjector] Could not find {faceName} child of GalaxyCubeControl");
                return false;
            }
            
            Renderer faceRenderer = faceTransform.GetComponent<Renderer>();
            if (faceRenderer == null)
            {
                Debug.LogError($"[KSPCubemapInjector] {faceName} has no Renderer component");
                return false;
            }
            
            // Create a Texture2D from the cubemap face
            Texture2D faceTexture = ExtractCubemapFace(cubemap, face);
            if (faceTexture == null)
            {
                Debug.LogError($"[KSPCubemapInjector] Failed to extract {face} from cubemap");
                return false;
            }
            
            // Apply to material
            faceRenderer.material.mainTexture = faceTexture;
            faceRenderer.material.SetTextureScale("_MainTex", new Vector2(1, 1));
            faceRenderer.material.SetTextureOffset("_MainTex", new Vector2(0, 0));
            
            // Per-face injection log removed for brevity
            return true;
        }
        
        /// <summary>
        /// Extracts a single face from a cubemap into a Texture2D.
        /// </summary>
        private static Texture2D ExtractCubemapFace(Cubemap cubemap, CubemapFace face)
        {
            try
            {
                int width = cubemap.width;
                int height = cubemap.height;
                
                // Create texture with same dimensions
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                
                // Copy pixels from cubemap face
                Color[] pixels = cubemap.GetPixels(face);
                tex.SetPixels(pixels);
                tex.Apply(false, false);
                
                return tex;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[KSPCubemapInjector] Error extracting cubemap face {face}: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Injects directly from RenderTextures using fast GPU copy (no CPU readback).
        /// This is much faster than going through Cubemap/ReadPixels/SetPixels.
        /// </summary>
        public static bool InjectFromRenderTextures(RenderTexture[] renderTextures)
        {
            if (renderTextures == null || renderTextures.Length != 6)
            {
                Debug.LogError("[KSPCubemapInjector] Invalid render textures array");
                return false;
            }
            
            GalaxyCubeControl galaxyCube = GalaxyCubeControl.Instance;
            if (galaxyCube == null)
            {
                Debug.LogWarning("[KSPCubemapInjector] GalaxyCubeControl.Instance is null");
                return false;
            }
            
            Debug.Log("[KSPCubemapInjector] Injecting from RenderTextures...");
            
            bool allFacesInjected = true;
            for (int i = 0; i < 6; i++)
            {
                if (!InjectRenderTexture(galaxyCube, FaceNames[i], renderTextures[i]))
                {
                    allFacesInjected = false;
                }
            }
            
            if (allFacesInjected)
            {
                TriggerParallaxReextraction();
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Injects a single RenderTexture using fast GPU→GPU copy.
        /// </summary>
        private static bool InjectRenderTexture(GalaxyCubeControl galaxyCube, string faceName, RenderTexture rt)
        {
            Transform faceTransform = galaxyCube.transform.Find(faceName);
            if (faceTransform == null)
            {
                Debug.LogError($"[KSPCubemapInjector] Could not find {faceName}");
                return false;
            }
            
            Renderer faceRenderer = faceTransform.GetComponent<Renderer>();
            if (faceRenderer == null)
            {
                Debug.LogError($"[KSPCubemapInjector] {faceName} has no Renderer");
                return false;
            }
            
            // Create destination Texture2D
            Texture2D faceTexture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            
            // Fast GPU→GPU copy using Graphics.CopyTexture
            // This avoids the CPU readback stall of ReadPixels/GetPixels
            Graphics.CopyTexture(rt, 0, 0, faceTexture, 0, 0);
            
            // Apply to material
            faceRenderer.material.mainTexture = faceTexture;
            faceRenderer.material.SetTextureScale("_MainTex", new Vector2(1, 1));
            faceRenderer.material.SetTextureOffset("_MainTex", new Vector2(0, 0));
            
            return true;
        }
        
        /// <summary>
        /// Triggers Parallax to re-extract the skybox on next scene change.
        /// Uses reflection to reset SkyboxControl.alreadyGenerated.
        /// </summary>
        private static void TriggerParallaxReextraction()
        {
            try
            {
                InitializeReflection();
                
                if (_alreadyGeneratedField != null)
                {
                    _alreadyGeneratedField.SetValue(null, false);
                    Debug.Log("[KSPCubemapInjector] Reset Parallax SkyboxControl.alreadyGenerated = false");
                    Debug.Log("[KSPCubemapInjector] Parallax will re-extract on next scene change");
                }
                else
                {
                    Debug.Log("[KSPCubemapInjector] Parallax not detected (SkyboxControl not found)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KSPCubemapInjector] Could not reset Parallax state: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Initializes reflection info for Parallax SkyboxControl.
        /// </summary>
        private static void InitializeReflection()
        {
            if (_reflectionInitialized) return;
            
            try
            {
                // Look for Parallax SkyboxControl type (ParallaxContinued for Kopernicus version)
                _skyboxControlType = Type.GetType("Parallax.Scaled_System.SkyboxControl, ParallaxContinued");
                if (_skyboxControlType == null)
                {
                    // Fallback to original Parallax
                    _skyboxControlType = Type.GetType("Parallax.Scaled_System.SkyboxControl, Parallax");
                }
                
                if (_skyboxControlType != null)
                {
                    _alreadyGeneratedField = _skyboxControlType.GetField("alreadyGenerated", 
                        BindingFlags.Static | BindingFlags.Public);
                    
                    if (_alreadyGeneratedField != null)
                    {
                        Debug.Log("[KSPCubemapInjector] Found Parallax SkyboxControl.alreadyGenerated field");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"[KSPCubemapInjector] Reflection initialization failed (Parallax may not be installed): {ex.Message}");
            }
            
            _reflectionInitialized = true;
        }
        
        /// <summary>
        /// Checks if Parallax is installed and accessible.
        /// </summary>
        public static bool IsParallaxInstalled()
        {
            InitializeReflection();
            return _skyboxControlType != null;
        }
    }
}
