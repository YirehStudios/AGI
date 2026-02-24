using Godot;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;

namespace Logic.Utils
{
    public partial class SetupWizard : Node
    {
        [Signal]
        public delegate void SetupCompletedEventHandler(bool isGpuEnabled);

        // Path configuration following architectural standards
        private string _localAppPath;
        private string _enginePath;

        public override void _Ready()
        {
            // Globalize path to ~/.local/share/godot/app_userdata/[ProjectName] (Linux) 
            // or %APPDATA%/... (Windows) depending on OS, though logic here focuses on Linux portability.
            _localAppPath = ProjectSettings.GlobalizePath("user://");
            _enginePath = Path.Combine(_localAppPath, "Engine/ComfyUI");
            
            // Fire and forget the setup sequence
            _ = RunSetupSequence();
        }

        /// <summary>
        /// Core sequence to prepare the AGI environment.
        /// Executes strictly in order: Discovery -> Integrity -> Permissions -> Hardware -> Config.
        /// </summary>
        private async Task RunSetupSequence()
        {
            GD.Print("AGI Setup Wizard: Initiating System Discovery...");

            if (!CheckEnvironmentIntegrity())
            {
                await DeployEnvironment();
            }

            await GrantLinuxPermissions();
            
            bool hasGpu = CheckHardwareCapabilities();
            UpdateLocalConfiguration(hasGpu);

            GD.Print("AGI Setup Wizard: Environment Ready.");
            
            // Notify the main system (ChatbotMain) that the handshake can begin
            EmitSignal(SignalName.SetupCompleted, hasGpu);
        }

        /// <summary>
        /// Validates if the ComfyUI engine exists in the local AppData.
        /// </summary>
        private bool CheckEnvironmentIntegrity()
        {
            return Directory.Exists(_enginePath);
        }

        /// <summary>
        /// Handles the deployment of the Lite engine to local storage.
        /// Currently a placeholder for ZIP extraction logic.
        /// </summary>
        private async Task DeployEnvironment()
        {
            GD.Print("Deploying AGI Engine to local storage...");
            
            // TODO: Implement ZIP extraction logic here.
            // For now, we simulate a delay representing the operation.
            await ToSignal(GetTree().CreateTimer(0.1f), SceneTreeTimer.SignalName.Timeout);
        }

        /// <summary>
        /// Grants execution permissions to binary files in Linux to ensure Python embedded works.
        /// </summary>
        private async Task GrantLinuxPermissions()
        {
            if (OS.GetName() == "Linux")
            {
                GD.Print("AGI Setup Wizard: Granting executable permissions...");
                string pythonPath = Path.Combine(_enginePath, "python_embeded/python"); // Removed .exe for Linux logic, adjust if using Wine
                
                // Ensure the path is absolute for chmod
                string globalPath = ProjectSettings.GlobalizePath(pythonPath);

                // Execute chmod +x on the python binary
                // Note: Godot 4 OS.Execute returns the exit code.
                string[] args = { "+x", globalPath };
                int exitCode = OS.Execute("chmod", args, new Godot.Collections.Array(), true);
                
                if (exitCode != 0)
                {
                    GD.PrintErr($"AGI Setup Wizard: Failed to grant permissions. Exit code: {exitCode}");
                }
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// Detects GPU presence to apply the Hardware Agnosticism principle.
        /// Checks for standard Linux driver paths.
        /// </summary>
        private bool CheckHardwareCapabilities()
        {
            // Simple check for NVIDIA drivers in Linux
            // This is a naive check; production code might use `nvidia-smi` via OS.Execute for robustness.
            if (File.Exists("/proc/driver/nvidia/version"))
            {
                GD.Print("NVIDIA GPU Detected. Enabling CUDA Mode.");
                return true;
            }
            
            GD.Print("NVIDIA GPU Not Found. Defaulting to CPU Fallback.");
            return false;
        }

        /// <summary>
        /// Saves the detected settings to the local config file (user://config.json).
        /// </summary>
        private void UpdateLocalConfiguration(bool useGpu)
        {
            try
            {
                string configPath = Path.Combine(_localAppPath, "config.json");
                
                var configData = new Dictionary<string, object>
                {
                    { "hardware_mode", useGpu ? "cuda" : "cpu" },
                    { "last_setup_check", DateTime.UtcNow.ToString("o") },
                    { "engine_path", _enginePath }
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonOutput = JsonSerializer.Serialize(configData, options);
                File.WriteAllText(configPath, jsonOutput);
                GD.Print($"Configuration saved to: {configPath}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"AGI Setup Wizard: Failed to save configuration. Error: {ex.Message}");
            }
        }
    }
}