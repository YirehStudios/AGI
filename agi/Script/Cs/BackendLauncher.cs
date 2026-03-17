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

        private Process _backendProcess;
        private bool _isRunning = false;
        private int _retryCount = 0;
        private const int MaxRetries = 3;

        public void StartBackend()
        {
            // 1. LEEMOS TODO LO DE GODOT EN EL HILO PRINCIPAL (Seguro)
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

            // Reemplaza tu condicional actual por este:
            if (adapterName.Contains("nvidia") || adapterName.Contains("tesla") || adapterName.Contains("geforce") || adapterName.Contains("rtx") || adapterName.Contains("quadro"))
            {
                // El puente NVIDIA perfecto
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

            // 2. INICIAMOS EL HILO SECUNDARIO PASANDO SOLO TEXTO (Evita el Deadlock)
            Task.Run(async () => 
            {
                bool imageReady = await EnsureDockerImageExistsAsync();
                if (imageReady)
                {
                    await ManageBackendLifecycle(modelsDir, safeFileName, hardwareBridge, llamaDeviceEnv);
                }
                else
                {
                    // Si algo falla al construir, avisamos a la interfaz
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

            // 1. Intentamos leer el archivo local
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

            // 2. PLAN DE EMERGENCIA: Descargamos desde GitHub si falló lo anterior
            if (string.IsNullOrEmpty(fileContent))
            {
                try
                {
                    using global::System.Net.Http.HttpClient client = new global::System.Net.Http.HttpClient();
                    // Usamos el enlace 'raw' para obtener solo el texto del código
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

            // 3. Escribimos el contenido (ya sea local o remoto) en la carpeta de usuario
            global::System.IO.File.WriteAllText(dockerfilePath, fileContent);

            // 4. Verificamos si la imagen ya estaba construida
            var output = new Godot.Collections.Array();
            int inspectCode = Godot.OS.Execute("docker", new string[] { "image", "inspect", "yirehstudios/agi-backend:latest" }, output, true);

            if (inspectCode == 0)
            {
                CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, "La imagen Docker ya existe y está lista.");
                return true;
            }

            // 5. Comenzamos la compilación en segundo plano
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
                // Limpieza del contenedor viejo usando C# PURO (Nada de Godot.OS)
                Process.Start(new ProcessStartInfo { FileName = "docker", Arguments = "rm -f agi-llama-server", CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();

                int threadCount = Math.Max(1, global::System.Environment.ProcessorCount / 2);
                string modelDockerPath = $"/app/models/{safeFileName}";

                // 3. ENVOLVEMOS EL COMANDO EN COMILLAS SIMPLES PARA EL SHELL
                string dockerCmd = $"docker run --name agi-llama-server --rm {hardwareBridge} {llamaDeviceEnv} -v '{modelsDir}:/app/models' -p 8080:8080 yirehstudios/agi-backend:latest llama-server --host 0.0.0.0 --model '{modelDockerPath}' --port 8080 --ctx-size 4096 --threads {threadCount} --n-gpu-layers 99 --temp 0.7 --repeat-penalty 1.15";

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "sh", // Llamamos a un shell puro
                    Arguments = $"-c \"{dockerCmd}\"", // Inyectamos tu comando idéntico a una terminal
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
                        // ¡Magia! Le enviamos el texto a la pantalla de carga
                        CallDeferred(MethodName.EmitSignal, SignalName.BuildLogReceived, e.Data); 
                    }
                };
                                
                _backendProcess.ErrorDataReceived += (sender, e) => 
                { 
                    if (!string.IsNullOrEmpty(e.Data)) 
                    {
                        GD.Print($"[Docker Llama ERROR] {e.Data}");
                        // Llama escupe sus logs de carga por el canal de Error, ¡avísale a la UI!
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
                
                // ¡LA MAGIA AQUÍ! Delegamos el reinicio al hilo principal de Godot
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