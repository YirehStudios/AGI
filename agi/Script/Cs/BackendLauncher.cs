using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Logic.Utils;

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

        private Process _backendProcess;
        private bool _isRunning = false;
        private int _retryCount = 0;
        private const int MaxRetries = 3;

        public void StartBackend()
        {
            Task.Run(async () => await ManageBackendLifecycle());
        }

        /// <summary>
        /// Configures and orchestrates the llama-server execution within a hardware-aware Docker container.
        /// </summary>
        private async Task ManageBackendLifecycle()
        {
            try
            {
                // Executes a synchronous system call to forcefully terminate and remove any existing container instances to prevent port collisions.
                Godot.OS.Execute("docker", new string[] { "rm", "-f", "agi-llama-server" }, new Godot.Collections.Array(), true);

                // Resolves the Godot-specific user path to a universal absolute system path required for Docker volume mapping.
                string modelsDir = ProjectSettings.GlobalizePath("user://models"); 
                
                // Allocates optimal thread count based on available logical processors to balance performance without saturating host resources.
                int threadCount = Math.Max(1, System.Environment.ProcessorCount / 2);
                
                // Queries the RenderingServer for the active video adapter to dynamically assign the correct hardware abstraction layer for the container.
                string hardwareBridge = "";
                string adapterName = Godot.RenderingServer.GetVideoAdapterName().ToLower();
                
                if (adapterName.Contains("nvidia"))
                {
                    hardwareBridge = "--gpus all";
                    GD.Print("BackendLauncher: NVIDIA GPU detected. Initializing CUDA container bridge.");
                }
                else
                {
                    hardwareBridge = "--device /dev/dri";
                    GD.Print("BackendLauncher: Non-NVIDIA adapter detected. Initializing universal DRI container bridge.");
                }

                // Constructs the Docker execution string interpolating hardware bindings, volume mappings, and engine parameters, binding the host to 0.0.0.0 for external access.
                string arguments = $"run --name agi-llama-server --rm --gpus all --device /dev/dri -v \"{modelsDir}:/app/models\" -p 8080:8080 yirehstudios/agi-backend:latest llama-server --host 0.0.0.0 --model /app/models/Ministral-3b-instruct.Q4_K_S.gguf --port 8080 --ctx-size 4096 --threads {threadCount} --n-gpu-layers 99";                
                // Configures the native process environment, suppressing window creation and isolating standard output/error streams for intercepting.
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Initializes the process wrapper and subscribes to structural lifecycle and data stream events.
                _backendProcess = new Process { StartInfo = startInfo };
                _backendProcess.EnableRaisingEvents = true;
                _backendProcess.Exited += OnProcessExited;

                _backendProcess.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) GD.Print($"[Docker Llama] {e.Data}"); };
                _backendProcess.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) GD.PrintErr($"[Docker Llama ERROR] {e.Data}"); };

                _backendProcess.Start();

                // Initiates asynchronous read operations on the redirected output and error streams to prevent pipeline blocking.
                _backendProcess.BeginOutputReadLine(); 
                _backendProcess.BeginErrorReadLine();  
                _isRunning = true;

                GD.Print($"BackendLauncher: Docker Llama-server process started [ID: {_backendProcess.Id}]");

                // Decouples the health monitoring routine to a separate task, preventing blockage of the current execution thread.
                _ = MonitorProcessHealth();
                
                // Dispatches the readiness signal via the Godot message queue to ensure thread-safe propagation.
                CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
            }
            catch (Exception ex)
            {
                // Captures critical initialization exceptions and invokes the recovery/retry mechanism.
                GD.PrintErr($"BackendLauncher: Critical Failure. {ex.Message}");
                HandleCrash();
            }
        }

        public void StartWhisper(string audioFilePath)
        {
            try
            {
                string modelsDir = PathConstants.ModelsDir;
                string audioDir = Path.GetDirectoryName(audioFilePath);
                string audioFileName = Path.GetFileName(audioFilePath);

                string arguments = $"run --rm -v \"{modelsDir}:/app/models\" -v \"{audioDir}:/app/audio\" yirehstudios/agi-backend:latest whisper-cli -m /app/models/base.bin -f /app/audio/{audioFileName} --output-txt";

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process whisperProcess = new Process { StartInfo = startInfo };
                whisperProcess.Start();
                GD.Print($"BackendLauncher: Docker Whisper process started [ID: {whisperProcess.Id}]");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Docker Whisper execution failed. {ex.Message}");
            }
        }

        /// <summary>
        /// Initializes the Sherpa-ONNX acoustic synthesis engine asynchronously via Docker container.
        /// </summary>
        public void StartSherpaTTS(string textToSynthesize)
        {
            try
            {
                string modelsDir = PathConstants.ModelsDir;
                string outputAudioDir = ProjectSettings.GlobalizePath("user://agi");

                // Structures command line parameters by injecting containerized volume maps.
                string arguments = $"run --rm -v \"{modelsDir}:/app/models\" -v \"{outputAudioDir}:/app/audio\" yirehstudios/agi-backend:latest sherpa-onnx-offline-tts --vits-model=\"/app/models/vits-piper-es_ES-miro-high/es_ES-miro-high.onnx\" --vits-tokens=\"/app/models/vits-piper-es_ES-miro-high/tokens.txt\" --vits-lexicon=\"/app/models/vits-piper-es_ES-miro-high/lexicon.txt\" --output-filename=\"/app/audio/temp_voice.wav\" \"{textToSynthesize}\"";

                // Defines execution parameters, suppressing window creation and redirecting I/O streams.
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Instantiates and starts the child process at the operating system level.
                Process sherpaProcess = new Process { StartInfo = startInfo };
                sherpaProcess.Start();
                
                GD.Print($"BackendLauncher: Docker Sherpa-ONNX process started [ID: {sherpaProcess.Id}]");
            }
            catch (Exception ex)
            {
                // Intercepts exceptions during process invocation to prevent main thread interruption.
                GD.PrintErr($"BackendLauncher: Docker Sherpa-ONNX execution failed. {ex.Message}");
            }
        }

        private async Task MonitorProcessHealth()
        {
            while (_isRunning && _backendProcess != null && !_backendProcess.HasExited)
            {
                await Task.Delay(5000);
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            _isRunning = false;
            GD.PrintErr("BackendLauncher: Process exited unexpectedly.");
            HandleCrash();
        }

        private void HandleCrash()
        {
            if (_retryCount < MaxRetries)
            {
                _retryCount++;
                GD.Print($"BackendLauncher: Attempting revival ({_retryCount}/{MaxRetries})...");
                StartBackend();
            }
            else
            {
                CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);
            }
        }
        
        public override void _ExitTree()
        {
            GD.Print("BackendLauncher: Alt+F4 detectado o cerrando app. ¡Asesinando contenedores Zombis!");
            _isRunning = false;
            
            // Comando aniquilador de Docker: borra a la fuerza el contenedor aunque esté en ejecución
            var output = new Godot.Collections.Array();
            OS.Execute("docker", new string[] { "rm", "-f", "agi-llama-server" }, output, true);
            
            if (_backendProcess != null && !_backendProcess.HasExited)
            {
                _backendProcess.Kill();
            }
        }
    }
}