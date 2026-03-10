using System;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Metadata for a star catalog file
    /// </summary>
    public class StarCatalogInfo
    {
        public string FilePath { get; set; }
        public string DisplayName { get; set; }
        public bool IsReadOnly { get; set; }
        public DateTime CreatedDate { get; set; }
        public int StarCount { get; set; }
        public int HeroCount { get; set; }
        public int GenerationSeed { get; set; }
        
        // Generation parameters stored for "Clone & Modify" functionality
        public float MinMagnitude { get; set; }
        public float MaxMagnitude { get; set; }
        public float MagnitudeBias { get; set; }
        public float Clustering { get; set; }
        public float PopulationBias { get; set; }
        public float MainSequenceStrength { get; set; }
        public float RedGiantRarity { get; set; }
        public float GalacticFlatness { get; set; }
        
        /// <summary>
        /// Returns the display name, or filename if no custom name set
        /// </summary>
        public string GetDisplayName()
        {
            if (!string.IsNullOrEmpty(DisplayName))
                return DisplayName;
            
            // Extract filename without extension
            try
            {
                return System.IO.Path.GetFileNameWithoutExtension(FilePath);
            }
            catch
            {
                return FilePath;
            }
        }
        
        /// <summary>
        /// Returns string for dropdown with read-only indicator
        /// </summary>
        public string GetDropdownLabel()
        {
            string name = GetDisplayName();
            if (IsReadOnly)
                return "🔒 " + name;
            return name;
        }
    }
}
