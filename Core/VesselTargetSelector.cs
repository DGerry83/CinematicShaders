using System;
using System.Linq;
using CinematicShaders.Native;
using CinematicShaders.Shaders.Starfield;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Animation phases for target/situation UI
    /// </summary>
    public enum TargetAnimationPhase
    {
        Circle,     // 0-0.4s: Circle flickers
        Box,        // 0.4s: Box snaps on
        Text,       // 0.4s-1.9s: Text types on
        Complete    // 1.9s+: Cursor blinks, dynamic updates
    }

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
        private ITargetable _currentTarget = null;
        private ITargetable _lastCheckedTarget = null;
        private int _frameCounter = 0;

        // Animation state for target info
        private TargetAnimationPhase _targetAnimationPhase = TargetAnimationPhase.Complete;
        private float _targetAnimationT = 1.0f;
        private float _textTypeT = 0.0f;
        private string _fullTargetText = "";
        private string _currentDisplayText = "";
        private float _starHash = 0f;
        private float _lastDynamicUpdate = 0f;
        private const float DYNAMIC_UPDATE_INTERVAL = 0.1f; // 10 FPS

        // Situation display state
        private float _situationAnimationT = 0f;
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
            UpdateTargetInfo();

            // Update situation info display (grid-fixed, dual-sided)
            UpdateSituationInfo();
        }

        /// <summary>
        /// Update target info display (circle + box with type-on text)
        /// </summary>
        private void UpdateTargetInfo()
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
                // Animation already reset when target was acquired
            }
            else if (!visible && _isTrackingTarget)
            {
                _isTrackingTarget = false;
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
        /// Update target info animation (circle flicker, type-on, cursor blink)
        /// </summary>
        private void UpdateTargetAnimation()
        {
            if (_targetAnimationPhase == TargetAnimationPhase.Circle)
            {
                _targetAnimationT += Time.deltaTime / 0.4f;
                if (_targetAnimationT >= 1.0f)
                {
                    _targetAnimationT = 1.0f;
                    _targetAnimationPhase = TargetAnimationPhase.Box;
                }
            }
            else if (_targetAnimationPhase == TargetAnimationPhase.Box)
            {
                _targetAnimationPhase = TargetAnimationPhase.Text;
            }
            else if (_targetAnimationPhase == TargetAnimationPhase.Text)
            {
                _textTypeT += Time.deltaTime / 1.5f;
                if (_textTypeT >= 1.0f)
                {
                    _textTypeT = 1.0f;
                    _targetAnimationPhase = TargetAnimationPhase.Complete;
                }
                UpdateDisplayText();
            }
            else // Complete
            {
                // Blink cursor at 2Hz
                UpdateDisplayText();

                // Update dynamic values at 10 FPS
                float now = Time.time;
                if (now - _lastDynamicUpdate > DYNAMIC_UPDATE_INTERVAL)
                {
                    _lastDynamicUpdate = now;
                    _fullTargetText = BuildTargetText(_currentTarget);
                    UpdateDisplayText();
                }
            }
        }

        /// <summary>
        /// Build display text with cursor for animation
        /// Cursor only appears after box phase (matches star selector behavior)
        /// </summary>
        private void UpdateDisplayText()
        {
            if (_targetAnimationPhase == TargetAnimationPhase.Circle)
            {
                // Circle phase: no text, no cursor
                _currentDisplayText = "";
            }
            else if (_targetAnimationPhase == TargetAnimationPhase.Box)
            {
                // Box phase: just cursor, no text yet
                _currentDisplayText = "^|";
            }
            else if (_targetAnimationPhase == TargetAnimationPhase.Text)
            {
                // Text phase: progressively reveal characters with cursor
                int visibleChars = (int)(_fullTargetText.Length * _textTypeT);
                visibleChars = Mathf.Clamp(visibleChars, 0, _fullTargetText.Length);
                _currentDisplayText = _fullTargetText.Substring(0, visibleChars) + "^|";
            }
            else // Complete
            {
                // Complete phase: full text with blinking cursor at 2Hz
                bool cursorVisible = (Time.time * 2.0f) % 2.0f < 1.0f;
                _currentDisplayText = _fullTargetText + (cursorVisible ? "^|" : " ");
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
            int rotationStep = StarfieldSettings.KartographerSituationRotationStep[preset] % numLong;
            int lonCell1 = rotationStep;
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
                string fontPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(assemblyPath, "..", "PluginData", "Fonts", "Ac437_Rainbow100_re_66.ttf"));

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
        /// Update text texture - matches KartographerSelector implementation
        /// Clears texture during Circle phase, renders during Box phase and later
        /// </summary>
        private void UpdateTextTexture()
        {
            if (_textSystem == IntPtr.Zero) return;
            
            // During Circle phase: clear texture and don't render anything
            // This prevents old text from flashing briefly when acquiring a new target
            if (_targetAnimationPhase == TargetAnimationPhase.Circle)
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
            if (string.IsNullOrEmpty(_currentDisplayText)) return;

            uint color = 0xFFFFFFFF; // White ARGB
            int glyphCount = StarfieldNative.CR_TextLayout(_textSystem, _currentDisplayText, FONT_SIZE, color);
            if (glyphCount <= 0) return;

            // Get actual rendered bounds
            StarfieldNative.CR_TextMeasure(_textSystem, _currentDisplayText, FONT_SIZE, out _, out _);
            StarfieldNative.CR_TextGetBounds(_textSystem, out _textWidthPixels, out _textHeightPixels);

            // Dispatch to texture
            StarfieldNative.CR_TextDispatch(_textSystem, _textTexture.GetNativeTexturePtr(), glyphCount, 1024, 1024);
            
            // IMPORTANT: Set texture for shader sampling (use separate slot from star selector)
            StarfieldNative.CR_SetVesselTargetTextTexture(_textTexture.GetNativeTexturePtr());
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
            float radius = 0.02f;
            float boxTopLeftX = centerX + radius + radius * 0.25f;
            float boxTopLeftY = centerY + radius + radius * 1.25f;

            float pixelsToUv = 2.0f / Screen.height;
            float boxWidthUV = (_textWidthPixels + BOX_PADDING_PIXELS * 2) * pixelsToUv;
            float boxHeightUV = (_textHeightPixels + BOX_PADDING_PIXELS * 2) * pixelsToUv;
            boxWidthUV = Mathf.Max(boxWidthUV, 0.08f);
            boxHeightUV = Mathf.Max(boxHeightUV, 0.06f);

            bool showBox = visible && _targetAnimationPhase >= TargetAnimationPhase.Box;

            // Use VesselTarget* fields (separate from Star Selector)
            kartParams.VesselTargetBoxTopLeftX = boxTopLeftX;
            kartParams.VesselTargetBoxTopLeftY = boxTopLeftY;
            kartParams.VesselTargetBoxSizeX = showBox ? boxWidthUV : 0f;
            kartParams.VesselTargetBoxSizeY = showBox ? boxHeightUV : 0f;
            kartParams.VesselTargetBoxThickness = 0.001f;

            // Circle
            kartParams.VesselTargetEnabled = visible ? 1 : 0;
            kartParams.VesselTargetCircleCenterX = centerX;
            kartParams.VesselTargetCircleCenterY = centerY;
            kartParams.VesselTargetCircleT = _targetAnimationT;
            kartParams.VesselTargetCircleIntensity = 0.002f;
            kartParams.VesselTargetCircleThickness = 0.001f;
            kartParams.VesselTargetCircleRadius = 0.02f;
            kartParams.VesselTargetHash = _starHash;

            // Text - only show after box phase with type-on animation
            float textT = 0.0f;
            if (_targetAnimationPhase == TargetAnimationPhase.Text)
                textT = _textTypeT;
            else if (_targetAnimationPhase >= TargetAnimationPhase.Complete)
                textT = 1.0f;
            
            float textWidthUV = 1024f * pixelsToUv;
            float textHeightUV = 1024f * pixelsToUv;
            kartParams.VesselTargetTextOriginX = boxTopLeftX + 0.01f;
            kartParams.VesselTargetTextOriginY = boxTopLeftY + 0.01f;
            kartParams.VesselTargetTextAreaSizeX = textWidthUV;
            kartParams.VesselTargetTextAreaSizeY = textHeightUV;
            kartParams.VesselTargetTextT = textT;

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
            kartParams.VesselTargetEnabled = 0;
            kartParams.VesselTargetBoxSizeX = 0f;
            kartParams.VesselTargetBoxSizeY = 0f;
            kartParams.VesselTargetTextAreaSizeX = 0f;
            kartParams.VesselTargetTextAreaSizeY = 0f;
            StarfieldNative.LastKartographerParams = kartParams;
            StarfieldNative.CR_StarfieldSetKartographerParams(ref kartParams);
        }

        private void CopyGridParams(ref StarfieldNative.KartographerParamsNative kartParams, float focalLength)
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
                    _currentTarget = current;
                    _targetAnimationPhase = TargetAnimationPhase.Circle;
                    _targetAnimationT = 0f;
                    _textTypeT = 0f;
                    _starHash = UnityEngine.Random.value;
                    _fullTargetText = BuildTargetText(current);
                    _currentDisplayText = "";
                    InitializeTextSystem();
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
            return Vector3.zero; // Scaled space camera at origin
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
