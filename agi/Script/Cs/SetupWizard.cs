using Godot;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using Logic.Network;

namespace Logic.Utils
{
    public partial class SetupWizard : Node
    {
        [Signal]
        public delegate void SetupCompletedEventHandler(bool isGpuEnabled);

        // Path configuration following architectural standards
        private string _localAppPath;
        private DownloadManager _downloadManager;

        /// <summary>
        /// Represents the expected structure of the requirements JSON configuration file.
        /// </summary>
        private class Requirement
        {
            public string FileName { get; set; }
            public string Url { get; set; }
            public string Target { get; set; } // Resolves to e.g. "bin/" or "models/"
        }

        public override void _Ready()
        {
            _localAppPath = ProjectSettings.GlobalizePath("user://");
            
            _downloadManager = new DownloadManager();
            AddChild(_downloadManager);
            
            _ = RunSetupSequence();
        }

        /// <summary>
        /// Core sequence to prepare the AGI environment.
        /// Executes strictly in order: Discovery/Download -> Permissions -> Hardware -> Config.
        /// </summary>
        private async Task RunSetupSequence()
        {
            GD.Print("AGI Setup Wizard: Initiating System Discovery...");

            await VerifyAndDownloadRequirements();
            
            bool hasGpu = CheckHardwareCapabilities();
            UpdateLocalConfiguration(hasGpu);

            GD.Print("AGI Setup Wizard: Environment Ready.");
            
            EmitSignal(SignalName.SetupCompleted, hasGpu);
        }

        /// <summary>
        /// Retrieves the correct manifest path based on Godot Feature Tags.
        /// </summary>
        private string GetRequirementFilePath()
        {
            if (OS.HasFeature("Lite")) return "res://Script/Cs/System/Config/RequerimentsLite.json";
            if (OS.HasFeature("Server")) return "res://Script/Cs/System/Config/RequerimentsServer.json";
            if (OS.HasFeature("IU")) return "res://Script/Cs/System/Config/RequerimentsIU.json";
            return "res://Script/Cs/System/Config/Requeriments.json";
        }

        /// <summary>
        /// Reads the dynamic requirements file, verifies existence and integrity of native files,
        /// queues missing components, and grants execution permissions immediately post-validation.
        /// </summary>
        private async Task VerifyAndDownloadRequirements()
        {
            string requirementsPath = GetRequirementFilePath();
            
            // CORRECCIÓN CS0104: Uso explícito de Godot.FileAccess
            if (!Godot.FileAccess.FileExists(requirementsPath))
            {
                GD.PrintErr($"AGI Setup Wizard: Requirements file missing at {requirementsPath}");
                return;
            }

            // CORRECCIÓN CS0104: Uso explícito de Godot.FileAccess y su enumerador
            using var file = Godot.FileAccess.Open(requirementsPath, Godot.FileAccess.ModeFlags.Read);
            string jsonContent = file.GetAsText();
            
            List<Requirement> requirements = new List<Requirement>();
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                requirements = System.Text.Json.JsonSerializer.Deserialize<List<Requirement>>(jsonContent, options);
            }
            catch(Exception ex)
            {
                GD.PrintErr($"AGI Setup Wizard: Failed to parse requirements JSON. {ex.Message}");
                return;
            }

            using System.Net.Http.HttpClient httpClient = new System.Net.Http.HttpClient();

            foreach (var req in requirements)
            {
                string targetSubDir = string.IsNullOrEmpty(req.Target) ? "models/" : req.Target;
                string globalTargetDir = ProjectSettings.GlobalizePath($"user://agi/{targetSubDir}");
                string filePath = System.IO.Path.Combine(globalTargetDir, req.FileName);

                bool needsDownload = false;
                long serverSize = -1;

                // 1. Fetch Content-Length from server to validate integrity
                try
                {
                    var headRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, req.Url);
                    var headResponse = await httpClient.SendAsync(headRequest);
                    if (headResponse.IsSuccessStatusCode && headResponse.Content.Headers.ContentLength.HasValue)
                    {
                        serverSize = headResponse.Content.Headers.ContentLength.Value;
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"AGI Setup Wizard: Could not fetch Content-Length for {req.FileName}. {ex.Message}");
                }

                // 2. Validate local file
                if (!System.IO.File.Exists(filePath))
                {
                    needsDownload = true;
                }
                else if (serverSize > 0)
                {
                    System.IO.FileInfo fileInfo = new System.IO.FileInfo(filePath);
                    if (fileInfo.Length != serverSize)
                    {
                        GD.Print($"AGI Setup Wizard: Size mismatch for {req.FileName} ({fileInfo.Length} vs {serverSize}). Re-downloading...");
                        needsDownload = true;
                    }
                }

                // 3. Dispatch download
                if (needsDownload)
                {
                    GD.Print($"AGI Setup Wizard: Queuing download for {req.FileName} into {globalTargetDir}");
                    bool success = await _downloadManager.DownloadFileAsync(req.Url, globalTargetDir, req.FileName);

                    if (success && serverSize > 0)
                    {
                        System.IO.FileInfo postFileInfo = new System.IO.FileInfo(filePath);
                        if (postFileInfo.Length != serverSize)
                        {
                            GD.PrintErr($"AGI Setup Wizard: Post-download integrity check failed for {req.FileName}.");
                            continue; // Saltar validación de permisos si la integridad se vio comprometida
                        }
                    }
                }

                // 4. Apply execution permissions immediately post-validation for binaries
                if (targetSubDir.StartsWith("bin") && OS.GetName() == "Linux")
                {
                    string[] chmodArgs = { "+x", filePath };
                    OS.Execute("chmod", chmodArgs, new Godot.Collections.Array(), true);
                    GD.Print($"AGI Setup Wizard: Execution permissions granted for {req.FileName}");
                }
            }
            
            GD.Print("AGI Setup Wizard: All deployment resources verified.");
        }

        /// <summary>
        /// Detects GPU presence to apply the Hardware Agnosticism principle.
        /// Checks for standard Linux driver paths.
        /// </summary>
        private bool CheckHardwareCapabilities()
        {
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
                    // CORRECCIÓN CS0103: Reemplazo de _enginePath obsoleta por la nueva ruta de binarios
                    { "engine_path", ProjectSettings.GlobalizePath("user://agi/bin") } 
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