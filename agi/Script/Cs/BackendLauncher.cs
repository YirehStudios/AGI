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

        private async Task ManageBackendLifecycle()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = PathConstants.PythonExecutable,
                    Arguments = PathConstants.MainScript + " --listen 127.0.0.1 --port 8188",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = PathConstants.EngineRoot
                };

                // Read run mode from config (simplified for brevity)
                if (System.IO.File.Exists(PathConstants.ConfigFile))
                {
                     string configText = System.IO.File.ReadAllText(PathConstants.ConfigFile);
                     if (configText.Contains("\"cpu\"")) 
                        startInfo.Arguments += " --cpu";
                }

                _backendProcess = new Process { StartInfo = startInfo };
                _backendProcess.EnableRaisingEvents = true;
                _backendProcess.Exited += OnProcessExited;

                _backendProcess.Start();
                _isRunning = true;
                
                GD.Print($"BackendLauncher: Process started [ID: {_backendProcess.Id}]");

                // Start Monitoring Loop
                _ = MonitorProcessHealth();
                
                // Signal ready (optimistic, NetworkManager will verify)
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