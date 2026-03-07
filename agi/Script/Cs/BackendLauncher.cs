using Godot;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Logic.Utils;

namespace Logic.Backend
{
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

        /// <summary>
        /// Starts the backend process asynchronously.
        /// </summary>
        public void StartBackend()
        {
            Task.Run(async () => await ManageBackendLifecycle());
        }

        // Lógica actualizada para ejecutar llama-server nativo y leer el config.json para el Offloading de GPU
        private async Task ManageBackendLifecycle()
        {
            try
            {
                string binaryPath = ProjectSettings.GlobalizePath("user://agi/bin/llama-server");
                string modelPath = ProjectSettings.GlobalizePath("user://agi/models/model.gguf"); 
                
                // CORRECCIÓN: Uso explícito de System.Environment para evitar ambigüedad con Godot.Environment
                int threadCount = Math.Max(1, System.Environment.ProcessorCount / 2);
                
                string arguments = $"--model \"{modelPath}\" --port 8080 --ctx-size 4096 --threads {threadCount}";

                if (System.IO.File.Exists(PathConstants.ConfigFile))
                {
                    string configText = System.IO.File.ReadAllText(PathConstants.ConfigFile);
                    // Verifica el modo de hardware para determinar la descarga de GPU (Offloading)
                    if (configText.Contains("\"hardware_mode\": \"cuda\""))
                    {
                        arguments += " -ngl 99";
                    }
                    else
                    {
                        arguments += " -ngl 0";
                    }
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = binaryPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = ProjectSettings.GlobalizePath("user://agi/bin/")
                };

                _backendProcess = new Process { StartInfo = startInfo };
                _backendProcess.EnableRaisingEvents = true;
                _backendProcess.Exited += OnProcessExited;

                _backendProcess.Start();
                _isRunning = true;
                
                GD.Print($"BackendLauncher: Process started [ID: {_backendProcess.Id}]");

                _ = MonitorProcessHealth();
                
                CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Critical Failure. {ex.Message}");
                HandleCrash();
            }
        }

        private async Task MonitorProcessHealth()
        {
            while (_isRunning && _backendProcess != null && !_backendProcess.HasExited)
            {
                // Ping-like check or simple existence check
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
            // Cleanup on exit
            if (_backendProcess != null && !_backendProcess.HasExited)
            {
                _backendProcess.Kill();
            }
        }
    }
}