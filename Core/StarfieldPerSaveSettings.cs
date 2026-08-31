using System.Linq;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Per-save settings for Starfield - persisted to the save file via ScenarioModule
    /// These are the visual rendering settings and active catalog, not generation params
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames | ScenarioCreationOptions.AddToExistingGames,
        GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public class StarfieldPerSaveSettings : ScenarioModule
    {
        private static StarfieldPerSaveSettings _instance;
        public static StarfieldPerSaveSettings Instance
        {
            get
            {
                // Lazy resolution fallback if the static reference was lost (e.g., scene transition timing)
                if (_instance == null && ScenarioRunner.Instance != null)
                {
                    _instance = ScenarioRunner.GetLoadedModules().OfType<StarfieldPerSaveSettings>().FirstOrDefault();
                }
                return _instance;
            }
        }

        // Per-save: Visual rendering settings
        [KSPField(isPersistant = true)]
        public bool EnableStarfield = true;

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

        [KSPField(isPersistant = true)]
        public float ExtinctionFactor = 1.0f;

        [KSPField(isPersistant = true)]
        public float DimmingFactor = 1.0f;

        // Per-save: Active catalog
        private const string DefaultCatalogPath = "GameData/CinematicShaders/PluginData/StarCatalogs/hyg_v42.bin";

        [KSPField(isPersistant = true)]
        public string ActiveCatalogPath = DefaultCatalogPath;

        //[KSPField(isPersistant = true)]
        //public bool IsReadOnly = false;

        public override void OnAwake()
        {
            base.OnAwake();

            // Scene transitions destroy and recreate the module; log if we replace a live instance
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[StarfieldPerSaveSettings] Replacing instance (scene transition)");
            }
            _instance = this;
        }

        public void OnDestroy()
        {
            // Clear the static reference so it never dangles to a destroyed instance
            if (_instance == this)
            {
                _instance = null;
            }
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
            StarfieldSettings.ExtinctionFactor = ExtinctionFactor;
            StarfieldSettings.DimmingFactor = DimmingFactor;
            StarfieldSettings.ActiveCatalogPath = ActiveCatalogPath;
            // StarfieldSettings.IsReadOnly = IsReadOnly;
            
            // NOTE: Kartographer settings are NOT per-save - they persist via Settings.cfg
            // Do NOT add them here or they will override user settings on scene change
            
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
            ExtinctionFactor = StarfieldSettings.ExtinctionFactor;
            DimmingFactor = StarfieldSettings.DimmingFactor;
            ActiveCatalogPath = StarfieldSettings.NormalizeCatalogPath(StarfieldSettings.ActiveCatalogPath);
            // IsReadOnly = StarfieldSettings.IsReadOnly;
            
            // NOTE: Kartographer settings are NOT per-save - they persist via Settings.cfg
            // Do NOT capture them here
        }

        public override void OnSave(ConfigNode node)
        {
            // Stock ScenarioModule.Save() serializes the [KSPField]s via Fields.Save(node)
            // BEFORE invoking OnSave (KSPSOURCE/ScenarioModule.cs:87-88), and the game
            // captures scenario data before onGameStateSave fires, so the values already
            // written to this node are stale. Capture runtime state now, then overwrite
            // the serialized values with the fresh ones.
            CaptureFromSettings();
            foreach (BaseField field in Fields)
            {
                if (!field.isPersistant || field.uiControlOnly) continue;
                node.SetValue(field.name, field.GetStringValue(this, false), true);
            }
            base.OnSave(node);
            Debug.Log($"[StarfieldPerSaveSettings] OnSave - EnableStarfield={EnableStarfield}, Catalog={ActiveCatalogPath}");
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            // Migration: saves predating this module get an empty node (injected via
            // AddToExistingGames), and brand-new saves start the same way. These visual
            // settings are per-save only (not in Settings.cfg), so seed from the module's
            // code defaults - freshly constructed fields already hold them - rather than
            // the statics, which may still carry the previously loaded save's values.
            if (!node.HasValue("EnableStarfield"))
            {
                Debug.Log("[StarfieldPerSaveSettings] OnLoad - no saved data, seeding defaults");
            }

            // Applies loaded values; in the migration path this resets any stale statics
            // to the defaults and triggers the catalog-reload invalidation a save load requires
            ApplyToSettings();
        }
    }
}
