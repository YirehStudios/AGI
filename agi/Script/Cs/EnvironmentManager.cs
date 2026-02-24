using Godot;
using System;
using System.IO;
using Logic.Utils;
using System.Collections.Generic;
using System.Text.Json;

namespace Logic.Managers
{
    public partial class EnvironmentManager : Node
    {
        /// <summary>
        /// Validates the environment and ensures Linux execution permissions.
        /// Implements "Hardware Agnosticism" by detecting GPU capabilities.
        /// </summary>
        public void EnsureEnvironmentIntegrity()
        {
            if (!Directory.Exists(PathConstants.EngineRoot))
            {
                GD.PrintErr("EnvironmentManager: Engine root not found.");
                return;
            }

            GrantExecutionPermissions();
            UpdateHardwareConfiguration();
        }

        /// <summary>
        /// Critical: Applies chmod +x to Python binaries and shell scripts.
        /// Required for the embedded environment to function on Linux.
        /// </summary>
        private void GrantExecutionPermissions()
        {
            if (OS.GetName() != "Linux") return;

            // Target the python binary specifically
            if (File.Exists(PathConstants.PythonExecutable))
            {
                OS.Execute("chmod", new string[] { "+x", PathConstants.PythonExecutable });
            }

            // Target all .sh files in the engine root
            string[] shFiles = Directory.GetFiles(PathConstants.EngineRoot, "*.sh", SearchOption.AllDirectories);
            foreach (string shFile in shFiles)
            {
                OS.Execute("chmod", new string[] { "+x", shFile });
            }
            
            GD.Print("EnvironmentManager: Linux execution permissions granted.");
        }

        /// <summary>
        /// Detects NVIDIA GPU presence and updates config.json.
        /// Falls back to CPU mode if no dedicated hardware is found.
        /// Uses System.Text.Json for standard dependency-free serialization.
        /// </summary>
        private void UpdateHardwareConfiguration()
        {
            bool hasNvidia = File.Exists("/proc/driver/nvidia/version");
            string mode = hasNvidia ? "cuda" : "cpu";
            
            var config = new Dictionary<string, string>
            {
                { "run_mode", mode },
                { "updated_at", DateTime.UtcNow.ToString("o") }
            };

            try 
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(config, options);
                File.WriteAllText(PathConstants.ConfigFile, jsonString);
                GD.Print($"EnvironmentManager: Hardware configured to [{mode}].");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"EnvironmentManager: Failed to write config. {ex.Message}");
            }
        }
    }
}