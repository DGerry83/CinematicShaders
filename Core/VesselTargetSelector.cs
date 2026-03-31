using CinematicShaders.Native;
using CinematicShaders.Shaders.Starfield;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Draws a selection circle and info box around the current vessel target.
    /// Similar to KartographerSelector but for vessel targets instead of stars.
    /// </summary>
    public class VesselTargetSelector
    {
        // Tracking state
        public bool IsTracking { get; private set; } = false;
        public Vector2 TargetScreenUV { get; private set; } = new Vector2(-1, -1);
        
        // Cached camera basis from StarfieldCompositor
        public Vector3 CameraRight { get; set; }
        public Vector3 CameraUp { get; set; }
        public Vector3 CameraForward { get; set; }
        public float AspectRatio { get; set; } = 1.777f;
        public float VerticalFOV { get; set; } = 1.0472f;
        
        // Visual params
        private float _animationT = 1.0f;
        private bool _wasVisible = false;
        
        /// <summary>
        /// Update target projection and push to native
        /// Call this every frame when Kartographer is enabled
        /// </summary>
        public void Update()
        {
            // Only works in Flight scene with a vessel
            if (HighLogic.LoadedScene != GameScenes.FLIGHT || 
                FlightGlobals.ActiveVessel == null)
            {
                if (IsTracking)
                {
                    IsTracking = false;
                    PushToNative(false);
                }
                return;
            }
            
            // Get target position
            Vector3 targetPosition = GetTargetPosition();
            if (targetPosition == Vector3.zero)
            {
                if (IsTracking)
                {
                    IsTracking = false;
                    PushToNative(false);
                }
                return;
            }
            
            // Validate camera basis
            if (CameraForward.sqrMagnitude < 0.5f)
            {
                PushToNative(false);
                return;
            }
            
            // Convert target position to world direction from camera
            Vector3 worldDir = (targetPosition - GetCameraPosition()).normalized;
            
            // Project to screen UV
            TargetScreenUV = KartographerMath.WorldDirectionToScreenUV(
                worldDir,
                CameraRight,
                CameraUp,
                CameraForward,
                AspectRatio,
                VerticalFOV
            );
            
            // Check if on screen
            bool onScreen = KartographerMath.IsOnScreen(TargetScreenUV, 0.1f);
            bool visible = onScreen && TargetScreenUV.x >= 0;
            
            if (visible && !IsTracking)
            {
                // Just became visible
                _animationT = 0f;
                IsTracking = true;
            }
            else if (!visible && IsTracking)
            {
                IsTracking = false;
            }
            
            // Update animation
            if (_animationT < 1f)
            {
                _animationT += Time.deltaTime / 0.4f;
                if (_animationT > 1f) _animationT = 1f;
            }
            
            PushToNative(visible);
        }
        
        /// <summary>
        /// Get the current target position in world space
        /// </summary>
        private Vector3 GetTargetPosition()
        {
            // Check for targeted vessel
            if (FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget != null)
            {
                return FlightGlobals.fetch.VesselTarget.GetTransform().position;
            }
            
            // Check for targeted celestial body
            if (FlightGlobals.currentMainBody != null)
            {
                // Return the body's position
                return FlightGlobals.currentMainBody.position;
            }
            
            return Vector3.zero;
        }
        
        /// <summary>
        /// Get camera position in world space
        /// </summary>
        private Vector3 GetCameraPosition()
        {
            // Use the scaled space camera position (same as starfield)
            if (StarfieldCompositor.CameraForward.sqrMagnitude > 0.5f)
            {
                // We can approximate camera position from the compositor's basis
                // This is a simplification - in reality we'd need the actual camera transform
                return Vector3.zero; // Scaled space camera is at origin
            }
            return Vector3.zero;
        }
        
        /// <summary>
        /// Push selection circle and info box params to native
        /// Uses the same params as star selection but with different content
        /// </summary>
        private void PushToNative(bool visible)
        {
            if (!StarfieldNative.IsLoaded)
                return;
            
            // Convert UV to shader space
            float u = TargetScreenUV.x;
            float v = TargetScreenUV.y;
            float centerX = (u - 0.5f) * 2.0f * AspectRatio;
            float centerY = (v - 0.5f) * 2.0f;
            
            float focalLength = VerticalFOV > 0.001f
                ? 1.0f / Mathf.Tan(VerticalFOV * 0.5f)
                : 1.732f;
            
            // Merge with cached params
            var kartParams = StarfieldNative.LastKartographerParams;
            kartParams.GridIntensity = StarfieldSettings.KartographerGridIntensity;
            kartParams.GridThickness = StarfieldSettings.KartographerGridThickness;
            kartParams.ChromaticAberrationStrength = StarfieldSettings.KartographerCAStrength;
            kartParams.VignetteStrength = StarfieldSettings.KartographerVignetteStrength;
            kartParams.VignetteStart = StarfieldSettings.KartographerVignetteStart;
            kartParams.VignetteEnd = StarfieldSettings.KartographerVignetteEnd;
            kartParams.PreRotationYaw = StarfieldSettings.KartographerRotationYaw;
            kartParams.PreRotationPitch = StarfieldSettings.KartographerRotationPitch;
            kartParams.GridSizePreset = StarfieldSettings.KartographerGridSize;
            kartParams.GridColorIndex = StarfieldSettings.KartographerGridColor;
            kartParams.DebugShapesEnabled = 0;
            kartParams.FocalLength = focalLength;
            
            // Box position (below and right of circle)
            float radius = 0.02f;
            float boxTopLeftX = centerX + radius + radius * 0.25f;
            float boxTopLeftY = centerY + radius + radius * 1.25f;
            
            // Fixed box size for empty text
            float pixelsToUv = 2.0f / Screen.height;
            float boxWidthUV = 150f * pixelsToUv;  // Fixed width
            float boxHeightUV = 60f * pixelsToUv;  // Fixed height for empty box
            
            kartParams.DebugBoxTopLeftX = boxTopLeftX;
            kartParams.DebugBoxTopLeftY = boxTopLeftY;
            kartParams.DebugBoxSizeX = visible ? boxWidthUV : 0.0f;
            kartParams.DebugBoxSizeY = visible ? boxHeightUV : 0.0f;
            kartParams.DebugBoxThickness = 0.001f;
            
            // Selection circle
            kartParams.SelectionCircleEnabled = visible ? 1 : 0;
            kartParams.SelectionCircleCenterX = centerX;
            kartParams.SelectionCircleCenterY = centerY;
            kartParams.SelectionCircleT = _animationT;
            kartParams.SelectionCircleIntensity = 0.002f;
            kartParams.SelectionCircleThickness = 0.001f;
            kartParams.SelectionCircleRadius = 0.02f;
            kartParams.SelectionStarHash = 0f;  // No flicker variation
            
            // Text params - empty text for now
            kartParams.TextOriginX = boxTopLeftX + 0.01f;
            kartParams.TextOriginY = boxTopLeftY + 0.01f;
            kartParams.TextAreaSizeX = 0f;  // No text texture
            kartParams.TextAreaSizeY = 0f;
            kartParams.SelectionTextT = 1.0f;
            
            StarfieldNative.LastKartographerParams = kartParams;
            StarfieldNative.CR_StarfieldSetKartographerParams(ref kartParams);
        }
        
        /// <summary>
        /// Stop tracking and clear native state
        /// </summary>
        public void StopTracking()
        {
            IsTracking = false;
            PushToNative(false);
        }
    }
}
