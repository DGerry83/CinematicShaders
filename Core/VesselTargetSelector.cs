using System;
using System.Linq;
using CinematicShaders.Native;
using CinematicShaders.Native.Structs;
using CinematicShaders.Shaders.Starfield;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Draws selection circles and info displays for vessel targets and situation.
    /// Supports two modes:
    /// 1. Target Info: Circle + box with text (like star selector)
    /// 2. Situation Info: Grid-fixed text display (like HUCK), dual-sided
    /// </summary>
    public class VesselTargetSelector
    {
        // Camera basis from StarfieldCompositor
        public Vector3 CameraRight { get; set; }
        public Vector3 CameraUp { get; set; }
        public Vector3 CameraForward { get; set; }
        public float AspectRatio { get; set; } = 1.777f;
        public float VerticalFOV { get; set; } = 1.0472f;

        // Target tracking state
        private bool _isTrackingTarget = false;
        private Vector2 _targetScreenUV = new Vector2(-1, -1);
        
        /// <summary>
        /// Current target screen position in UV coordinates (0-1 range).
        /// (-1, -1) if target is not visible or not set.
        /// </summary>
        public Vector2 TargetScreenUV => _isTrackingTarget ? _targetScreenUV : new Vector2(-1, -1);
        
        /// <summary>
        /// True if currently tracking a target that is visible on screen.
        /// </summary>
        public bool IsTrackingTarget => _isTrackingTarget;
        
        private ITargetable _currentTarget = null;
        private ITargetable _lastCheckedTarget = null;
        private int _frameCounter = 0;
        private int _logFrameCounter = 0;
        private float _lastLoggedBoxTopLeftX = 0f;
        private float _lastLoggedBoxTopLeftY = 0f;
        
        // Debug logging state
        private int _debugFrameThrottle = 0;
        private const int DEBUG_LOG_INTERVAL = 30; // Log every 30 frames max
        private string _lastAnimPhase = "";
        private bool _lastTrackingState = false;
        private bool _lastVisibleState = false;

        // Animation controller for target info (replaces individual animation state)
        private TypeOnAnimationController _animController = new TypeOnAnimationController();
        private float _starHash = 0f;
        private float _lastDynamicUpdate = 0f;
        private const float DYNAMIC_UPDATE_INTERVAL = 0.1f; // 10 FPS
        
        // Text rendering optimization - only update texture when text changes (matches KartographerSelector)
        private string _lastRenderedText = null;
        private bool _textDirty = true;

        // Situation display state
        private string _situationText = "";
        private float _lastSituationUpdate = 0f;

        // Text system
        private IntPtr _textSystem = IntPtr.Zero;
        private RenderTexture _textTexture = null;
        private static readonly float FONT_SIZE = 24f;
        private static readonly float BOX_PADDING_PIXELS = 20f;
        private float _textWidthPixels = 0f;
        private float _textHeightPixels = 0f;

        /// <summary>
        /// Main update - called every frame by CinematicShadersAddon
        /// </summary>
        public void Update()
        {
            _debugFrameThrottle++;
            bool canLog = _debugFrameThrottle >= DEBUG_LOG_INTERVAL;
            if (canLog) _debugFrameThrottle = 0;
            
            UpdateInternal(canLog);
        }
        
        private void UpdateInternal(bool canLog)
        {
            // Don't show in map view (would interfere with map interaction)
            if (AtmosphericScatteringData.IsMapView())
            {
                if (_isTrackingTarget)
                {
                    StopTracking();
                }
                return;
            }

            // Only works in Flight scene
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                if (_isTrackingTarget)
                {
                    StopTracking();
                }
                return;
            }

            // Validate camera - use surface frame basis for target tracking
            if (CameraForward.sqrMagnitude < 0.5f)
            {
                PushEmptyToNative();
                return;
            }

            // Update target info display (screen-space, follows target)
            UpdateTargetInfo(canLog);

            // Update situation info display (grid-fixed, dual-sided)
            UpdateSituationInfo();
        }

        /// <summary>
        /// Update target info display (circle + box with type-on text)
        /// </summary>
        private void UpdateTargetInfo(bool canLog)
        {
            _frameCounter++;
            
            // Poll for target changes every 5 frames (event-driven catches immediate changes)
            if (_frameCounter % 5 == 0)
            {
                CheckTargetChanged();
            }

            // Get current target position
            if (_currentTarget == null)
            {
                if (_isTrackingTarget)
                {
                    StopTracking();
                }
                PushEmptyToNative();
                return;
            }

            Vector3 targetPos = GetTargetPosition(_currentTarget);
            if (targetPos == Vector3.zero)
            {
                if (_isTrackingTarget)
                {
                    StopTracking();
                }
                return;
            }

            // Convert to world direction and project
            Vector3 worldDir = (targetPos - GetCameraPosition()).normalized;
            _targetScreenUV = KartographerMath.WorldDirectionToScreenUV(
                worldDir, CameraRight, CameraUp, CameraForward, AspectRatio, VerticalFOV);

            bool onScreen = KartographerMath.IsOnScreen(_targetScreenUV, 0.1f);
            bool visible = onScreen && _targetScreenUV.x >= 0;

            // Handle visibility change
            if (visible && !_isTrackingTarget)
            {
                _isTrackingTarget = true;
            }
            else if (!visible && _isTrackingTarget)
            {
                _isTrackingTarget = false;
            }
            
            // Track state transitions
            if (canLog && (_isTrackingTarget != _lastTrackingState || visible != _lastVisibleState))
            {
                _lastTrackingState = _isTrackingTarget;
                _lastVisibleState = visible;
            }

            // Update animation
            if (_isTrackingTarget)
            {
                UpdateTargetAnimation();
            }

            // Update text texture if needed
            if (_isTrackingTarget && _textSystem != IntPtr.Zero)
            {
                UpdateTextTexture();
            }

            // Push to native
            PushTargetToNative(visible);
        }

        /// <summary>
        /// Update situation info display (grid-fixed, dual-sided)
        /// Uses GridLabelSystem to render text on the holographic grid
        /// </summary>
        private void UpdateSituationInfo()
        {
            if (!StarfieldSettings.KartographerSituationDisplay)
                return;

            if (FlightGlobals.ActiveVessel == null)
                return;

            // Update text periodically
            float now = Time.time;
            if (now - _lastSituationUpdate > DYNAMIC_UPDATE_INTERVAL)
            {
                _lastSituationUpdate = now;
                _situationText = BuildSituationText();
            }

            // The actual rendering is handled by GridLabelSystem via the "situation_info" label
            // The label's position is calculated based on grid rotation setting
            // This is managed in CinematicShadersAddon.Update() which updates the label system
        }

        /// <summary>
        /// Update target info animation using TypeOnAnimationController
        /// </summary>
        private void UpdateTargetAnimation()
        {
            // Track previous phase to detect transitions
            var prevPhase = _animController.CurrentPhase;
            string prevPhaseName = prevPhase.ToString();
            
            // Track previous text to detect changes
            string prevText = _animController.DisplayText;
            
            // Update animation state
            _animController.Update(Time.deltaTime);
            
            // Track phase transitions
            if (_animController.CurrentPhase != prevPhase)
            {
                _lastAnimPhase = _animController.CurrentPhase.ToString();
            }
            
            // CRITICAL: Set text content when entering Text phase
            // This prevents text from appearing during Circle/Box phases
            // Note: Box phase transitions instantly, so we check "entered Text" not "from Box"
            if (prevPhase < TypeOnAnimationController.Phase.Text && 
                _animController.CurrentPhase >= TypeOnAnimationController.Phase.Text)
            {
                string targetText = BuildTargetText(_currentTarget);
                _animController.SetFullText(targetText);
            }
            
            // Mark dirty if text changed (cursor blink, type-on progression, etc.)
            if (_animController.DisplayText != prevText)
                _textDirty = true;

            // Update dynamic values at 10 FPS (only in Complete phase)
            if (!_animController.IsAnimating)
            {
                float now = Time.time;
                if (now - _lastDynamicUpdate > DYNAMIC_UPDATE_INTERVAL)
                {
                    _lastDynamicUpdate = now;
                    string newTargetText = BuildTargetText(_currentTarget);
                    _animController.UpdateFullText(newTargetText);
                    // UpdateFullText changes DisplayText, so mark dirty
                    if (_animController.DisplayText != prevText)
                        _textDirty = true;
                }
            }
        }

        /// <summary>
        /// Sanitize text to remove non-printable characters and KSP rich text tags
        /// </summary>
        private string SanitizeText(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            
            // First pass: remove control characters (0-31 and 127+)
            string clean = new string(input.Where(c => c >= 32 && c < 127).ToArray());
            
            // Second pass: remove KSP color tags like "^N" (caret notation for control chars)
            // KSP uses ^ followed by a letter to encode colors in display names
            var sb = new System.Text.StringBuilder(clean.Length);
            for (int i = 0; i < clean.Length; i++)
            {
                // Skip caret+letter sequences (KSP color codes)
                if (clean[i] == '^' && i + 1 < clean.Length && char.IsLetter(clean[i + 1]))
                {
                    i++; // Skip the letter too
                    continue;
                }
                sb.Append(clean[i]);
            }
            
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Build target info text
        /// </summary>
        private string BuildTargetText(ITargetable target)
        {
            if (target == null) return "";

            var sb = new System.Text.StringBuilder();

            // Target name (no label) - sanitized
            string targetName = SanitizeText(target.GetDisplayName()) ?? "UNKNOWN";
            sb.Append(targetName.ToUpper() + '\n');

            // Distance
            double distance = GetDistanceToTarget(target);
            if (distance > 1000000)
                sb.Append($"DIST: {distance/1000:F1} KM\n");
            else
                sb.Append($"DIST: {distance:F1} M\n");

            // Relative velocity
            double rvel = GetRelativeVelocity(target);
            sb.Append($"RVEL: {rvel:F1} M/S\n");

            // Time to encounter
            string tte = GetTimeToEncounter(target);
            sb.Append($"TTE: {tte}");

            return sb.ToString();
        }

        /// <summary>
        /// Build situation info text
        /// </summary>
        private string BuildSituationText()
        {
            var sb = new System.Text.StringBuilder();

            // SOI (no label) - sanitized
            if (FlightGlobals.currentMainBody != null)
                sb.Append(SanitizeText(FlightGlobals.currentMainBody.bodyDisplayName).ToUpper() + '\n');

            // Situation (no label)
            if (FlightGlobals.ActiveVessel != null)
                sb.Append(FlightGlobals.ActiveVessel.situation.ToString().ToUpper() + '\n');

            // Altitude
            if (FlightGlobals.ActiveVessel != null)
            {
                double alt = FlightGlobals.ActiveVessel.altitude;
                if (alt > 1000000)
                    sb.Append($"ALT: {alt/1000:F1} KM\n");
                else
                    sb.Append($"ALT: {alt:F1} M\n");
            }

            // Apoapsis/Periapsis
            if (FlightGlobals.ActiveVessel?.orbit != null)
            {
                double ap = FlightGlobals.ActiveVessel.orbit.ApA;
                double pe = FlightGlobals.ActiveVessel.orbit.PeA;

                if (ap > 1000000)
                    sb.Append($"A/P: {ap/1000:F1} KM\n");
                else
                    sb.Append($"A/P: {ap:F1} M\n");

                if (pe > 1000000)
                    sb.Append($"P/E: {pe/1000:F1} KM");
                else
                    sb.Append($"P/E: {pe:F1} M");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Calculate dual-sided grid positions for situation display
        /// </summary>
        private Vector3[] CalculateSituationPositions()
        {
            // Get grid preset
            int[] gridMeridians = { 8, 12, 16, 24, 32 };
            int[] gridParallels = { 5, 8, 10, 15, 20 };
            int preset = Mathf.Clamp(StarfieldSettings.KartographerGridSize, 0, 3);
            int numLong = gridMeridians[preset];
            int numLat = gridParallels[preset];

            // Second cell past equator going up
            int latCell = numLat / 2 + 2;
            latCell = Mathf.Min(latCell, numLat - 1);

            // Rotation step (0 to numLong-1, discrete meridian alignment)
            // Negate the step so slider to the right rotates labels clockwise
            int rotationStep = StarfieldSettings.KartographerSituationRotationStep[preset] % numLong;
            int lonCell1 = (numLong - rotationStep) % numLong;
            int lonCell2 = (lonCell1 + numLong / 2) % numLong; // Opposite side

            // Calculate spherical coordinates
            float thetaStep = 2.0f * Mathf.PI / numLong;
            float phiStep = Mathf.PI / numLat;

            float phi = latCell * phiStep;
            float theta1 = -Mathf.PI + (lonCell1 + 0.5f) * thetaStep;
            float theta2 = -Mathf.PI + (lonCell2 + 0.5f) * thetaStep;

            // Convert to Cartesian
            Vector3 pos1 = SphericalToCartesian(phi, theta1);
            Vector3 pos2 = SphericalToCartesian(phi, theta2);

            // Apply grid rotation
            pos1 = KartographerMath.ApplyCatalogRotation(pos1, 0,
                StarfieldSettings.KartographerRotationYaw,
                StarfieldSettings.KartographerRotationPitch);
            pos2 = KartographerMath.ApplyCatalogRotation(pos2, 0,
                StarfieldSettings.KartographerRotationYaw,
                StarfieldSettings.KartographerRotationPitch);

            return new Vector3[] { pos1, pos2 };
        }

        private Vector3 SphericalToCartesian(float phi, float theta)
        {
            float sinPhi = Mathf.Sin(phi);
            return new Vector3(
                sinPhi * Mathf.Cos(theta),
                Mathf.Cos(phi),
                sinPhi * Mathf.Sin(theta)
            );
        }

        /// <summary>
        /// Initialize text system
        /// </summary>
        private void InitializeTextSystem()
        {
            if (_textSystem != IntPtr.Zero) return;
            if (!StarfieldNative.IsLoaded) return;

            try
            {
                string assemblyPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string fontPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(assemblyPath, "..", "PluginData", "Fonts", "AcPlus_Rainbow100_re_66.ttf"));

                if (!System.IO.File.Exists(fontPath)) return;

                _textSystem = StarfieldNative.CR_TextInit(Texture2D.whiteTexture.GetNativeTexturePtr(), fontPath);
                if (_textSystem == IntPtr.Zero) return;

                _textTexture = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32);
                _textTexture.enableRandomWrite = true;
                _textTexture.Create();
            }
            catch { }
        }

        /// <summary>
        /// Update text texture - uses TypeOnAnimationController for animation state
        /// Only re-renders when text content changes (matches KartographerSelector pattern)
        /// </summary>
        private void UpdateTextTexture()
        {
            if (_textSystem == IntPtr.Zero) return;
            
            string displayText = _animController.DisplayText;
            
            // Skip if text hasn't changed (unless dirty flag is set)
            if (displayText == _lastRenderedText && !_textDirty)
                return;
            
            _lastRenderedText = displayText;
            _textDirty = false;
            
            // During Circle phase: clear texture and don't render anything
            // This prevents old text from flashing briefly when acquiring a new target
            if (_animController.CurrentPhase == TypeOnAnimationController.Phase.Circle)
            {
                if (_textTexture != null)
                {
                    RenderTexture.active = _textTexture;
                    GL.Clear(true, true, Color.clear);
                    RenderTexture.active = null;
                }
                _textWidthPixels = 0;
                _textHeightPixels = 0;
                return;
            }
            
            // During Box phase and later: render text (even if just cursor)
            if (string.IsNullOrEmpty(displayText)) return;

            uint color = 0xFFFFFFFF; // White ARGB
            int glyphCount = StarfieldNative.CR_TextLayoutEx(_textSystem, displayText, FONT_SIZE, 
                color, 0f, 0f, 0f, 1.0f);  // 1.0f = 1:1 aspect ratio (normal)
            if (glyphCount <= 0) return;

            // Get actual rendered bounds
            StarfieldNative.CR_TextMeasure(_textSystem, displayText, FONT_SIZE, out _, out _);
            StarfieldNative.CR_TextGetBounds(_textSystem, out _textWidthPixels, out _textHeightPixels);

            // Dispatch to texture with proper active texture handling
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = _textTexture;
                
                StarfieldNative.CR_TextDispatch(_textSystem, _textTexture.GetNativeTexturePtr(), glyphCount, 1024, 1024);
                GL.IssuePluginEvent(StarfieldNative.CR_GetTextDispatchRenderEventFunc(), 0);
                
                // IMPORTANT: Set texture for shader sampling (use separate slot from star selector)
                StarfieldNative.CR_SetVesselTargetTextTexture(_textTexture.GetNativeTexturePtr());
            }
            finally
            {
                // Always reset active render texture, even if an exception occurred
                RenderTexture.active = prevActive;
            }
        }

        /// <summary>
        /// Push target info to native
        /// </summary>
        private void PushTargetToNative(bool visible)
        {
            if (!StarfieldNative.IsLoaded) return;

            float u = _targetScreenUV.x;
            float v = _targetScreenUV.y;
            float centerX = (u - 0.5f) * 2.0f * AspectRatio;
            float centerY = (v - 0.5f) * 2.0f;

            float focalLength = VerticalFOV > 0.001f ? 1.0f / Mathf.Tan(VerticalFOV * 0.5f) : 1.732f;

            var kartParams = StarfieldNative.LastKartographerParams;
            CopyGridParams(ref kartParams, focalLength);

            // Box position
            // In shader-uv: +X = right, +Y = up. The shader treats BoxTopLeft as lower-left.
            // To place the box below the circle, lower-left Y = centerY - offset - boxHeightUV.
            float radius = 0.02f;
            float boxTopLeftX = centerX + radius + radius * 0.25f;
            float boxOffsetY = radius + radius * 1.25f;

            float pixelsToUv = 2.0f / Screen.height;
            float boxWidthUV = (_textWidthPixels + BOX_PADDING_PIXELS * 2) * pixelsToUv;
            float boxHeightUV = (_textHeightPixels + BOX_PADDING_PIXELS * 2) * pixelsToUv;
            boxWidthUV = Mathf.Max(boxWidthUV, 0.08f);
            boxHeightUV = Mathf.Max(boxHeightUV, 0.06f);

            float boxTopLeftY = centerY - boxOffsetY - boxHeightUV;

            bool showBox = visible && _animController.CurrentPhase >= TypeOnAnimationController.Phase.Box;

            // Use VesselTarget* fields (separate from Star Selector)
            kartParams.VesselTargetBoxTopLeftX = boxTopLeftX;
            kartParams.VesselTargetBoxTopLeftY = boxTopLeftY;
            kartParams.VesselTargetBoxSizeX = showBox ? boxWidthUV : 0f;
            kartParams.VesselTargetBoxSizeY = showBox ? boxHeightUV : 0f;
            kartParams.VesselTargetBoxThickness = 0.001f;

            // Circle (using controller's circle progress for flicker)
            kartParams.VesselTargetEnabled = visible ? 1 : 0;
            kartParams.VesselTargetCircleCenterX = centerX;
            kartParams.VesselTargetCircleCenterY = centerY;
            kartParams.VesselTargetCircleT = _animController.CircleProgress;
            kartParams.VesselTargetCircleIntensity = 0.002f;
            kartParams.VesselTargetCircleThickness = 0.001f;
            kartParams.VesselTargetCircleRadius = 0.02f;
            kartParams.VesselTargetHash = _starHash;

            // Text params - Match KartographerSelector: always use full texture, 
            // type-on animation happens via progressive text content, not shader
            float textWidthUV = 1024f * pixelsToUv;
            float textHeightUV = 1024f * pixelsToUv;
            
            kartParams.VesselTargetTextOriginX = boxTopLeftX + 0.01f;
            kartParams.VesselTargetTextOriginY = boxTopLeftY + 0.01f;
            kartParams.VesselTargetTextAreaSizeX = textWidthUV;
            kartParams.VesselTargetTextAreaSizeY = textHeightUV;
            // Always 1.0f - animation happens via progressive DisplayText content changes
            kartParams.VesselTargetTextT = 1.0f;
            
            // Animated label intensity: 0 during Circle/Box (hidden), 1 during Text/Complete (visible)
            kartParams.AnimatedLabelIntensity = _animController.Intensity;

            // Update tracking variables for change detection
            _logFrameCounter++;
            bool valuesChanged = Mathf.Abs(boxTopLeftX - _lastLoggedBoxTopLeftX) > 0.01f || 
                                Mathf.Abs(boxTopLeftY - _lastLoggedBoxTopLeftY) > 0.01f;
            if (_logFrameCounter >= 30 || valuesChanged)
            {
                _logFrameCounter = 0;
                _lastLoggedBoxTopLeftX = boxTopLeftX;
                _lastLoggedBoxTopLeftY = boxTopLeftY;
            }

            StarfieldNative.LastKartographerParams = kartParams;
            StarfieldNative.CR_StarfieldSetKartographerParams(ref kartParams);
        }

        /// <summary>
        /// Push empty params when not tracking
        /// </summary>
        private void PushEmptyToNative()
        {
            if (!StarfieldNative.IsLoaded) return;

            var kartParams = StarfieldNative.LastKartographerParams;
            
            // Clear vessel target if it was previously enabled
            
            kartParams.VesselTargetEnabled = 0;
            kartParams.VesselTargetBoxSizeX = 0f;
            kartParams.VesselTargetBoxSizeY = 0f;
            kartParams.VesselTargetTextAreaSizeX = 0f;
            kartParams.VesselTargetTextAreaSizeY = 0f;
            kartParams.VesselTargetCircleIntensity = 0f;
            kartParams.AnimatedLabelIntensity = 0f;
            StarfieldNative.LastKartographerParams = kartParams;
            StarfieldNative.CR_StarfieldSetKartographerParams(ref kartParams);
        }

        private void CopyGridParams(ref KartographerParamsNative kartParams, float focalLength)
        {
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
        }

        // Helper methods for target data
        private ITargetable GetTarget()
        {
            // Try multiple ways to get the target
            
            // Method 1: FlightGlobals.fetch.VesselTarget
            if (FlightGlobals.fetch != null && FlightGlobals.fetch.VesselTarget != null)
                return FlightGlobals.fetch.VesselTarget;
            
            // Method 2: Active vessel's targetObject property
            if (FlightGlobals.ActiveVessel != null)
            {
                var target = FlightGlobals.ActiveVessel.targetObject;
                if (target != null)
                    return target;
            }
            
            return null;
        }
        
        /// <summary>
        /// Check if target changed and handle animation reset
        /// Called from Update with polling (every 5 frames) + event-driven
        /// </summary>
        private void CheckTargetChanged()
        {
            ITargetable current = GetTarget();
            
            // Target acquired or changed
            if (current != _lastCheckedTarget)
            {
                if (current != null && current != _currentTarget)
                {
                    // New target - reset animation
                    string targetName = current.GetDisplayName() ?? "UNKNOWN";
                    
                    _currentTarget = current;
                    _starHash = UnityEngine.Random.value;
                    InitializeTextSystem();
                    
                    // Start animation WITHOUT text content
                    // Text will be set when Box phase completes to prevent early display
                    _animController.Start();
                    
                    // Reset dirty tracking for new animation
                    _textDirty = true;
                    _lastRenderedText = null;
                }
                else if (current == null && _currentTarget != null)
                {
                    // Target lost
                    _currentTarget = null;
                }
                
                _lastCheckedTarget = current;
            }
        }

        private Vector3 GetTargetPosition(ITargetable target)
        {
            if (target == null) return Vector3.zero;
            return target.GetTransform().position;
        }

        private Vector3 GetCameraPosition()
        {
            // The projection basis vectors come from GalaxyCamera (which mirrors FlightCamera rotation).
            // We must use the actual flight camera position in world space to avoid parallax error.
            if (FlightCamera.fetch != null)
                return FlightCamera.fetch.transform.position;
            return Vector3.zero;
        }

        private double GetDistanceToTarget(ITargetable target)
        {
            if (target == null || FlightGlobals.ActiveVessel == null) return 0;
            return Vector3d.Distance(target.GetTransform().position, FlightGlobals.ActiveVessel.GetWorldPos3D());
        }

        private double GetRelativeVelocity(ITargetable target)
        {
            if (target == null || FlightGlobals.ActiveVessel == null) return 0;
            var targetVel = target.GetObtVelocity();
            var vesselVel = FlightGlobals.ActiveVessel.obt_velocity;
            return (targetVel - vesselVel).magnitude;
        }

        private string GetTimeToEncounter(ITargetable target)
        {
            // TODO: Calculate actual encounter time
            // For now return N/A
            return "N/A";
        }

        /// <summary>
        /// Stop tracking and clear native state
        /// </summary>
        public void StopTracking()
        {
            _isTrackingTarget = false;
            _currentTarget = null;
            _lastCheckedTarget = null;
            PushEmptyToNative();
        }
    }
}
