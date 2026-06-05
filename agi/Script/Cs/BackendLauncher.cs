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
            string[] errorPatterns = { "ERR", "Error", "Exception", "Fault", "Critical", "Failure", "Unprocessable", "422", "500", "404", "Traceback" };
            bool containsErrorPattern = false;

            foreach (string pattern in errorPatterns)
            {
                if (data.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    containsErrorPattern = true;
                    break;
                }
            }

            bool isInfoLog = data.Contains("INFO:", StringComparison.OrdinalIgnoreCase) || 
                             data.Contains("WARNING:", StringComparison.OrdinalIgnoreCase);

            if ((isErrorStream && !isInfoLog) || (containsErrorPattern && !isInfoLog))
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
            GD.Print("ResourceMonitor: Initiating resource reconciliation routine for native engines...");

            try
            {
                if (_environmentManager != null && _environmentManager.Bridge != null)
                {
                    GD.Print("ResourceMonitor: Delegating resource reconciliation routine to operating system bridge...");
                    _environmentManager.Bridge.TerminateOrphanedResources();
                }
                else
                {
                    GD.PrintErr("ResourceMonitor: Unable to delegate cleanup. Platform Bridge is uninitialized.");
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

                string osFolder = _environmentManager.Bridge.OperatingSystemIdentifier.ToLower();
                string llamaDir = global::System.IO.Path.Combine(_environmentManager.BinPath, osFolder, "llama");
                string whisperDir = global::System.IO.Path.Combine(_environmentManager.BinPath, osFolder, "whisper");

                string llamaArgs = $"--model \"{modelLlamaPath}\" --host {bindAddress} --port {LlamaPort} --ctx-size {ctxSize} --threads {threadCount} -ngl {(_configManager?.PerformanceProfile?.GpuLayers ?? 99)}{extraLlamaArgs}";
                string whisperArgs = $"-m \"{modelWhisperPath}\" --host {bindAddress} --port {WhisperPort} --threads {threadCount}";

                ProcessStartInfo llamaInfo;
                ProcessStartInfo whisperInfo;
                try
                {
                    llamaInfo = _environmentManager.Bridge.ConfigureEngineExecution("llama-server", llamaArgs, llamaDir);
                    whisperInfo = _environmentManager.Bridge.ConfigureEngineExecution("whisper-server", whisperArgs, whisperDir);
                }
                catch (global::System.IO.FileNotFoundException)
                {
                    GD.PrintErr("BackendLauncher: Fatal - Essential binaries (Llama/Whisper) are missing.");
                    CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);
                    return;
                }

                string ttsScriptPath = global::System.IO.Path.Combine(_environmentManager.BinPath, "tts_server.py");
                string searchScriptPath = global::System.IO.Path.Combine(_environmentManager.BinPath, "search_server.py");
                string mcpScriptPath = global::System.IO.Path.Combine(_environmentManager.BinPath, "mcp_server.py");
                string projRoot = ProjectSettings.GlobalizePath("res://");

                ProcessStartInfo searchInfo = _environmentManager.Bridge.ConfigurePythonMicroservice(searchScriptPath, $"--port {SearchPort}", projRoot, "python_search");
                ProcessStartInfo mcpInfo = _environmentManager.Bridge.ConfigurePythonMicroservice(mcpScriptPath, $"--port {SearchPort + 2}", projRoot, "python_search");
                
                string defaultWorkspace = ProjectSettings.GlobalizePath("user://workspace");
                string activeWorkspace = !string.IsNullOrEmpty(_configManager?.PersistedWorkspacePath) 
                                         ? _configManager.PersistedWorkspacePath : defaultWorkspace;
                mcpInfo.EnvironmentVariables["AGI_WORKSPACE"] = activeWorkspace;

                ProcessStartInfo sherpaInfo = null;
                try 
                {
                    string ttsModelFolder = _configManager?.ActiveTTSModel ?? "";
                    string ttsModelsDir = global::System.IO.Path.Combine(modelsDir, ttsModelFolder);
                    sherpaInfo = _environmentManager.Bridge.ConfigurePythonMicroservice(ttsScriptPath, $"--port {SherpaPort} --models-dir \"{ttsModelsDir}\"", projRoot);
                } 
                catch (global::System.IO.FileNotFoundException) { /* Optional dependency */ }

                // ── Vulkan-exclusive GPU routing for llama-server ─────────────────────
                // llama-server is compiled against libggml-vulkan.so — NOT libggml-cuda.so.
                // Passing a real CUDA device index causes driver initialization conflicts.
                // CUDA_VISIBLE_DEVICES is ALWAYS set to "-1" to suppress the CUDA backend.
                // GGML_VK_VISIBLE_DEVICES selects the correct Vulkan physical device.
                llamaInfo.EnvironmentVariables["CUDA_VISIBLE_DEVICES"] = "-1";

                int gpuIndex = _configManager?.SelectedGpuIndex ?? -1;
                if (gpuIndex >= 0)
                {
                    // Translate the CUDA device index to its matching Vulkan device index.
                    string vulkanGpuStr = GetVulkanIndexForGpu(gpuIndex);
                    llamaInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = vulkanGpuStr;
                    GD.Print($"[BackendLauncher] Vulkan GPU routing: CUDA index {gpuIndex} → Vulkan device {vulkanGpuStr}. CUDA suppressed.");
                }
                else
                {
                    // No discrete GPU selected — use Vulkan device 0 (integrated GPU / first available).
                    llamaInfo.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = "0";
                    GD.Print("[BackendLauncher] Vulkan GPU routing: No GPU selected. Defaulting to Vulkan device 0 (integrated/first available).");
                }

                _whisperProcess = new Process { StartInfo = whisperInfo, EnableRaisingEvents = true };
                _whisperProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Whisper] {e.Data}"); };
                _whisperProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, $"[Whisper ERR] {e.Data}"); };
                _whisperProcess.Exited += OnProcessExited;

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

                if (!isCloudMode)
                {
                    _llamaProcess = new Process { StartInfo = llamaInfo, EnableRaisingEvents = true };

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

            if (_environmentManager != null && _environmentManager.Bridge != null)
            {
                GD.Print("BackendLauncher: Executing secondary native process tree sweep via Platform Bridge.");
                _environmentManager.Bridge.TerminateOrphanedResources();
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

        private string GetVulkanIndexForGpu(int cudaIndex)
        {
            try
            {
                // 1. Get UUID for cudaIndex
                string uuid = "";
                using (Process p1 = new Process())
                {
                    p1.StartInfo.FileName = "nvidia-smi";
                    p1.StartInfo.Arguments = "--query-gpu=index,uuid --format=csv,noheader";
                    p1.StartInfo.UseShellExecute = false;
                    p1.StartInfo.RedirectStandardOutput = true;
                    p1.StartInfo.CreateNoWindow = true;
                    p1.Start();
                    
                    string output1 = p1.StandardOutput.ReadToEnd();
                    p1.WaitForExit();

                    string[] lines = output1.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 2 && parts[0].Trim() == cudaIndex.ToString())
                        {
                            uuid = parts[1].Trim();
                            if (uuid.StartsWith("GPU-", StringComparison.OrdinalIgnoreCase)) 
                                uuid = uuid.Substring(4);
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(uuid)) return cudaIndex.ToString();

                // 2. Find Vulkan index for UUID
                using (Process p2 = new Process())
                {
                    p2.StartInfo.FileName = "vulkaninfo";
                    p2.StartInfo.Arguments = "--summary";
                    p2.StartInfo.UseShellExecute = false;
                    p2.StartInfo.RedirectStandardOutput = true;
                    p2.StartInfo.CreateNoWindow = true;
                    p2.Start();

                    string output2 = p2.StandardOutput.ReadToEnd();
                    p2.WaitForExit();

                    string currentGpu = "";
                    string[] lines2 = output2.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines2)
                    {
                        if (line.StartsWith("GPU"))
                        {
                            currentGpu = line.Split(':')[0].Replace("GPU", "").Trim();
                        }
                        else if (line.Contains("deviceUUID") && !string.IsNullOrEmpty(currentGpu))
                        {
                            string vUuid = line.Split('=')[1].Trim();
                            if (vUuid.Equals(uuid, StringComparison.OrdinalIgnoreCase))
                            {
                                return currentGpu;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[BackendLauncher] Error matching Vulkan ID: {ex.Message}");
            }
            
            return cudaIndex.ToString();
        }
    }
}