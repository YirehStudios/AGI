using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SysPath = System.IO.Path;

namespace Logic.Backend
{
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
        [Export] public int SearchPort = 8000;
        private Process _searchProcess;
        private Process _llamaProcess;
        private Process _whisperProcess;
        private Process _sherpaProcess;
        private Process _mcpProcess;
        private const long MaxRamAllowed = 12L * 1024 * 1024 * 1024;
        private bool _isPanicking = false;
        private bool _isRunning = false;
        private int _retryCount = 0;
        private const int MaxRetries = 3;
        private Logic.System.Config.ConfigManager _configManager;
        private Logic.Backend.NativeTTSManager _ttsManager;
        private dynamic _environmentManager;

        public override void _Ready()
        {
            _configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            _environmentManager = GetNodeOrNull("/root/EnvironmentManager");
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

                // Enforces port release by eliminating any zombie instances of the Python microservices.
                if (_environmentManager != null && (_environmentManager.IsLinux || !_environmentManager.IsWindows))
                {
                    OS.Execute("pkill", new string[] { "-f", "search_server.py" }, new Godot.Collections.Array(), true);
                    OS.Execute("pkill", new string[] { "-f", "tts_server.py" }, new Godot.Collections.Array(), true);
                    OS.Execute("pkill", new string[] { "-f", "mcp_server.py" }, new Godot.Collections.Array(), true);
                }
                GD.Print("ResourceMonitor: Infrastructure cleanup completed. System ready for C++ engine initialization.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ResourceMonitor: Critical failure during resource management: {ex.Message}");
            }
        }

        /// <summary>
        /// Orchestrates the lifecycle of inference engines (Llama, Whisper), the Search Microservice, the TTS bridge, 
        /// and the MCP tool gateway. Performs path resolution, binary validation, and dynamic hardware 
        /// configuration for all subprocesses.
        /// Updated to selectively launch heavy engines only when not in CloudAPI mode to optimize RAM usage.
        /// </summary>
        /// <param name="modelsDir">Absolute path to the models directory.</param>
        /// <param name="safeFileName">Sanitized filename of the LLM model to be loaded by Llama.</param>
        private async Task ManageBackendLifecycle(string modelsDir, string safeFileName)
        {
            // Detects the operational mode to determine which process trees should be instantiated.
            bool isCloudMode = _configManager != null && _configManager.CurrentMode == Logic.System.Config.ConfigManager.AppMode.CloudAPI;

            try
            {
                // Configures processing resources and resolves paths for core model files.
                int threadCount = Math.Max(1, global::System.Environment.ProcessorCount / 2);
                string modelLlamaPath = global::System.IO.Path.Combine(modelsDir, safeFileName);
                
                string sttModel = _configManager?.ActiveSTTModel ?? "Whisper_Base.bin"; 
                string modelWhisperPath = global::System.IO.Path.Combine(modelsDir, sttModel);

                string osFolder = _environmentManager.IsWindows ? "windows" : "linux";
                string llamaDir = global::System.IO.Path.Combine(_environmentManager.BinPath, osFolder, "llama");
                string whisperDir = global::System.IO.Path.Combine(_environmentManager.BinPath, osFolder, "whisper");

                string llamaBinPath = Logic.Utils.FileResolver.FindExecutable(llamaDir, _environmentManager.IsWindows, "llama-server");
                string whisperBinPath = Logic.Utils.FileResolver.FindExecutable(whisperDir, _environmentManager.IsWindows, "whisper-server");

                // Validates presence of critical native binaries.
                if (!global::System.IO.File.Exists(llamaBinPath) || !global::System.IO.File.Exists(whisperBinPath))
                {
                    GD.PrintErr("BackendLauncher: Fatal - Essential binaries (Llama/Whisper) are missing.");
                    CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);
                    return;
                }

                // Resolves Python executable paths for Windows (portable) and Linux (virtual environment).
                string pythonExe = _environmentManager.IsWindows ? 
                    global::System.IO.Path.Combine(_environmentManager.EnvPath, "python", "python.exe") : 
                    global::System.IO.Path.Combine(_environmentManager.EnvPath, "python", "bin", "python3");
                
                string searchPythonExe = _environmentManager.IsWindows ? 
                    global::System.IO.Path.Combine(_environmentManager.EnvPath, "python_search", "python.exe") : 
                    global::System.IO.Path.Combine(_environmentManager.EnvPath, "python_search", "bin", "python3");

                // Identifies script locations for TTS, Search, and MCP services.
                string ttsScriptPath = global::System.IO.Path.Combine(_environmentManager.BinPath, "tts_server.py");
                string searchScriptPath = global::System.IO.Path.Combine(_environmentManager.BinPath, "search_server.py");
                string mcpScriptPath = global::System.IO.Path.Combine(_environmentManager.BinPath, "mcp_server.py");

                // Whisper Configuration.
                ProcessStartInfo whisperInfo = new ProcessStartInfo
                {
                    FileName = whisperBinPath,
                    Arguments = $"-m \"{modelWhisperPath}\" --host 127.0.0.1 --port {WhisperPort}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Llama Configuration.
                ProcessStartInfo llamaInfo = new ProcessStartInfo
                {
                    FileName = llamaBinPath,
                    Arguments = $"--model \"{modelLlamaPath}\" --host 127.0.0.1 --port {LlamaPort} --ctx-size 4096 --threads {threadCount} --n-gpu-layers 99",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Search Microservice Configuration.
                ProcessStartInfo searchInfo = new ProcessStartInfo
                {
                    FileName = searchPythonExe,
                    Arguments = $"-u \"{searchScriptPath}\" --port {SearchPort}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Standardized MCP Tool Gateway Configuration on port 8002.
                ProcessStartInfo mcpInfo = new ProcessStartInfo
                {
                    FileName = searchPythonExe,
                    Arguments = $"-u \"{mcpScriptPath}\" --port {SearchPort + 2}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Sherpa (TTS) Configuration.
                ProcessStartInfo sherpaInfo = null;
                if (global::System.IO.File.Exists(pythonExe))
                {
                    string ttsModelFolder = _configManager?.ActiveTTSModel ?? "";
                    string ttsModelsDir = global::System.IO.Path.Combine(modelsDir, ttsModelFolder);

                    sherpaInfo = new ProcessStartInfo
                    {
                        FileName = pythonExe,
                        Arguments = $"-u \"{ttsScriptPath}\" --port {SherpaPort} --models-dir \"{ttsModelsDir}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                }

                // Environment variable injection for GPU acceleration.
                whisperInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = whisperDir;
                llamaInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = llamaDir;

                int gpuIndex = _configManager?.SelectedGpuIndex ?? -1;
                if (gpuIndex >= 0)
                {
                    string gpuStr = gpuIndex.ToString();
                    whisperInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = gpuStr;
                    llamaInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = gpuStr;
                }

                // HEAVY ENGINE STARTUP: Only if NOT in cloud mode.
                if (!isCloudMode)
                {
                    _whisperProcess = new Process { StartInfo = whisperInfo, EnableRaisingEvents = true };
                    _llamaProcess = new Process { StartInfo = llamaInfo, EnableRaisingEvents = true };

                    _whisperProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Whisper] {e.Data}"); };
                    _whisperProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Whisper ERR] {e.Data}"); };
                    _whisperProcess.Exited += OnProcessExited;

                    _llamaProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Llama] {e.Data}"); };
                    _llamaProcess.ErrorDataReceived += (s, e) => 
                    { 
                        if (!string.IsNullOrEmpty(e.Data)) 
                        {
                            CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Llama ERR] {e.Data}");
                            // BackendReady is emitted when the native Llama server signals it is listening.
                            if (e.Data.Contains("server is listening")) CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
                        }
                    };
                    _llamaProcess.Exited += OnProcessExited;

                    _whisperProcess.Start();
                    _whisperProcess.BeginOutputReadLine();
                    _whisperProcess.BeginErrorReadLine();

                    _llamaProcess.Start();
                    _llamaProcess.BeginOutputReadLine();
                    _llamaProcess.BeginErrorReadLine();
                }

                // LIGHTWEIGHT MICROSERVICES: Always launched.
                _searchProcess = new Process { StartInfo = searchInfo, EnableRaisingEvents = true };
                _searchProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) GD.Print($"[Search] {e.Data}"); };
                _searchProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) GD.PrintErr($"[Search ERR] {e.Data}"); };
                _searchProcess.Exited += OnProcessExited;
                _searchProcess.Start();
                _searchProcess.BeginOutputReadLine();
                _searchProcess.BeginErrorReadLine();

                _mcpProcess = new Process { StartInfo = mcpInfo, EnableRaisingEvents = true };
                _mcpProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) GD.Print($"[MCP] {e.Data}"); };
                _mcpProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) GD.PrintErr($"[MCP ERR] {e.Data}"); };
                _mcpProcess.Exited += OnProcessExited;
                _mcpProcess.Start();
                _mcpProcess.BeginOutputReadLine();
                _mcpProcess.BeginErrorReadLine();

                if (sherpaInfo != null)
                {
                    _sherpaProcess = new Process { StartInfo = sherpaInfo, EnableRaisingEvents = true };
                    _sherpaProcess.Exited += OnProcessExited;
                    _sherpaProcess.Start();
                    _sherpaProcess.BeginOutputReadLine();
                    _sherpaProcess.BeginErrorReadLine();
                }

                _isRunning = true;

                // Manual BackendReady signal for Cloud mode, as Llama won't emit it.
                if (isCloudMode)
                {
                    CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, "[Microservices] Search, TTS, and MCP ready. Local Llama bypassed.");
                    CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
                }

                // Initiates the asynchronous monitoring loop for RAM constraints.
                await MonitorProcessHealth();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Unexpected lifecycle fault: {ex.Message}");
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

                    if (_mcpProcess != null && !_mcpProcess.HasExited)
                    {
                        _mcpProcess.Refresh();
                        if (_mcpProcess.WorkingSet64 > MaxRamAllowed)
                        {
                            PanicKill("RAM overflow detected in MCP Server.");
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
                if (_searchProcess != null && !_searchProcess.HasExited)
                {
                    _searchProcess.Kill(true);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Secondary fault executing process purge (Kill): {ex.Message}");
            }

            OS.Execute("pkill", new string[] { "-f", "tts_server.py" }, new Godot.Collections.Array(), true);
            OS.Execute("pkill", new string[] { "-f", "search_server.py" }, new Godot.Collections.Array(), true);
            OS.Execute("pkill", new string[] { "-f", "mcp_server.py" }, new Godot.Collections.Array(), true);

            _retryCount = MaxRetries;
            
            CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);

            GD.Print("BackendLauncher: Freezing boot operations for 5 seconds to allow OS level purge...");
            await Task.Delay(5000);
        }

        /// <summary>
        /// Intercepta eventos de terminación emitidos por procesos secundarios del sistema operativo acoplados.
        /// Evalúa la identidad del emisor mediante validación de tipos para un reporte preciso de errores
        /// y gestiona el flujo de estado de ejecución para despachar instrucciones de desmontaje inmediatas, evitando interbloqueos.[cite: 4]
        /// </summary>
        private void OnProcessExited(object sender, EventArgs e)
        {
            // Aborta el flujo de ejecución para evitar interferencias de concurrencia si el sistema ya se encuentra ejecutando un ciclo de pánico estructural.[cite: 4]
            if (_isPanicking) return;
            
            // Asigna un valor predeterminado como descriptor del proceso en caso de no ser posible determinar su identidad.[cite: 4]
            string processName = "Desconocido";

            // Ejecuta validación de tipos sobre el remitente del evento para extraer el nombre del binario ejecutable
            // desde los metadatos de inicio de la instancia de proceso, suprimiendo excepciones de acceso a disco de forma segura.[cite: 4]
            if (sender is Process p) 
            { 
                try 
                { 
                    processName = Path.GetFileName(p.StartInfo.FileName); 
                } 
                catch { } 
            }
            
            // Evalúa el estado de ejecución global para determinar el vector de respuesta ante la terminación anómala del proceso hijo.[cite: 4]
            if (_isRunning)
            {
                // Despacha el protocolo de terminación forzada notificando a la interfaz sobre el binario exacto responsable de la interrupción.[cite: 4]
                PanicKill($"Motor nativo ({processName}) terminó inesperadamente. Abortando para evitar UI congelada.");
            }
            else
            {
                // Actualiza la bandera de estado general e imprime la traza de error con el origen identificado en la consola del motor antes de iniciar la lógica de recuperación.[cite: 4]
                _isRunning = false;
                GD.PrintErr($"BackendLauncher: Cese inesperado del motor nativo ({processName}).");
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