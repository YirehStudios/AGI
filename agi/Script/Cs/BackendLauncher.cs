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
        private Logic.Backend.NativeTTSManager _ttsManager;

        public override void _Ready()
        {
            _configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            _ttsManager = GetNodeOrNull<Logic.Backend.NativeTTSManager>("/root/NativeTTSManager");
        }

        public void StartBackend()
        {
            //_retryCount = 0;
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
        /// Orchestrates the asynchronous allocation and binding of persistent C++ inference servers.
        /// Maps absolute runtime environment paths processing user-defined configuration payloads.
        /// Executes preemptive binary dependency validations to trigger panic protocols prior to allocation sequences.
        /// Resolves shared library constraints by physically mirroring required components across execution directories and enforcing strict soname bindings.
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

                // Evaluates the physical presence of the LLM host container before process formulation.
                if (!global::System.IO.File.Exists(llamaBinPath))
                {
                    GD.PrintErr("BackendLauncher: Fatal - Native C++ Server (llama-server) not found.");
                    CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);
                    return;
                }

                // Evaluates the physical presence of the STT host container before process formulation.
                if (!global::System.IO.File.Exists(whisperBinPath))
                {
                    GD.PrintErr("BackendLauncher: Fatal - Native C++ Server (whisper-server) not found.");
                    CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);
                    return;
                }

                // Mirrors dynamically linked libraries and automatically generates strict soname bindings (.so.0)
                try
                {
                    string[] ggmlLibs = global::System.IO.Directory.GetFiles(llamaBinDir, "libggml*.so*");
                    foreach (string libPath in ggmlLibs)
                    {
                        string fileName = global::System.IO.Path.GetFileName(libPath);
                        string destPath = global::System.IO.Path.Combine(binDir, fileName);
                        
                        // 1. Copia la librería base
                        if (!global::System.IO.File.Exists(destPath))
                        {
                            global::System.IO.File.Copy(libPath, destPath, false);
                        }

                        // 2. AUTO-PATCH: Si la librería original termina en ".so", crea inmediatamente la versión ".so.0" que exige Whisper.
                        if (fileName.EndsWith(".so"))
                        {
                            string versionedFileName = fileName + ".0";
                            string versionedDestPath = global::System.IO.Path.Combine(binDir, versionedFileName);
                            if (!global::System.IO.File.Exists(versionedDestPath))
                            {
                                global::System.IO.File.Copy(libPath, versionedDestPath, false);
                            }
                        }
                    }

                    // 3. HARD-PATCH: Whisper asume que siempre existe un fallback genérico de CPU con nombre estricto.
                    string targetWhisperCpuLib = global::System.IO.Path.Combine(binDir, "libggml-cpu.so.0");
                    if (!global::System.IO.File.Exists(targetWhisperCpuLib))
                    {
                        string fallbackCpu = global::System.IO.Path.Combine(binDir, "libggml-cpu-x64.so");
                        if (global::System.IO.File.Exists(fallbackCpu)) 
                        {
                            global::System.IO.File.Copy(fallbackCpu, targetWhisperCpuLib, false);
                        }
                    }
                }
                catch (Exception copyEx)
                {
                    GD.PrintErr($"BackendLauncher: Failure synchronizing shared libraries for native instances. {copyEx.Message}");
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

                // Concatenates multiple library paths resolving segmentation faults natively triggered by CPU compilation bindings.
                whisperInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = $"{binDir}:{llamaBinDir}";
                whisperInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = "1";
                
                llamaInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = llamaBinDir;
                llamaInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = "1";

                _whisperProcess = new Process { StartInfo = whisperInfo, EnableRaisingEvents = true };
                _llamaProcess = new Process { StartInfo = llamaInfo, EnableRaisingEvents = true };

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
                            _retryCount = 0; 
                            GD.Print("BackendLauncher: Llama Server natively loaded into memory successfully.");
                            CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
                        }
                    }
                };
                _llamaProcess.Exited += OnProcessExited;

                // Explicitly resolves and binds the TTS unmanaged engine sequence strictly prior to process execution contexts.
                GD.Print("[DEBUG] BackendLauncher: Solicitando inicialización nativa de Sherpa-ONNX (TTS)...");
                if (_ttsManager != null) 
                {
                    bool ttsReady = _ttsManager.InitializeNativeEngine();
                    if (ttsReady) GD.Print("[DEBUG] BackendLauncher: Motor TTS Nativo enlazado correctamente a la memoria.");
                    else GD.PrintErr("[DEBUG] BackendLauncher: FALLO Crítico al enlazar Motor TTS Nativo.");
                } 
                else 
                {
                    GD.PrintErr("[DEBUG] BackendLauncher: NativeTTSManager no fue encontrado en el SceneTree.");
                }

                GD.Print($"[DEBUG] BackendLauncher: Arrancando motor STT (Whisper) con PID asignado por el OS...");
                _whisperProcess.Start();
                _whisperProcess.BeginOutputReadLine();
                _whisperProcess.BeginErrorReadLine();

                GD.Print($"[DEBUG] BackendLauncher: Arrancando motor LLM (Llama) en puerto {LlamaPort}...");
                _llamaProcess.Start();
                _llamaProcess.BeginOutputReadLine();
                _llamaProcess.BeginErrorReadLine();

                _isRunning = true;

                GD.Print($"BackendLauncher: Natives initialized. Llama PID: {_llamaProcess.Id}, Whisper wrapper initialized. TTS is mapped via Unmanaged P/Invoke.");
                
                await MonitorProcessHealth();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: General fault instantiating native binaries. {ex.Message}");
                CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);
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

        /// <summary>
        /// Intercepts termination events emitted by attached operating system child processes.
        /// Computes runtime boolean evaluations to dispatch immediate teardown instructions, preventing main thread deadlocks.
        /// </summary>
        private void OnProcessExited(object sender, EventArgs e)
        {
            // Aborts processing flow ensuring no interference occurs against ongoing structural purge cycles.
            if (_isPanicking) return;
            
            // Evaluates active states redirecting unhandled process failures towards forced panic cycles rather than endless recovery matrices.
            if (_isRunning)
            {
                PanicKill("Motor nativo terminó inesperadamente. Abortando para evitar UI congelada.");
            }
            else
            {
                _isRunning = false;
                GD.PrintErr("BackendLauncher: Cese inesperado de la ejecución en uno de los motores nativos.");
                HandleCrash();
            }
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