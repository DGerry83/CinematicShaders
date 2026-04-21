using System;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Schedules cubemap generation in response to various trigger events.
    /// Handles scene transition awareness and queuing.
    /// </summary>
    public static class CubemapGenerationScheduler
    {
        // State tracking
        private static bool _hasPendingUpdate = false;
        private static bool _isGenerating = false;
        
        // Async cubemap render state (Fix 4)
        internal static bool _cubemapRenderPending = false;
        internal static RenderTexture[] _pendingRenderTextures;
        
        /// <summary>
        /// Requests a cubemap update. Called by various trigger events.
        /// </summary>
        public static void RequestCubemapUpdate()
        {
            if (_isGenerating)
            {
                Debug.Log("[CubemapGenerationScheduler] Generation already in progress, queueing update");
                _hasPendingUpdate = true;
                return;
            }
            
            // Check if we're in a valid game state
            if (!IsValidGameState())
            {
                Debug.Log("[CubemapGenerationScheduler] Not in valid game state, queueing for next scene");
                _hasPendingUpdate = true;
                return;
            }
            
            // Perform the update
            PerformUpdate();
        }
        
        /// <summary>
        /// Called when a scene loads to process any queued updates.
        /// </summary>
        public static void OnSceneLoad()
        {
            if (_hasPendingUpdate)
            {
                Debug.Log("[CubemapGenerationScheduler] Processing queued update on scene load");
                _hasPendingUpdate = false;
                PerformUpdate();
            }
        }
        
        /// <summary>
        /// Performs the actual cubemap generation and injection.
        /// FIX 4: Now stages an async native render. Completion is polled via CheckCubemapCompletion.
        /// </summary>
        private static void PerformUpdate()
        {
            if (_isGenerating)
            {
                Debug.LogWarning("[CubemapGenerationScheduler] Already generating, skipping duplicate request");
                return;
            }
            
            _isGenerating = true;
            
            try
            {
                Debug.Log("[CubemapGenerationScheduler] Starting cubemap generation...");
                
                // Stage async native render
                bool staged = StarfieldCubemapRenderer.RenderAndInjectCubemap();
                
                if (_cubemapRenderPending)
                {
                    // Async render successfully staged; completion handled by polling
                    Debug.Log("[CubemapGenerationScheduler] Cubemap render staged asynchronously");
                }
                else if (staged)
                {
                    // Synchronous completion (fallback / old path)
                    Debug.Log("[CubemapGenerationScheduler] Cubemap generation complete");
                    _isGenerating = false;
                }
                else
                {
                    // Actual failure
                    Debug.LogWarning("[CubemapGenerationScheduler] Cubemap generation failed, will retry on next trigger");
                    _hasPendingUpdate = true;
                    _isGenerating = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CubemapGenerationScheduler] Error during cubemap generation: {ex}");
                _hasPendingUpdate = true;
                _isGenerating = false;
            }
        }
        
        /// <summary>
        /// Polls native cubemap render completion. Called every frame from CinematicShadersAddon.Update().
        /// FIX 4: Completes the async render by generating mips, injecting, and disposing textures.
        /// </summary>
        public static void CheckCubemapCompletion()
        {
            if (!_cubemapRenderPending) return;
            
            int status = Native.StarfieldNative.CR_CubemapRenderStatus();
            
            if (status == 1)
            {
                // Still running
                return;
            }
            
            if (status == 0)
            {
                // Success — generate mips and inject
                try
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (_pendingRenderTextures[i] != null)
                        {
                            _pendingRenderTextures[i].GenerateMips();
                        }
                    }
                    Debug.Log("[CubemapGenerationScheduler] Mipmaps generated for cubemap faces");
                    
                    bool injected = KSPCubemapInjector.InjectFromRenderTextures(_pendingRenderTextures);
                    if (injected)
                    {
                        Debug.Log("[CubemapGenerationScheduler] Cubemap injected into KSP skybox");
                    }
                    else
                    {
                        Debug.LogWarning("[CubemapGenerationScheduler] Failed to inject cubemap, will retry on next trigger");
                        _hasPendingUpdate = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[CubemapGenerationScheduler] Error completing cubemap: {ex}");
                    _hasPendingUpdate = true;
                }
            }
            else if (status < 0)
            {
                // Error or no job
                Debug.LogError($"[CubemapGenerationScheduler] Cubemap render failed with status: {status}");
                _hasPendingUpdate = true;
            }
            
            // Dispose textures regardless of success/failure
            if (_pendingRenderTextures != null)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (_pendingRenderTextures[i] != null)
                    {
                        UnityEngine.Object.Destroy(_pendingRenderTextures[i]);
                        _pendingRenderTextures[i] = null;
                    }
                }
                _pendingRenderTextures = null;
            }
            
            _cubemapRenderPending = false;
            _isGenerating = false;
        }
        
        /// <summary>
        /// Checks if we're in a valid game state for cubemap generation.
        /// </summary>
        private static bool IsValidGameState()
        {
            // Need the starfield to be initialized
            if (!StarfieldSettings.EnableStarfield)
            {
                return false;
            }
            
            // Don't generate during scene transitions
            if (HighLogic.LoadedScene == GameScenes.LOADING || 
                HighLogic.LoadedScene == GameScenes.LOADINGBUFFER)
            {
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Trigger: Called after catalog load completes.
        /// </summary>
        public static void OnCatalogLoaded()
        {
            Debug.Log("[CubemapGenerationScheduler] Catalog loaded trigger");
            RequestCubemapUpdate();
        }
        
        /// <summary>
        /// Trigger: Called after catalog save completes.
        /// </summary>
        public static void OnCatalogSaved()
        {
            Debug.Log("[CubemapGenerationScheduler] Catalog saved trigger");
            RequestCubemapUpdate();
        }
        
        /// <summary>
        /// Trigger: Called after new catalog generation.
        /// </summary>
        public static void OnCatalogGenerated()
        {
            Debug.Log("[CubemapGenerationScheduler] Catalog generated trigger");
            RequestCubemapUpdate();
        }
        
        /// <summary>
        /// Trigger: Called when UI closes.
        /// </summary>
        public static void OnUIClose()
        {
            Debug.Log("[CubemapGenerationScheduler] UI close trigger");
            RequestCubemapUpdate();
        }
    }
}
