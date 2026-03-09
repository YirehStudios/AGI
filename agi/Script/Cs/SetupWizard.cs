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
    public partial class SetupWizard : Panel
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
            [System.Text.Json.Serialization.JsonPropertyName("name")]
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

        private bool CheckIfImageExists(string imageName)
        {
            var output = new Godot.Collections.Array();
            // Runs a docker command to see if the image is registered
            int exitCode = OS.Execute("docker", new string[] { "images", "-q", imageName }, output, true);
            
            // If output is not empty, the image exists locally
            return exitCode == 0 && output.Count > 0 && !string.IsNullOrEmpty(output[0].ToString());
        }

        private async Task RunSetupSequence()
        {
            try
            {

                bool isDockerReady = await EnsureDockerInstalled();
                if (!isDockerReady) 
                {
                    return; // Detiene la secuencia si falló la instalación o si pide reiniciar
                }
                
                UpdateStatus("Initiating system discovery...");
                GD.Print("AGI Setup Wizard: Initiating System Discovery...");

                UpdateStatus("Verifying Docker installation...");
                if (!CheckDockerAvailability())
                {
                    UpdateStatus("Critical error: Docker is not installed or not running.");
                    GD.PrintErr("AGI Setup Wizard: Docker daemon unreachable. Halting startup.");
                    return;
                }

                // 1. DESCARGA LOS MODELOS (GGUF, BIN, ONNX)
                UpdateStatus("Verifying and downloading AI models...");
                await VerifyAndDownloadRequirements();

                // 2. CONSTRUYE LA IMAGEN DE DOCKER LOCALMENTE
                
                UpdateStatus("Verifying AI engine container...");
                if (!CheckIfImageExists("yirehstudios/agi-backend:latest"))
                {
                    UpdateStatus("Building backend Docker container... (This happens only once)");
                    await BuildDockerImage();
                    
                    // Verificación post-construcción (por si falla el internet a la mitad)
                    if (!CheckIfImageExists("yirehstudios/agi-backend:latest")) 
                    {
                        UpdateStatus("Critical Error: AI Image could not be built. Check logs.");
                        GD.PrintErr("AGI Setup Wizard: Halting startup. Docker image missing.");
                        return; 
                    }
                }
                else
                {
                    GD.Print("AGI Setup Wizard: Docker image already exists. Skipping build phase! (Fast Boot)");
                }

                if (!CheckIfImageExists("yirehstudios/agi-backend:latest")) {
                    UpdateStatus("Critical Error: AI Image could not be built. Check logs.");
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

                // 3. ENCIENDE EL SERVIDOR (Ahora sí, con la imagen construida)
                Node backendLauncher = GetNodeOrNull("/root/BackendLauncher");
                if (backendLauncher != null)
                {
                    GD.Print("AGI Setup Wizard: Invoking initial startup of Docker engine (StartBackend).");
                    backendLauncher.Call("StartBackend");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"AGI Setup Wizard: FATAL ERROR - {ex.Message}\n{ex.StackTrace}");
                UpdateStatus("Error crítico durante la instalación. Revisa la consola.");
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

        private async Task<bool> EnsureDockerInstalled()
        {
            UpdateStatus("Checking Docker engine...");

            // 1. Verificamos si docker ya responde
            var checkOutput = new Godot.Collections.Array();
            int checkExitCode = OS.Execute("docker", new string[] { "--version" }, checkOutput, true);

            if (checkExitCode == 0)
            {
                GD.Print("SetupWizard: Docker is already installed.");
                return true;
            }

            // 2. Si no existe, iniciamos el protocolo de instalación
            GD.Print("SetupWizard: Docker not found. Initiating secure installation...");
            UpdateStatus("Docker missing. Please enter your password in the popup to install it...");

            // Obtenemos el nombre del usuario real (ej. Yahir_js) para darle permisos
            string currentUser = System.Environment.UserName;

            // Creamos el comando bash: Actualiza repositorios, instala docker y añade al usuario al grupo
            string installCommand = $"apt-get update && apt-get install -y docker.io && usermod -aG docker {currentUser}";

            // pkexec es el puente gráfico nativo de Linux para permisos root
            var output = new Godot.Collections.Array();
            int installExitCode = OS.Execute("pkexec", new string[] { "bash", "-c", installCommand }, output, true);

            if (installExitCode == 0)
            {
                GD.Print("SetupWizard: Docker installed successfully.");
                UpdateStatus("Docker installed! IMPORTANT: Please RESTART your computer to apply permissions.");
                
                // Linux requiere cerrar sesión o reiniciar para que el grupo 'docker' tenga efecto.
                // Pausamos la ejecución para que el usuario lea el mensaje.
                await ToSignal(GetTree().CreateTimer(5.0f), "timeout"); 
                
                // Cerramos la app porque sin reiniciar, los comandos de Godot hacia Docker darán "Permission Denied"
                GetTree().Quit(); 
                return false; 
            }
            else
            {
                GD.PrintErr("SetupWizard: Installation failed or user canceled the password prompt.");
                UpdateStatus("Installation failed. Annie needs Docker to run.");
                return false;
            }
        }

        private async Task BuildDockerImage()
        {
            UpdateStatus("Building Docker AI Engine... (This may take several minutes)");
            GD.Print("AGI Setup Wizard: Starting Docker build in background...");

            await Task.Run(() =>
            {
                try
                {
                    string dockerFileDir = ProjectSettings.GlobalizePath("res://Script/Cs/System/Drivers/"); 
                    string dockerFilePath = System.IO.Path.Combine(dockerFileDir, "DockerFile");

                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "docker",
                        Arguments = $"build -f \"{dockerFilePath}\" -t yirehstudios/agi-backend:latest \"{dockerFileDir}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = new System.Diagnostics.Process { StartInfo = startInfo };
                    
                    // Esto imprimirá en Godot cada línea que Docker vaya descargando/construyendo
                    process.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) GD.Print($"[Docker] {e.Data}"); };
                    process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) GD.PrintErr($"[Docker] {e.Data}"); };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        GD.Print("AGI Setup Wizard: Docker image built successfully!");
                    }
                    else
                    {
                        GD.PrintErr($"AGI Setup Wizard: Docker build failed with code {process.ExitCode}.");
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"AGI Setup Wizard: Docker build process failed. {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Verifies the disk persistence of fundamental components (Model, Tokens, and Lexicon) 
        /// required by the Sherpa-ONNX architecture to guarantee stability in the operational phase.
        /// </summary>
        private bool ValidateSherpaModels()
        {
            string modelPath = ProjectSettings.GlobalizePath("user://models/vits-piper-es_ES-miro-high/es_ES-miro-high.onnx");
            string tokensPath = ProjectSettings.GlobalizePath("user://models/vits-piper-es_ES-miro-high/tokens.txt");
            string lexiconPath = ProjectSettings.GlobalizePath("user://models/vits-piper-es_ES-miro-high/lexicon.txt");

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
            //if (OS.HasFeature("Lite")) 
            return "res://Script/Cs/System/Config/RequerimentsLite.json";
            //if (OS.HasFeature("Server")) return "res://Script/Cs/System/Config/RequerimentsServer.json";
            //if (OS.HasFeature("IU")) return "res://Script/Cs/System/Config/RequerimentsIU.json";
            //return "res://Script/Cs/System/Config/Requeriments.json";
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
                
                // ¡Magia! Ahora descarga y organiza todo lo que pongas en el JSON
                if (root != null)
                {
                    if (root.ContainsKey("models")) requirements.AddRange(root["models"]);
                    if (root.ContainsKey("binaries")) requirements.AddRange(root["binaries"]);
                    if (root.ContainsKey("drivers")) requirements.AddRange(root["drivers"]); // Por si agregas esta lista al JSON
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
                // Esto respeta la carpeta que pusiste en el JSON (ej. "bin/" o "settings/")
                string targetSubDir = string.IsNullOrEmpty(req.Target) ? "models/" : req.Target;
                string globalTargetDir = ProjectSettings.GlobalizePath($"user://{targetSubDir}");
                string filePath = System.IO.Path.Combine(globalTargetDir, req.FileName);

                bool needsDownload = false;
                long serverSize = -1;

                UpdateStatus($"Verifying integrity: {req.FileName}...");
                await Task.Delay(50); // <-- ESTO EVITA QUE LA PANTALLA SE CONGELE

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
                    await Task.Delay(50); // <-- OBLIGA A REFRESCAR LA BARRA DE CARGA
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
                // 1. Creamos la ruta completa user://agi/settings/
                string settingsDir = ProjectSettings.GlobalizePath("user://settings");
                if (!Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

                // 2. Apuntamos el archivo config.json allí dentro
                string configPath = Path.Combine(settingsDir, "config.json");
                
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