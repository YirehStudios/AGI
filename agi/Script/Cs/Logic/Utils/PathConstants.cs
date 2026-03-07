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
        
        // Models Directory (Weights, GGUF, ONNX, etc.)
        public static string ModelsDir => Path.Combine(UserDataDir, "agi/models");
        
        // Config & Logs
        public static string ConfigFile => Path.Combine(UserDataDir, "config.json");
        public static string LogDir => Path.Combine(UserDataDir, "Logs");
    }
}