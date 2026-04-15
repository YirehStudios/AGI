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
        private const long MaxRamAllowed = 12L * 1024 * 1024 * 1024;
        private bool _isPanicking = false;
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
            _isPanicking = false;
            
            // Enforces a sterile execution environment prior to instantiation.
            TerminateOrphanedResources();
            
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

        private void ExecuteInstantCommand(string exePath, string args, string engineName, string targetFilePath, Action<int> onFinished = null)
        {
            Task.Run(() => {
                // Instantiates a local process bypassing Docker, pointing directly to the extracted native binary.
                ProcessStartInfo info = new ProcessStartInfo {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using Process p = Process.Start(info);
                p.WaitForExit();
                
                onFinished?.Invoke(p.ExitCode);

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

        /// <summary>
        /// Realiza una purga de procesos huérfanos de ejecuciones anteriores 
        /// para garantizar un entorno limpio antes del despliegue del Backend.
        /// </summary>
        private void TerminateOrphanedResources()
        {
            // Definimos los nombres de los binarios a monitorear
            string[] targetResources = { "llama-server", "whisper-server" };
            
            GD.Print("ResourceMonitor: Iniciando rutina de conciliación de recursos...");

            try
            {
                foreach (string resourceName in targetResources)
                {
                    Process[] orphanedProcesses = Process.GetProcessesByName(resourceName);
                    
                    foreach (Process process in orphanedProcesses)
                    {
                        try 
                        {
                            // Verificamos si el proceso sigue activo antes de intentar terminarlo
                            if (!process.HasExited)
                            {
                                process.Kill(true); // 'true' mata también a los procesos hijos (árbol completo)
                                process.WaitForExit(1000); // Espera hasta 1 segundo para confirmar el cierre
                                GD.Print($"ResourceMonitor: Recurso huérfano '{resourceName}' (PID: {process.Id}) terminado exitosamente.");
                            }
                        }
                        catch (Exception innerEx)
                        {
                            GD.PushWarning($"ResourceMonitor: No se pudo liberar el PID {process.Id}: {innerEx.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                GD.Print("ResourceMonitor: Limpieza de infraestructura completada. Sistema listo para inicialización.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ResourceMonitor: Error crítico durante la gestión de recursos: {ex.Message}");
            }
        }

        public void GenerateTextToSpeech(string textToSynthesize)
        {
            string modelFolder = _configManager?.ActiveTTSModel ?? "vits-piper-es_MX-claude-high";
            string modelName = modelFolder.Replace("vits-piper-", "");
            string outputFileName = "temp_voice.wav";

            // Resolves standard internal data paths mapping to the user directory context.
            string modelsDir = ProjectSettings.GlobalizePath("user://models");
            string audioDir = ProjectSettings.GlobalizePath("user://audio");
            string sherpaBinPath = ProjectSettings.GlobalizePath("user://bin/sherpa-onnx-offline-tts");

            // Constructs absolute OS structural paths for ONNX architectural models and dependencies.
            string vitsModelPath = global::System.IO.Path.Combine(modelsDir, modelFolder, $"{modelName}.onnx");
            string vitsTokensPath = global::System.IO.Path.Combine(modelsDir, modelFolder, "tokens.txt");
            string vitsLexiconPath = global::System.IO.Path.Combine(modelsDir, modelFolder, "lexicon.txt");
            string outputFilePath = global::System.IO.Path.Combine(audioDir, outputFileName);

            // Formats strictly structured arguments pointing to validated local paths to execute audio synthesis natively.
            string arguments = $"--vits-model=\"{vitsModelPath}\" " +
                            $"--vits-tokens=\"{vitsTokensPath}\" " +
                            $"--vits-lexicon=\"{vitsLexiconPath}\" " +
                            $"--output-filename=\"{outputFilePath}\" \"{textToSynthesize}\"";

            ExecuteInstantCommand(sherpaBinPath, arguments, "Sherpa", outputFilePath);
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
                string llamaBinDir = global::System.IO.Path.Combine(binDir, "llama-b8770");
                string llamaBinPath = global::System.IO.Path.Combine(llamaBinDir, "llama-server");
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
                // Purges generation-specific arguments (temp, repeat-penalty) to comply with native server runtime parameter constraints.
                ProcessStartInfo llamaInfo = new ProcessStartInfo
                {
                    FileName = llamaBinPath,
                    Arguments = $"--model \"{modelLlamaPath}\" --host 127.0.0.1 --port {LlamaPort} --ctx-size 4096 --threads {threadCount} --n-gpu-layers 99",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _whisperProcess = new Process { StartInfo = whisperInfo, EnableRaisingEvents = true };
                _llamaProcess = new Process { StartInfo = llamaInfo, EnableRaisingEvents = true };

                // Subscribes local delegates to capture the asynchronous standard output streams for console replication and engine diagnostics.
                _whisperProcess.OutputDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                    {
                        GD.Print($"[Whisper] {e.Data}");
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Whisper] {e.Data}"); 
                    }
                };
                
                _whisperProcess.ErrorDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                    {
                        GD.PrintErr($"[Whisper ERR] {e.Data}");
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Whisper ERR] {e.Data}"); 
                        
                        string lowerData = e.Data.ToLower();
                        
                        // Evaluates unhandled system exceptions or memory allocation faults to trigger the internal panic handler.
                        bool isFatalError = lowerData.Contains("out of memory") || 
                                            lowerData.Contains("bad allocation") || 
                                            lowerData.Contains("failed to allocate") || 
                                            lowerData.Contains("segmentation fault") || 
                                            lowerData.Contains("core dumped");

                        if (isFatalError)
                        {
                            PanicKill($"Fallo crítico de memoria/sistema reportado por el motor: {e.Data}");
                            return;
                        }
                    }
                };
                _whisperProcess.Exited += OnProcessExited;

                _llamaProcess.OutputDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                    {
                        GD.Print($"[Llama] {e.Data}");
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Llama] {e.Data}"); 
                    }
                };
                
                // Evaluates the standard error output of the Llama process to detect the network binding success string or fatal execution faults.
                _llamaProcess.ErrorDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                    {
                        GD.PrintErr($"[Llama ERR] {e.Data}");
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Llama ERR] {e.Data}");
                        
                        string lowerData = e.Data.ToLower();
                        
                        // Evaluates unhandled system exceptions or memory allocation faults to trigger the internal panic handler.
                        bool isFatalError = lowerData.Contains("out of memory") || 
                                            lowerData.Contains("bad allocation") || 
                                            lowerData.Contains("failed to allocate") || 
                                            lowerData.Contains("segmentation fault") || 
                                            lowerData.Contains("core dumped");

                        if (isFatalError)
                        {
                            PanicKill($"Fallo crítico de memoria/sistema reportado por el motor: {e.Data}");
                            return;
                        }
                        
                        // Validates the successful port binding and internal host initialization to dispatch the readiness flag.
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
                GD.PrintErr($"BackendLauncher: Fallo general al instanciar los binarios nativos. {ex.Message}");
                HandleCrash();
            }
        }

        private async Task MonitorProcessHealth()
        {
            // Evaluates memory allocation constraints iteratively while bypassing state cycles during panic mode.
            while (_isRunning && !_isPanicking)
            {
                try
                {
                    if (_llamaProcess != null && !_llamaProcess.HasExited)
                    {
                        _llamaProcess.Refresh();
                        if (_llamaProcess.WorkingSet64 > MaxRamAllowed)
                        {
                            PanicKill("Desbordamiento de RAM detectado en Llama Server.");
                            break;
                        }
                    }

                    if (_whisperProcess != null && !_whisperProcess.HasExited)
                    {
                        _whisperProcess.Refresh();
                        if (_whisperProcess.WorkingSet64 > MaxRamAllowed)
                        {
                            PanicKill("Desbordamiento de RAM detectado en Whisper Server.");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"BackendLauncher: Excepción interceptada durante la interrogación de memoria: {ex.Message}");
                }

                await Task.Delay(2000);
            }
        }

        private async void PanicKill(string reason)
        {
            // Establishes a mutex lock to prevent recursive panic dispatch cascades across concurrent thread callbacks.
            if (_isPanicking) return;
            _isPanicking = true;
            _isRunning = false;

            string panicMessage = $"[PANIC] Secuencia de aborto inicializada. Cancelando todo. Motivo: {reason}";
            GD.PrintErr(panicMessage);
            CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, panicMessage);

            try
            {
                // Executes tree-level immediate termination against active native dependencies.
                if (_llamaProcess != null && !_llamaProcess.HasExited)
                {
                    _llamaProcess.Kill(true);
                }
                if (_whisperProcess != null && !_whisperProcess.HasExited)
                {
                    _whisperProcess.Kill(true);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Fallo secundario al ejecutar la purga de procesos (Kill): {ex.Message}");
            }

            // Invalidates the internal retry queue to enforce a hard lock constraint against cyclical reboots.
            _retryCount = MaxRetries;
            
            // Dispatches the failure state to the connected UI nodes to prompt user intervention.
            CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);

            GD.Print("BackendLauncher: Congelando operaciones de arranque durante 5 segundos para purga del OS...");
            await Task.Delay(5000);
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            // Bypasses the standardized crash handler route if the lifecycle is actively managed by the Panic Controller.
            if (_isPanicking) return;
            
            _isRunning = false;
            GD.PrintErr("BackendLauncher: Cese inesperado de la ejecución en uno de los motores nativos.");
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