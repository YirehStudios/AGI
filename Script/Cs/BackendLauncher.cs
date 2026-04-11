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
        
        [Signal]
        public delegate void BuildLogReceivedEventHandler(string logMessage);

        /// <summary>
        /// Señal emitida tras la culminación de la transcripción de audio a texto, retornando la cadena procesada.
        /// </summary>
        [Signal]
        public delegate void STTCompletedEventHandler(string recognizedText);

        /// <summary>
        /// Señal emitida tras la culminación de la síntesis de texto a voz, indicando la ruta del archivo de salida.
        /// </summary>
        [Signal]
        public delegate void TTSCompletedEventHandler(string audioFilePath);

        private Process _backendProcess;
        private bool _isRunning = false;
        private int _retryCount = 0;
        private const int MaxRetries = 3;

        public void StartBackend()
        {
            Logic.System.Config.ConfigManager configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            string safeFileName = "default.gguf";
            int userGpuPreference = -1;

            if (configManager != null)
            {
                if (!string.IsNullOrEmpty(configManager.ActiveModelName))
                    safeFileName = configManager.ActiveModelName.Replace(" ", "_") + ".gguf";
                userGpuPreference = configManager.SelectedGpuIndex; 
            }
            
            string modelsDir = ProjectSettings.GlobalizePath("user://models"); 
            string adapterName = Godot.RenderingServer.GetVideoAdapterName().ToLower();
            
            GD.Print($"BackendLauncher: GPU detectada por el motor: {adapterName}");

            string hardwareBridge = "";
            string llamaDeviceEnv = "";

            if (adapterName.Contains("nvidia") || adapterName.Contains("tesla") || adapterName.Contains("geforce") || adapterName.Contains("rtx") || adapterName.Contains("quadro"))
            {
                hardwareBridge = "--runtime=nvidia --gpus all --privileged -e NVIDIA_DRIVER_CAPABILITIES=all -e NVIDIA_VISIBLE_DEVICES=all"; 
                llamaDeviceEnv = $"-e CUDA_VISIBLE_DEVICES={(userGpuPreference >= 0 ? userGpuPreference : 0)}";
            }
            else if (adapterName.Contains("amd") || adapterName.Contains("radeon"))
            {
                hardwareBridge = "--device /dev/kfd --device /dev/dri"; 
                llamaDeviceEnv = $"-e GGML_VK_VISIBLE_DEVICES={(userGpuPreference >= 0 ? userGpuPreference : 0)}";
            }
            else 
            {
                hardwareBridge = "--device /dev/dri"; 
                llamaDeviceEnv = $"-e GGML_VK_VISIBLE_DEVICES={(userGpuPreference >= 0 ? userGpuPreference : 0)}";
            }

            Task.Run(async () => 
            {
                bool imageReady = await EnsureDockerImageExistsAsync();
                if (imageReady)
                {
                    await ManageBackendLifecycle(modelsDir, safeFileName, hardwareBridge, llamaDeviceEnv);
                }
                else
                {
                    GD.PrintErr("BackendLauncher: Error crítico. No se pudo construir la imagen Docker.");
                    CallDeferred(MethodName.EmitSignal, SignalName.ConnectionLost);
                }
            });
        }

        private async Task<bool> EnsureDockerImageExistsAsync()
        {
            string serverDir = ProjectSettings.GlobalizePath("user://server");
            global::System.IO.Directory.CreateDirectory(serverDir);
            
            string dockerfilePath = global::System.IO.Path.Combine(serverDir, "Dockerfile");
            string sourceResPath = "res://Script/Cs/System/Drivers/Dockerfile";
            string fileContent = "";

            using (var file = Godot.FileAccess.Open(sourceResPath, Godot.FileAccess.ModeFlags.Read))
            {
                if (file != null) 
                {
                    fileContent = file.GetAsText();
                    GD.Print("BackendLauncher: Dockerfile cargado desde recursos locales.");
                }
                else
                {
                    GD.PrintErr($"BackendLauncher: No se encontró el Dockerfile local en {sourceResPath}. Activando protocolo de emergencia...");
                }
            }

            if (string.IsNullOrEmpty(fileContent))
            {
                try
                {
                    using global::System.Net.Http.HttpClient client = new global::System.Net.Http.HttpClient();
                    string rawUrl = "https://raw.githubusercontent.com/YirehStudios/AGI/main/agi/Script/Cs/System/Drivers/Dockerfile";
                    
                    GD.Print("BackendLauncher: Obteniendo Dockerfile desde el repositorio oficial...");
                    fileContent = await client.GetStringAsync(rawUrl);
                    GD.Print("BackendLauncher: ¡Dockerfile descargado con éxito!");
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"BackendLauncher: Fallo crítico. No se pudo obtener el Dockerfile ni local ni remoto. Verifica tu conexión a internet. {ex.Message}");
                    return false;
                }
            }

            global::System.IO.File.WriteAllText(dockerfilePath, fileContent);

            var output = new Godot.Collections.Array();
            int inspectCode = Godot.OS.Execute("docker", new string[] { "image", "inspect", "yirehstudios/agi-backend:latest" }, output, true);

            if (inspectCode == 0)
            {
                CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, "La imagen Docker ya existe y está lista.");
                return true;
            }

            GD.Print("BackendLauncher: Iniciando construcción en segundo plano...");
            CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, "Descargando entorno y compilando (Esto tomará varios minutos)...");

            ProcessStartInfo buildInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"build -t yirehstudios/agi-backend:latest \"{serverDir}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process buildProcess = new Process { StartInfo = buildInfo };
            
            buildProcess.OutputDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data)) {
                    GD.Print($"[Docker Build] {e.Data}");
                    CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, e.Data);
                }
            };
            buildProcess.ErrorDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data)) {
                    GD.Print($"[Docker Build] {e.Data}");
                    CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, e.Data);
                }
            };

            buildProcess.Start();
            buildProcess.BeginOutputReadLine();
            buildProcess.BeginErrorReadLine();

            await Task.Run(() => buildProcess.WaitForExit());

            return buildProcess.ExitCode == 0;
        }

        /// <summary>
        /// Configures and orchestrates the llama-server execution within a hardware-aware Docker container.
        /// </summary>
        private async Task ManageBackendLifecycle(string modelsDir, string safeFileName, string hardwareBridge, string llamaDeviceEnv)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "docker", Arguments = "rm -f agi-llama-server", CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();

                int threadCount = Math.Max(1, global::System.Environment.ProcessorCount / 2);
                string modelDockerPath = $"/app/models/{safeFileName}";

                string dockerCmd = $"docker run --name agi-llama-server --rm {hardwareBridge} {llamaDeviceEnv} -v '{modelsDir}:/app/models' -p 8080:8080 yirehstudios/agi-backend:latest llama-server --host 0.0.0.0 --model '{modelDockerPath}' --port 8080 --ctx-size 4096 --threads {threadCount} --n-gpu-layers 99 --temp 0.7 --repeat-penalty 1.15";

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "sh", 
                    Arguments = $"-c \"{dockerCmd}\"", 
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _backendProcess = new Process { StartInfo = startInfo };

                _backendProcess.OutputDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                    {
                        GD.Print($"[Docker Llama] {e.Data}");
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, e.Data); 
                    }
                };
                                
                _backendProcess.ErrorDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                    {
                        GD.Print($"[Docker Llama ERROR] {e.Data}");
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, e.Data);
                        
                        if (e.Data.Contains("server is listening on") || e.Data.Contains("HTTP server listening"))
                        {
                            GD.Print("BackendLauncher: Llama Server cargado en memoria exitosamente.");
                            CallDeferred(MethodName.EmitSignal, SignalName.BackendReady);
                        }
                    }
                };

                _backendProcess.EnableRaisingEvents = true;
                _backendProcess.Exited += OnProcessExited;
                _backendProcess.Start();
                _backendProcess.BeginOutputReadLine();
                _backendProcess.BeginErrorReadLine();
                _isRunning = true;
                _retryCount = 0;

                GD.Print($"BackendLauncher: Docker Llama-server process started [ID: {_backendProcess.Id}]");
                
                await MonitorProcessHealth();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BackendLauncher: Failed to start backend process. {ex.Message}");
                HandleCrash();
            }
        }

        /// <summary>
        /// Procesa la transcripción de audio a texto ejecutando el motor configurado mediante un proceso contenedor.
        /// Centraliza la ejecución a través de ExecuteDockerCommand aislando el proceso en un hilo asíncrono.
        /// </summary>
        /// <param name="audioFilePath">Ruta absoluta del archivo de audio a procesar.</param>
        public void ProcessSpeechToText(string audioFilePath)
        {
            _ = Task.Run(() => 
            {
                try
                {
                    var config = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
                    string engine = config?.ActiveSTTEngine ?? "whisper.cpp";
                    string model = config?.ActiveSTTModel ?? "base.bin";

                    string modelsDir = ProjectSettings.GlobalizePath("user://models"); 
                    string audioDir = Path.GetDirectoryName(audioFilePath);
                    string audioFileName = Path.GetFileName(audioFilePath);
                    string arguments = "";

                    switch (engine.ToLower())
                    {
                        case "whisper.cpp":
                            arguments = $"run --rm -v \"{modelsDir}:/app/models\" -v \"{audioDir}:/app/audio\" yirehstudios/agi-backend:latest whisper-cli -m /app/models/{model} -f /app/audio/{audioFileName} --output-txt";
                            break;
                        default:
                            GD.PrintErr($"STT: Motor '{engine}' no implementado.");
                            return;
                    }

                    ExecuteDockerCommand(arguments, (exitCode) => {
                        if (exitCode == 0) {
                            string txtPath = audioFilePath + ".txt";
                            if (File.Exists(txtPath)) {
                                string text = File.ReadAllText(txtPath);
                                CallDeferred(MethodName.EmitSignal, SignalName.STTCompleted, text);
                            }
                        }
                    });
                }
                catch (Exception ex) { GD.PrintErr($"STT Error: {ex.Message}"); }
            });
        }

        /// <summary>
        /// Sintetiza voz a partir de una cadena de texto delegando el cómputo al contenedor Docker correspondiente.
        /// Utiliza parámetros dinámicos basados en la selección en caché y orquesta el comando genérico encapsulado.
        /// </summary>
        /// <param name="textToSynthesize">Cadena de texto de entrada a ser sintetizada.</param>
        public void GenerateTextToSpeech(string textToSynthesize)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var config = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
                    string engine = config?.ActiveTTSEngine ?? "sherpa-onnx";
                    string modelFolder = config?.ActiveTTSModel ?? "vits-piper-es_MX-claude-high";

                    string modelsDir = ProjectSettings.GlobalizePath("user://models");
                    string outputDir = ProjectSettings.GlobalizePath("user://audio"); 
                    Directory.CreateDirectory(outputDir);
                    
                    string outputFileName = "temp_voice.wav";
                    string arguments = "";

                    switch (engine.ToLower())
                    {
                        case "sherpa-onnx":
                            string modelName = modelFolder.Replace("vits-piper-", "");
                            arguments = $"run --rm -v \"{modelsDir}:/app/models\" -v \"{outputDir}:/app/audio\" yirehstudios/agi-backend:latest sherpa-onnx-offline-tts --vits-model=\"/app/models/{modelFolder}/{modelName}.onnx\" --vits-tokens=\"/app/models/{modelFolder}/tokens.txt\" --vits-lexicon=\"/app/models/{modelFolder}/lexicon.txt\" --output-filename=\"/app/audio/{outputFileName}\" \"{textToSynthesize}\"";
                            break;
                        default:
                            GD.PrintErr($"TTS: Motor '{engine}' no implementado.");
                            return;
                    }

                    ExecuteDockerCommand(arguments, (exitCode) => {
                        if (exitCode == 0) {
                            string finalPath = Path.Combine(outputDir, outputFileName);
                            CallDeferred(MethodName.EmitSignal, SignalName.TTSCompleted, finalPath);
                        }
                    });
                }
                catch (Exception ex) { GD.PrintErr($"TTS Error: {ex.Message}"); }
            });
        }

        /// <summary>
        /// Ejecuta procesos a nivel del sistema operativo para abstraer la instanciación de Docker y su ciclo de vida.
        /// Inyecta un callback para manejar el estado post-ejecución en el hilo pertinente.
        /// </summary>
        /// <param name="args">Argumentos del proceso inyectados al binario de Docker.</param>
        /// <param name="onFinished">Acción invocada una vez que el proceso concluye de manera síncrona dentro del subproceso.</param>
        private void ExecuteDockerCommand(string args, Action<int> onFinished)
        {
            ProcessStartInfo info = new ProcessStartInfo {
                FileName = "docker",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process p = Process.Start(info);
            p.WaitForExit();
            onFinished?.Invoke(p.ExitCode);
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
                CallDeferred(MethodName.StartBackend); 
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
            
            var output = new Godot.Collections.Array();
            OS.Execute("docker", new string[] { "rm", "-f", "agi-llama-server" }, output, true);
            
            if (_backendProcess != null && !_backendProcess.HasExited)
            {
                _backendProcess.Kill();
            }
        }
    }
}