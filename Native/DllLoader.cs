using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CinematicShaders.Native
{
    /// <summary>
    /// Centralized native DLL loader with dependency path resolution.
    /// Thread-safe and idempotent - handles simultaneous initialization from GTAO and Starfield.
    /// </summary>
    public static class DllLoader
    {
        private static readonly object _lock = new object();
        private static IntPtr _handle = IntPtr.Zero;
        private static bool _loaded = false;

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        public static bool IsLoaded => _loaded;

        /// <summary>
        /// Ensures the native DLL is loaded with proper dependency resolution.
        /// Safe to call from multiple threads and static constructors.
        /// </summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;

            lock (_lock)
            {
                if (_loaded) return;

                try
                {
                    string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    string pluginDataPath = Path.GetFullPath(Path.Combine(assemblyPath, "..", "PluginData"));
                    string dllPath = Path.Combine(pluginDataPath, "CinematicShadersNative.dll");

                    Debug.Log($"[DllLoader] Loading native DLL from: {dllPath}");

                    if (!File.Exists(dllPath))
                    {
                        Debug.LogError($"[DllLoader] Native DLL not found at: {dllPath}");
                        return;
                    }

                    // CRITICAL: Set DLL directory for dependencies BEFORE LoadLibrary
                    // This ensures DirectXTK.dll (for Stage 1 DDS loading) is found
                    SetDllDirectory(pluginDataPath);
                    Debug.Log($"[DllLoader] Set DLL directory to: {pluginDataPath}");

                    _handle = LoadLibrary(dllPath);

                    if (_handle == IntPtr.Zero)
                    {
                        int errorCode = Marshal.GetLastWin32Error();
                        Debug.LogError($"[DllLoader] LoadLibrary failed with error {errorCode}: {new System.ComponentModel.Win32Exception(errorCode).Message}");
                        return;
                    }

                    // Verify GTAO exports exist
                    if (GetProcAddress(_handle, "CR_GTAOSetSettings") == IntPtr.Zero)
                    {
                        Debug.LogError("[DllLoader] CR_GTAOSetSettings export not found!");
                        return;
                    }

                    // Verify Starfield exports exist  
                    if (GetProcAddress(_handle, "CR_StarfieldSetSettings") == IntPtr.Zero)
                    {
                        Debug.LogError("[DllLoader] CR_StarfieldSetSettings export not found!");
                        return;
                    }

                    // Verify Kartographer exports exist
                    if (GetProcAddress(_handle, "CR_StarfieldSetKartographerEnabled") == IntPtr.Zero)
                    {
                        Debug.LogError("[DllLoader] CR_StarfieldSetKartographerEnabled export not found!");
                        return;
                    }

                    _loaded = true;
                    Debug.Log("[DllLoader] Native DLL loaded successfully with dependency path resolution");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[DllLoader] Failed to load native DLL: {ex}");
                }
            }
        }
    }
}