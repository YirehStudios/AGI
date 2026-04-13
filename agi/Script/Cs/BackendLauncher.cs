using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SysPath = System.IO.Path;

namespace Logic.Backend
{
    /// <summary>
    /// Manages the lifecycle of Docker-isolated engines (Llama, Whisper, Sherpa-ONNX).
    /// </summary>
    public partial class BackendLauncher : Node
    {
        
        [Signal]
        public delegate void ConnectionLostEventHandler();

        [Signal]
        public delegate void BackendReadyEventHandler();
        
        [Signal]
        public delegate void BuildLogReceivedEventHandler(string logMessage);

        [Signal]
        public delegate void STTCompletedEventHandler(string recognizedText);

        [Signal]
        public delegate void TTSCompletedEventHandler(string audioFilePath);
        [Export] public int LlamaPort = 8080;
        [Export] public int WhisperPort = 8081;
        private Process _llamaProcess;
        private Process _whisperProcess;

        private Process _backendProcess;
        private bool _isRunning = false;
        private int _retryCount = 0;
        private const int MaxRetries = 3;

        private Logic.System.Config.ConfigManager _configManager;

        public override void _Ready()
        {
            _configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
        }

        public void StartBackend()
        {
            _retryCount = 0;
            
            Logic.System.Config.ConfigManager configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            string safeFileName = "default.gguf";

            if (configManager != null)
            {
                if (!string.IsNullOrEmpty(configManager.ActiveModelName))
                    safeFileName = configManager.ActiveModelName.Replace(" ", "_") + ".gguf";
            }
            
            string modelsDir = ProjectSettings.GlobalizePath("user://models"); 
            string audioDir = ProjectSettings.GlobalizePath("user://audio"); 

            // Allocates the execution to a background task context to prevent UI thread blockages.
            Task.Run(async () => 
            {
                global::System.IO.Directory.CreateDirectory(audioDir);
                await ManageBackendLifecycle(modelsDir, safeFileName);
            });
        }

        public void ProcessSpeechToText(string audioFilePath)
        {
            GD.Print($"[FLAG] STT: Enviando {Path.GetFileName(audioFilePath)} vía HTTP...");

            // Offloads the synchronous network blocking request onto an asynchronous task thread.
            Task.Run(async () => 
            {
                try
                {
                    // Instantiates connection client and initializes payload structure for multipart forms.
                    using var client = new global::System.Net.Http.HttpClient();
                    using var form = new global::System.Net.Http.MultipartFormDataContent();
                    
                    // Reads binary constraints of the file system wav and enforces the strictly typed MIME parameter.
                    byte[] audioBytes = global::System.IO.File.ReadAllBytes(audioFilePath);
                    var audioContent = new global::System.Net.Http.ByteArrayContent(audioBytes);
                    audioContent.Headers.ContentType = global::System.Net.Http.Headers.MediaTypeHeaderValue.Parse("audio/wav");
                    
                    form.Add(audioContent, "file", global::System.IO.Path.GetFileName(audioFilePath));
                    
                    // Dispatches payload to the local HTTP Whisper endpoint handling inference natively.
                    var response = await client.PostAsync("http://127.0.0.1:8081/inference", form);
                    response.EnsureSuccessStatusCode();
                    
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    
                    // Executes JSON string parsing to extract and isolate the strictly text-formatted response.
                    using var doc = global::System.Text.Json.JsonDocument.Parse(jsonResponse);
                    if (doc.RootElement.TryGetProperty("text", out global::System.Text.Json.JsonElement textElement))
                    {
                        string recognizedText = textElement.GetString().Trim();
                        GD.Print($"[FLAG] STT SUCCESS: {recognizedText}");
                        CallDeferred(MethodName.EmitSignal, SignalName.STTCompleted, recognizedText);
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[FLAG] STT ERROR: Falló la petición HTTP a Whisper. {ex.Message}");
                }
            });
        }

        private void ExecuteInstantCommand(string args, string engineName, string targetFilePath, Action<int> onFinished = null)
        {
            Task.Run(() => {
                ProcessStartInfo info = new ProcessStartInfo {
                    FileName = "docker",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using Process p = Process.Start(info);
                p.WaitForExit();
                
                onFinished?.Invoke(p.ExitCode); // Avisamos si terminó bien

                if (p.ExitCode != 0) {
                    GD.PrintErr($"[FLAG] ERROR: {engineName} falló con código {p.ExitCode}");
                    return;
                }

                if (engineName == "Whisper") {
                    string txtPath = targetFilePath + ".txt";
                    if (global::System.IO.File.Exists(txtPath)) {
                        string text = global::System.IO.File.ReadAllText(txtPath).Trim();
                        CallDeferred(MethodName.EmitSignal, SignalName.STTCompleted, text);
                    }
                }
            });
        }

        public void GenerateTextToSpeech(string textToSynthesize)
        {
            string modelFolder = _configManager?.ActiveTTSModel ?? "vits-piper-es_MX-claude-high";
            string modelName = modelFolder.Replace("vits-piper-", "");
            string outputFileName = "temp_voice.wav";

            string command = $"exec agi-llama-server sherpa-onnx-offline-tts " +
                            $"--vits-model=\"/app/models/{modelFolder}/{modelName}.onnx\" " +
                            $"--vits-tokens=\"/app/models/{modelFolder}/tokens.txt\" " +
                            $"--vits-lexicon=\"/app/models/{modelFolder}/lexicon.txt\" " +
                            $"--output-filename=\"/app/audio/{outputFileName}\" \"{textToSynthesize}\"";

            ExecuteInstantCommand(command, "Sherpa", outputFileName);
        }

        private async Task<bool> EnsureDockerImageExistsAsync()
        {
            // Resolves the internal application file system constraints and initializes the directory structure.
            string serverDir = ProjectSettings.GlobalizePath("user://server");
            global::System.IO.Directory.CreateDirectory(serverDir);
            
            string dockerfilePath = global::System.IO.Path.Combine(serverDir, "Dockerfile");
            string sourceResPath = "res://Script/Cs/System/Drivers/Dockerfile";
            string fileContent = "";

            // Attempts to fetch the Dockerfile definitions from local Godot mapped resources.
            using (var file = Godot.FileAccess.Open(sourceResPath, Godot.FileAccess.ModeFlags.Read))
            {
                if (file != null) 
                {
                    fileContent = file.GetAsText();
                    GD.Print("BackendLauncher: Dockerfile cargado desde recursos locales.");
                }
                else
                {
                    GD.PrintErr($"BackendLauncher: No se encontró el Dockerfile local en {sourceResPath}. Activando protocolo de emergencia...");
                }
            }

            // Executes an external web request to retrieve fallback Dockerfile definitions if the local asset is unavailable.
            if (string.IsNullOrEmpty(fileContent))
            {
                try
                {
                    using global::System.Net.Http.HttpClient client = new global::System.Net.Http.HttpClient();
                    string rawUrl = "https://raw.githubusercontent.com/YirehStudios/AGI/main/agi/Script/Cs/System/Drivers/Dockerfile";
                    
                    GD.Print("BackendLauncher: Obteniendo Dockerfile desde el repositorio oficial...");
                    fileContent = await client.GetStringAsync(rawUrl);
                    GD.Print("BackendLauncher: ¡Dockerfile descargado con éxito!");
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"BackendLauncher: Fallo crítico. No se pudo obtener el Dockerfile ni local ni remoto. Verifica tu conexión a internet. {ex.Message}");
                    return false;
                }
            }

            global::System.IO.File.WriteAllText(dockerfilePath, fileContent);

            // Instantiates an asynchronous standard process to query the local Docker engine regarding the image integrity,
            // bypassing the Godot OS.Execute call to prevent rendering thread deadlocks.
            ProcessStartInfo inspectInfo = new ProcessStartInfo {
                FileName = "docker",
                Arguments = "image inspect yirehstudios/agi-backend:latest",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process inspectProcess = Process.Start(inspectInfo);
            inspectProcess.WaitForExit();
            int inspectCode = inspectProcess.ExitCode;

            if (inspectCode == 0)
            {
                CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, "La imagen Docker ya existe y está lista.");
                return true;
            }

            GD.Print("BackendLauncher: Iniciando construcción en segundo plano...");
            CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, "Descargando entorno y compilando (Esto tomará varios minutos)...");

            // Allocates an independent subprocess mapping stdout/stderr streams to track the container build sequence incrementally.
            ProcessStartInfo buildInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"build -t yirehstudios/agi-backend:latest \"{serverDir}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process buildProcess = new Process { StartInfo = buildInfo };
            
            buildProcess.OutputDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data)) {
                    GD.Print($"[Docker Build] {e.Data}");
                    CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, e.Data);
                }
            };
            buildProcess.ErrorDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data)) {
                    GD.Print($"[Docker Build] {e.Data}");
                    CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, e.Data);
                }
            };

            buildProcess.Start();
            buildProcess.BeginOutputReadLine();
            buildProcess.BeginErrorReadLine();

            await Task.Run(() => buildProcess.WaitForExit());

            return buildProcess.ExitCode == 0;
        }

        private async Task ManageBackendLifecycle(string modelsDir, string safeFileName)
        {
            try
            {
                int threadCount = Math.Max(1, global::System.Environment.ProcessorCount / 2);
                string modelLlamaPath = global::System.IO.Path.Combine(modelsDir, safeFileName);
                
                string sttModel = _configManager?.ActiveSTTModel ?? "Whisper_Base.bin"; 
                string modelWhisperPath = global::System.IO.Path.Combine(modelsDir, sttModel);

                string binDir = ProjectSettings.GlobalizePath("user://bin");
                string llamaBinPath = global::System.IO.Path.Combine(binDir, "llama-server");
                string whisperBinPath = global::System.IO.Path.Combine(binDir, "whisper-server");

                // Instantiates the ProcessStartInfo strictly configuring standard stream redirection and suppressing window creation for Whisper.
                ProcessStartInfo whisperInfo = new ProcessStartInfo
                {
                    FileName = whisperBinPath,
                    Arguments = $"-m \"{modelWhisperPath}\" --host 127.0.0.1 --port {WhisperPort}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Instantiates the ProcessStartInfo strictly configuring standard stream redirection and suppressing window creation for Llama.
                ProcessStartInfo llamaInfo = new ProcessStartInfo
                {
                    FileName = llamaBinPath,
                    Arguments = $"--model \"{modelLlamaPath}\" --host 127.0.0.1 --port {LlamaPort} --ctx-size 4096 --threads {threadCount} --n-gpu-layers 99 --temp 0.7 --repeat-penalty 1.15",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _whisperProcess = new Process { StartInfo = whisperInfo, EnableRaisingEvents = true };
                _llamaProcess = new Process { StartInfo = llamaInfo, EnableRaisingEvents = true };

                // Subscribes local delegates to capture the asynchronous standard output streams for console replication.
                _whisperProcess.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Whisper] {e.Data}"); };
                _whisperProcess.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Whisper ERR] {e.Data}"); };
                _whisperProcess.Exited += OnProcessExited;

                _llamaProcess.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Llama] {e.Data}"); };
                
                // Evaluates the standard error output of the Llama process to detect the network binding success string.
                _llamaProcess.ErrorDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                    {
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Llama ERR] {e.Data}");
                        
                        if (e.Data.Contains("server is listening on") || e.Data.Contains("HTTP server listening"))
                        {
                            GD.Print("BackendLauncher: Llama Server nativo cargado en memoria exitosamente.");
                            CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
                        }
                    }
                };
                _llamaProcess.Exited += OnProcessExited;

                _whisperProcess.Start();
                _whisperProcess.BeginOutputReadLine();
                _whisperProcess.BeginErrorReadLine();

                _llamaProcess.Start();
                _llamaProcess.BeginOutputReadLine();
                _llamaProcess.BeginErrorReadLine();

                _isRunning = true;

                GD.Print($"BackendLauncher: Nativos iniciados. Llama PID: {_llamaProcess.Id}, Whisper PID: {_whisperProcess.Id}");
                
                await MonitorProcessHealth();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Fallo al arrancar los binarios nativos. {ex.Message}");
                HandleCrash();
            }
        }

        private async Task MonitorProcessHealth()
        {
            // Continuously evaluates the active status of the instantiated system process references.
            while (_isRunning && _llamaProcess != null && !_llamaProcess.HasExited && _whisperProcess != null && !_whisperProcess.HasExited)
            {
                await Task.Delay(5000);
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            // Acts as the primary callback for process termination exceptions, enforcing lifecycle state resets.
            _isRunning = false;
            GD.PrintErr("BackendLauncher: Uno de los procesos nativos se detuvo de forma inesperada.");
            HandleCrash();
        }

        private void HandleCrash()
        {
            // Implements a recursive attempt pattern bound by the defined maximum retry constraint.
            if (_retryCount < MaxRetries)
            {
                _retryCount++;
                GD.Print($"BackendLauncher: Intentando revivir motores ({_retryCount}/{MaxRetries})...");
                CallDeferred(MethodName.StartBackend); 
            }
            else
            {
                CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);
            }
        }
        
        public override void _ExitTree()
        {
            // Enforces memory hygiene by forcefully terminating active child process execution trees upon application closure.
            GD.Print("BackendLauncher: Limpieza de procesos nativos (Evitando Zombis).");
            _isRunning = false;
            
            try
            {
                if (_llamaProcess != null && !_llamaProcess.HasExited)
                {
                    _llamaProcess.Kill();
                    _llamaProcess.Dispose();
                }
                
                if (_whisperProcess != null && !_whisperProcess.HasExited)
                {
                    _whisperProcess.Kill();
                    _whisperProcess.Dispose();
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Error durante la limpieza de procesos: {ex.Message}");
            }
        }
    }
}