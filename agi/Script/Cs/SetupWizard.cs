using Godot;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using Logic.Network;

namespace Logic.Utils
{
    /// <summary>
    /// Handles initial configuration, dependency validation, Docker image orchestration,
    /// and ensures the environment is prepared for execution.
    /// </summary>
    public partial class SetupWizard : Node
    {
        [Signal]
        public delegate void SetupCompletedEventHandler(bool isGpuEnabled);

        [Export] public Control SetupOverlay;
        [Export] public ProgressBar SetupProgressBar;
        [Export] public Label SetupStatusLabel;

        private string _localAppPath;
        private DownloadManager _downloadManager;

        private class Requirement
        {
            public string FileName { get; set; }
            public string Url { get; set; }
            public string Target { get; set; }
        }

        public override void _Ready()
        {
            _localAppPath = ProjectSettings.GlobalizePath("user://");
            
            _downloadManager = new DownloadManager();
            AddChild(_downloadManager);
            
            if (SetupOverlay != null)
            {
                SetupOverlay.Show();
            }

            _ = RunSetupSequence();
        }

        private async Task RunSetupSequence()
        {
            UpdateStatus("Initiating system discovery...");
            GD.Print("AGI Setup Wizard: Initiating System Discovery...");

            UpdateStatus("Verifying Docker installation...");
            if (!CheckDockerAvailability())
            {
                UpdateStatus("Critical error: Docker is not installed or not running.");
                GD.PrintErr("AGI Setup Wizard: Docker daemon unreachable. Halting startup.");
                return;
            }

            await VerifyAndDownloadRequirements();

            UpdateStatus("Pulling backend Docker container...");
            PullDockerImage();
            
            UpdateStatus("Verifying acoustic model integrity...");
            
            // Inspects the strict availability of the three layers of the Sherpa-ONNX engine
            if (!ValidateSherpaModels())
            {
                UpdateStatus("Critical error: Voice model files missing.");
                GD.PrintErr("AGI Setup Wizard: Strict validation of Sherpa-ONNX models failed. Halting startup.");
                return;
            }

            UpdateStatus("Verifying hardware capabilities...");
            bool hasGpu = CheckHardwareCapabilities();

            UpdateStatus("Configuring the local environment...");
            UpdateLocalConfiguration(hasGpu);

            UpdateStatus("Setup completed successfully. Welcome!");
            GD.Print("AGI Setup Wizard: Environment Ready.");
            
            FinalizeSetup();
            EmitSignal(SignalName.SetupCompleted, hasGpu);

            // Startup Trigger: Ignites the native server after securing dependencies
            Node backendLauncher = GetNodeOrNull("/root/BackendLauncher");
            if (backendLauncher != null)
            {
                GD.Print("AGI Setup Wizard: Invoking initial startup of Docker engine (StartBackend).");
                backendLauncher.Call("StartBackend");
            }
        }

        private bool CheckDockerAvailability()
        {
            try
            {
                var output = new Godot.Collections.Array();
                int exitCode = OS.Execute("docker", new string[] { "--version" }, output, true);
                
                if (exitCode == 0)
                {
                    GD.Print($"AGI Setup Wizard: Docker detected. {(output.Count > 0 ? output[0] : string.Empty)}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"AGI Setup Wizard: Docker validation failed. {ex.Message}");
            }
            return false;
        }

        private void PullDockerImage()
        {
            try
            {
                GD.Print("AGI Setup Wizard: Requesting yirehstudios/agi-backend:latest from Docker Hub...");
                var output = new Godot.Collections.Array();
                string dockerfilePath = ProjectSettings.GlobalizePath("res://Script/Cs/System/Drivers/"); 
                int exitCode = OS.Execute("docker", new string[] { "build", "-t", "yirehstudios/agi-backend:latest", dockerfilePath }, output, true);
                
                if (exitCode != 0)
                {
                    GD.PrintErr("AGI Setup Wizard: Failed to pull Docker image.");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"AGI Setup Wizard: Docker pull execution failed. {ex.Message}");
            }
        }

        /// <summary>
        /// Verifies the disk persistence of fundamental components (Model, Tokens, and Lexicon) 
        /// required by the Sherpa-ONNX architecture to guarantee stability in the operational phase.
        /// </summary>
        private bool ValidateSherpaModels()
        {
            string modelPath = ProjectSettings.GlobalizePath("user://agi/models/vits-piper-es_ES-miro-high/es_ES-miro-high.onnx");
            string tokensPath = ProjectSettings.GlobalizePath("user://agi/models/vits-piper-es_ES-miro-high/tokens.txt");
            string lexiconPath = ProjectSettings.GlobalizePath("user://agi/models/vits-piper-es_ES-miro-high/lexicon.txt");

            // Checks the directory tree to ensure models will not fail due to orphaned dependencies
            return File.Exists(modelPath) && File.Exists(tokensPath) && File.Exists(lexiconPath);
        }

        private void UpdateStatus(string currentStatus)
        {
            if (SetupStatusLabel != null)
            {
                SetupStatusLabel.Text = currentStatus;
            }
        }

        private void OnDownloadProgress(float progressValue)
        {
            if (SetupProgressBar != null)
            {
                SetupProgressBar.Value = progressValue;
            }
        }

        private void FinalizeSetup()
        {
            if (SetupOverlay != null)
            {
                SetupOverlay.Hide();
            }
        }

        private string GetRequirementFilePath()
        {
            if (OS.HasFeature("Lite")) return "res://Script/Cs/System/Config/RequerimentsLite.json";
            if (OS.HasFeature("Server")) return "res://Script/Cs/System/Config/RequerimentsServer.json";
            if (OS.HasFeature("IU")) return "res://Script/Cs/System/Config/RequerimentsIU.json";
            return "res://Script/Cs/System/Config/Requeriments.json";
        }

        private async Task VerifyAndDownloadRequirements()
        {
            string requirementsPath = GetRequirementFilePath();
            
            if (!Godot.FileAccess.FileExists(requirementsPath))
            {
                GD.PrintErr($"AGI Setup Wizard: Requirements file not found at {requirementsPath}");
                UpdateStatus("Error: Requirements file not found.");
                return;
            }

            using var file = Godot.FileAccess.Open(requirementsPath, Godot.FileAccess.ModeFlags.Read);
            string jsonContent = file.GetAsText();
            
            List<Requirement> requirements = new List<Requirement>();
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<Requirement>>>(jsonContent, options);
                
                if (root != null && root.ContainsKey("models"))
                {
                    requirements = root["models"];
                }
            }
            catch(Exception ex)
            {
                GD.PrintErr($"AGI Setup Wizard: Failed to parse JSON. {ex.Message}");
                UpdateStatus("Error interpreting JSON configuration.");
                return;
            }

            using System.Net.Http.HttpClient httpClient = new System.Net.Http.HttpClient();
            int totalFiles = requirements.Count;
            int processedFiles = 0;

            foreach (var req in requirements)
            {
                string targetSubDir = string.IsNullOrEmpty(req.Target) ? "models/" : req.Target;
                string globalTargetDir = ProjectSettings.GlobalizePath($"user://agi/{targetSubDir}");
                string filePath = System.IO.Path.Combine(globalTargetDir, req.FileName);

                bool needsDownload = false;
                long serverSize = -1;

                UpdateStatus($"Verifying integrity: {req.FileName}...");

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
                    GD.PrintErr($"AGI Setup Wizard: Could not verify Content-Length for {req.FileName}. {ex.Message}");
                }

                if (!System.IO.File.Exists(filePath))
                {
                    needsDownload = true;
                }
                else if (serverSize > 0)
                {
                    System.IO.FileInfo fileInfo = new System.IO.FileInfo(filePath);
                    if (fileInfo.Length != serverSize)
                    {
                        GD.Print($"AGI Setup Wizard: Size discrepancy in {req.FileName}. Re-downloading...");
                        needsDownload = true;
                    }
                }

                if (needsDownload)
                {
                    UpdateStatus($"Downloading and processing: {req.FileName}...");
                    GD.Print($"AGI Setup Wizard: Queuing download for {req.FileName} into {globalTargetDir}");
                    
                    await _downloadManager.DownloadFileAsync(req.Url, globalTargetDir, req.FileName);
                }

                processedFiles++;
                float progressPercentage = ((float)processedFiles / totalFiles) * 100f;
                OnDownloadProgress(progressPercentage);
            }
            
            GD.Print("AGI Setup Wizard: All deployment resources verified.");
        }

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

        private void UpdateLocalConfiguration(bool useGpu)
        {
            try
            {
                string configPath = Path.Combine(_localAppPath, "config.json");
                
                var configData = new Dictionary<string, object>
                {
                    { "hardware_mode", useGpu ? "cuda" : "cpu" },
                    { "last_setup_check", DateTime.UtcNow.ToString("o") },
                    { "engine_mode", "docker" } 
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