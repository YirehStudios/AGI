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
        private dynamic _environmentManager;

        public override void _Ready()
        {
            _configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            _environmentManager = GetNodeOrNull("/root/EnvironmentManager");
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
        /// Intercepts and parses continuous output streams from background microservices.
        /// Performs conditional pattern analysis to detect execution errors and routes telemetry to the appropriate diagnostic interface.
        /// </summary>
        private static void LogMicroserviceStream(string serviceName, string data, bool isErrorStream = false)
        {
            if (string.IsNullOrEmpty(data)) return;

            string formattedLog = $"[{serviceName}] {data}";
            string[] errorPatterns = { "ERR", "Error", "Exception", "Fault", "Critical", "Failure", "Unprocessable", "422", "500", "404" };
            bool containsErrorPattern = false;

            foreach (string pattern in errorPatterns)
            {
                if (data.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    containsErrorPattern = true;
                    break;
                }
            }

            if (isErrorStream || containsErrorPattern)
            {
                GD.PrintErr($"[DIAGNOSTIC-ERR] {formattedLog}");
            }
            else
            {
                GD.Print($"[DIAGNOSTIC-INFO] {formattedLog}");
            }
        }

        /// <summary>
        /// Executes a preemptive traversal of the operating system's active process tree prior to initialization,
        /// forcefully terminating detached binary instances to guarantee unhindered port binding and memory allocation.
        /// </summary>
        public void TerminateOrphanedResources()
        {
            string[] targetResources = { "llama-server", "whisper-server", "sherpa-onnx-tts-server" };

            GD.Print("ResourceMonitor: Initiating resource reconciliation routine for native engines...");

            try
            {
                foreach (string resourceName in targetResources)
                {
                    Process[] orphanedProcesses = Process.GetProcessesByName(resourceName);
                    GD.Print($"ResourceMonitor: Found {orphanedProcesses.Length} instances matching tracking template '{resourceName}'.");

                    foreach (Process process in orphanedProcesses)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                GD.Print($"ResourceMonitor: Active orphan detected (PID: {process.Id}). Executing conditional teardown...");
                                process.Kill(true);
                                process.WaitForExit(1000);
                                GD.Print($"ResourceMonitor: Orphaned C++ native resource '{resourceName}' (PID: {process.Id}) terminated successfully.");
                            }
                            else
                            {
                                GD.Print($"ResourceMonitor: Orphan target (PID: {process.Id}) has already transitioned to an exited state.");
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
                if (_environmentManager != null)
                {
                    GD.Print("ResourceMonitor: Dispatched active port clearing routines to operating system subsystems.");
                    if (_environmentManager.IsWindows)
                    {
                        // Barrido de puertos en Windows para evitar que scripts bloqueen la reinstalación
                        string portsToClear = $"{LlamaPort}, {WhisperPort}, {SherpaPort}, {SearchPort}, {SearchPort + 2}";
                        string psCommand = $"foreach ($port in @({portsToClear})) {{ Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue | ForEach-Object {{ Stop-Process -Id $_.OwningProcess -Force }} }}";
                        OS.Execute("powershell", new string[] { "-NoProfile", "-Command", psCommand }, new Godot.Collections.Array(), true);
                    }
                    else
                    {
                        OS.Execute("pkill", new string[] { "-f", "search_server.py" }, new Godot.Collections.Array(), true);
                        OS.Execute("pkill", new string[] { "-f", "tts_server.py" }, new Godot.Collections.Array(), true);
                        OS.Execute("pkill", new string[] { "-f", "mcp_server.py" }, new Godot.Collections.Array(), true);
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
        /// Orchestrates the lifecycle of inference engines (Llama, Whisper), the Search Microservice, the TTS bridge, 
        /// and the MCP tool gateway. Performs path resolution, binary validation, network binding mapping, and dynamic hardware 
        /// configuration for all subprocesses based on designated performance tiers and network states.
        /// </summary>
        /// <param name="modelsDir">Absolute path to the models directory.</param>
        /// <param name="safeFileName">Sanitized filename of the LLM model to be loaded by Llama.</param>
        private async Task ManageBackendLifecycle(string modelsDir, string safeFileName)
        {
            bool isCloudMode = _configManager != null &&
                (_configManager.CurrentMode == Logic.System.Config.ConfigManager.AppMode.CloudAPI ||
                _configManager.CurrentNetworkState == Logic.System.Config.ConfigManager.NetworkState.CloudAPI);

            try
            {
                int threadCount = Math.Max(1, global::System.Environment.ProcessorCount / 2);
                int ctxSize = 4096;
                string extraLlamaArgs = string.Empty;
                bool deferSpeechServices = false;

                if (_configManager != null)
                {
                    switch (_configManager.CurrentPerformanceTier)
                    {
                        case Logic.System.Config.ConfigManager.PerformanceTier.Low:
                            threadCount = Math.Max(1, global::System.Environment.ProcessorCount / 4);
                            ctxSize = 2048;
                            deferSpeechServices = true;
                            break;
                        case Logic.System.Config.ConfigManager.PerformanceTier.High:
                            threadCount = global::System.Environment.ProcessorCount;
                            ctxSize = 8192;
                            extraLlamaArgs = " --no-mmap";
                            break;
                        case Logic.System.Config.ConfigManager.PerformanceTier.Medium:
                        default:
                            threadCount = Math.Max(1, global::System.Environment.ProcessorCount / 2);
                            ctxSize = 4096;
                            break;
                    }
                }

                string bindAddress = _configManager?.TargetBindAddress ?? "127.0.0.1";
                string modelLlamaPath = global::System.IO.Path.Combine(modelsDir, safeFileName);
                string sttModel = _configManager?.ActiveSTTModel ?? "Whisper_Base.bin";
                string modelWhisperPath = global::System.IO.Path.Combine(modelsDir, sttModel);

                string osFolder = _environmentManager.IsWindows ? "windows" : "linux";
                string llamaDir = global::System.IO.Path.Combine(_environmentManager.BinPath, osFolder, "llama");
                string whisperDir = global::System.IO.Path.Combine(_environmentManager.BinPath, osFolder, "whisper");

                string llamaBinPath = Logic.Utils.FileResolver.FindExecutable(llamaDir, _environmentManager.IsWindows, "llama-server");
                string whisperBinPath = Logic.Utils.FileResolver.FindExecutable(whisperDir, _environmentManager.IsWindows, "whisper-server");

                if (!global::System.IO.File.Exists(llamaBinPath) || !global::System.IO.File.Exists(whisperBinPath))
                {
                    GD.PrintErr("BackendLauncher: Fatal - Essential binaries (Llama/Whisper) are missing.");
                    CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);
                    return;
                }

                string pythonExe = _environmentManager.IsWindows ?
                    global::System.IO.Path.Combine(_environmentManager.EnvPath, "python", "python.exe") :
                    global::System.IO.Path.Combine(_environmentManager.EnvPath, "python", "bin", "python3");

                string searchPythonExe = _environmentManager.IsWindows ?
                    global::System.IO.Path.Combine(_environmentManager.EnvPath, "python_search", "python.exe") :
                    global::System.IO.Path.Combine(_environmentManager.EnvPath, "python_search", "bin", "python3");

                string ttsScriptPath = global::System.IO.Path.Combine(_environmentManager.BinPath, "tts_server.py");
                string searchScriptPath = global::System.IO.Path.Combine(_environmentManager.BinPath, "search_server.py");
                string mcpScriptPath = global::System.IO.Path.Combine(_environmentManager.BinPath, "mcp_server.py");

                ProcessStartInfo whisperInfo = new ProcessStartInfo
                {
                    FileName = whisperBinPath,
                    Arguments = $"-m \"{modelWhisperPath}\" --host {bindAddress} --port {WhisperPort} --threads {threadCount}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                ProcessStartInfo llamaInfo = new ProcessStartInfo
                {
                    FileName = llamaBinPath,
                    Arguments = $"--model \"{modelLlamaPath}\" --host {bindAddress} --port {LlamaPort} --ctx-size {ctxSize} --threads {threadCount} -ngl 99{extraLlamaArgs}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                ProcessStartInfo searchInfo = new ProcessStartInfo
                {
                    FileName = searchPythonExe,
                    Arguments = $"-u \"{searchScriptPath}\" --port {SearchPort}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                ProcessStartInfo mcpInfo = new ProcessStartInfo
                {
                    FileName = searchPythonExe,
                    Arguments = $"-u \"{mcpScriptPath}\" --port {SearchPort + 2}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

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

                whisperInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = whisperDir;
                llamaInfo.EnvironmentVariables["LD_LIBRARY_PATH"] = llamaDir;

                int gpuIndex = _configManager?.SelectedGpuIndex ?? -1;
                if (gpuIndex >= 0)
                {
                    string gpuStr = gpuIndex.ToString();
                    whisperInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = gpuStr;
                    llamaInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = gpuStr;
                }

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
                            if (e.Data.Contains("server is listening")) CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
                        }
                    };
                    _llamaProcess.Exited += OnProcessExited;

                    if (!deferSpeechServices)
                    {
                        _whisperProcess.Start();
                        _whisperProcess.BeginOutputReadLine();
                        _whisperProcess.BeginErrorReadLine();
                    }
                    else
                    {
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, "[Performance-Tier] Low allocation configuration detected. Whisper STT engine deployment deferred.");
                    }

                    _llamaProcess.Start();
                    _llamaProcess.BeginOutputReadLine();
                    _llamaProcess.BeginErrorReadLine();
                }

                _searchProcess = new Process { StartInfo = searchInfo, EnableRaisingEvents = true };
                _searchProcess.OutputDataReceived += (s, e) => LogMicroserviceStream("Search", e.Data, false);
                _searchProcess.ErrorDataReceived += (s, e) => LogMicroserviceStream("Search", e.Data, true);
                _searchProcess.Exited += OnProcessExited;
                _searchProcess.Start();
                _searchProcess.BeginOutputReadLine();
                _searchProcess.BeginErrorReadLine();

                _mcpProcess = new Process { StartInfo = mcpInfo, EnableRaisingEvents = true };
                _mcpProcess.OutputDataReceived += (s, e) => LogMicroserviceStream("MCP", e.Data, false);
                _mcpProcess.ErrorDataReceived += (s, e) => LogMicroserviceStream("MCP", e.Data, true);
                _mcpProcess.Exited += OnProcessExited;
                _mcpProcess.Start();
                _mcpProcess.BeginOutputReadLine();
                _mcpProcess.BeginErrorReadLine();

                if (sherpaInfo != null)
                {
                    _sherpaProcess = new Process { StartInfo = sherpaInfo, EnableRaisingEvents = true };
                    _sherpaProcess.OutputDataReceived += (s, e) => LogMicroserviceStream("Sherpa-TTS", e.Data, false);
                    _sherpaProcess.ErrorDataReceived += (s, e) => LogMicroserviceStream("Sherpa-TTS", e.Data, true);
                    _sherpaProcess.Exited += OnProcessExited;

                    if (!deferSpeechServices)
                    {
                        _sherpaProcess.Start();
                        _sherpaProcess.BeginOutputReadLine();
                        _sherpaProcess.BeginErrorReadLine();
                    }
                    else
                    {
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, "[Performance-Tier] Low allocation configuration detected. Sherpa TTS engine deployment deferred.");
                    }
                }

                _isRunning = true;

                if (isCloudMode)
                {
                    CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, "[Microservices] Search, TTS, and MCP infrastructure mapped. Local Llama bypassed.");
                    CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
                }

                await MonitorProcessHealth();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Unexpected lifecycle fault: {ex.Message}");
                CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);
            }
        }

        /// <summary>
        /// Evaluates state tracking metadata and forces execution instantiation on dormant, 
        /// deferred background microservices (Whisper STT and Sherpa TTS) at system runtime.
        /// </summary>
        public void StartSpeechServices()
        {
            if (_configManager != null &&
                (_configManager.CurrentMode == Logic.System.Config.ConfigManager.AppMode.CloudAPI ||
                _configManager.CurrentNetworkState == Logic.System.Config.ConfigManager.NetworkState.CloudAPI))
            {
                GD.Print("[BackendLauncher] Speech engine runtime deployment rejected. Cloud execution mode is active.");
                return;
            }

            try
            {
                if (_whisperProcess != null)
                {
                    try
                    {
                        if (_whisperProcess.HasExited || _whisperProcess.Responding) { }
                    }
                    catch (InvalidOperationException)
                    {
                        GD.Print("[BackendLauncher] Initializing runtime instantiation context for Whisper STT pipeline.");
                        _whisperProcess.Start();
                        _whisperProcess.BeginOutputReadLine();
                        _whisperProcess.BeginErrorReadLine();
                    }
                }

                if (_sherpaProcess != null)
                {
                    try
                    {
                        if (_sherpaProcess.HasExited || _sherpaProcess.Responding) { }
                    }
                    catch (InvalidOperationException)
                    {
                        GD.Print("[BackendLauncher] Initializing runtime instantiation context for Sherpa TTS pipeline.");
                        _sherpaProcess.Start();
                        _sherpaProcess.BeginOutputReadLine();
                        _sherpaProcess.BeginErrorReadLine();
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Dynamic speech system instantiation fault: {ex.Message}");
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
                if (_llamaProcess != null)
                {
                    try
                    {
                        if (!_llamaProcess.HasExited)
                        {
                            _llamaProcess.Refresh();
                            if (_llamaProcess.WorkingSet64 > MaxRamAllowed)
                            {
                                PanicKill("RAM overflow detected in Llama Server.");
                                break;
                            }
                        }
                    }
                    catch (InvalidOperationException) { }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"BackendLauncher: Llama memory telemetry failure: {ex.Message}");
                    }
                }

                if (_whisperProcess != null)
                {
                    try
                    {
                        if (!_whisperProcess.HasExited)
                        {
                            _whisperProcess.Refresh();
                            if (_whisperProcess.WorkingSet64 > MaxRamAllowed)
                            {
                                PanicKill("RAM overflow detected in Whisper Server.");
                                break;
                            }
                        }
                    }
                    catch (InvalidOperationException) { }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"BackendLauncher: Whisper memory telemetry failure: {ex.Message}");
                    }
                }

                if (_sherpaProcess != null)
                {
                    try
                    {
                        if (!_sherpaProcess.HasExited)
                        {
                            _sherpaProcess.Refresh();
                            if (_sherpaProcess.WorkingSet64 > MaxRamAllowed)
                            {
                                PanicKill("RAM overflow detected in Sherpa Server.");
                                break;
                            }
                        }
                    }
                    catch (InvalidOperationException) { }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"BackendLauncher: Sherpa memory telemetry failure: {ex.Message}");
                    }
                }

                if (_mcpProcess != null)
                {
                    try
                    {
                        if (!_mcpProcess.HasExited)
                        {
                            _mcpProcess.Refresh();
                            if (_mcpProcess.WorkingSet64 > MaxRamAllowed)
                            {
                                PanicKill("RAM overflow detected in MCP Server.");
                                break;
                            }
                        }
                    }
                    catch (InvalidOperationException) { }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"BackendLauncher: MCP memory telemetry failure: {ex.Message}");
                    }
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
                if (_llamaProcess != null)
                {
                    GD.Print($"BackendLauncher: Evaluating Llama process context. Exited state: {_llamaProcess.HasExited}");
                    if (!_llamaProcess.HasExited) { GD.Print("BackendLauncher: Dispatched kill signal to Llama server."); _llamaProcess.Kill(true); }
                }
                if (_whisperProcess != null)
                {
                    GD.Print($"BackendLauncher: Evaluating Whisper process context. Exited state: {_whisperProcess.HasExited}");
                    if (!_whisperProcess.HasExited) { GD.Print("BackendLauncher: Dispatched kill signal to Whisper server."); _whisperProcess.Kill(true); }
                }
                if (_sherpaProcess != null)
                {
                    GD.Print($"BackendLauncher: Evaluating Sherpa process context. Exited state: {_sherpaProcess.HasExited}");
                    if (!_sherpaProcess.HasExited) { GD.Print("BackendLauncher: Dispatched kill signal to Sherpa server."); _sherpaProcess.Kill(true); }
                }
                if (_searchProcess != null)
                {
                    GD.Print($"BackendLauncher: Evaluating Search microservice context. Exited state: {_searchProcess.HasExited}");
                    if (!_searchProcess.HasExited) { GD.Print("BackendLauncher: Dispatched kill signal to Search microservice."); _searchProcess.Kill(true); }
                }
                if (_mcpProcess != null)
                {
                    GD.Print($"BackendLauncher: Evaluating MCP process context. Exited state: {_mcpProcess.HasExited}");
                    if (!_mcpProcess.HasExited) { GD.Print("BackendLauncher: Dispatched kill signal to MCP gateway."); _mcpProcess.Kill(true); }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Secondary fault executing process purge (Kill): {ex.Message}");
            }

            if (_environmentManager != null && _environmentManager.IsWindows)
            {
                GD.Print("BackendLauncher: Executing secondary OS-level CIM pipeline purge on Windows hosts.");
                OS.Execute("powershell", new string[] { "-NoProfile", "-Command", "Get-CimInstance Win32_Process -Filter \"Name='python.exe' AND CommandLine LIKE '%tts_server.py%'\" | Invoke-CimMethod -MethodName Terminate" }, new Godot.Collections.Array(), true);
                OS.Execute("powershell", new string[] { "-NoProfile", "-Command", "Get-CimInstance Win32_Process -Filter \"Name='python.exe' AND CommandLine LIKE '%search_server.py%'\" | Invoke-CimMethod -MethodName Terminate" }, new Godot.Collections.Array(), true);
                OS.Execute("powershell", new string[] { "-NoProfile", "-Command", "Get-CimInstance Win32_Process -Filter \"Name='python.exe' AND CommandLine LIKE '%mcp_server.py%'\" | Invoke-CimMethod -MethodName Terminate" }, new Godot.Collections.Array(), true);
            }
            else
            {
                GD.Print("BackendLauncher: Executing secondary Linux pkill process tree sweep.");
                OS.Execute("pkill", new string[] { "-f", "tts_server.py" }, new Godot.Collections.Array(), true);
                OS.Execute("pkill", new string[] { "-f", "search_server.py" }, new Godot.Collections.Array(), true);
                OS.Execute("pkill", new string[] { "-f", "mcp_server.py" }, new Godot.Collections.Array(), true);
            }

            _retryCount = MaxRetries;

            CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);

            GD.Print("BackendLauncher: Freezing boot operations for 5 seconds to allow OS level purge...");
            await Task.Delay(5000);
        }

        /// <summary>
        /// Intercepts termination signals dispatched by underlying operating system child processes.
        /// Validates process identities using safe type interrogation routines, and evaluates operational flags
        /// to trigger appropriate diagnostic teardowns or recovery routines while preventing interface thread blockages.
        /// </summary>
        private void OnProcessExited(object sender, EventArgs e)
        {
            if (_isPanicking) return;

            string processName = "Unknown";

            if (sender is Process p)
            {
                try
                {
                    processName = Path.GetFileName(p.StartInfo.FileName);
                }
                catch { }
            }

            if (_isRunning)
            {
                if (sender == _whisperProcess || sender == _sherpaProcess)
                {
                    GD.PrintErr($"[DIAGNOSTIC-ERR] Native speech service ({processName}) exited. Core microservices (Search/MCP) remain operational.");
                    return;
                }

                PanicKill($"Native microservice engine ({processName}) terminated unexpectedly. Initializing teardown sequence.");
            }
            else
            {
                _isRunning = false;
                GD.PrintErr($"BackendLauncher: Systemic execution collapse observed on target component ({processName}).");
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
        public void StopBackend()
        {
            GD.Print("BackendLauncher: Stopping active backend instances manually.");
            _isRunning = false;

            try
            {
                if (_llamaProcess != null && !_llamaProcess.HasExited) { _llamaProcess.Kill(); _llamaProcess.Dispose(); }
                if (_whisperProcess != null && !_whisperProcess.HasExited) { _whisperProcess.Kill(); _whisperProcess.Dispose(); }
                if (_sherpaProcess != null && !_sherpaProcess.HasExited) { _sherpaProcess.Kill(); _sherpaProcess.Dispose(); }
                if (_searchProcess != null && !_searchProcess.HasExited) { _searchProcess.Kill(); _searchProcess.Dispose(); }
                if (_mcpProcess != null && !_mcpProcess.HasExited) { _mcpProcess.Kill(); _mcpProcess.Dispose(); }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Error during StopBackend: {ex.Message}");
            }
        }

        public override void _ExitTree()
        {
            GD.Print("BackendLauncher: Purging native C++ and Python processes (Preventing Zombies).");
            StopBackend();
        }
    }
}