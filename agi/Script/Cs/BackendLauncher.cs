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

        [Export] public int LlamaPort = 8080;
        [Export] public int WhisperPort = 8081;
        [Export] public int SherpaPort = 8888; 
        private Process _llamaProcess;
        private Process _whisperProcess;
        private Process _sherpaProcess;
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

        /// <summary>
        /// Executes a preemptive traversal of the operating system's active process tree prior to initialization,
        /// forcefully terminating detached binary instances to guarantee unhindered port binding and memory allocation.
        /// </summary>
        private void TerminateOrphanedResources()
        {
            // Asigna los descriptores exactos de los procesos nativos en C++ hacia el arreglo de rastreo.
            string[] targetResources = { "llama-server", "whisper-server", "sherpa-onnx-tts-server" };
            
            GD.Print("ResourceMonitor: Initiating resource reconciliation routine for native engines...");

            try
            {
                foreach (string resourceName in targetResources)
                {
                    Process[] orphanedProcesses = Process.GetProcessesByName(resourceName);
                    
                    foreach (Process process in orphanedProcesses)
                    {
                        try 
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(true); 
                                process.WaitForExit(1000); 
                                GD.Print($"ResourceMonitor: Orphaned C++ native resource '{resourceName}' (PID: {process.Id}) terminated successfully.");
                            }
                        }
                        catch (Exception innerEx)
                        {
                            GD.PushWarning($"ResourceMonitor: Failed to release PID {process.Id}: {innerEx.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                GD.Print("ResourceMonitor: Infrastructure cleanup completed. System ready for C++ engine initialization.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ResourceMonitor: Critical failure during resource management: {ex.Message}");
            }
        }

        /// <summary>
        /// Orchestrates the asynchronous initialization sequence for the persistent local C++ servers.
        /// Computes absolute file paths dynamically leveraging the serialized configuration properties
        /// to allocate memory environments for Llama, Whisper, and Sherpa processes natively.
        /// Dynamically injects conditional data directories depending on the acoustic model's requirement for espeak-ng.
        /// </summary>
        private async Task ManageBackendLifecycle(string modelsDir, string safeFileName)
        {
            try
            {
                int threadCount = Math.Max(1, global::System.Environment.ProcessorCount / 2);
                string modelLlamaPath = global::System.IO.Path.Combine(modelsDir, safeFileName);
                
                string binDir = ProjectSettings.GlobalizePath("user://bin");
                string sttModel = _configManager?.ActiveSTTModel ?? "Whisper_Base.bin"; 
                string modelWhisperPath = global::System.IO.Path.Combine(modelsDir, sttModel);

                string llamaBinDir = global::System.IO.Path.Combine(binDir, "llama-b8770");
                string llamaBinPath = global::System.IO.Path.Combine(llamaBinDir, "llama-server");
                string whisperBinPath = global::System.IO.Path.Combine(binDir, "whisper-server");

                string sherpaBinDir = global::System.IO.Path.Combine(binDir, "sherpa-onnx");
                string sherpaBinPath = global::System.IO.Path.Combine(sherpaBinDir, "sherpa-onnx-tts-server");
                
                // Extrae dinámicamente el directorio del modelo Kokoro desde la configuración activa.
                string ttsFolder = _configManager?.ActiveTTSModel ?? "kokoro-multi-lang-v1_1";
                
                // Construye las estructuras de sistema de archivos base esperadas por la integración Kokoro en Sherpa-ONNX.
                string kokoroModelPath = global::System.IO.Path.Combine(modelsDir, ttsFolder, "model.onnx");
                string kokoroVoicesPath = global::System.IO.Path.Combine(modelsDir, ttsFolder, "voices.bin");
                string kokoroTokensPath = global::System.IO.Path.Combine(modelsDir, ttsFolder, "tokens.txt");
                
                // Mapea la ruta del diccionario espeak-ng-data e inyecta la bandera correspondiente de forma heurística.
                string kokoroDataDir = global::System.IO.Path.Combine(modelsDir, ttsFolder, "espeak-ng-data");
                string dataDirFlag = global::System.IO.Directory.Exists(kokoroDataDir) ? $"--kokoro-data-dir=\"{kokoroDataDir}\"" : "";

                if (!global::System.IO.File.Exists(sherpaBinPath))
                {
                    GD.PrintErr("BackendLauncher: Fatal - Native C++ TTS Server (sherpa-onnx-tts-server) not found.");
                    HandleCrash();
                    return;
                }

                ProcessStartInfo whisperInfo = new ProcessStartInfo
                {
                    FileName = whisperBinPath,
                    Arguments = $"-m \"{modelWhisperPath}\" --host 127.0.0.1 --port {WhisperPort}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                ProcessStartInfo llamaInfo = new ProcessStartInfo
                {
                    FileName = llamaBinPath,
                    Arguments = $"--model \"{modelLlamaPath}\" --host 127.0.0.1 --port {LlamaPort} --ctx-size 4096 --threads {threadCount} --n-gpu-layers 99",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Asigna las directivas CLI exigidas por la implementación nativa C++ interpolando las rutas pre-evaluadas y la bandera condicional.
                ProcessStartInfo ttsEngineInfo = new ProcessStartInfo
                {
                    FileName = sherpaBinPath,
                    Arguments = $"--kokoro-model=\"{kokoroModelPath}\" --kokoro-voices=\"{kokoroVoicesPath}\" --kokoro-tokens=\"{kokoroTokensPath}\" {dataDirFlag} --port={SherpaPort}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                whisperInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = binDir;
                whisperInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = "1";
                
                llamaInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = llamaBinDir;
                llamaInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = "1";

                ttsEngineInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = sherpaBinDir;

                _whisperProcess = new Process { StartInfo = whisperInfo, EnableRaisingEvents = true };
                _llamaProcess = new Process { StartInfo = llamaInfo, EnableRaisingEvents = true };
                _sherpaProcess = new Process { StartInfo = ttsEngineInfo, EnableRaisingEvents = true };

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
                        bool isFatalError = lowerData.Contains("out of memory") || lowerData.Contains("bad allocation") || lowerData.Contains("segmentation fault");
                        if (isFatalError) PanicKill($"Critical memory fault: {e.Data}");
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
                
                _llamaProcess.ErrorDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                    {
                        GD.PrintErr($"[Llama ERR] {e.Data}");
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Llama ERR] {e.Data}");
                        
                        string lowerData = e.Data.ToLower();
                        bool isFatalError = lowerData.Contains("out of memory") || lowerData.Contains("bad allocation") || lowerData.Contains("segmentation fault");
                        if (isFatalError) PanicKill($"Critical memory fault: {e.Data}");
                        
                        if (e.Data.Contains("server is listening on") || e.Data.Contains("HTTP server listening"))
                        {
                            GD.Print("BackendLauncher: Llama Server natively loaded into memory successfully.");
                            CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
                        }
                    }
                };
                _llamaProcess.Exited += OnProcessExited;

                _sherpaProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        GD.Print($"[Sherpa-ONNX] {e.Data}");
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Sherpa-ONNX] {e.Data}");
                    }
                };

                _sherpaProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        GD.PrintErr($"[Sherpa-ONNX ERR] {e.Data}");
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Sherpa-ONNX ERR] {e.Data}");

                        string lowerData = e.Data.ToLower();
                        bool isFatalError = lowerData.Contains("out of memory") || lowerData.Contains("bad allocation") || lowerData.Contains("segmentation fault");
                        if (isFatalError) PanicKill($"Critical memory fault: {e.Data}");
                    }
                };
                _sherpaProcess.Exited += OnProcessExited;

                _whisperProcess.Start();
                _whisperProcess.BeginOutputReadLine();
                _whisperProcess.BeginErrorReadLine();

                _llamaProcess.Start();
                _llamaProcess.BeginOutputReadLine();
                _llamaProcess.BeginErrorReadLine();

                _sherpaProcess.Start();
                _sherpaProcess.BeginOutputReadLine();
                _sherpaProcess.BeginErrorReadLine();

                _isRunning = true;

                GD.Print($"BackendLauncher: Natives initialized. Llama PID: {_llamaProcess.Id}, Whisper PID: {_whisperProcess.Id}, Sherpa PID: {_sherpaProcess.Id}");
                
                await MonitorProcessHealth();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: General fault instantiating native binaries. {ex.Message}");
                HandleCrash();
            }
        }

        /// <summary>
        /// Continuously interrogates the resident memory footprint of the managed subsystems.
        /// Enforces strict allocation ceilings, dispatching internal shutdown signals upon violation.
        /// </summary>
        private async Task MonitorProcessHealth()
        {
            while (_isRunning && !_isPanicking)
            {
                try
                {
                    if (_llamaProcess != null && !_llamaProcess.HasExited)
                    {
                        _llamaProcess.Refresh();
                        if (_llamaProcess.WorkingSet64 > MaxRamAllowed)
                        {
                            PanicKill("RAM overflow detected in Llama Server.");
                            break;
                        }
                    }

                    if (_whisperProcess != null && !_whisperProcess.HasExited)
                    {
                        _whisperProcess.Refresh();
                        if (_whisperProcess.WorkingSet64 > MaxRamAllowed)
                        {
                            PanicKill("RAM overflow detected in Whisper Server.");
                            break;
                        }
                    }

                    if (_sherpaProcess != null && !_sherpaProcess.HasExited)
                    {
                        _sherpaProcess.Refresh();
                        if (_sherpaProcess.WorkingSet64 > MaxRamAllowed)
                        {
                            PanicKill("RAM overflow detected in Sherpa Server.");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"BackendLauncher: Exception intercepted during memory polling: {ex.Message}");
                }

                await Task.Delay(2000);
            }
        }

        /// <summary>
        /// Executes an emergency systemic teardown of all tracked application sub-processes.
        /// Invalidates standard runtime constraints and prevents recursive execution cycles upon fatal exceptions.
        /// </summary>
        private async void PanicKill(string reason)
        {
            if (_isPanicking) return;
            _isPanicking = true;
            _isRunning = false;

            string panicMessage = $"[PANIC] Abort sequence initialized. Terminating all operations. Reason: {reason}";
            GD.PrintErr(panicMessage);
            CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, panicMessage);

            try
            {
                if (_llamaProcess != null && !_llamaProcess.HasExited)
                {
                    _llamaProcess.Kill(true);
                }
                if (_whisperProcess != null && !_whisperProcess.HasExited)
                {
                    _whisperProcess.Kill(true);
                }
                if (_sherpaProcess != null && !_sherpaProcess.HasExited)
                {
                    _sherpaProcess.Kill(true);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Secondary fault executing process purge (Kill): {ex.Message}");
            }

            // GARANTÍA ANTIMUERTE: Destrucción forzada a nivel de OS en caso de Pánico
            OS.Execute("pkill", new string[] { "-f", "tts_server.py" }, new Godot.Collections.Array(), true);

            _retryCount = MaxRetries;
            
            CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);

            GD.Print("BackendLauncher: Freezing boot operations for 5 seconds to allow OS level purge...");
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

        /// <summary>
        /// Ensures structural memory hygiene by forcefully dispatching termination signals to active
        /// child processes synchronously during the Godot node tree deallocation sequence.
        /// </summary>
        /// <summary>
        /// Ensures structural memory hygiene by forcefully dispatching termination signals to active
        /// child processes synchronously during the Godot node tree deallocation sequence.
        /// </summary>
        /// <summary>
        /// Ensures structural memory hygiene by forcefully dispatching termination signals to active
        /// child processes synchronously during the Godot node tree deallocation sequence.
        /// </summary>
        public override void _ExitTree()
        {
            GD.Print("BackendLauncher: Purging native C++ processes (Preventing Zombies).");
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

                if (_sherpaProcess != null && !_sherpaProcess.HasExited)
                {
                    _sherpaProcess.Kill();
                    _sherpaProcess.Dispose();
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Error during process cleanup: {ex.Message}");
            }
        }
    }
}