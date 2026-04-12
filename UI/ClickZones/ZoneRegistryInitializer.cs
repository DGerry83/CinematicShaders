namespace CinematicShaders.ClickZones
{
    /// <summary>
    /// Initializes the MultiScreenClickZoneRegistry with all screen zone sets.
    /// Call this once during application startup.
    /// </summary>
    public static class ZoneRegistryInitializer
    {
        /// <summary>
        /// Initializes the global zone registry with all screens.
        /// </summary>
        public static void Initialize()
        {
            // Clear any existing registrations
            MultiScreenClickZoneRegistry.ClearAll();
            
            // Register MainScreen with zones for all display sizes
            var mainScreenZones = ZoneSetGenerator.GenerateMainScreenZones();
            MultiScreenClickZoneRegistry.RegisterScreen("Main", mainScreenZones);
            
            // Register ScanScreen
            var scanScreenZones = ZoneSetGenerator.GenerateScanScreenZones();
            MultiScreenClickZoneRegistry.RegisterScreen("Scan", scanScreenZones);
            
            // Register ConfirmRescanScreen
            var confirmScreenZones = ZoneSetGenerator.GenerateConfirmScreenZones();
            MultiScreenClickZoneRegistry.RegisterScreen("Confirm", confirmScreenZones);
        }
    }
}
