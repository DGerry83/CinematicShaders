using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CinematicShaders.Native;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Manages star catalog binary files - save, load, enumerate
    /// </summary>
    public static class StarCatalogManager
    {
        // Binary format constants
        private const uint MAGIC = 0x53545243; // 'STRC'
        private const ushort VERSION = 4;       // Version 4: includes Flags (IsHero)
        private const int HEADER_SIZE = 256;
        private const int STAR_SIZE = 48; // sizeof(StarDataNative)
        
        [Flags]
        private enum CatalogFlags : ushort
        {
            None = 0,
            ReadOnly = 1,
            HasCustomName = 2
        }
        
        /// <summary>
        /// Raised when the active catalog changes
        /// </summary>
        public static event Action OnCatalogChanged;
        
        /// <summary>
        /// Currently active/loaded catalog metadata
        /// </summary>
        public static StarCatalogInfo ActiveCatalog { get; set; }
        
        /// <summary>
        /// True if current catalog has been modified since last save
        /// </summary>
        public static bool IsDirty { get; set; }
        
        /// <summary>
        /// Gets the folder path where catalogs are stored
        /// </summary>
        public static string CatalogFolderPath
        {
            get
            {
                string pluginData = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "CinematicShaders", "PluginData");
                string catalogFolder = Path.Combine(pluginData, "StarCatalogs");
                return catalogFolder;
            }
        }
        
        /// <summary>
        /// Initialize and ensure folder exists
        /// </summary>
        public static void Initialize()
        {
            try
            {
                if (!Directory.Exists(CatalogFolderPath))
                {
                    Directory.CreateDirectory(CatalogFolderPath);
                    Debug.Log($"[CinematicShaders] Created star catalog folder: {CatalogFolderPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to create catalog folder: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Get all available catalogs in the folder
        /// </summary>
        public static List<StarCatalogInfo> GetAvailableCatalogs()
        {
            var catalogs = new List<StarCatalogInfo>();
            
            try
            {
                if (!Directory.Exists(CatalogFolderPath))
                    return catalogs;
                
                var files = Directory.GetFiles(CatalogFolderPath, "*.bin");
                foreach (var file in files)
                {
                    var info = ReadCatalogHeader(file);
                    if (info != null)
                        catalogs.Add(info);
                }
                
                // Sort by creation date, newest first
                catalogs.Sort((a, b) => b.CreatedDate.CompareTo(a.CreatedDate));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Error enumerating catalogs: {ex.Message}");
            }
            
            return catalogs;
        }
        
        /// <summary>
        /// Read only the header/metadata from a catalog file
        /// </summary>
        public static StarCatalogInfo ReadCatalogHeader(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(fs))
                {
                    // Read magic
                    uint magic = reader.ReadUInt32();
                    if (magic != MAGIC)
                    {
                        Debug.LogWarning($"[CinematicShaders] Invalid catalog file: {filePath}");
                        return null;
                    }
                    
                    ushort version = reader.ReadUInt16();
                    ushort flags = reader.ReadUInt16();
                    int starCount = reader.ReadInt32();
                    int heroCount = reader.ReadInt32();
                    int generationSeed = reader.ReadInt32();
                    
                    // Read generation params
                    float minMag = reader.ReadSingle();
                    float maxMag = reader.ReadSingle();
                    float magBias = reader.ReadSingle();
                    float clustering = reader.ReadSingle();
                    float popBias = reader.ReadSingle();
                    float mainSeqStr = reader.ReadSingle();
                    float redGiantFrequency = reader.ReadSingle();
                    float galacticFlatness = reader.ReadSingle();
                    
                    // Skip to display name (offset 52: after magic(4)+version(2)+flags(2)+count(4)+heroes(4)+seed(4)+params(32))
                    fs.Seek(52, SeekOrigin.Begin);
                    byte[] nameBytes = reader.ReadBytes(64);
                    string displayName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
                    
                    // Read date
                    byte[] dateBytes = reader.ReadBytes(32);
                    string dateStr = Encoding.UTF8.GetString(dateBytes).TrimEnd('\0');
                    DateTime createdDate;
                    if (!DateTime.TryParse(dateStr, out createdDate))
                        createdDate = File.GetCreationTime(filePath);
                    
                    return new StarCatalogInfo
                    {
                        FilePath = filePath,
                        DisplayName = displayName,
                        IsReadOnly = (flags & (ushort)CatalogFlags.ReadOnly) != 0,
                        StarCount = starCount,
                        HeroCount = heroCount,
                        GenerationSeed = generationSeed,
                        MinMagnitude = minMag,
                        MaxMagnitude = maxMag,
                        MagnitudeBias = magBias,
                        Clustering = clustering,
                        PopulationBias = popBias,
                        MainSequenceStrength = mainSeqStr,
                        RedGiantFrequency = redGiantFrequency,
                        GalacticFlatness = galacticFlatness,
                        CreatedDate = createdDate
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Error reading catalog header: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Load a catalog from disk and upload to GPU
        /// </summary>
        public static bool LoadCatalog(string filePath)
        {
            try
            {
                var info = ReadCatalogHeader(filePath);
                if (info == null)
                    return false;
                
                // Read star data
                StarfieldNative.StarDataNative[] stars;
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Seek(HEADER_SIZE, SeekOrigin.Begin);
                    
                    stars = new StarfieldNative.StarDataNative[info.StarCount];
                    byte[] buffer = new byte[STAR_SIZE * info.StarCount];
                    int read = fs.Read(buffer, 0, buffer.Length);
                    
                    if (read != buffer.Length)
                    {
                        Debug.LogError($"[CinematicShaders] Catalog file truncated: {filePath}");
                        return false;
                    }
                    
                    // Marshal bytes to structs
                    for (int i = 0; i < info.StarCount; i++)
                    {
                        IntPtr ptr = Marshal.AllocHGlobal(STAR_SIZE);
                        try
                        {
                            Marshal.Copy(buffer, i * STAR_SIZE, ptr, STAR_SIZE);
                            stars[i] = Marshal.PtrToStructure<StarfieldNative.StarDataNative>(ptr);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(ptr);
                        }
                    }
                }
                
                // Upload to native plugin
                StarfieldNative.LoadCatalog(stars, info.HeroCount);
                
                ActiveCatalog = info;
                IsDirty = false;
                OnCatalogChanged?.Invoke();
                
                Debug.Log($"[CinematicShaders] Loaded catalog: {info.GetDisplayName()} ({info.StarCount} stars)");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to load catalog: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Save current catalog to disk
        /// </summary>
        public static bool SaveCatalog(string filePath, string displayName, bool readOnly)
        {
            try
            {
                // Ensure directory exists
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                int count = StarfieldNative.GetCatalogSize();
                int heroCount = StarfieldNative.GetHeroCount();
                
                if (count <= 0)
                {
                    Debug.LogWarning("[CinematicShaders] No catalog to save");
                    return false;
                }
                
                // Get star data from native plugin
                StarfieldNative.StarDataNative[] stars = StarfieldNative.GetCatalogData(count);
                if (stars == null || stars.Length != count)
                {
                    Debug.LogError("[CinematicShaders] Failed to get catalog data from native");
                    return false;
                }
                
                // Build header
                ushort flags = (ushort)(readOnly ? CatalogFlags.ReadOnly : CatalogFlags.None);
                if (!string.IsNullOrEmpty(displayName))
                    flags |= (ushort)CatalogFlags.HasCustomName;
                
                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(fs))
                {
                    // Write header (256 bytes total)
                    // Offset 0: Magic (4) + Version (2) + Flags (2) = 8 bytes
                    writer.Write(MAGIC);
                    writer.Write(VERSION);
                    writer.Write(flags);
                    
                    // Offset 8: Count (4) + HeroCount (4) + Seed (4) = 12 bytes, total 20
                    writer.Write(count);
                    writer.Write(heroCount);
                    writer.Write(StarfieldSettings.CatalogSeed);
                    
                    // Offset 20: Gen params (8 floats = 32 bytes), total 52
                    writer.Write(StarfieldSettings.MinMagnitude);
                    writer.Write(StarfieldSettings.MaxMagnitude);
                    writer.Write(StarfieldSettings.MagnitudeBias);
                    writer.Write(StarfieldSettings.Clustering);
                    writer.Write(StarfieldSettings.PopulationBias);
                    writer.Write(StarfieldSettings.MainSequenceStrength);
                    writer.Write(StarfieldSettings.RedGiantFrequency);
                    writer.Write(StarfieldSettings.GalacticFlatness);
                    
                    // Offset 52: Pad to 64 (12 bytes), total 64
                    writer.Write(new byte[12]);
                    
                    // Display name (64 bytes)
                    byte[] nameBytes = new byte[64];
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        byte[] nameSrc = Encoding.UTF8.GetBytes(displayName);
                        Array.Copy(nameSrc, nameBytes, Math.Min(nameSrc.Length, 63));
                    }
                    writer.Write(nameBytes);
                    
                    // Date (32 bytes) - offset 128
                    byte[] dateBytes = new byte[32];
                    string dateStr = DateTime.Now.ToString("O");
                    byte[] dateSrc = Encoding.UTF8.GetBytes(dateStr);
                    Array.Copy(dateSrc, dateBytes, Math.Min(dateSrc.Length, 31));
                    writer.Write(dateBytes);
                    
                    // Reserved (96 bytes) - offset 160 to 256
                    writer.Write(new byte[96]);
                    
                    // Write star data
                    foreach (var star in stars)
                    {
                        writer.Write(star.HipparcosID);
                        writer.Write(star.DistancePc);
                        writer.Write(star.SpectralType);
                        writer.Write(star.Flags);
                        writer.Write(star.DirectionX);
                        writer.Write(star.DirectionY);
                        writer.Write(star.DirectionZ);
                        writer.Write(star.Magnitude);
                        writer.Write(star.ColorR);
                        writer.Write(star.ColorG);
                        writer.Write(star.ColorB);
                        writer.Write(star.Temperature);
                    }
                }
                
                // Update active catalog info
                ActiveCatalog = ReadCatalogHeader(filePath);
                IsDirty = false;
                OnCatalogChanged?.Invoke();
                
                Debug.Log($"[CinematicShaders] Saved catalog: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to save catalog: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Create a new catalog with auto-generated filename
        /// </summary>
        public static string CreateNewCatalog(string displayName, bool readOnly = false)
        {
            string fileName = SanitizeFileName(displayName) + ".bin";
            string filePath = Path.Combine(CatalogFolderPath, fileName);
            
            // If file exists, append number
            int counter = 1;
            string basePath = filePath.Substring(0, filePath.Length - 4);
            while (File.Exists(filePath))
            {
                filePath = $"{basePath}_{counter}.bin";
                counter++;
            }
            
            if (SaveCatalog(filePath, displayName, readOnly))
                return filePath;
            
            return null;
        }
        
        /// <summary>
        /// Save catalog with specific filename
        /// </summary>
        public static string SaveCatalogAs(string fileName, string displayName, bool readOnly = false)
        {
            string safeName = SanitizeFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "StarCatalog";
            
            string filePath = Path.Combine(CatalogFolderPath, safeName + ".bin");
            
            // If file exists, append number
            int counter = 1;
            string basePath = Path.Combine(CatalogFolderPath, safeName);
            while (File.Exists(filePath))
            {
                filePath = $"{basePath}_{counter}.bin";
                counter++;
            }
            
            if (SaveCatalog(filePath, displayName, readOnly))
                return filePath;
            
            return null;
        }
        
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "StarCatalog";
            
            // Remove invalid filesystem characters
            string invalid = new string(Path.GetInvalidFileNameChars());
            foreach (char c in invalid)
                name = name.Replace(c, '_');
            
            // Trim whitespace and dots
            name = name.Trim().Trim('.');
            
            // Limit length
            if (name.Length > 50)
                name = name.Substring(0, 50);
            
            return string.IsNullOrWhiteSpace(name) ? "StarCatalog" : name;
        }
        
        /// <summary>
        /// Rename a catalog (updates display name in header)
        /// </summary>
        public static bool RenameCatalog(string filePath, string newDisplayName)
        {
            var info = ReadCatalogHeader(filePath);
            if (info == null)
                return false;
            
            // Re-save with new name
            // First load stars
            if (!LoadCatalog(filePath))
                return false;
            
            // Save with new name
            bool result = SaveCatalog(filePath, newDisplayName, info.IsReadOnly);
            
            // Reload to update active info
            if (result)
                LoadCatalog(filePath);
            
            return result;
        }
        
        /// <summary>
        /// Delete a catalog file
        /// </summary>
        public static bool DeleteCatalog(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    
                    // If this was the active catalog, clear it
                    if (ActiveCatalog != null && ActiveCatalog.FilePath == filePath)
                    {
                        ActiveCatalog = null;
                        OnCatalogChanged?.Invoke();
                    }
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to delete catalog: {ex.Message}");
            }
            return false;
        }
        
        /// <summary>
        /// Open the catalog folder in Explorer
        /// </summary>
        public static void OpenCatalogFolder()
        {
            try
            {
                string folder = CatalogFolderPath;
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                
                // Use ProcessStartInfo for proper argument handling
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folder}\"",
                    UseShellExecute = false
                };
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to open catalog folder: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Check if there's a "Real Sky" placeholder catalog
        /// </summary>
        public static bool HasRealSkyCatalog()
        {
            string realSkyPath = Path.Combine(CatalogFolderPath, "RealSky.bin");
            return File.Exists(realSkyPath);
        }
        
        /// <summary>
        /// Create placeholder "Real Sky" catalog (empty, marked read-only)
        /// </summary>
        public static void CreateRealSkyPlaceholder()
        {
            string realSkyPath = Path.Combine(CatalogFolderPath, "RealSky.bin");
            if (File.Exists(realSkyPath))
                return;
            
            try
            {
                // Create minimal valid catalog with 0 stars
                using (var fs = new FileStream(realSkyPath, FileMode.Create))
                using (var writer = new BinaryWriter(fs))
                {
                    writer.Write(MAGIC);
                    writer.Write(VERSION);
                    writer.Write((ushort)CatalogFlags.ReadOnly); // Read-only
                    writer.Write(0); // 0 stars
                    writer.Write(0); // 0 heroes
                    writer.Write(0); // seed
                    
                    // Rest of header is zeros
                    writer.Write(new byte[HEADER_SIZE - 16]);
                }
                
                Debug.Log("[CinematicShaders] Created RealSky placeholder catalog");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to create RealSky placeholder: {ex.Message}");
            }
        }
        
        private static string GetRandomLetters(int count)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var sb = new StringBuilder(count);
            System.Random rnd = new System.Random();
            for (int i = 0; i < count; i++)
                sb.Append(chars[rnd.Next(chars.Length)]);
            return sb.ToString();
        }
    }
}
