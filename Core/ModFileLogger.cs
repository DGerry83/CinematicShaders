using System;
using System.IO;
using UnityEngine;

namespace CinematicShaders.Core
{
    public static class ModFileLogger
    {
        private static string LogFilePath;
        private static StreamWriter LogWriter;
        private static readonly object WriteLock = new object();
        private static bool IsInitialized = false;

        public static void Initialize()
        {
            if (IsInitialized) return;

            try
            {
                string modDirectory = Path.Combine(
                    KSPUtil.ApplicationRootPath, 
                    "GameData", 
                    "CinematicShaders"
                );
                
                LogFilePath = Path.Combine(modDirectory, "CinematicShadersDebug.log");

                LogWriter = new StreamWriter(LogFilePath, append: true);
                LogWriter.AutoFlush = true;
                
                IsInitialized = true;
                WriteToFile("INFO", "ModFileLogger initialized");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[CinematicShaders] Failed to initialize file logger: {ex.Message}");
            }
        }

        public static void Log(string message)
        {
            if (!IsInitialized) Initialize();
            
            lock (WriteLock)
            {
                WriteToFile("INFO", message);
                UnityEngine.Debug.Log($"[CinematicShadersFile] {message}");
            }
        }

        public static void LogWarning(string message)
        {
            if (!IsInitialized) Initialize();
            
            lock (WriteLock)
            {
                WriteToFile("WARN", message);
                UnityEngine.Debug.LogWarning($"[CinematicShadersFile] {message}");
            }
        }

        public static void LogError(string message)
        {
            if (!IsInitialized) Initialize();
            
            lock (WriteLock)
            {
                WriteToFile("ERROR", message);
                UnityEngine.Debug.LogError($"[CinematicShadersFile] {message}");
            }
        }

        private static void WriteToFile(string level, string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string formattedLine = $"[{timestamp}] [{level}] {message}";
            LogWriter.WriteLine(formattedLine);
        }

        public static void Shutdown()
        {
            lock (WriteLock)
            {
                if (LogWriter != null)
                {
                    WriteToFile("SHUTDOWN", "Logger closing normally");
                    LogWriter.Close();
                    LogWriter = null;
                    IsInitialized = false;
                }
            }
        }
    }
}
