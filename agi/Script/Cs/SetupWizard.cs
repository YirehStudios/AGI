using Godot;
using System;
using System.Collections.Generic;
using Logic.System.Config;
using Logic.System.Drivers;
using Logic.Network;
using System.Threading.Tasks;
namespace Logic.Utils
{
    /// <summary>
    /// Orchestrates the initial application setup through a State Machine approach.
    /// Manages UI transitions, dependency installations, and configuration binding.
    /// </summary>
    public partial class SetupWizard : Control
    {
        public enum WizardState
        {
            Welcome,
            Dependencies,
            ModeSelection,
            ModelSelection,
            Downloading,
            StartingServer
        }

        [Export] public Control PanelWelcome;
        [Export] public Control PanelDependencies;
        [Export] public Control PanelModeSelection;
        [Export] public Control PanelModelSelection;
        [Export] public Control PanelDownloading;

        [Export] public Button BtnTemaOscuro;
        [Export] public Button BtnTemaClaro;
        [Export] public Control SetupBackground;

        [Export] public RichTextLabel TerminalLog;
        [Export] public ProgressBar InstallProgress;
        [Export] public Button BtnComenzar;
        [Export] public Button BtnServidorRemoto;
        [Export] public Button BtnLocalHost;
        [Export] public TextEdit TxtCommandDisplay;
        [Export] public Button BtnCopyCommand;
        [Export] public RichTextLabel LblRestartWarning;
        [Export] public VBoxContainer ModelListContainer;

        [Export] public string MainChatScenePath = "res://Scenes/IAScene/MainApp.tscn";
        [Export] public ProgressBar ModelDownloadProgress;
        [Export] public RichTextLabel ModelDownloadStatus;
        [Export] public Button BtnConnect;
        [Export] public LineEdit TxtRemoteUrlInput;
        [Export] public CheckBox ChkIsLan;
        [Export] public Button BtnAdvancedSettings;
        [Export] public VBoxContainer AdvancedContainer;
        [Export] public LineEdit TxtCustomPort;
        [Export] public Button BtnStartBatchDownload;

        private DownloadManager _downloadManager;
        private WizardState _currentState;
        private DependencyInstaller _dependencyInstaller;
        private ConfigManager _configManager;
        private PackageManager _packageManager;
        private EnvironmentManager _environmentManager;

        private ConfigManager.ModelPreset _selectedLLM;
        private ConfigManager.ModelPreset _selectedSTT;
        private ConfigManager.ModelPreset _selectedTTS;
        

        private List<ConfigManager.ModelPreset> _debugSelectedList = new List<ConfigManager.ModelPreset>();
        private bool _esModoOscuro = true; 


        /// <summary>
        /// Initializes the core managers and child nodes, and binds the UI signals to their respective handlers.
        /// Evaluates the underlying platform to determine the initial routing logic.
        /// </summary>
        public override void _Ready()
        {
            _configManager = GetNode<ConfigManager>("/root/ConfigManager");
            _packageManager = GetNode<PackageManager>("/root/PackageManager");
            _environmentManager = GetNode<EnvironmentManager>("/root/EnvironmentManager");

            _dependencyInstaller = new DependencyInstaller();
            AddChild(_dependencyInstaller);

            _downloadManager = GetNode<DownloadManager>("/root/DownloadManager");

            _downloadManager.DownloadCompleted += OnModelDownloadCompleted;
            _downloadManager.DownloadProgress += OnModelDownloadProgress;

            if (BtnTemaOscuro != null)
                BtnTemaOscuro.Pressed += () => SeleccionarTema(true);
            
            if (BtnTemaClaro != null)
                BtnTemaClaro.Pressed += () => SeleccionarTema(false);

            if (Logic.System.Config.ConfigManager.Instance != null)
            {
                SeleccionarTema(Logic.System.Config.ConfigManager.Instance.DarkMode);
            }

            if (BtnComenzar != null)
            {
                BtnComenzar.Pressed += () => SwitchState(WizardState.Dependencies);
            }

            if (AdvancedContainer != null)
            {
                AdvancedContainer.Visible = false;
            }

            if (BtnAdvancedSettings != null)
            {
                BtnAdvancedSettings.Pressed += () =>
                {
                    if (AdvancedContainer != null)
                        AdvancedContainer.Visible = !AdvancedContainer.Visible;
                };
            }

            if (BtnConnect != null)
            {
                BtnConnect.Pressed += () =>
                {
                    string urlIngresada = TxtRemoteUrlInput != null ? TxtRemoteUrlInput.Text.Trim() : "";
                    string puerto = TxtCustomPort != null && !string.IsNullOrWhiteSpace(TxtCustomPort.Text) ? TxtCustomPort.Text.Trim() : "8080";
                    bool isLan = ChkIsLan != null && ChkIsLan.ButtonPressed;

                    if (string.IsNullOrWhiteSpace(urlIngresada))
                    {
                        urlIngresada = "192.168.1.100";
                    }

                    if (!urlIngresada.StartsWith("http"))
                        urlIngresada = "http://" + urlIngresada;

                    ConfirmRemoteConnection(urlIngresada, isLan, puerto);
                };
            }

            if (BtnLocalHost != null)
            {
                BtnLocalHost.Pressed += SelectLocalMode;
            }

            if (BtnCopyCommand != null)
            {
                BtnCopyCommand.Pressed += OnCopyCommandPressed;
            }

            if (BtnStartBatchDownload != null)
            {
                BtnStartBatchDownload.Disabled = true;
                        BtnStartBatchDownload.Pressed += StartModelDownload;
            }

            if (_configManager.SetupCompleted)
            {
                FastBootSequence();
                return;
            }

            string osName = OS.GetName();
            bool isMobile = osName == "Android" || osName == "iOS";

            if (isMobile)
            {
                if (BtnLocalHost != null) BtnLocalHost.Visible = false;
                SwitchState(WizardState.ModeSelection);
            }
            else
            {
                SwitchState(WizardState.Welcome);
            }
        }

        /// <summary>
        /// Executes the fast boot sequence, bypassing the initial configuration interface.
        /// Performs a mandatory audit of system dependencies before proceeding with service instantiation.
        /// Updated to recognize CloudAPI mode and bypass local infrastructure initialization.
        /// </summary>
        private async void FastBootSequence()
        {
            PanelWelcome.Visible = false;

            // Performs an asynchronous comprehensive verification of required system tools and binaries.
            var auditResult = await _dependencyInstaller.AuditSystemDependenciesAsync();

            // Evaluates environment integrity; in the absence of dependencies, invalidates configuration state and redirects to setup flow.
            if (!auditResult.IsReady) 
            {    
                GD.PrintErr("FastBoot: Missing critical dependencies. Reverting to installation menu.");    
                _configManager.SetupCompleted = false;    
                _configManager.SaveConfiguration();    
                SwitchState(WizardState.Dependencies);    
                
                if (TerminalLog != null) TerminalLog.Text = auditResult.AuditLog;    
                if (TxtCommandDisplay != null) TxtCommandDisplay.Text = auditResult.RequiredCommand;    
                return;
            }

            // Proceeds with normal initialization flow by validating the application mode persisted in ConfigManager.
            if (_configManager.CurrentMode == ConfigManager.AppMode.LocalHost)
            {
                StartLlamaServer();
            }
            else if (_configManager.CurrentMode == ConfigManager.AppMode.RemoteUI)
            {
                Logic.Network.NetworkManager network = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
                network.PerformHandshake();

                // Suspends execution until receiving the handshake completion signal from the network layer.
                var signalResult = await ToSignal(network, Logic.Network.NetworkManager.SignalName.HandshakeCompleted);
                bool success = (bool)signalResult[0];

                if (success)
                {
                    TransitionToMainScene();
                }
                else
                {
                    // Resets configuration flags and returns to mode selector upon failure to negotiate with remote host.
                    _configManager.SetupCompleted = false;
                    _configManager.SaveConfiguration();

                    SwitchState(WizardState.ModeSelection);
                    if (ModelDownloadStatus != null)
                        ModelDownloadStatus.Text = "Se perdió la conexión con el servidor guardado. Configura uno nuevo.";
                }
            }
            else if (_configManager.CurrentMode == ConfigManager.AppMode.CloudAPI)
            {
                // CloudAPI mode detected. Initializing local microservices (Search/TTS) through the backend launcher.
                GD.Print("SetupWizard: CloudAPI mode detected. Initializing local microservices (Search/TTS)...");
                StartLlamaServer();
            }
        }

        private async void HandleStateInitialization(WizardState state)
        {
            switch (state)
            {
                case WizardState.Dependencies:
                    // Implements a timed execution context to prevent UI freezing during dependency verification.
                    var auditTask = _dependencyInstaller.AuditSystemDependenciesAsync();
                    var timeoutTask = Task.Delay(4000);

                    if (await Task.WhenAny(auditTask, timeoutTask) == auditTask)
                    {
                        var result = auditTask.Result;
                        // Evaluates the boolean flag to determine if the local environment meets the operational baseline.
                        if (result.IsReady)
                        {
                            SwitchState(WizardState.ModeSelection);
                        }
                        else
                        {
                            if (PanelDependencies != null) PanelDependencies.Visible = true;
                            
                            if (TerminalLog != null) TerminalLog.Text = result.AuditLog;

                            if (TxtCommandDisplay != null)
                            {
                                string displayText = result.RequiredCommand;
                                if (displayText.Contains("aria2"))
                                {
                                    displayText = "# Sugerencia: Se ha incluido aria2 en el comando para descargas rápidas.\n" + displayText;
                                }
                                TxtCommandDisplay.Text = displayText;
                            }

                            if (LblRestartWarning != null)
                            {
                                LblRestartWarning.Text = "[center]Por favor, ejecuta este comando en tu terminal, luego REINICIA esta aplicación.[/center]";
                            }
                        }
                    }
                    else
                    {
                        // Forces the state transition if the shell execution halts indefinitely.
                        GD.Print("SetupWizard: La auditoría se atascó. Forzando salto a ModeSelection...");
                        SwitchState(WizardState.ModeSelection);
                    }
                    break;

                case WizardState.ModelSelection:
                    PopulateModelPresets();
                    break;

                case WizardState.Downloading:
                    break;
            }
        }

        /// <summary>
        /// Classifies the selected model into LLM, STT, or TTS internal properties based on object nomenclature.
        /// Unlocks the batch download operation once all semantic dependencies are assigned.
        /// </summary>
        /// <param name="preset">The configuration object of the selected model.</param>
       private void OnModelSelected(global::Logic.System.Config.ConfigManager.ModelPreset preset, Button clickedButton, bool isPressed)
        {
            if (isPressed)
            {
                clickedButton.Text = "¡Seleccionado!";
                if (!_debugSelectedList.Contains(preset)) _debugSelectedList.Add(preset);
                
                if (preset.Name.Contains("Whisper")) _selectedSTT = preset;
                else if (preset.Name.Contains("Sherpa") || preset.Name.Contains("Piper") || preset.Name.Contains("Kokoro")) _selectedTTS = preset;
                else _selectedLLM = preset;
            }
            else
            {
                clickedButton.Text = "Seleccionar";
                if (_debugSelectedList.Contains(preset)) _debugSelectedList.Remove(preset);
                
                if (preset.Name.Contains("Whisper")) _selectedSTT = null;
                else if (preset.Name.Contains("Sherpa") || preset.Name.Contains("Piper") || preset.Name.Contains("Kokoro")) _selectedTTS = null;
                else _selectedLLM = null;
            }

            if (BtnStartBatchDownload != null)
            {
                bool listo = _debugSelectedList.Count >= 3;
                BtnStartBatchDownload.Disabled = !listo;
                BtnStartBatchDownload.Text = listo ? 
                    $"Descargar {_debugSelectedList.Count} Componentes" : 
                    "Selecciona 3 modelos para continuar";
                
                BtnStartBatchDownload.Modulate = listo ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0.5f);
            }
        }
        /// <summary>
        /// Orchestrates the asynchronous retrieval of execution engines, Python environments, and selected models.
        /// Resolves all URLs via the JSON manifest to ensure environment consistency and data-driven updates.
        /// </summary>
        private async void StartModelDownload()
        {
            SwitchState(WizardState.Downloading);

            // ==========================================
            // NUEVO: Limpieza preventiva pre-descarga
            // ==========================================
            Logic.Backend.BackendLauncher backend = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
            if (backend != null)
            {
                GD.Print("SetupWizard: Ejecutando purga preventiva de procesos para liberar bloqueos de archivos...");
                backend.TerminateOrphanedResources();
            }
            // ==========================================

            // Manifest retrieval of engines from remote source or local cache.
            ConfigManager.EngineConfig engineConfigs = await _configManager.GetOrDownloadEnginesAsync();
            

            if (engineConfigs == null)
            {
                GD.PrintErr("SetupWizard: Critical error. Engine configuration could not be retrieved.");
                if (ModelDownloadStatus != null) 
                    ModelDownloadStatus.Text = "[center][color=red]Error: Failed to recover engine manifest.[/color][/center]";
                return;
            }

            // Operating environment evaluation for binary selection and sharing path definition.
            bool isWindows = _environmentManager.IsWindows;
            string osFolder = isWindows ? "windows" : "linux";
            
            string shareBinPath = "user://bin/";
            string globalSharePath = ProjectSettings.GlobalizePath(shareBinPath);

            if (!global::System.IO.Directory.Exists(globalSharePath))
            {
                global::System.IO.Directory.CreateDirectory(globalSharePath);
            }

            string currentLlamaUrl = isWindows ? engineConfigs.Llama.WindowsUrl : engineConfigs.Llama.LinuxUrl;
            string llamaArchive = isWindows ? "llama-server.zip" : "llama-server.tar.gz";

            string currentWhisperUrl = isWindows ? engineConfigs.Whisper.WindowsUrl : engineConfigs.Whisper.LinuxUrl;
            string whisperArchive = isWindows ? "whisper-server.zip" : "whisper-server.tar.gz";

            string currentSherpaUrl = isWindows ? engineConfigs.Sherpa.WindowsUrl : engineConfigs.Sherpa.LinuxUrl;
            string currentPythonUrl = isWindows ? engineConfigs.Python.WindowsUrl : engineConfigs.Python.LinuxUrl;
            string sherpaArchive = isWindows ? "sherpa-onnx-win.tar.bz2" : "sherpa-onnx-linux.tar.bz2";

            // Provisioning of the tts_server.py bridge script using manifest URL.
            if (engineConfigs.TtsServer != null && !string.IsNullOrEmpty(engineConfigs.TtsServer.Url))
            {
                if (ModelDownloadStatus != null) ModelDownloadStatus.Text = "[center]Descargando puente de comunicación TTS...[/center]";
                await _downloadManager.DownloadFileAsync(engineConfigs.TtsServer.Url, shareBinPath, "tts_server.py");
            }

            // Provisioning both the legacy TTS and the new Search/MCP Python environments independently.
            // Dynamically retrieves the Search Server and MCP Gateway URLs from the engine manifest.
            string searchServerUrl = engineConfigs.search_server?.Url ?? ""; 
            string mcpServerUrl = engineConfigs.McpServer?.Url ?? ""; 
            
            // Updated call targeting the refactored microservices environment provisioning logic.
            bool searchOk = await _packageManager.EnsureMicroservicesEnvironmentAsync(currentPythonUrl, searchServerUrl, mcpServerUrl);
            bool ttsOk = await _packageManager.EnsurePythonEnvironmentAsync(currentPythonUrl);
            bool pythonOk = searchOk && ttsOk;

            // Engine preparation phase: download, integrity verification, and extraction.
            if (ModelDownloadStatus != null) ModelDownloadStatus.Text = "[center]Descargando/Verificando Llama Server...[/center]";
            bool llamaOk = await _packageManager.DownloadAndPrepareEngineAsync(currentLlamaUrl, llamaArchive, "llama", "llama-server");

            if (ModelDownloadStatus != null) ModelDownloadStatus.Text = "[center]Descargando/Verificando Whisper Server...[/center]";
            bool whisperOk = await _packageManager.DownloadAndPrepareEngineAsync(currentWhisperUrl, whisperArchive, "whisper", "whisper-server");

            if (ModelDownloadStatus != null) ModelDownloadStatus.Text = "[center]Descargando/Verificando Sherpa-ONNX Server...[/center]";
            bool sherpaOk = await _packageManager.DownloadAndPrepareEngineAsync(currentSherpaUrl, sherpaArchive, "sherpa", "sherpa-onnx");

            if (!llamaOk || !whisperOk || !sherpaOk || !pythonOk)
            {
                string errorMessage = !pythonOk ? "Error configuring Python environments." : "Base execution engine preparation failed.";
                GD.PrintErr($"SetupWizard: {errorMessage}");
                if (ModelDownloadStatus != null) ModelDownloadStatus.Text = $"[center][color=red]{errorMessage}[/color][/center]";
                return;
            }

            // Models directory initialization and selected presets processing queue.
            string modelsDir = _environmentManager.ModelsPath;
            global::System.IO.Directory.CreateDirectory(modelsDir);

            List<ConfigManager.ModelPreset> presetsToDownload = new List<ConfigManager.ModelPreset> { _selectedLLM, _selectedSTT, _selectedTTS };

            foreach (ConfigManager.ModelPreset preset in presetsToDownload)
            {
                if (preset == null) continue;

                string safeFileName = preset.Name.Replace(" ", "_");

                if (preset.Name.Contains("Whisper")) safeFileName += ".bin";
                else if (preset.Name.Contains("Piper") || preset.Name.Contains("Sherpa") || preset.Name.Contains("Kokoro"))
                {
                    safeFileName = global::System.IO.Path.GetFileName(new global::System.Uri(preset.DownloadLinks[0]).LocalPath);
                }
                else safeFileName += ".gguf";

                if (preset.Name.Contains("Piper") || preset.Name.Contains("Sherpa") || preset.Name.Contains("Kokoro"))
                {
                    _configManager.ActiveTTSEngine = "sherpa-onnx";
                    _configManager.ActiveTTSModel = safeFileName.Replace(".tar.bz2", "").Replace(".zip", "");
                }
                else if (preset.Name.Contains("Whisper"))
                {
                    _configManager.ActiveSTTModel = safeFileName;
                }
                else
                {
                    _configManager.ActiveModelName = preset.Name;
                    _configManager.ActiveModelPath = global::System.IO.Path.Combine(modelsDir, safeFileName);
                }

                _configManager.ActiveModelUrl = preset.DownloadLinks[0];
                _configManager.SaveConfiguration();

                string globalPath = global::System.IO.Path.Combine(modelsDir, safeFileName);
                bool isAlreadyExtracted = false;

                if (preset.Name.Contains("Piper") || preset.Name.Contains("Sherpa") || preset.Name.Contains("Kokoro"))
                {
                    string extractedFolderPath = global::System.IO.Path.Combine(modelsDir, _configManager.ActiveTTSModel);
                    if (global::System.IO.Directory.Exists(extractedFolderPath)) isAlreadyExtracted = true;
                }

                if (global::System.IO.File.Exists(globalPath) || isAlreadyExtracted)
                {
                    GD.Print($"SetupWizard: Local cache validated for {safeFileName}");
                    if (ModelDownloadStatus != null) ModelDownloadStatus.Text = $"[center]{preset.Name} ya está presente. Omitiendo...[/center]";
                    await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
                    continue;
                }

                if (ModelDownloadStatus != null) ModelDownloadStatus.Text = $"[center]Descargando tensores: {preset.Name}...[/center]";

                bool success = await _downloadManager.DownloadFileAsync(preset.DownloadLinks[0], modelsDir, safeFileName);

                if (!success)
                {
                    GD.PrintErr($"SetupWizard: Network failure during {preset.Name} download");
                    if (ModelDownloadStatus != null) 
                        ModelDownloadStatus.Text = $"[center][color=red]Error downloading {preset.Name}.[/color][/center]";
                    return;
                }

                if (preset.Name.Contains("Kokoro"))
                {
                    string kokoroDir = global::System.IO.Path.Combine(modelsDir, _configManager.ActiveTTSModel);
                    string voicesPyPath = global::System.IO.Path.Combine(kokoroDir, "voices_python.bin");

                    if (!global::System.IO.File.Exists(voicesPyPath))
                    {
                        if (ModelDownloadStatus != null) ModelDownloadStatus.Text = $"[center]Descargando voces Python para {preset.Name}...[/center]";
                        await _downloadManager.DownloadFileAsync("https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin", kokoroDir, "voices_python.bin");
                    }
                }
            }

            StartLlamaServer();
        }

        /// <summary>
        /// Evaluates the network transfer boolean result for an individual model mapping.
        /// Updates the UI text and resets or maximizes the visual progress bar based on the boolean flag.
        /// </summary>
        /// <param name="fileName">The string identifier of the processed file.</param>
        /// <param name="success">The final integrity validation flag post-download.</param>
        private void OnModelDownloadCompleted(string fileName, bool success)
        {
            if (success)
            {
                if (ModelDownloadStatus != null) ModelDownloadStatus.Text = $"Descarga validada: {fileName}. Aguardando en cola...";
                if (ModelDownloadProgress != null) ModelDownloadProgress.Value = ModelDownloadProgress.MaxValue;
            }
            else
            {
                if (ModelDownloadStatus != null) ModelDownloadStatus.Text = $"Divergencia de red o de sistema de archivos procesando {fileName}.";
                if (ModelDownloadProgress != null) ModelDownloadProgress.Value = 0;
            }
        }

        /// <summary>
        /// Transitions the internal UI state and subscribes to the BackendLauncher singleton events.
        /// </summary>
        private void StartLlamaServer()
        {
            SwitchState(WizardState.StartingServer);

            if (ModelDownloadStatus != null)
                ModelDownloadStatus.Text = "Iniciando Preparativos de IA...";
            
            if (ModelDownloadProgress != null)
                ModelDownloadProgress.Value = 0;

            Logic.Backend.BackendLauncher backend = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
            if (backend != null)
            {
                backend.BackendReady += OnBackendReady;
                backend.ConnectionLost += OnBackendError;
                backend.BuildLogReceived += OnBuildLogReceived;

                backend.StartBackend();
            }
            else
            {
                GD.PrintErr("SetupWizard: ¡BackendLauncher no encontrado en /root/!");
            }
        }

        /// <summary>
        /// Processes the raw terminal string output originating from the container initialization.
        /// Truncates the text array to fit the UI constraints and interpolates the progress bar visually.
        /// </summary>
        /// <param name="logMessage">The unformatted string received from the standard output.</param>
        private void OnBuildLogReceived(string logMessage)
        {
            if (ModelDownloadStatus != null)
            {
                string cleanMsg = logMessage.Length > 85 ? logMessage.Substring(0, 85) + "..." : logMessage;
                ModelDownloadStatus.Text = "> " + cleanMsg;
            }

            if (ModelDownloadProgress != null && ModelDownloadProgress.Value < 95)
            {
                ModelDownloadProgress.Value += 0.1f;
            }
        }

        /// <summary>
        /// Executes upon receiving the server ready signal. Unsubscribes from backend events, 
        /// maximizes the progress variable, and routes to the main application scene.
        /// </summary>
        private void OnBackendReady()
        {
            Logic.Backend.BackendLauncher backend = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
            if (backend != null)
            {
                backend.BackendReady -= OnBackendReady;
                backend.ConnectionLost -= OnBackendError;
                backend.BuildLogReceived -= OnBuildLogReceived;
            }

            if (ModelDownloadProgress != null) ModelDownloadProgress.Value = 100;

            _configManager.SetupCompleted = true;
            _configManager.SaveConfiguration();

            TransitionToMainScene();
        }

        /// <summary>
        /// Executes upon receiving a server failure signal. Unsubscribes from connection events,
        /// applies a red-tint font override, and resets the progress visual tracker.
        /// </summary>
        private void OnBackendError()
        {
            Logic.Backend.BackendLauncher backend = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
            if (backend != null)
            {
                backend.BackendReady -= OnBackendReady;
                backend.ConnectionLost -= OnBackendError;
            }

            if (ModelDownloadStatus != null)
            {
                ModelDownloadStatus.Text = "Error crítico al iniciar.";
                ModelDownloadStatus.AddThemeColorOverride("font_color", new Color(1, 0, 0));
            }
            if (ModelDownloadProgress != null)
            {
                ModelDownloadProgress.Value = 0;
            }
        }

        /// <summary>
        /// Subscriber method reflecting byte transfer progression via linear assignment 
        /// over the UI elements based on parameters emitted by the underlying network thread.
        /// </summary>
        /// <param name="fileName">The string identifier of the file in transit.</param>
        /// <param name="percentage">The calculated float fraction of the total file size.</param>
        private void OnModelDownloadProgress(string fileName, float percentage)
        {
            if (ModelDownloadProgress != null)
            {
                ModelDownloadProgress.Value = percentage;
            }

            if (ModelDownloadStatus != null)
            {
                ModelDownloadStatus.Text = $"Descargando {fileName}... {percentage:F1}%";
            }
        }

        /// <summary>
        /// Transfers the bash command generated by the dependency resolver into the OS clipboard 
        /// via the DisplayServer interface. Yields a scene tree timeout to toggle the UI state temporarily.
        /// </summary>
        private async void OnCopyCommandPressed()
        {
            if (TxtCommandDisplay != null && !string.IsNullOrEmpty(TxtCommandDisplay.Text))
            {
                DisplayServer.ClipboardSet(TxtCommandDisplay.Text);
                GD.Print("SetupWizard: Comando bash copiado al portapapeles de forma exitosa.");

                if (BtnCopyCommand != null)
                {
                    string originalText = BtnCopyCommand.Text;
                    BtnCopyCommand.Text = "¡Copiado!";

                    // Yields the thread context until the SceneTree timer emits its timeout signal.
                    await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);

                    BtnCopyCommand.Text = originalText;
                }
            }
        }

        /// <summary>
        /// Overwrites the internal enumerator property tracking the state and evaluates 
        /// boolean assignments for the UI panel visibilities based on the requested target state.
        /// </summary>
        /// <param name="newState">The target state structure to enforce across the UI.</param>
        public void SwitchState(WizardState state)
        {
            // Resets the visibility state of all primary interface panels to ensure a clean rendering context.
            if (PanelWelcome != null) PanelWelcome.Visible = false;
            if (PanelDependencies != null) PanelDependencies.Visible = false;
            if (PanelModeSelection != null) PanelModeSelection.Visible = false;
            if (PanelModelSelection != null) PanelModelSelection.Visible = false;
            if (PanelDownloading != null) PanelDownloading.Visible = false;

            // Evaluates the requested enumerator state to conditionally map the visibility boolean of the corresponding target UI component.
            switch (state)
            {
                case WizardState.Welcome:
                    if (PanelWelcome != null) PanelWelcome.Visible = true;
                    break;
                case WizardState.Dependencies:
                    if (PanelDependencies != null) PanelDependencies.Visible = true;
                    break;
                case WizardState.ModeSelection:
                    if (PanelModeSelection != null) PanelModeSelection.Visible = true;
                    break;
                case WizardState.ModelSelection:
                    if (PanelModelSelection != null) PanelModelSelection.Visible = true;
                    break;
                case WizardState.Downloading:
                case WizardState.StartingServer:
                    if (PanelDownloading != null) PanelDownloading.Visible = true;
                    break;
            }

            // Dispatches the internal workflow initialization bindings for the activated logical state.
            HandleStateInitialization(state);
        }

        /// <summary>
        /// Yields execution for a single engine process frame to ensure node parameters are fully drawn,
        /// then assigns the vertical scrollbar offset to its respective maximum constraint limit.
        /// </summary>
        private async void ScrollToBottom()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            if (TerminalLog != null)
            {
                var scrollBar = TerminalLog.GetVScrollBar();
                if (scrollBar != null)
                {
                    scrollBar.Value = scrollBar.MaxValue;
                }
            }
        }

        /// <summary>
        /// Evaluates the outcome boolean of the installation script subprocess.
        /// Transitions the internal state index or appends standard error output formatting depending on the result.
        /// </summary>
        /// <param name="success">The parsed process exit code validation flag.</param>
        private void OnInstallationCompleted(bool success)
        {
            if (success)
            {
                SwitchState(WizardState.ModeSelection);
            }
            else
            {
                if (TerminalLog != null)
                {
                    TerminalLog.AppendText("\n[SYSTEM] Installation failed. Please review the logs above.\n");
                }
            }
        }

        /// <summary>
        /// Overwrites the application mode enum to RemoteUI in the ConfigManager, assigns the string URL, 
        /// triggers a file persistence operation, and transitions the active scene.
        /// </summary>
        /// <param name="hostUrl">The serialized string representing the target IP and port.</param>
        public void SelectRemoteMode(string hostUrl)
        {
            _configManager.CurrentMode = ConfigManager.AppMode.RemoteUI;
            _configManager.RemoteHostUrl = hostUrl;
            _configManager.SaveConfiguration();

            TransitionToMainScene();
        }

        /// <summary>
        /// Sets the internal application mode property to LocalHost and invokes the state transition 
        /// method to render the model selection user interface.
        /// </summary>
        public void SelectLocalMode()
        {
            _configManager.CurrentMode = ConfigManager.AppMode.LocalHost;
            SwitchState(WizardState.ModelSelection);
        }

        /// <summary>
        /// Iteratively invokes QueueFree on all child nodes within the list container. Awaits an asynchronous 
        /// fetch request pointing to the configuration preset array, then dynamically instantiates 
        /// PanelContainers and Control UI elements for each parsed JSON mapping.
        /// </summary>
        private async void PopulateModelPresets()
        {
            if (ModelListContainer != null)
            {
                foreach (Node child in ModelListContainer.GetChildren())
                {
                    child.QueueFree();
                }
            }

            // 1. TU CÓDIGO ORIGINAL (Descarga asíncrona perfecta)
            List<ConfigManager.ModelPreset> presets = await _configManager.GetOrDownloadPresetsAsync();

            if (presets == null || presets.Count == 0) return;

            // 2. Revisamos el tema desde tu ConfigManager de interfaz (usando ruta global para no confundir)
            bool esOscuro = _esModoOscuro;

            foreach (ConfigManager.ModelPreset preset in presets)
            {
                // 3. LA TARJETA APPLE (Con sus márgenes y redondeos)
                PanelContainer cardPanel = new PanelContainer();
                
                Godot.StyleBoxFlat cardStyle = new Godot.StyleBoxFlat
                {
                    BgColor = esOscuro ? new Color(0.12f, 0.12f, 0.12f, 0.85f) : new Color(1f, 1f, 1f, 0.9f),
                    BorderColor = esOscuro ? new Color(0.3f, 0.3f, 0.3f, 0.4f) : new Color(0.85f, 0.85f, 0.85f, 0.8f),
                    BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 20, CornerRadiusTopRight = 20,
                    CornerRadiusBottomLeft = 20, CornerRadiusBottomRight = 20,
                    ContentMarginLeft = 25, ContentMarginTop = 25, 
                    ContentMarginRight = 25, ContentMarginBottom = 25
                };
                cardPanel.AddThemeStyleboxOverride("panel", cardStyle);

                // 4. TU ESTRUCTURA ORIGINAL (HBoxContainer para separar texto a la izquierda y botón a la derecha)
                HBoxContainer cardLayout = new HBoxContainer();
                VBoxContainer textContainer = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

                // 5. TEXTOS INTELIGENTES (Blancos en modo oscuro, negros en claro)
                Label nameLabel = new Label { Text = preset.Name };
                nameLabel.AddThemeFontSizeOverride("font_size", 22);
                nameLabel.AddThemeColorOverride("font_color", esOscuro ? new Color(0.9f, 0.9f, 0.9f, 1) : new Color(0.1f, 0.1f, 0.1f, 1));

                Label descLabel = new Label { Text = preset.Description, AutowrapMode = TextServer.AutowrapMode.WordSmart };
                descLabel.AddThemeColorOverride("font_color", esOscuro ? new Color(0.7f, 0.7f, 0.7f, 1) : new Color(0.4f, 0.4f, 0.4f, 1));
                
                textContainer.AddChild(nameLabel);
                textContainer.AddChild(descLabel);
                
                // Espaciador invisible para que el botón no se pegue al texto
                Control spacer = new Control { CustomMinimumSize = new Vector2(15, 0) };

                // 6. TU BOTÓN ORIGINAL
                Button actionButton = new Button();
                actionButton.Text = "Seleccionar";
                actionButton.ToggleMode = true;
                actionButton.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; // Para que no se estire a lo alto

                actionButton.Toggled += (isPressed) => 
                {
                    var presetExacto = (Logic.System.Config.ConfigManager.ModelPreset)preset;
                    OnModelSelected(presetExacto, actionButton, isPressed);
                };
                
                // Ensamblamos todo como tú lo tenías
                cardLayout.AddChild(textContainer);
                cardLayout.AddChild(spacer);
                cardLayout.AddChild(actionButton);
                cardPanel.AddChild(cardLayout);

                if (ModelListContainer != null) ModelListContainer.AddChild(cardPanel);
            }
        }
        /// <summary>
        /// Validates the disk size constraint of the allocated binary asset through the ConfigManager's internal evaluation function.
        /// Persists configuration states mapped to the scene transitions upon validating the file.
        /// </summary>
        /// <param name="expectedSize">The numerical constraint defining the integrity check byte limit.</param>
        public void ConfirmModelSelection(long expectedSize)
        {
            var validationResult = _configManager.ValidateModelIntegrity(expectedSize);

            if (!validationResult.IsValid)
            {
                GD.PrintErr($"SetupWizard: Validation Error - {validationResult.ErrorMessage}");
                return;
            }

            _configManager.SaveConfiguration();
            TransitionToMainScene();
        }

        /// <summary>
        /// Interacts directly with the active Godot SceneTree API to request an unmanaged scene swap 
        /// utilizing the defined persistent system path.
        /// </summary>
        private void TransitionToMainScene()
        {
            GetTree().ChangeSceneToFile(MainChatScenePath);
        }

        /// <summary>
        /// Instructs the NetworkManager to dispatch an asynchronous HTTP handshake sequence to the defined remote interface.
        /// Awaits the Signal resolution natively and persists variables prior to scene traversal logic on HTTP 200/Success.
        /// </summary>
        /// <param name="baseUrl">The sanitized string sequence representing the IPv4 or domain routing.</param>
        /// <param name="isLan">A boolean validation identifier determining internal or external structural endpoints.</param>
        /// <param name="port">The concatenated system port targeting the remote API daemon process.</param>
        public async void ConfirmRemoteConnection(string baseUrl, bool isLan, string port)
        {
            if (ModelDownloadStatus != null) ModelDownloadStatus.Text = "Verificando conexión con el servidor...";

            _configManager.CurrentMode = ConfigManager.AppMode.RemoteUI;
            _configManager.RemoteHostUrl = $"{baseUrl}:{port}";

            Logic.Network.NetworkManager network = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");

            if (network != null)
            {
                network.PerformHandshake();

                var signalResult = await ToSignal(network, Logic.Network.NetworkManager.SignalName.HandshakeCompleted);
                bool success = (bool)signalResult[0];

                if (success)
                {
                    _configManager.SetupCompleted = true;
                    _configManager.IsLanConnection = isLan;
                    _configManager.CustomPort = port;
                    _configManager.SaveConfiguration();

                    TransitionToMainScene();
                }
                else
                {
                    if (ModelDownloadStatus != null)
                        ModelDownloadStatus.Text = "Error: No se pudo conectar al servidor. Verifica la IP y el puerto.";
                }
            }
        }
        /// <summary>
        /// Sets the internal configuration theme variable, updates the unified configuration singleton property,
        /// triggers global data persistence, and loads the corresponding theme resource layout file.
        /// </summary>
        /// <param name="esOscuro">A boolean evaluation flag representing dark mode selection state.</param>
        private void SeleccionarTema(bool esOscuro)
        {
            _esModoOscuro = esOscuro;

            if (Logic.System.Config.ConfigManager.Instance != null)
            {
                Logic.System.Config.ConfigManager.Instance.DarkMode = esOscuro;
                Logic.System.Config.ConfigManager.Instance.SaveConfiguration();
            }

            string path = esOscuro ? "res://Resources/UI_Themes/minimal_theme.tres" : "res://Resources/UI_Themes/tema_claro.tres";
            Theme temaCorrecto = ResourceLoader.Load<Theme>(path);
            this.Theme = temaCorrecto; 

            if (SetupBackground is ColorRect bgRect)
            {
                bgRect.Color = esOscuro ? new Color("#131313") : new Color("#f5f5f7");
            }
            else if (SetupBackground is PanelContainer bgPanel)
            {
                var style = new StyleBoxFlat { BgColor = esOscuro ? new Color("#131313") : new Color("#f5f5f7") };
                bgPanel.AddThemeStyleboxOverride("panel", style);
            }
        }

    }
}