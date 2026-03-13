using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Per-save settings for Starfield - persisted to the save file via ScenarioModule
    /// These are the visual rendering settings and active catalog, not generation params
    /// </summary>
    public class StarfieldPerSaveSettings : ScenarioModule
    {
        private static StarfieldPerSaveSettings _instance;
        public static StarfieldPerSaveSettings Instance => _instance;

        // Per-save: Visual rendering settings
        [KSPField(isPersistant = true)]
        public bool EnableStarfield = false;

        [KSPField(isPersistant = true)]
        public float Exposure = 3.0f;

        [KSPField(isPersistant = true)]
        public float BlurPixels = 0.00029f;

        [KSPField(isPersistant = true)]
        public float BloomThreshold = 0.08f;

        [KSPField(isPersistant = true)]
        public float BloomIntensity = 0.5f;

        [KSPField(isPersistant = true)]
        public float ColorSaturation = 1.0f;

        // Per-save: Active catalog
        [KSPField(isPersistant = true)]
        public string ActiveCatalogPath = "";

        //[KSPField(isPersistant = true)]
        //public bool IsReadOnly = false;

        public override void OnAwake()
        {
            base.OnAwake();
            _instance = this;
        }

        /// <summary>
        /// Apply per-save settings to the static StarfieldSettings
        /// Called when a save is loaded
        /// </summary>
        public void ApplyToSettings()
        {
            StarfieldSettings.EnableStarfield = EnableStarfield;
            StarfieldSettings.Exposure = Exposure;
            StarfieldSettings.BlurPixels = BlurPixels;
            StarfieldSettings.BloomThreshold = BloomThreshold;
            StarfieldSettings.BloomIntensity = BloomIntensity;
            StarfieldSettings.ColorSaturation = ColorSaturation;
            StarfieldSettings.ActiveCatalogPath = ActiveCatalogPath;
            // StarfieldSettings.IsReadOnly = IsReadOnly;
            
            // Mark catalog for reload since we're changing saves
            StarfieldSettings.InvalidateCatalogForReload();

            Debug.Log($"[StarfieldPerSaveSettings] Applied per-save settings: Enabled={EnableStarfield}, Catalog={ActiveCatalogPath}");
        }

        /// <summary>
        /// Capture current settings from StarfieldSettings
        /// Called before saving
        /// </summary>
        public void CaptureFromSettings()
        {
            EnableStarfield = StarfieldSettings.EnableStarfield;
            Exposure = StarfieldSettings.Exposure;
            BlurPixels = StarfieldSettings.BlurPixels;
            BloomThreshold = StarfieldSettings.BloomThreshold;
            BloomIntensity = StarfieldSettings.BloomIntensity;
            ColorSaturation = StarfieldSettings.ColorSaturation;
            ActiveCatalogPath = StarfieldSettings.ActiveCatalogPath;
            // IsReadOnly = StarfieldSettings.IsReadOnly;
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            CaptureFromSettings();
            Debug.Log("[StarfieldPerSaveSettings] OnSave - captured settings to save file");
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            // Settings are loaded from the [KSPField] attributes automatically
            // We just need to apply them
            ApplyToSettings();
            Debug.Log("[StarfieldPerSaveSettings] OnLoad - applied settings from save file");
        }
    }
}
