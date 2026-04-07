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
        private const ushort VERSION = 6;       // Version 6: includes IsProcedural flag
        private const int HEADER_SIZE = 256;
        private const int STAR_SIZE = 48; // sizeof(StarDataNative)
        
        [Flags]
        private enum CatalogFlags : ushort
        {
            None = 0,
            ReadOnly = 1,
            HasCustomName = 2,
            IsProcedural = 4  // Bit 2: true for procedural, false for intentional (real sky/curated)
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
                    
                    // Read rotation values (Version 5+) from offset 52
                    float rotationX = 0.0f;
                    float rotationY = 0.0f;
                    float rotationZ = 0.0f;
                    if (version >= 5)
                    {
                        rotationX = reader.ReadSingle();
                        rotationY = reader.ReadSingle();
                        rotationZ = reader.ReadSingle();
                    }
                    
                    // Skip to display name (offset 52 for v4, offset 64 for v5: after magic(4)+version(2)+flags(2)+count(4)+heroes(4)+seed(4)+params(32)+rotation(12))
                    int headerDataOffset = (version >= 5) ? 64 : 52;
                    fs.Seek(headerDataOffset, SeekOrigin.Begin);
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
                        IsProcedural = (flags & (ushort)CatalogFlags.IsProcedural) != 0,
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
                        RotationX = rotationX,
                        RotationY = rotationY,
                        RotationZ = rotationZ,
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
                
                // Apply catalog rotation to settings (catalog orientation is part of the catalog)
                // Rotation is available for both procedural and intentional catalogs
                StarfieldSettings.RotationX = info.RotationX;
                StarfieldSettings.RotationY = info.RotationY;
                StarfieldSettings.RotationZ = info.RotationZ;
                
                // For procedural catalogs, also sync generation params
                // For intentional catalogs (real sky), generation params are meaningless
                if (info.IsProcedural)
                {
                    StarfieldSettings.MinMagnitude = info.MinMagnitude;
                    StarfieldSettings.MaxMagnitude = info.MaxMagnitude;
                    StarfieldSettings.MagnitudeBias = info.MagnitudeBias;
                    StarfieldSettings.Clustering = info.Clustering;
                    StarfieldSettings.PopulationBias = info.PopulationBias;
                    StarfieldSettings.MainSequenceStrength = info.MainSequenceStrength;
                    StarfieldSettings.RedGiantFrequency = info.RedGiantFrequency;
                    StarfieldSettings.GalacticFlatness = info.GalacticFlatness;
                }
                
                ActiveCatalog = info;
                StarfieldSettings.IsReadOnly = info.IsReadOnly; // Per-catalog flag, not per-save
                IsDirty = false;
                OnCatalogChanged?.Invoke();
                
                Debug.Log($"[CinematicShaders] Loaded catalog: {info.GetDisplayName()} ({info.StarCount} stars)");
                
                // Trigger cubemap update for new catalog
                CubemapGenerationScheduler.OnCatalogLoaded();
                
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
                // Preserve IsProcedural flag from active catalog, or default to true for new saves
                if (ActiveCatalog != null && ActiveCatalog.IsProcedural)
                    flags |= (ushort)CatalogFlags.IsProcedural;
                else if (ActiveCatalog == null)
                    flags |= (ushort)CatalogFlags.IsProcedural; // New catalogs default to procedural
                
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
                    
                    // Offset 52: Rotation X/Y/Z (3 floats = 12 bytes), total 64
                    writer.Write(StarfieldSettings.RotationX);
                    writer.Write(StarfieldSettings.RotationY);
                    writer.Write(StarfieldSettings.RotationZ);
                    
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
                
                // Trigger cubemap update after save
                CubemapGenerationScheduler.OnCatalogSaved();
                
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

                // Use Unity's OpenURL with file:// protocol for reliable folder opening
                // This works more consistently than Process.Start with explorer.exe
                string url = "file:///" + folder.Replace("\\", "/");
                Application.OpenURL(url);
                Debug.Log($"[CinematicShaders] Opening catalog folder: {folder}");
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
        
        /// <summary>
        /// Generate a JSON sidecar file for a procedural catalog.
        /// Only works on catalogs with IsProcedural flag set.
        /// Creates minimal JSON with KIP IDs and direction vectors,
        /// leaving name fields blank for user editing.
        /// </summary>
        /// <param name="binPath">Path to the .bin file</param>
        /// <returns>True if JSON was created successfully</returns>
        public static bool GenerateJsonForProceduralCatalog(string binPath)
        {
            // 1. Verify file exists
            if (!File.Exists(binPath))
            {
                Debug.LogError($"[CinematicShaders] Cannot generate JSON - file not found: {binPath}");
                return false;
            }
            
            try
            {
                using (var fs = new FileStream(binPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(fs))
                {
                    // 2. Read header (256 bytes)
                    // Check magic (0x53545243)
                    uint magic = reader.ReadUInt32();
                    if (magic != MAGIC)
                    {
                        Debug.LogWarning($"[CinematicShaders] Invalid catalog file (bad magic): {binPath}");
                        return false;
                    }
                    
                    // Check version (support 4+)
                    ushort version = reader.ReadUInt16();
                    if (version < 4)
                    {
                        Debug.LogWarning($"[CinematicShaders] Catalog version {version} not supported (need 4+): {binPath}");
                        return false;
                    }
                    
                    // Check flags for IsProcedural (bit 2)
                    ushort flags = reader.ReadUInt16();
                    bool isProcedural = (flags & (ushort)CatalogFlags.IsProcedural) != 0;
                    if (!isProcedural)
                    {
                        Debug.LogWarning($"[CinematicShaders] Catalog is not procedural, skipping JSON generation: {binPath}");
                        return false;
                    }
                    
                    // Get starCount from header
                    int totalStarCount = reader.ReadInt32();
                    if (totalStarCount <= 0)
                    {
                        Debug.LogWarning($"[CinematicShaders] Catalog has no stars: {binPath}");
                        return false;
                    }
                    
                    // Limit to first 5000 stars (covers heroes plus extras)
                    // Hero stars are first in procedurally generated catalogs
                    int starCount = Math.Min(totalStarCount, 5000);
                    
                    // 3. Read star records (starting at offset 256)
                    fs.Seek(HEADER_SIZE, SeekOrigin.Begin);
                    
                    // Spectral type conversion: 0=O, 1=B, 2=A, 3=F, 4=G, 5=K, 6=M, 7=L
                    string[] spectralLetters = { "O", "B", "A", "F", "G", "K", "M", "L" };
                    
                    // Build JSON using StringBuilder
                    var json = new StringBuilder();
                    string fileName = Path.GetFileName(binPath);
                    string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                    
                    // Metadata header
                    json.Append("{\n");
                    json.Append("  \"metadata\": {\n");
                    json.Append("    \"version\": 1,\n");
                    json.Append("    \"source_catalog\": \"Generated\",\n");
                    json.Append($"    \"bin_file\": \"./{fileName}\",\n");
                    json.Append($"    \"star_count\": {totalStarCount},\n");
                    json.Append($"    \"named_star_count\": {starCount},\n");
                    json.Append("    \"constellation_count\": 0,\n");
                    json.Append($"    \"generated\": \"{timestamp}\"\n");
                    json.Append("  },\n");
                    json.Append("  \"stars\": {\n");
                    
                    // Read each star (48 bytes per star)
                    for (int i = 0; i < starCount; i++)
                    {
                        // Read star data fields
                        int hipparcosID = reader.ReadInt32();      // offset 0
                        float distancePc = reader.ReadSingle();    // offset 4
                        float distanceLy = distancePc * 3.26156f;    // Convert parsecs to light years
                        int spectralType = reader.ReadInt32();     // offset 8
                        uint starFlags = reader.ReadUInt32();      // offset 12 (skip)
                        float dirX = reader.ReadSingle();          // offset 16
                        float dirY = reader.ReadSingle();          // offset 20 - NEGATE this (Y-flip)
                        float dirZ = reader.ReadSingle();          // offset 24
                        float magnitude = reader.ReadSingle();     // offset 28
                        float colorR = reader.ReadSingle();        // offset 32 (skip)
                        float colorG = reader.ReadSingle();        // offset 36 (skip)
                        float colorB = reader.ReadSingle();        // offset 40 (skip)
                        float temperature = reader.ReadSingle();   // offset 44 (skip)
                        
                        // Use numeric ID as key (parser expects integer IDs)
                        string starKey = hipparcosID.ToString();
                        
                        // Build star entry
                        json.Append($"    \"{starKey}\": {{ ");
                        
                        // Spectral type (omit if 255 = unknown)
                        if (spectralType >= 0 && spectralType <= 7)
                        {
                            json.Append($"\"spectral\": \"{spectralLetters[spectralType]}\", ");
                        }
                        // If 255 or out of range, omit spectral field
                        
                        // Magnitude
                        json.Append($"\"magnitude\": {magnitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}, ");
                        
                        // Distance in light years
                        json.Append($"\"distance_ly\": {distanceLy.ToString(System.Globalization.CultureInfo.InvariantCulture)}, ");
                        
                        // Direction vectors (Y is negated)
                        json.Append($"\"x\": {dirX.ToString(System.Globalization.CultureInfo.InvariantCulture)}, ");
                        json.Append($"\"y\": {(-dirY).ToString(System.Globalization.CultureInfo.InvariantCulture)}, ");
                        json.Append($"\"z\": {dirZ.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                        
                        json.Append(" }");
                        
                        // Add comma if not last star
                        if (i < starCount - 1)
                        {
                            json.Append(",");
                        }
                        json.Append("\n");
                    }
                    
                    // Close stars object and root object
                    json.Append("  },\n");
                    json.Append("  \"constellations\": {}\n");
                    json.Append("}");
                    
                    // 6. Write JSON to: Path.ChangeExtension(binPath, ".json")
                    string jsonPath = Path.ChangeExtension(binPath, ".json");
                    File.WriteAllText(jsonPath, json.ToString());
                    
                    Debug.Log($"[CinematicShaders] Generated JSON sidecar: {jsonPath} ({starCount} stars)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Failed to generate JSON for {binPath}: {ex.Message}");
                return false;
            }
        }
    }
}
