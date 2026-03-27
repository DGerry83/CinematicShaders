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
                
                // Render and inject directly (no intermediate copies)
                bool success = StarfieldCubemapRenderer.RenderAndInjectCubemap();
                
                if (success)
                {
                    Debug.Log("[CubemapGenerationScheduler] Cubemap generation complete");
                }
                else
                {
                    Debug.LogWarning("[CubemapGenerationScheduler] Cubemap generation failed, will retry on next trigger");
                    _hasPendingUpdate = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CubemapGenerationScheduler] Error during cubemap generation: {ex}");
                _hasPendingUpdate = true;
            }
            finally
            {
                _isGenerating = false;
            }
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
