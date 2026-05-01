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
                GD.Print("ResourceMonitor: Infrastructure cleanup completed. System ready for C++ engine initialization.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ResourceMonitor: Critical failure during resource management: {ex.Message}");
            }
        }

        /// <summary>
        /// Orquestra el ciclo de vida de los motores de inferencia (Llama, Whisper) y el puente TTS de Python.
        /// Realiza la resolución de rutas, validación de binarios, sincronización de librerías GGML 
        /// y configuración dinámica de hardware para los subprocesos.
        /// </summary>
        /// <param name="modelsDir">Ruta absoluta al directorio de modelos.</param>
        /// <param name="safeFileName">Nombre del archivo del modelo LLM sanitizado.</param>
        private async Task ManageBackendLifecycle(string modelsDir, string safeFileName)
        {
            try
            {
                // 1. Configuración de Recursos y Resolución de Rutas de Modelos
                // Define el conteo de hilos para CPU y las rutas de los tensores para los motores.
                int threadCount = Math.Max(1, global::System.Environment.ProcessorCount / 2);
                string modelLlamaPath = global::System.IO.Path.Combine(modelsDir, safeFileName);
                
                string sttModel = _configManager?.ActiveSTTModel ?? "Whisper_Base.bin"; 
                string modelWhisperPath = global::System.IO.Path.Combine(modelsDir, sttModel);

                // 2. Enrutamiento Dinámico Basado en el Sistema Operativo
                // Localiza los directorios específicos de binarios según la plataforma de ejecución.
                string osFolder = _environmentManager.IsWindows ? "windows" : "linux";
                string llamaDir = global::System.IO.Path.Combine(_environmentManager.BinPath, osFolder, "llama");
                string whisperDir = global::System.IO.Path.Combine(_environmentManager.BinPath, osFolder, "whisper");

                // 3. Descubrimiento de Binarios vía Logic.Utils.FileResolver
                // Identifica los puntos de entrada para los servidores de inferencia nativos.
                string llamaBinPath = Logic.Utils.FileResolver.FindExecutable(llamaDir, _environmentManager.IsWindows, "llama-server");
                string whisperBinPath = Logic.Utils.FileResolver.FindExecutable(whisperDir, _environmentManager.IsWindows, "whisper-server");

                // 4. Validación Crítica de Integridad de Binarios
                // Asegura que los archivos ejecutables existan antes de intentar la instanciación de procesos.
                if (!global::System.IO.File.Exists(llamaBinPath))
                {
                    GD.PrintErr($"BackendLauncher: Fatal - Llama Server binary missing in {llamaDir}.");
                    CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);
                    return;
                }

                if (!global::System.IO.File.Exists(whisperBinPath))
                {
                    GD.PrintErr($"BackendLauncher: Fatal - Whisper Server binary missing in {whisperDir}.");
                    CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);
                    return;
                }

                // 5. Sincronización y Auto-Parcheo de Librerías Compartidas
                // Garantiza la disponibilidad de librerías dinámicas GGML en el directorio raíz de binarios.
                try
                {
                    string[] ggmlLibs = global::System.IO.Directory.GetFiles(llamaDir, "libggml*.so*");
                    foreach (string libPath in ggmlLibs)
                    {
                        string fileName = global::System.IO.Path.GetFileName(libPath);
                        string destPath = global::System.IO.Path.Combine(_environmentManager.BinPath, fileName);
                        
                        if (!global::System.IO.File.Exists(destPath))
                        {
                            global::System.IO.File.Copy(libPath, destPath, false);
                        }

                        if (fileName.EndsWith(".so"))
                        {
                            string versionedDestPath = global::System.IO.Path.Combine(_environmentManager.BinPath, fileName + ".0");
                            if (!global::System.IO.File.Exists(versionedDestPath))
                            {
                                global::System.IO.File.Copy(libPath, versionedDestPath, false);
                            }
                        }
                    }

                    string targetWhisperCpuLib = global::System.IO.Path.Combine(_environmentManager.BinPath, "libggml-cpu.so.0");
                    if (!global::System.IO.File.Exists(targetWhisperCpuLib))
                    {
                        string fallbackCpu = global::System.IO.Path.Combine(_environmentManager.BinPath, "libggml-cpu-x64.so");
                        if (global::System.IO.File.Exists(fallbackCpu)) 
                        {
                            global::System.IO.File.Copy(fallbackCpu, targetWhisperCpuLib, false);
                        }
                    }
                }
                catch (Exception libSyncEx)
                {
                    GD.PrintErr($"BackendLauncher: Library synchronization fault: {libSyncEx.Message}");
                }

                // 6. Configuración del Puente de Python y Estructuras de Proceso
                // Define el intérprete de Python, la ruta del script del servidor TTS y configura los argumentos de inicio.
                string pythonExe = _environmentManager.IsWindows ? 
                    global::System.IO.Path.Combine(_environmentManager.EnvPath, "python", "python.exe") : 
                    global::System.IO.Path.Combine(_environmentManager.EnvPath, "python", "bin", "python3");

                string ttsScriptPath = global::System.IO.Path.Combine(_environmentManager.BinPath, "tts_server.py");

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

                ProcessStartInfo sherpaInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{ttsScriptPath}\" --port {SherpaPort}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // 7. Inyección de Variables de Entorno y Detección Inteligente de GPU
                // Configura el LD_LIBRARY_PATH y asigna dinámicamente el índice de GPU. 
                // Si gpuIndex es -1, se omite la declaración para permitir la autoselección de Vulkan.
                whisperInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = $"{_environmentManager.BinPath}:{llamaDir}";
                llamaInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = llamaDir;

                int gpuIndex = _configManager != null ? _configManager.SelectedGpuIndex : -1;
                if (gpuIndex >= 0)
                {
                    string gpuStr = gpuIndex.ToString();
                    whisperInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = gpuStr;
                    llamaInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = gpuStr;
                }

                // 8. Instanciación de Procesos y Vinculación de Eventos
                // Crea las instancias de Process y suscribe los delegados para el monitoreo de logs y errores.
                _whisperProcess = new Process { StartInfo = whisperInfo, EnableRaisingEvents = true };
                _llamaProcess = new Process { StartInfo = llamaInfo, EnableRaisingEvents = true };
                _sherpaProcess = new Process { StartInfo = sherpaInfo, EnableRaisingEvents = true };

                _whisperProcess.OutputDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Whisper] {e.Data}"); 
                };
                
                _whisperProcess.ErrorDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                    {
                        string lowerData = e.Data.ToLower();
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Whisper ERR] {e.Data}"); 
                        if (lowerData.Contains("out of memory") || lowerData.Contains("bad allocation") || lowerData.Contains("segmentation fault") || lowerData.Contains("memory fault"))
                            PanicKill($"Critical STT memory fault: {e.Data}");
                    }
                };
                _whisperProcess.Exited += OnProcessExited;

                _llamaProcess.OutputDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Llama] {e.Data}"); 
                };
                
                _llamaProcess.ErrorDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                    {
                        string lowerData = e.Data.ToLower();
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Llama ERR] {e.Data}");
                        if (lowerData.Contains("out of memory") || lowerData.Contains("bad allocation") || lowerData.Contains("segmentation fault"))
                            PanicKill($"Critical LLM memory fault: {e.Data}");
                        if (e.Data.Contains("server is listening") || e.Data.Contains("HTTP server listening"))
                        {
                            _retryCount = 0; 
                            CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
                        }
                    }
                };
                _llamaProcess.Exited += OnProcessExited;

                _sherpaProcess.OutputDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Kokoro] {e.Data}"); 
                };

                _sherpaProcess.ErrorDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Kokoro ERR] {e.Data}"); 
                };
                _sherpaProcess.Exited += OnProcessExited;

                // 9. Ejecución de los Sistemas de Backend
                // Inicia los subprocesos y activa la lectura asíncrona de sus flujos de salida.
                _sherpaProcess.Start();
                _sherpaProcess.BeginOutputReadLine();
                _sherpaProcess.BeginErrorReadLine();

                _whisperProcess.Start();
                _whisperProcess.BeginOutputReadLine();
                _whisperProcess.BeginErrorReadLine();

                _llamaProcess.Start();
                _llamaProcess.BeginOutputReadLine();
                _llamaProcess.BeginErrorReadLine();

                _isRunning = true;
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