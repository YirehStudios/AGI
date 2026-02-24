using Godot;
using System.IO;

namespace Logic.Utils
{
    /// <summary>
    /// Centralized registry for all file system paths.
    /// Ensures consistency across Environment, Backend, and Setup modules.
    /// </summary>
    public static class PathConstants
    {
        // Base Paths
        public static string UserDataDir => ProjectSettings.GlobalizePath("user://");
        public static string EngineRoot => Path.Combine(UserDataDir, "Engine/ComfyUI");
        
        // Python & Venv
        public static string VenvPath => Path.Combine(EngineRoot, "venv");
        public static string PythonExecutable => Path.Combine(VenvPath, "bin", "python3"); // Linux specific
        
        // Backend Entry Point
        public static string MainScript => Path.Combine(EngineRoot, "main.py");
        
        // Config & Logs
        public static string ConfigFile => Path.Combine(UserDataDir, "config.json");
        public static string LogDir => Path.Combine(UserDataDir, "Logs");
    }
}