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
        /// Configures llama-server executing within a Docker container.
        /// </summary>
        private async Task ManageBackendLifecycle()
        {
            try
            {
                string modelsDir = PathConstants.ModelsDir;
                
                // Calculates optimal execution threads based on current hardware architecture.
                int threadCount = Math.Max(1, System.Environment.ProcessorCount / 2);
                
                // Builds Docker execution arguments mapping local volumes and routing ports.
                string arguments = $"run --rm -v \"{modelsDir}:/app/models\" -p 8080:8080 yirehstudios/agi-backend:latest llama-server --model /app/models/Ministral-3b-instruct.Q4_K_S.gguf --port 8080 --ctx-size 4096 --threads {threadCount} --n-gpu-layers 0";

                // Configures the native process environment by isolating standard output and error.
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                // Initializes and subscribes to process lifecycle events.
                _backendProcess = new Process { StartInfo = startInfo };
                _backendProcess.EnableRaisingEvents = true;
                _backendProcess.Exited += OnProcessExited;

                _backendProcess.Start();
                _isRunning = true;
                
                GD.Print($"BackendLauncher: Docker Llama-server process started [ID: {_backendProcess.Id}]");

                // Decouples health monitoring to avoid blocking the main execution thread.
                _ = MonitorProcessHealth();
                
                // Emits the readiness signal using the Godot main thread.
                CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
            }
            catch (Exception ex)
            {
                // Catches and logs critical exceptions during startup, delegating recovery.
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
            if (_backendProcess != null && !_backendProcess.HasExited)
            {
                _backendProcess.Kill();
            }
        }
    }
}