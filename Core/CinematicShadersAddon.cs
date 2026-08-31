using System.Linq;
using CinematicShaders.Native;
using CinematicShaders.Native.Structs;
using CinematicShaders.Shaders.GTAO;
using CinematicShaders.Shaders.Starfield;
using CinematicShaders.UI;
using CinematicShaders.UI.Tabs;
using KSP.UI.Screens;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CinematicShaders.Core
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class CinematicShadersAddon : MonoBehaviour
    {
        public static CinematicShadersAddon Instance { get; private set; }

        private static ApplicationLauncherButton _toolbarButton;
        private static Texture2D _toolbarIcon;

        private CinematicShadersWindow _mainWindow;
        
        // Vessel target selector - needs frame updates independent of UI
        private VesselTargetSelector _vesselTargetSelector;
        
        // Situation display label system - shared with UI for debug sliders
        public static GridLabelSystem SituationLabelSystem { get; private set; }
        
        // Navball indicator label manager - shared with UI for settings
        public static NavballLabelManager NavballManager { get; private set; }
        
        private float _lastSituationUpdate = 0f;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            ModFileLogger.Initialize();
            ModFileLogger.Log("CinematicShadersAddon awakened");
            
            // Ensure native DLL is loaded before any Update() methods are JIT-compiled.
            // Fixes cubemap DllNotFoundException caused by P/Invoke resolution race.
            DllLoader.EnsureLoaded();
        }

        void Start()
        {
            // Skip initialization in MAINMENU/LOADING scenes - GameDatabase not ready, no vessel exists
            if (HighLogic.LoadedScene == GameScenes.MAINMENU || 
                HighLogic.LoadedScene == GameScenes.LOADING)
            {
                return;
            }
            
            GTAOSettings.Load();
            StarfieldSettings.Load();
            
            // Size contract from StructToolset generator output ("Total Size: 1120 bytes");
            // matches static_assert in NativePlugin/include/KartographerParams_generated.h
            System.Diagnostics.Debug.Assert(System.Runtime.InteropServices.Marshal.SizeOf(typeof(KartographerParamsNative)) == 1120,
                $"KartographerParamsNative size mismatch");
            
            // If we're already in a game session, re-apply per-save settings to override
            // the global settings we just loaded. This happens on scene changes within
            // the same save (e.g., Flight -> Tracking Station -> Flight).
            // OnGameStateLoad only fires when first loading a save, not on scene changes.
            if (HighLogic.LoadedScene != GameScenes.MAINMENU &&
                HighLogic.LoadedScene != GameScenes.LOADING &&
                StarfieldPerSaveSettings.Instance != null)
            {
                Debug.Log("[CinematicShaders] Re-applying per-save settings after scene change");
                StarfieldPerSaveSettings.Instance.ApplyToSettings();
            }
            StarCatalogManager.Initialize();  // Ensure catalog folder exists
            
            // Only auto-enable if in a playable scene (not LOADING, MAINMENU, or EDITOR)
            if (IsPlayableScene() && (GTAOSettings.EnableGTAO || StarfieldSettings.EnableStarfield))
            {
                Invoke(nameof(DelayedInit), 0.5f);
            }

            GameEvents.onGUIApplicationLauncherReady.Add(OnGUIApplicationLauncherReady);
            GameEvents.onLevelWasLoadedGUIReady.Add(OnLevelWasLoadedGUIReady);
            
            // Listen for game load/save events for per-save settings
            GameEvents.onGameStateLoad.Add(OnGameStateLoad);
            GameEvents.onGameStateSave.Add(OnGameStateSave);

            // Save settings before a scene switch so the capture lands in the old scene's profile (#035)
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);

            if (_toolbarIcon == null)
            {
                _toolbarIcon = GameDatabase.Instance.GetTexture(CinematicShadersUIResources.Textures.ToolbarIconPath, false);
                if (_toolbarIcon == null)
                {
                    _toolbarIcon = new Texture2D(38, 38, TextureFormat.RGBA32, false);
                    Color[] pixels = new Color[38 * 38];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = CinematicShadersUIResources.Colors.TOOLBAR_FALLBACK_ORANGE;
                    _toolbarIcon.SetPixels(pixels);
                    _toolbarIcon.Apply();
                }
            }
        }

        private void DelayedInit()
        {
            if (GTAOSettings.EnableGTAO)
                GTAOManager.Initialize();
            if (StarfieldSettings.EnableStarfield)
                StarfieldManager.Initialize();
            
            // Initialize Kartographer from saved settings (no UI required)
            // Done here instead of OnLevelWasLoadedGUIReady to ensure DLL is loaded
            KartographerTab.InitializeFromSettings();
        }

        /// <summary>
        /// Check if current scene is a playable scene (not LOADING, MAINMENU, or EDITOR)
        /// Starfield needs a sky camera which only exists in these scenes
        /// </summary>
        private bool IsPlayableScene()
        {
            return HighLogic.LoadedScene == GameScenes.SPACECENTER ||
                   HighLogic.LoadedScene == GameScenes.FLIGHT ||
                   HighLogic.LoadedScene == GameScenes.TRACKSTATION;
        }

        private void OnLevelWasLoadedGUIReady(GameScenes scene)
        {
            if (scene == GameScenes.MAINMENU) return;

            // If coming from MAINMENU to a playable scene, reset the starfield compositor
            // It may be in a bad state from failed initialization during game startup
            if (StarfieldManager.IsActive)
            {
                Debug.Log("[CinematicShaders] Scene change from menu - resetting starfield compositor...");
                StarfieldManager.DisableStarfield();
            }

            // Mark catalog for reload on scene change (device may have reset)
            StarfieldSettings.InvalidateCatalogForReload();

            // Switch to this scene's GTAO profile before the enable checks below
            GTAOSettings.ApplySceneProfile(scene);

            if (GTAOSettings.EnableGTAO)
            {
                if (scene == GameScenes.EDITOR && GTAOManager.IsActive)
                {
                    // Check if compositor is on the wrong (destroyed) camera
                    if (!GTAOManager.IsCompositorOnCurrentCamera())
                    {
                        Debug.Log("[CinematicShaders] Detected stale compositor in Editor, resetting...");
                        GTAOManager.DisableGTAO();
                    }
                }

                GTAOManager.Initialize();

                if (!GTAOManager.IsActive && scene == GameScenes.EDITOR)
                {
                    CancelInvoke(nameof(RetryInit));
                    Invoke(nameof(RetryInit), 0.5f);
                    Invoke(nameof(RetryInit), 1.5f);
                    Invoke(nameof(RetryInit), 3.0f);
                }
            }

            // Initialize Starfield completely independently of GTAO
            if (StarfieldSettings.EnableStarfield && IsPlayableScene())
            {
                StarfieldManager.Initialize();
            }
            
            // Process any queued cubemap updates on scene load
            CubemapGenerationScheduler.OnSceneLoad();
        }

        private void RetryInit()
        {
            if (GTAOSettings.EnableGTAO && !GTAOManager.IsActive)
            {
                Debug.Log("[CinematicShaders] Retrying GTAO initialization...");
                GTAOManager.Initialize();
            }
        }
        
        void Update()
        {
            // Update vessel target selector every frame (independent of UI)
            if (StarfieldSettings.EnableKartographer && StarfieldSettings.KartographerVesselTargetSelect)
            {
                if (_vesselTargetSelector == null)
                {
                    _vesselTargetSelector = new VesselTargetSelector();
                }
                
                // Update camera params from compositor
                // Use SURFACE FRAME for target tracking (matches world space target positions)
                _vesselTargetSelector.CameraRight = StarfieldCompositor.CameraRightSurface;
                _vesselTargetSelector.CameraUp = StarfieldCompositor.CameraUpSurface;
                _vesselTargetSelector.CameraForward = StarfieldCompositor.CameraForwardSurface;
                _vesselTargetSelector.AspectRatio = StarfieldCompositor.CameraAspect;
                _vesselTargetSelector.VerticalFOV = StarfieldCompositor.CachedVerticalFOV;
                
                // Update projection
                _vesselTargetSelector.Update();
            }
            else if (_vesselTargetSelector != null)
            {
                // Selector exists but should be disabled
                _vesselTargetSelector.StopTracking();
                _vesselTargetSelector = null;
            }
            
            // Update grid label system (HUCK, situation labels) - runs whenever Kartographer is enabled
            UpdateGridLabelSystem();
            
            // Update navball indicators if enabled
            if (StarfieldSettings.EnableKartographer && NavballManager != null)
            {
                NavballManager.Update();
            }
            
            // Poll async cubemap render completion (Fix 4)
            CubemapGenerationScheduler.CheckCubemapCompletion();
            
            // Update situation display only in playable scenes (Flight, Tracking Station, KSC)
            // Shows "NO VESSEL" when not in a vessel (e.g., Tracking Station)
            if (IsPlayableScene())
            {
                UpdateSituationDisplay();
            }
            else
            {
                // Disable situation labels when not in a playable scene
                if (SituationLabelSystem != null)
                {
                    var labelA = SituationLabelSystem.GetLabel("situation_a");
                    var labelB = SituationLabelSystem.GetLabel("situation_b");
                    if (labelA != null && labelA.Enabled)
                        SituationLabelSystem.SetLabelEnabled("situation_a", false);
                    if (labelB != null && labelB.Enabled)
                        SituationLabelSystem.SetLabelEnabled("situation_b", false);
                }
            }
        }
        
        /// <summary>
        /// Updates the shared GridLabelSystem that manages HUCK and situation labels.
        /// This runs whenever Kartographer is enabled, independent of situation display setting.
        /// </summary>
        private void UpdateGridLabelSystem()
        {
            if (!StarfieldSettings.EnableKartographer)
            {
                // Disable all grid labels when Kartographer is off
                if (SituationLabelSystem != null)
                {
                    if (SituationLabelSystem.GetLabel("situation_a") is var a && a != null && a.Enabled)
                        SituationLabelSystem.SetLabelEnabled("situation_a", false);
                    if (SituationLabelSystem.GetLabel("situation_b") is var b && b != null && b.Enabled)
                        SituationLabelSystem.SetLabelEnabled("situation_b", false);
                    if (SituationLabelSystem.GetLabel("huck") is var h && h != null && h.Enabled)
                        SituationLabelSystem.SetLabelEnabled("huck", false);
                }
                return;
            }
            
            // Initialize label system if needed (shared between HUCK and situation display)
            if (SituationLabelSystem == null)
            {
                SituationLabelSystem = new GridLabelSystem();
                SituationLabelSystem.Initialize();
            }
            
            // Initialize navball label manager if needed
            if (NavballManager == null)
            {
                NavballManager = new NavballLabelManager();
                NavballManager.Initialize();
                // Apply saved settings
                NavballManager.SetEnabled(StarfieldSettings.KartographerNavballLabels);
                NavballManager.SetUseNavballColors(StarfieldSettings.KartographerNavballUseColors);
                NavballManager.SetOffscreenMode(StarfieldSettings.KartographerNavballOffscreenMode);
            }
            
            // Ensure HUCK label is enabled (unless Tiny preset)
            int currentPreset = Mathf.Clamp(StarfieldSettings.KartographerGridSize, 0, 4);
            if (SituationLabelSystem.GetLabel("huck") is var huck && huck != null)
            {
                if (currentPreset == 4 && huck.Enabled) // Tiny
                {
                    SituationLabelSystem.SetLabelEnabled("huck", false);
                }
                else if (currentPreset != 4 && !huck.Enabled)
                {
                    SituationLabelSystem.SetLabelEnabled("huck", true);
                }
            }
            
            // Update the label system - this handles preset changes, texture generation, and native pushing
            SituationLabelSystem.Update();
        }
        
        /// <summary>
        /// Updates situation-specific text and positioning.
        /// Only runs when situation display is enabled, but uses the shared GridLabelSystem.
        /// </summary>
        private void UpdateSituationDisplay()
        {
            if (!StarfieldSettings.EnableKartographer || !StarfieldSettings.KartographerSituationDisplay)
            {
                // Disable situation labels if display is off (HUCK remains managed by UpdateGridLabelSystem)
                if (SituationLabelSystem != null)
                {
                    if (SituationLabelSystem.GetLabel("situation_a") is var a && a != null && a.Enabled)
                        SituationLabelSystem.SetLabelEnabled("situation_a", false);
                    if (SituationLabelSystem.GetLabel("situation_b") is var b && b != null && b.Enabled)
                        SituationLabelSystem.SetLabelEnabled("situation_b", false);
                }
                return;
            }
            
            // Label system is guaranteed to be initialized by UpdateGridLabelSystem()
            // Update positions based on grid preset and rotation slider
            UpdateSituationPositions();
            
            // Enable the situation labels
            var labelA = SituationLabelSystem.GetLabel("situation_a");
            var labelB = SituationLabelSystem.GetLabel("situation_b");
            if (labelA != null && !labelA.Enabled)
                SituationLabelSystem.SetLabelEnabled("situation_a", true);
            if (labelB != null && !labelB.Enabled)
                SituationLabelSystem.SetLabelEnabled("situation_b", true);
            
            // Update text periodically (10 FPS)
            float now = Time.time;
            if (now - _lastSituationUpdate > 0.1f)
            {
                _lastSituationUpdate = now;
                string text = BuildSituationText();
                if (labelA != null)
                {
                    labelA.Text = text;
                    labelA.TextureDirty = true;
                }
                if (labelB != null)
                {
                    labelB.Text = text;
                    labelB.TextureDirty = true;
                }
            }
            
            // Note: LabelSystem.Update() is called in UpdateGridLabelSystem() which runs first
        }
        
        /// <summary>
        /// Update situation label grid cell positions based on grid preset and rotation slider
        /// Uses GridCellRow and GridCellCol for explicit cell positioning
        /// </summary>
        private void UpdateSituationPositions()
        {
            if (SituationLabelSystem == null) return;
            
            var labelA = SituationLabelSystem.GetLabel("situation_a");
            var labelB = SituationLabelSystem.GetLabel("situation_b");
            if (labelA == null || labelB == null) return;
            
            // Get grid preset
            int preset = Mathf.Clamp(StarfieldSettings.KartographerGridSize, 0, 3);
            
            // Row from top (0 = north pole)
            // Preset-specific base positions: Jumbo=2, Large=3, Medium=5, Small=7
            // Medium and Small shifted +2 towards south pole for better label positioning
            int[] baseRows = { 2, 3, 5, 7 };
            int baseRow = baseRows[preset];
            int rowOffset = StarfieldSettings.KartographerSituationRowOffset[preset];
            int rowFromTop = Mathf.Clamp(baseRow - rowOffset, 0, 15);
            
            // Get grid dimensions
            int[] gridMeridians = { 8, 12, 16, 24 };
            int numLong = gridMeridians[preset];
            
            // Get discrete rotation step (0 to numLong-1)
            // Negate the step so slider to the right rotates labels clockwise
            int rotationStep = StarfieldSettings.KartographerSituationRotationStep[preset] % numLong;
            int col = (numLong - rotationStep) % numLong;
            int oppositeStep = (col + numLong / 2) % numLong;
            
            // Update label A if position changed
            if (labelA.GridCellRow != rowFromTop || labelA.GridCellCol != col)
            {
                labelA.GridCellRow = rowFromTop;
                labelA.GridCellCol = col;
                labelA.PositionDirty = true;
            }
            
            // Update label B if position changed
            if (labelB.GridCellRow != rowFromTop || labelB.GridCellCol != oppositeStep)
            {
                labelB.GridCellRow = rowFromTop;
                labelB.GridCellCol = oppositeStep;
                labelB.PositionDirty = true;
            }
        }
        
        /// <summary>
        /// Sanitize text to remove non-printable characters and KSP formatting codes
        /// </summary>
        private string SanitizeText(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            // Remove KSP formatting codes like ^N (newline), ^C (color), etc.
            string cleaned = input.Replace("^N", "").Replace("^n", "");
            // Keep only printable ASCII
            return new string(cleaned.Where(c => c >= 32 && c < 127).ToArray());
        }

        /// <summary>
        /// Formats a distance in meters - always outputs meters
        /// Texture width overflow detection in GenerateTexture handles unit escalation per-line
        /// </summary>
        private string FormatDistanceSmart(double meters, string prefix)
        {
            // Always output meters - per-line width detection in GenerateTexture will 
            // compress individual lines to KM/MM/GM/TM if they don't fit in texture
            // (unit token shared with the parser — see UIStrings.Common)
            if (!StarfieldSettings.SituationCompressUnits)
            {
                if (meters >= 100) return $"{prefix}{meters:F0}{CinematicShadersUIStrings.Common.UnitMetersToken}";
                if (meters >= 10) return $"{prefix}{meters:F1}{CinematicShadersUIStrings.Common.UnitMetersToken}";
                return $"{prefix}{meters:F2}{CinematicShadersUIStrings.Common.UnitMetersToken}";
            }

            double scale;
            string unitToken;
            if (meters >= 1e13) { scale = 1e12; unitToken = CinematicShadersUIStrings.Common.UnitTerametersToken; }
            else if (meters >= 1e10) { scale = 1e9; unitToken = CinematicShadersUIStrings.Common.UnitGigametersToken; }
            else if (meters >= 1e7) { scale = 1e6; unitToken = CinematicShadersUIStrings.Common.UnitMegametersToken; }
            else if (meters >= 1e4) { scale = 1e3; unitToken = CinematicShadersUIStrings.Common.UnitKilometersToken; }
            else { scale = 1.0; unitToken = CinematicShadersUIStrings.Common.UnitMetersToken; }

            double scaled = meters / scale;
            if (scaled >= 100) return $"{prefix}{scaled:F0}{unitToken}";
            if (scaled >= 10) return $"{prefix}{scaled:F1}{unitToken}";
            return $"{prefix}{scaled:F2}{unitToken}";
        }
        
        /// <summary>
        /// Build situation info text for display
        /// </summary>
        private string BuildSituationText()
        {
            if (FlightGlobals.ActiveVessel == null)
                return CinematicShadersUIStrings.Kartographer.SituationNoVessel;
            
            var sb = new System.Text.StringBuilder();
            
            // SOI (no label) - sanitized
            if (FlightGlobals.currentMainBody != null)
                sb.Append(SanitizeText(FlightGlobals.currentMainBody.bodyDisplayName).ToUpper() + '\n');
            
            // Situation (no label)
            sb.Append(FlightGlobals.ActiveVessel.situation.ToString().ToUpper() + '\n');
            
            // Altitude - use smart formatting
            sb.Append(FormatDistanceSmart(FlightGlobals.ActiveVessel.altitude, CinematicShadersUIStrings.Kartographer.SituationAltPrefix) + '\n');
            
            // Apoapsis/Periapsis
            if (FlightGlobals.ActiveVessel.orbit != null)
            {
                double ap = FlightGlobals.ActiveVessel.orbit.ApA;
                double pe = FlightGlobals.ActiveVessel.orbit.PeA;
                
                sb.Append(FormatDistanceSmart(ap, CinematicShadersUIStrings.Kartographer.SituationApoapsisPrefix) + '\n');
                sb.Append(FormatDistanceSmart(pe, CinematicShadersUIStrings.Kartographer.SituationPeriapsisPrefix));
            }
            
            return sb.ToString();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            ModFileLogger.Log("CinematicShadersAddon destroying");
            ModFileLogger.Shutdown();
            
            CancelInvoke(nameof(RetryInit));

            GameEvents.onGUIApplicationLauncherReady.Remove(OnGUIApplicationLauncherReady);
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnLevelWasLoadedGUIReady);
            GameEvents.onGameStateLoad.Remove(OnGameStateLoad);
            GameEvents.onGameStateSave.Remove(OnGameStateSave);
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);

            if (_toolbarButton != null && ApplicationLauncher.Instance != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(_toolbarButton);
                _toolbarButton = null;
            }

            if (_mainWindow != null && _mainWindow.gameObject != null)
                Destroy(_mainWindow.gameObject);

            // Shutdown GTAO
            try
            {
                if (GTAONative.IsLoaded)
                    GTAONative.CR_GTAOShutdown();
            }
            catch (System.Exception)
            {
                /* DLL already unloaded, ignore */
            }

            // Shutdown Starfield
            try
            {
                if (StarfieldNative.IsLoaded)
                    StarfieldNative.CR_StarfieldShutdown();
            }
            catch (System.Exception)
            {
                /* DLL already unloaded, ignore */
            }

            Instance = null;
        }

        private void OnGameStateLoad(ConfigNode node)
        {
            Debug.Log("[CinematicShaders] Game state loaded - applying per-save settings");
            
            // Apply per-save settings from ScenarioModule if available
            if (StarfieldPerSaveSettings.Instance != null)
            {
                Debug.Log("[CinematicShaders] Found StarfieldPerSaveSettings, applying...");
                StarfieldPerSaveSettings.Instance.ApplyToSettings();
            }
            else
            {
                Debug.LogWarning("[CinematicShaders] StarfieldPerSaveSettings.Instance is null!");
            }
            
            Debug.Log($"[CinematicShaders] After per-save settings: EnableStarfield={StarfieldSettings.EnableStarfield}, Catalog={StarfieldSettings.ActiveCatalogPath}");
            
            // Initialize starfield if enabled and we're in a playable scene
            if (StarfieldSettings.EnableStarfield && IsPlayableScene())
            {
                Debug.Log("[CinematicShaders] Initializing Starfield...");
                StarfieldManager.Initialize();
            }
        }

        private void OnGameStateSave(ConfigNode node)
        {
            Debug.Log("[CinematicShaders] Game state saving - capturing per-save settings");
            // Per-save settings are automatically saved by KSP from StarfieldPerSaveSettings.Instance
        }

        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            // Fires before the scene switch: statics and HighLogic.LoadedScene are still
            // old-scene, so Save() captures into the correct (old) scene profile (#035).
            Debug.Log($"[CinematicShaders] Scene load requested ({scene}) - saving settings while {HighLogic.LoadedScene} is current");
            GTAOSettings.Save();
            StarfieldSettings.Save();
        }



        private void OnGUIApplicationLauncherReady()
        {
            if (_toolbarButton != null || Instance != this) return;

            if (ApplicationLauncher.Instance != null)
            {
                _toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                    OnToolbarButtonOn,
                    OnToolbarButtonOff,
                    null, null, null, null,
                    ApplicationLauncher.AppScenes.ALWAYS,
                    _toolbarIcon
                );
            }
        }

        private void OnToolbarButtonOn()
        {
            if (_mainWindow == null)
            {
                GameObject go = new GameObject("CinematicShadersWindow");
                // NOTE: Removed DontDestroyOnLoad - window is recreated per scene
                _mainWindow = go.AddComponent<CinematicShadersWindow>();
                _mainWindow.OnClose += () =>
                {
                    if (_toolbarButton != null)
                        _toolbarButton.SetFalse(false);
                    
                    // Trigger cubemap update when UI closes (visual settings may have changed)
                    CubemapGenerationScheduler.OnUIClose();
                };
            }
            _mainWindow.Show();
        }

        private void OnToolbarButtonOff()
        {
            if (_mainWindow != null)
            {
                _mainWindow.Hide();
            }
        }

        /// <summary>
        /// Get the current target tracker screen position for debug purposes.
        /// Returns (-1, -1) if no target is set or not visible.
        /// </summary>
        public Vector2? GetTargetTrackerScreenPos()
        {
            if (_vesselTargetSelector == null || !_vesselTargetSelector.IsTrackingTarget)
                return null;
            
            Vector2 uv = _vesselTargetSelector.TargetScreenUV;
            if (uv.x < 0 || uv.y < 0)
                return null;
            
            // Convert UV to NDC (matching what the shader uses)
            float aspect = StarfieldCompositor.CameraAspect;
            float ndcX = (uv.x - 0.5f) * 2.0f * aspect;
            float ndcY = (uv.y - 0.5f) * 2.0f;
            return new Vector2(ndcX, ndcY);
        }
    }
}