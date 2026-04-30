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
            _ttsManager = GetNodeOrNull<Logic.Backend.NativeTTSManager>("/root/NativeTTSManager");
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
        /// Orchestrates the asynchronous allocation and binding of persistent C++ inference servers.
        /// Resolves dynamic execution paths based on the host operating system and engine-specific directories.
        /// Performs preemptive binary validation and synchronizes shared library dependencies to the global binary root.
        /// </summary>
        /// <param name="modelsDir">The absolute path to the directory containing model tensors.</param>
        /// <param name="safeFileName">The sanitized filename of the primary LLM model.</param>
        private async Task ManageBackendLifecycle(string modelsDir, string safeFileName)
        {
            try
            {
                // 1. Resource Configuration and Model Path Resolution
                // Calcula el paralelismo óptimo dividiendo los núcleos lógicos y establece las rutas absolutas para los archivos de tensores.
                int threadCount = Math.Max(1, global::System.Environment.ProcessorCount / 2);
                string modelLlamaPath = global::System.IO.Path.Combine(modelsDir, safeFileName);
                
                string sttModel = _configManager?.ActiveSTTModel ?? "Whisper_Base.bin"; 
                string modelWhisperPath = global::System.IO.Path.Combine(modelsDir, sttModel);

                // 2. Dynamic OS-Based Routing and Directory Definition
                // Determina el subdirectorio de arquitectura basándose en la plataforma de ejecución para localizar los motores específicos.
                string osFolder = _environmentManager.IsWindows ? "windows" : "linux";
                string llamaDir = global::System.IO.Path.Combine(_environmentManager.BinPath, osFolder, "llama");
                string whisperDir = global::System.IO.Path.Combine(_environmentManager.BinPath, osFolder, "whisper");

                // 3. Binary Discovery via Logic.Utils.FileResolver
                // Ejecuta una búsqueda recursiva o directa para identificar los puntos de entrada de los servidores de inferencia.
                string llamaBinPath = Logic.Utils.FileResolver.FindExecutable(llamaDir, _environmentManager.IsWindows, "llama-server");
                string whisperBinPath = Logic.Utils.FileResolver.FindExecutable(whisperDir, _environmentManager.IsWindows, "whisper-server");

                // 4. Critical Binary Integrity Validation
                // Verifica la existencia física de los ejecutables antes de la invocación para prevenir excepciones de proceso no encontrado.
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

                // 5. Shared Library Synchronization and Auto-Patching
                try
                {
                    // Itera sobre las librerías dinámicas GGML y las desplaza al directorio raíz de ejecución para satisfacer el cargador dinámico.
                    string[] ggmlLibs = global::System.IO.Directory.GetFiles(llamaDir, "libggml*.so*");
                    foreach (string libPath in ggmlLibs)
                    {
                        string fileName = global::System.IO.Path.GetFileName(libPath);
                        string destPath = global::System.IO.Path.Combine(_environmentManager.BinPath, fileName);
                        
                        if (!global::System.IO.File.Exists(destPath))
                        {
                            global::System.IO.File.Copy(libPath, destPath, false);
                        }

                        // Genera una copia con sufijo versionado (.so.0) para mantener compatibilidad con binarios vinculados de forma rígida.
                        if (fileName.EndsWith(".so"))
                        {
                            string versionedDestPath = global::System.IO.Path.Combine(_environmentManager.BinPath, fileName + ".0");
                            if (!global::System.IO.File.Exists(versionedDestPath))
                            {
                                global::System.IO.File.Copy(libPath, versionedDestPath, false);
                            }
                        }
                    }

                    // Establece una redundancia para la librería de cálculo en CPU mediante la clonación de la variante x64 detectada.
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

                // 6. Process Start Information Initialization
                // Estructura los argumentos de línea de comandos y configura la redirección de flujos de E/S para el monitoreo.
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

                // 7. Environment Variable Injection for Dynamic Linker
                // Define el alcance de búsqueda del enlazador y asigna la visibilidad de dispositivos Vulkan para aceleración por hardware.
                whisperInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = $"{_environmentManager.BinPath}:{llamaDir}";
                whisperInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = "1";
                
                llamaInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = llamaDir;
                llamaInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = "1";

                // 8. Process Instance Instantiation and Event Binding
                // Instancia los controladores de proceso y suscribe delegados para la captura de logs y gestión de fallos críticos.
                _whisperProcess = new Process { StartInfo = whisperInfo, EnableRaisingEvents = true };
                _llamaProcess = new Process { StartInfo = llamaInfo, EnableRaisingEvents = true };

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

                // 9. Native TTS Initialization Sequence
                // Valida y arranca el subsistema de síntesis de voz nativo mediante la interfaz de gestión interna.
                if (_ttsManager != null && _ttsManager.InitializeNativeEngine()) 
                {
                    GD.Print("BackendLauncher: Native TTS engine online.");
                }

                // 10. OS-Level Process Execution
                // Dispara el inicio de los procesos y habilita la lectura asíncrona de los flujos de texto redirigidos.
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