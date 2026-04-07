namespace CinematicShaders.UI
{
    public static class CinematicShadersUIStrings
    {
        // ============================================================================
        // COMMON - Shared symbols and UI elements
        // ============================================================================
        public static class Common
        {
            public const string WindowTitle = "Cinematic Shaders";
            public const string Initializing = "Initializing...";
            
            // Symbols
            public const string CollapsedPrefix = "▶ ";
            public const string DropdownArrow = " ▼";
        }

        // ============================================================================
        // GTAO
        // ============================================================================
        public static class GTAO
        {
            public const string TabName = "GTAO";
            public const string SamplingSection = "SAMPLING";
            public const string ShadowStrengthSection = "SHADOW STRENGTH";
            public const string FilteringSection = "FILTERING";
            public const string DistanceFadeSection = "DISTANCE FADE";
            public const string AdvancedSection = "ADVANCED";
            public const string QualityLabel = "Quality";
            public const string RadiusLabel = "Radius";
            public const string RadiusTooltip = "How far to search for occluders (larger = more distant shadows)";
            public const string DetailRangeLabel = "Shadow Spread";
            public const string IntensityLabel = "Intensity";
            public const string EdgeSharpnessLabel = "Edge Sharpness";
            public const string DepthToleranceLabel = "Depth Tolerance";
            public const string StartFadeLabel = "Start Fade";
            public const string EndFadeLabel = "End Fade";
            public const string EdgeHardnessLabel = "Edge Hardness";
            public const string EdgeHardnessTooltip = "0.5=Soft, 1.0=Linear, 3.0=Sharp";
            public const string DistributionLabel = "Distribution";
            public const string EnableToggle = " Enable Ground-Truth AO";
            public const string RawAOOutputToggle = " Show Raw AO Output";
            public const string DeferredWarning = "GTAO requires deferred rendering.";
            public const string DistributionLinear = "Linear";
            public const string DistributionQuadratic = "Quadratic";
            public const string DistributionCubic = "Cubic";
            public const string QualityLow = "Low";
            public const string QualityMedium = "Medium";
            public const string QualityHigh = "High";
            public const string QualityUltra = "Ultra";
            public const string DebugVisualizationHeader = "Debug Visualization";
            public const string DebugViewLabel = "View";
            public const string DebugModeNone = "None";
            public const string DebugModeRawAO = "Raw AO";
            public const string DebugModeWorldNormals = "World Normals";
            public const string DebugModeViewNormals = "View Normals";
            public const string DebugModeNormalAlpha = "Normal Alpha";
            public const string NativeLoadError = "Native plugin failed to load. Check KSP.log for details.";
        }

        // ============================================================================
        // STARFIELDTAB - Organized by UI layout (top to bottom)
        // ============================================================================
        public static class Starfield
        {
            // ------------------------------------------------------------------------
            // GENERAL
            // ------------------------------------------------------------------------
            public const string TabName = "Starfield";
            public const string EnableToggle = " Enable Procedural Starfield";
            public const string NativeLoadError = "Native plugin failed to load. Check KSP.log for details.";
            public const string Initializing = "Initializing starfield...";

            // ------------------------------------------------------------------------
            // SECTION HEADERS (in order of appearance)
            // ------------------------------------------------------------------------
            public const string RenderingSection = "RENDERING";
            public const string MainGenerationSection = "MAIN GENERATION";
            public const string AdvancedGenerationSection = "ADVANCED GENERATION";
            public const string GalacticStructureSection = "GALACTIC STRUCTURE";
            public const string StarCatalogSection = "Star Catalog";

            // ------------------------------------------------------------------------
            // CATALOG MANAGEMENT UI (in order of appearance)
            // ------------------------------------------------------------------------
            public const string ActiveCatalogLabel = "Active Catalog";
            public const string ActiveCatalogNone = "(None)";
            public const string SaveCatalogAsTitle = "Save Catalog As:";
            public const string FilenameLabel = "Filename:";
            public const string DisplayNameLabel = "Display Name:";
            public const string DefaultCatalogFileName = "MyStarfield";
            public const string DefaultCatalogDisplayName = "My Starfield";
            
            // Buttons
            public const string CancelButton = "Cancel";
            public const string UnlockButton = "I Understand - Unlock";
            public const string SaveButton = "Save";
            public const string NewButton = "New";
            public const string SaveAsButton = "Save As...";
            public const string OpenFolderButton = "Open Folder";
            public const string DeleteCatalogButton = "Delete Catalog";

            // ------------------------------------------------------------------------
            // READ-ONLY PROTECTION UI
            // ------------------------------------------------------------------------
            public const string ReadOnlyLockMessage = "Generation parameters locked (Read-Only mode)";
            public const string ReadOnlyToggleOn = "Read-Only Protection <color=#33FF33>ON</color>";
            public const string ReadOnlyToggleOff = "Read-Only Protection <color=#FF3333>OFF</color>";
            public const string ReadOnlyWarningTitle = "WARNING: Disabling Read-Only Protection";
            public const string ReadOnlyWarningMessage = "You are about to unlock this catalog for editing. Any changes to generation parameters will PERMANENTLY modify this catalog. This cannot be undone.";

            // ------------------------------------------------------------------------
            // SLIDER LABELS (in order of appearance in UI)
            // ------------------------------------------------------------------------
            // Rendering Section
            public const string ExposureLabel = "Exposure";
            public const string BlurPixelsLabel = "Star Softness";
            public const string BloomThresholdLabel = "Bloom Threshold";
            public const string BloomIntensityLabel = "Bloom Intensity";
            public const string BloomIsotropyLabel = "Bloom Mode";
            public const string BloomModeClassic = "Classic (Spiky)";
            public const string BloomModeSoft = "Soft HDR";

            // Main Generation Section
            public const string CatalogSeedLabel = "Catalog Seed";
            public const string CatalogSizeLabel = "Catalog Size";
            public const string MinMagnitudeLabel = "Min Magnitude";
            public const string MaxMagnitudeLabel = "Max Magnitude";
            public const string HeroCountLabel = "Hero Count";
            public const string MainSequenceLabel = "Main Sequence Strength";
            public const string RedGiantFrequencyLabel = "Red Giant Frequency";
            public const string ColorSaturationLabel = "Color Saturation";
            
            // Advanced Generation Section
            public const string BrightnessDistributionLabel = "Brightness Distribution";
            public const string StellarPopulationLabel = "Stellar Population";
            public const string ClusteringLabel = "Star Clustering";
            
            // Galactic Structure Section
            public const string DiscFlatnessLabel = "Disc Flatness";
            public const string DiscFalloffLabel = "Disc Falloff";
            public const string BandCenterBoostLabel = "Band Boost";
            public const string BandCoreSharpnessLabel = "Band Sharpness";
            public const string BulgeIntensityLabel = "Bulge Intensity";
            public const string BulgeWidthLabel = "Bulge Width";
            public const string BulgeHeightLabel = "Bulge Height";
            public const string BulgeSoftnessLabel = "Bulge Softness";
            public const string BulgeNoiseScaleLabel = "Bulge Noise Scale";
            public const string BulgeNoiseStrengthLabel = "Bulge Noise Strength";

            // ------------------------------------------------------------------------
            // TOOLTIPS (grouped at bottom, in order of corresponding UI elements)
            // ------------------------------------------------------------------------
            // Rendering Section Tooltips
            public const string ExposureTooltip = "EV Stops";
            public const string BlurPixelsTooltip = "Angular size of star blur";
            public const string BloomThresholdTooltip = "HDR values above this trigger bloom";
            public const string BloomIntensityTooltip = "Bloom strength";
            public const string BloomIsotropyTooltip = "Classic uses original 4-spike, Soft uses 2-pass blur";

            // Main Generation Section Tooltips
            public const string CatalogSeedTooltip = "Random seed for star placement";
            public const string CatalogSizeTooltip = "Number of stars to generate";
            public const string HeroCountTooltip = "Number of bright hero stars (named/important stars)";
            public const string MainSequenceTooltip = "0.0=Wild West (any star type), 1.0=Strict (bright stars must be hot)";
            public const string ColorSaturationTooltip = "0.5=Realistic, 1.0=Slight Boost, 2.0=Vivid, 4.0=Hyper-saturated";
            
            // Advanced Generation Section Tooltips
            public const string StellarPopulationTooltip = "Star age bias: shift toward old/red or young/blue stars";

            // Debug
            public const string DebugAtmosphereButton = "Dump Atmosphere Data";
            public const string DebugAtmosphereTooltip = "Log atmospheric scattering data to KSP.log for debugging";
            
            // Coordinate Rotation
            public const string CoordinateRotationSection = "HYG CATALOG ROTATION";
            public const string RotationXLabel = "Rotation X (Tilt)";
            public const string RotationYLabel = "Rotation Y (Yaw)";
            public const string RotationZLabel = "Rotation Z (Roll)";
            public const string RotationTooltip = "Adjust to align real sky catalog with game coordinates";
        }

        // ============================================================================
        // KARTOGRAPHER - Holographic grid visualizer
        // ============================================================================
        public static class Kartographer
        {
            // ------------------------------------------------------------------------
            // GENERAL
            // ------------------------------------------------------------------------
            public const string TabName = "Kartographer";
            public const string EnableToggleLabel = " Enable Kartographer";
            public const string StarCatalogToggle = " Star Catalog";
            public const string VesselTargetToggle = " Show Vessel Target";
            public const string ResetButton = "Reset to Defaults";
            public const string NativeLoadError = "Native plugin failed to load. Check KSP.log for details.";
            public const string Initializing = "Initializing Kartographer...";

            // ------------------------------------------------------------------------
            // SECTION HEADERS
            // ------------------------------------------------------------------------
            public const string DisplayOptionsSection = " ▼ Display Options";
            public const string SituationDisplaySection = " ▼ Situation Display";
            public const string NavballIndicatorsSection = " ▼ Navball Indicators";

            // ------------------------------------------------------------------------
            // SITUATION DISPLAY
            // ------------------------------------------------------------------------
            public const string RotationStepFormat = "Rotation Step: {0} / {1}";
            public const string DisplayHeightFormat = "Display Height: {0}";
            public static readonly string[] RowOffsetLabels = { "-2 (Down)", "-1 (Down)", "0 (Default)", "+1 (Up)", "+2 (Up)" };

            // ------------------------------------------------------------------------
            // NAVBALL
            // ------------------------------------------------------------------------
            public const string NavballColorsToggle = " Use Navball Colors";
            public const string IconStyleLabel = "Icon Style:";
            public static readonly string[] IconStyleNames = { "KSP", "Retro" };
            public const string IconThicknessFormat = "Icon Thickness: {0:F1}";
            public const string IconSizeFormat = "Icon Size: {0:F1}";
            public const string HeadingIndicatorFormat = "Heading Indicator Size: {0:F1}";
            public const string ManeuverOffsetFormat = "Maneuver Text Offset: {0:F2}";
            public const string ManeuverScaleFormat = "Maneuver Text Scale: {0:F2}";

            // ------------------------------------------------------------------------
            // DISPLAY OPTIONS
            // ------------------------------------------------------------------------
            public const string GridSizeFormat = "Grid Size: {0}";
            public const string GridSizeTooltip = "Density of the holographic grid lines";
            public const string GridIntensityFormat = "Grid Intensity: {0:F1}";
            public const string GridIntensityTooltip = "Brightness of the holographic grid lines";
            public const string GridSoftnessFormat = "Grid Softness: {0:F1}";
            public const string GridSoftnessTooltip = "Softness of the grid lines (higher = softer, lower = sharper)";
            public const string VignetteStrengthFormat = "Vignette Strength: {0:F2}";
            public const string VignetteStrengthTooltip = "Darkening at screen corners (0 = no vignette, 1 = black corners)";
            public const string VignetteStartFormat = "Vignette Start: {0:F2}";
            public const string VignetteStartTooltip = "Distance from center where vignette begins";
            public const string VignetteEndFormat = "Vignette End: {0:F2}";
            public const string VignetteEndTooltip = "Distance from center where vignette reaches full strength";
            public const string DisplayColorLabel = "Display Color";

            // ------------------------------------------------------------------------
            // GRID SIZE LABELS
            // ------------------------------------------------------------------------
            public const string GridSizeJumbo = "Jumbo";
            public const string GridSizeLarge = "Large";
            public const string GridSizeMedium = "Medium";
            public const string GridSizeSmall = "Small";
            public const string GridSizeTiny = "Tiny";
            public static readonly string[] GridSizeLabels = { GridSizeJumbo, GridSizeLarge, GridSizeMedium, GridSizeSmall, GridSizeTiny };

            // ------------------------------------------------------------------------
            // COLOR NAMES
            // ------------------------------------------------------------------------
            public const string ColorSeafoam = "Seafoam";
            public const string ColorAmber = "Amber";
            public const string ColorWhite = "White";
            public const string ColorGreen = "Green";
            public static readonly string[] ColorNames = { ColorSeafoam, ColorAmber, ColorWhite, ColorGreen };

            // ------------------------------------------------------------------------
            // STAR CATALOG EDITOR
            // ------------------------------------------------------------------------
            public const string StarCatalogEditorTitle = "STAR CATALOG EDITOR";
            public const string StarCatalogEditorButton = "EDIT NAMES";
            public const string SearchLabel = "SEARCH:";
            public const string SelectStarPrompt = "SELECT A STAR TO EDIT";
            
            // FIELD ORDER: HIP, NAME, DISTANCE, SPECTRAL, MAGNITUDE, CONSTELLATION
            public const string HipLabel = "HIP:";
            public const string NameLabel = "NAME:";
            public const string DistanceLabel = "DISTANCE:";
            public const string SpectralLabel = "SPECTRAL:";
            public const string MagnitudeLabel = "MAGNITUDE:";
            public const string ConstellationLabel = "CONSTELLATION:";
            
            public const string SaveButton = "SAVE";
            public const string EditNamePrompt = "EDIT NAME:";
            
            // Empty States
            public const string EnterTermsMessage = "ENTER TERMS";
            public const string NoResultMessage = "NO RESULT";
        }
    }
}
