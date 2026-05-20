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
        /// <summary>
        /// Defines the explicit operational checkpoints within the initialization and setup pipeline.
        /// Unified to support both core dependency checks and advanced multi-panel model configurations.
        /// </summary>
        public enum WizardState
        {
            Welcome,
            Dependencies,
            ModeSelection,
            SelectNetworkTopology,
            ModelSelection,
            SelectLLM,
            SelectSTT,
            SelectTTS,
            SelectPerformance,
            Downloading,
            StartingServer,
            ExecutionReady
        }

        [Export] public Control PanelWelcome;
        [Export] public Control PanelDependencies;
        [Export] public Control PanelModeSelection;
        [Export] public Control PanelPerformanceProfile;
        [Export] public PackedScene MainSelectionPanelScene;
        [Export] public Control PanelDownloading;

        [Export] public Button BtnTemaOscuro;
        [Export] public Button BtnTemaClaro;
        [Export] public Control SetupBackground;

        [Export] public RichTextLabel TerminalLog;
        [Export] public ProgressBar InstallProgress;
        [Export] public Button BtnComenzar;
        [Export] public Button BtnLocalHost;
        [Export] public Button BtnConnectCloud;
        [Export] public Button BtnConnectLan;
        [Export] public TextEdit TxtCommandDisplay;
        [Export] public Button BtnCopyCommand;
        [Export] public RichTextLabel LblRestartWarning;

        [Export] public string MainChatScenePath = "res://Scenes/IAScene/MainApp.tscn";
        [Export] public ProgressBar ModelDownloadProgress;
        [Export] public RichTextLabel ModelDownloadStatus;

        [Export] public LineEdit TxtLanIpInput;
        [Export] public LineEdit TxtApiKeyInput;
        [Export] public LineEdit TxtCloudApiUrlInput;
        [Export] public LineEdit TxtCloudModelNameInput;
        [Export] public CheckBox ChkIsLanBroadcasting;
        [Export] public LineEdit TxtCustomPort;
        [Export] public Button BtnPerformanceContinuar;
        [Export] public Button BtnLowPerf;
        [Export] public Button BtnMedPerf;
        [Export] public Button BtnHighPerf;
        [Export] public Button BtnNextPerf;

        private int _currentPerformanceSelection = 1;

        private DownloadManager _downloadManager;
        private WizardState _currentWizardState = WizardState.Welcome;
        private Logic.System.Config.ConfigManager _configManager;
        private Logic.Backend.BackendLauncher _backendLauncher;
        private DependencyInstaller _dependencyInstaller;
        private PackageManager _packageManager;
        private EnvironmentManager _environmentManager;
        private Logic.UI.DynamicSelectionPanel _activeSelectionPanel;

        private ConfigManager.ModelPreset _selectedLLM;
        private ConfigManager.ModelPreset _selectedSTT;
        private ConfigManager.ModelPreset _selectedTTS;

        private bool _esModoOscuro = true;

        /// <summary>
        /// Initializes core subsystems, binds UI signals, and evaluates initial state conditions.
        /// Dynamic sub-scene confirmation wiring is deferred to the runtime instantiation sequence.
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

            if (BtnConnectLan != null)
            {
                BtnConnectLan.Pressed += () =>
                {
                    string urlIngresada = TxtLanIpInput != null ? TxtLanIpInput.Text.Trim() : "";
                    string puerto = TxtCustomPort != null && !string.IsNullOrWhiteSpace(TxtCustomPort.Text) ? TxtCustomPort.Text.Trim() : "8080";
                    bool isLan = ChkIsLanBroadcasting != null && ChkIsLanBroadcasting.ButtonPressed;

                    if (string.IsNullOrWhiteSpace(urlIngresada))
                    {
                        urlIngresada = "192.168.1.100";
                    }

                    if (!urlIngresada.StartsWith("http"))
                        urlIngresada = "http://" + urlIngresada;

                    ConfirmRemoteConnection(urlIngresada, isLan, puerto);
                };
            }

            if (BtnConnectCloud != null)
            {
                BtnConnectCloud.Pressed += () =>
                {
                    string apiKey = TxtApiKeyInput != null ? TxtApiKeyInput.Text.Trim() : "";
                    string apiUrl = TxtCloudApiUrlInput != null ? TxtCloudApiUrlInput.Text.Trim() : "";
                    string modelName = TxtCloudModelNameInput != null ? TxtCloudModelNameInput.Text.Trim() : "";

                    if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(apiUrl) && !string.IsNullOrWhiteSpace(modelName))
                    {
                        ConfigManager.ModelProfile newProfile = new ConfigManager.ModelProfile
                        {
                            Nombre = $"Cloud API ({modelName})",
                            Tipo = 3,
                            EndpointUrl = apiUrl,
                            ModelId = modelName,
                            ApiKey = apiKey
                        };
                        string safeFileName = modelName.Replace(" ", "_").ToLower() + ".json";
                        string modelsDir = ProjectSettings.GlobalizePath("user://models");
                        if (!global::System.IO.Directory.Exists(modelsDir))
                        {
                            global::System.IO.Directory.CreateDirectory(modelsDir);
                        }
                        string fullPath = global::System.IO.Path.Combine(modelsDir, safeFileName);
                        string jsonProfile = global::System.Text.Json.JsonSerializer.Serialize(newProfile, new global::System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        global::System.IO.File.WriteAllText(fullPath, jsonProfile);

                        _configManager.ActiveProfile = newProfile;
                        _configManager.ActiveProfilePath = fullPath;

                        _configManager.CloudApiUrl = apiUrl;
                        _configManager.CloudModelName = modelName;
                        _configManager.CloudApiKey = apiKey;
                        _configManager.CurrentMode = ConfigManager.AppMode.CloudAPI;
                        _configManager.CurrentNetworkState = ConfigManager.NetworkState.CloudAPI;

                        _configManager.CurrentPerformanceTier = ConfigManager.PerformanceTier.High;
                        _configManager.SetupCompleted = true;

                        _configManager.SaveConfiguration();
                        TransitionToMainScene();
                    }
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

            if (BtnLowPerf != null)
            {
                BtnLowPerf.Pressed += () => SetPerformanceSelection(0);
            }

            if (BtnMedPerf != null)
            {
                BtnMedPerf.Pressed += () => SetPerformanceSelection(1);
            }

            if (BtnHighPerf != null)
            {
                BtnHighPerf.Pressed += () => SetPerformanceSelection(2);
            }

            if (BtnNextPerf != null)
            {
                BtnNextPerf.Pressed += OnPerformanceNextPressed;
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
        /// Executes the fast boot sequence, bypassing the initial configuration interface if configuration is complete.
        /// </summary>
        private async void FastBootSequence()
        {
            PanelWelcome.Visible = false;

            if (!string.IsNullOrEmpty(_configManager.ActiveProfilePath) && global::System.IO.File.Exists(_configManager.ActiveProfilePath))
            {
                try
                {
                    string json = global::System.IO.File.ReadAllText(_configManager.ActiveProfilePath);
                    _configManager.ActiveProfile = global::System.Text.Json.JsonSerializer.Deserialize<ConfigManager.ModelProfile>(json);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"FastBoot: Failed to load ModelProfile from {_configManager.ActiveProfilePath}. {ex.Message}");
                    _configManager.SetupCompleted = false;
                    SwitchState(WizardState.ModeSelection);
                    return;
                }
            }
            else
            {
                GD.PrintErr("FastBoot: ActiveProfilePath is missing or invalid.");
                _configManager.SetupCompleted = false;
                SwitchState(WizardState.ModeSelection);
                return;
            }

            var auditResult = await _dependencyInstaller.AuditSystemDependenciesAsync();

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

            if (_configManager.CurrentMode == ConfigManager.AppMode.LocalHost)
            {
                StartLlamaServer();
            }
            else if (_configManager.CurrentMode == ConfigManager.AppMode.RemoteUI)
            {
                Logic.Network.NetworkManager network = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
                network.PerformHandshake();

                var signalResult = await ToSignal(network, Logic.Network.NetworkManager.SignalName.HandshakeCompleted);
                bool success = (bool)signalResult[0];

                if (success)
                {
                    TransitionToMainScene();
                }
                else
                {
                    _configManager.SetupCompleted = false;
                    _configManager.SaveConfiguration();

                    SwitchState(WizardState.ModeSelection);
                    if (ModelDownloadStatus != null)
                        ModelDownloadStatus.Text = "Se perdió la conexión con el servidor guardado. Configura uno nuevo.";
                }
            }
            else if (_configManager.CurrentMode == ConfigManager.AppMode.CloudAPI)
            {
                GD.Print("SetupWizard: CloudAPI mode detected. Initializing local microservices (Search/TTS)...");
                StartLlamaServer();
            }
        }

        /// <summary>
        /// Handles state-specific background initialization, fetching and filtering metadata for the shared panel context.
        /// Dynamically instantiates the selection panel sub-scene on-demand and enforces strict minimum dimensional bounds 
        /// to prevent layout collapse within parent CenterContainer viewports.
        /// </summary>
        private async void HandleStateInitialization(WizardState state)
        {
            switch (state)
            {
                case WizardState.Dependencies:
                    var auditTask = _dependencyInstaller.AuditSystemDependenciesAsync();
                    var timeoutTask = Task.Delay(4000);

                    if (await Task.WhenAny(auditTask, timeoutTask) == auditTask)
                    {
                        var result = auditTask.Result;
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
                        GD.Print("SetupWizard: La auditoría se atascó. Forzando salto a ModeSelection...");
                        SwitchState(WizardState.ModeSelection);
                    }
                    break;

                case WizardState.ModelSelection:
                case WizardState.SelectLLM:
                case WizardState.SelectSTT:
                case WizardState.SelectTTS:
                    if (_activeSelectionPanel == null && MainSelectionPanelScene != null)
                    {
                        _activeSelectionPanel = MainSelectionPanelScene.Instantiate<Logic.UI.DynamicSelectionPanel>();
                        var container = GetNodeOrNull<Control>("Background/CenterContainer");
                        if (container != null)
                        {
                            container.AddChild(_activeSelectionPanel);
                        }

                        // Enforces a structural dimension size block to override default layout constraints inside CenterContainers.
                        _activeSelectionPanel.CustomMinimumSize = new Vector2(800, 600);
                        _activeSelectionPanel.ModelConfirmed += OnDynamicModelConfirmed;
                    }

                    if (_activeSelectionPanel != null)
                    {
                        List<ConfigManager.ModelPreset> presets = await _configManager.GetOrDownloadPresetsAsync();
                        var displayPayload = new Logic.UI.PanelDisplayData();

                        if (state == WizardState.SelectSTT)
                        {
                            displayPayload.Title = "Reconocimiento de Voz (STT)";
                            displayPayload.Category = Logic.UI.ModelCategory.STT;
                            foreach (var preset in presets)
                            {
                                if (preset.Name.Contains("Whisper"))
                                {
                                    displayPayload.Items.Add(new Logic.UI.ModelItemData
                                    {
                                        Name = preset.Name,
                                        Description = preset.Description,
                                        TargetExecutable = "whisper-server"
                                    });
                                }
                            }
                        }
                        else if (state == WizardState.SelectTTS)
                        {
                            displayPayload.Title = "Síntesis de Voz (TTS)";
                            displayPayload.Category = Logic.UI.ModelCategory.TTS;
                            foreach (var preset in presets)
                            {
                                if (preset.Name.Contains("Sherpa") || preset.Name.Contains("Piper") || preset.Name.Contains("Kokoro"))
                                {
                                    displayPayload.Items.Add(new Logic.UI.ModelItemData
                                    {
                                        Name = preset.Name,
                                        Description = preset.Description,
                                        TargetExecutable = "sherpa-onnx"
                                    });
                                }
                            }
                        }
                        else
                        {
                            displayPayload.Title = "Modelos de Lenguaje (LLM)";
                            displayPayload.Category = Logic.UI.ModelCategory.LLM;
                            foreach (var preset in presets)
                            {
                                if (!preset.Name.Contains("Whisper") && !preset.Name.Contains("Sherpa") && !preset.Name.Contains("Piper") && !preset.Name.Contains("Kokoro"))
                                {
                                    displayPayload.Items.Add(new Logic.UI.ModelItemData
                                    {
                                        Name = preset.Name,
                                        Description = preset.Description,
                                        TargetExecutable = "llama-server"
                                    });
                                }
                            }
                        }

                        _activeSelectionPanel.LoadPanelData(displayPayload);
                    }
                    break;

                case WizardState.Downloading:
                    break;
            }
        }

        /// <summary>
        /// Orchestrates the asynchronous retrieval and installation of execution engines, execution environments, and selected models.
        /// </summary>
        private async void StartModelDownload()
        {
            SwitchState(WizardState.Downloading);

            Logic.Backend.BackendLauncher backend = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
            if (backend != null)
            {
                GD.Print("SetupWizard: Ejecutando purga preventiva de procesos para liberar bloqueos de archivos...");
                backend.TerminateOrphanedResources();
            }

            ConfigManager.EngineConfig engineConfigs = await _configManager.GetOrDownloadEnginesAsync();

            if (engineConfigs == null)
            {
                GD.PrintErr("SetupWizard: Critical error. Engine configuration could not be retrieved.");
                if (ModelDownloadStatus != null)
                    ModelDownloadStatus.Text = "[center][color=red]Error: Failed to recover engine manifest.[/color][/center]";
                return;
            }

            bool isWindows = _environmentManager.IsWindows;
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

            if (engineConfigs.TtsServer != null && !string.IsNullOrEmpty(engineConfigs.TtsServer.Url))
            {
                if (ModelDownloadStatus != null) ModelDownloadStatus.Text = "[center]Descargando puente de comunicación TTS...[/center]";
                await _downloadManager.DownloadFileAsync(engineConfigs.TtsServer.Url, shareBinPath, "tts_server.py");
            }

            string searchServerUrl = engineConfigs.search_server?.Url ?? "";
            string mcpServerUrl = engineConfigs.McpServer?.Url ?? "";

            bool searchOk = await _packageManager.EnsureMicroservicesEnvironmentAsync(currentPythonUrl, searchServerUrl, mcpServerUrl);
            bool ttsOk = await _packageManager.EnsurePythonEnvironmentAsync(currentPythonUrl);
            bool pythonOk = searchOk && ttsOk;

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

                    ConfigManager.ModelProfile newProfile = new ConfigManager.ModelProfile
                    {
                        Nombre = preset.Name,
                        Tipo = 2,
                        EndpointUrl = "http://127.0.0.1:8080",
                        ModelId = preset.Name,
                        ApiKey = "local-no-key",
                        Template = new ConfigManager.ChatTemplate()
                    };
                    string profileFileName = preset.Name.Replace(" ", "_").ToLower() + "_profile.json";
                    string fullPath = global::System.IO.Path.Combine(modelsDir, profileFileName);
                    string jsonProfile = global::System.Text.Json.JsonSerializer.Serialize(newProfile, new global::System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    global::System.IO.File.WriteAllText(fullPath, jsonProfile);

                    _configManager.ActiveProfile = newProfile;
                    _configManager.ActiveProfilePath = fullPath;
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
        /// Handles download completion notifications from the download manager thread.
        /// </summary>
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
        /// Switches UI state to server execution and hooks up backend communication signals.
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
        /// Processes real-time logs emitted by the active backend engine subprocess.
        /// </summary>
        private void OnBuildLogReceived(string logMessage)
        {
            if (ModelDownloadStatus != null)
            {
                string cleanMsg = logMessage.Length > 85 ? string.Concat(logMessage.AsSpan(0, 85), "...") : logMessage;
                ModelDownloadStatus.Text = "> " + cleanMsg;
            }

            if (ModelDownloadProgress != null && ModelDownloadProgress.Value < 95)
            {
                ModelDownloadProgress.Value += 0.1f;
            }
        }

        /// <summary>
        /// Event handler triggered once backend local microservices successfully finish initialization.
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
        /// Event handler triggered when a backend local service fails during startup execution.
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
        /// Updates the graphical progression bar coordinates following ongoing data stream reads.
        /// </summary>
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
        /// Copies the generated console tool requirements command directly onto the system clipboard.
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

                    await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);

                    BtnCopyCommand.Text = originalText;
                }
            }
        }

        /// <summary>
        /// Explicit state-switching boundary execution endpoint ensuring structural canvas sterility.
        /// </summary>
        public void SwitchState(WizardState state)
        {
            _currentWizardState = state;
            UpdateWizardUIOverview();
        }

        /// <summary>
        /// Adjusts scroll indices over logging contexts targeting bottom terminal boundaries.
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
        /// Evaluates outcome flags emitted post programmatic runtime dependency platform runs.
        /// </summary>
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
        /// Configures remote access connection profiles and instantly invokes scene swap operations.
        /// </summary>
        public void SelectRemoteMode(string hostUrl)
        {
            _configManager.CurrentMode = ConfigManager.AppMode.RemoteUI;
            _configManager.RemoteHostUrl = hostUrl;
            _configManager.SaveConfiguration();

            TransitionToMainScene();
        }

        /// <summary>
        /// Maps runtime choices securely towards local orchestration processing environments.
        /// </summary>
        public void SelectLocalMode()
        {
            _configManager.CurrentMode = ConfigManager.AppMode.LocalHost;
            SwitchState(WizardState.SelectLLM);
        }

        /// <summary>
        /// Commits individual machine tier properties targeting internal performance matrices.
        /// </summary>
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
        /// Procesa las transiciones secuenciales hacia adelante dentro de la máquina de estados del asistente.
        /// Corrige la omisión de la pantalla de rendimiento insertando el estado SelectPerformance inmediatamente
        /// después de culminar la etapa SelectTTS, permitiendo la selección explícita antes de iniciar la descarga.
        /// </summary>
        public void TransitionToNextState()
        {
            if (_configManager == null)
            {
                _configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            }

            switch (_currentWizardState)
            {
                case WizardState.Welcome:
                    _currentWizardState = WizardState.SelectNetworkTopology;
                    UpdateWizardUIOverview();
                    break;

                case WizardState.SelectNetworkTopology:
                    _currentWizardState = WizardState.SelectLLM;
                    UpdateWizardUIOverview();
                    break;

                case WizardState.ModelSelection:
                    _currentWizardState = WizardState.SelectSTT;
                    UpdateWizardUIOverview();
                    break;

                case WizardState.SelectLLM:
                    if (_selectedLLM == null && _configManager.CurrentNetworkState != Logic.System.Config.ConfigManager.NetworkState.CloudAPI)
                    {
                        GD.PrintErr("SetupWizard: Validation failed. A local language model target must be designated before proceeding.");
                        return;
                    }
                    _currentWizardState = WizardState.SelectSTT;
                    UpdateWizardUIOverview();
                    break;

                case WizardState.SelectSTT:
                    _currentWizardState = WizardState.SelectTTS;
                    UpdateWizardUIOverview();
                    break;

                case WizardState.SelectTTS:
                    _currentWizardState = WizardState.SelectPerformance;
                    UpdateWizardUIOverview();
                    break;

                case WizardState.SelectPerformance:
                    _currentWizardState = WizardState.Downloading;
                    UpdateWizardUIOverview();
                    StartModelDownload();
                    break;

                case WizardState.ExecutionReady:
                    GD.Print("SetupWizard: Pipeline already in operational state. Execution blocked.");
                    break;
            }
        }

        /// <summary>
        /// Recedes active context indices backwards over predefined linear pipeline coordinates.
        /// </summary>
        public void TransitionToPreviousState()
        {
            switch (_currentWizardState)
            {
                case WizardState.SelectNetworkTopology:
                    _currentWizardState = WizardState.Welcome;
                    UpdateWizardUIOverview();
                    break;

                case WizardState.SelectLLM:
                    _currentWizardState = WizardState.SelectNetworkTopology;
                    UpdateWizardUIOverview();
                    break;

                case WizardState.SelectSTT:
                    _currentWizardState = WizardState.SelectLLM;
                    UpdateWizardUIOverview();
                    break;

                case WizardState.SelectTTS:
                    _currentWizardState = WizardState.SelectSTT;
                    UpdateWizardUIOverview();
                    break;

                case WizardState.SelectPerformance:
                    _currentWizardState = WizardState.SelectTTS;
                    UpdateWizardUIOverview();
                    break;

                case WizardState.ExecutionReady:
                    _currentWizardState = WizardState.SelectPerformance;
                    UpdateWizardUIOverview();
                    break;

                case WizardState.Welcome:
                default:
                    GD.Print("SetupWizard: Boundary constraint reached. Backward navigation blocked.");
                    break;
            }
        }

        /// <summary>
        /// Commits individual resource performance metrics straight onto local disk partitions.
        /// </summary>
        public void ApplyPerformanceTierConfiguration(int uiSelectionIndex)
        {
            if (_configManager == null)
            {
                _configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            }

            if (_configManager == null)
            {
                GD.PrintErr("SetupWizard: Failed to coordinate serialization. ConfigManager infrastructure unresolved.");
                return;
            }

            switch (uiSelectionIndex)
            {
                case 0:
                    _configManager.CurrentPerformanceTier = Logic.System.Config.ConfigManager.PerformanceTier.Low;
                    GD.Print("SetupWizard: Performance tier updated to LOW profile. Lazy loading assigned to speech engines.");
                    break;

                case 1:
                    _configManager.CurrentPerformanceTier = Logic.System.Config.ConfigManager.PerformanceTier.Medium;
                    GD.Print("SetupWizard: Performance tier updated to MEDIUM profile. Baseline resource metrics applied.");
                    break;

                case 2:
                    _configManager.CurrentPerformanceTier = Logic.System.Config.ConfigManager.PerformanceTier.High;
                    GD.Print("SetupWizard: Performance tier updated to HIGH profile. Compute allocation expanded to maximum system ceiling.");
                    break;

                default:
                    _configManager.CurrentPerformanceTier = Logic.System.Config.ConfigManager.PerformanceTier.High;
                    GD.PushWarning("SetupWizard: Out-of-bounds index intercepted. Defaulting to balanced HIGH configuration.");
                    break;
            }

            _configManager.SaveConfiguration();
            GD.Print("SetupWizard: Performance parameters safely committed to disk data storage layers.");
            TransitionToNextState();
        }

        /// <summary>
        /// Negotiates network binding setups adapting sockets safely for local network distribution.
        /// </summary>
        public void ExecuteNetworkConnectionSequence(bool useLanTopology)
        {
            if (_configManager == null)
            {
                _configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            }

            if (_configManager == null)
            {
                GD.PrintErr("SetupWizard: Critical error encountered during initialization. System missing ConfigManager core components.");
                return;
            }

            if (useLanTopology)
            {
                _configManager.CurrentNetworkState = Logic.System.Config.ConfigManager.NetworkState.LanPublic;
                _configManager.IsLanConnection = true;

                string resolvedLocalIP = "127.0.0.1";
                try
                {
                    using (global::System.Net.Sockets.Socket networkSocket = new global::System.Net.Sockets.Socket(global::System.Net.Sockets.AddressFamily.InterNetwork, global::System.Net.Sockets.SocketType.Dgram, 0))
                    {
                        networkSocket.Connect("8.8.8.8", 65530);
                        if (networkSocket.LocalEndPoint is global::System.Net.IPEndPoint networkEndPoint)
                        {
                            resolvedLocalIP = networkEndPoint.Address.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"SetupWizard: Operating system interface extraction error. Falling back to loopback address structures: {ex.Message}");
                }

                _configManager.RemoteHostUrl = $"http://{resolvedLocalIP}:{_configManager.CustomPort}";
                GD.Print($"SetupWizard: LAN Public network settings applied. Host assigned external routing endpoint: {_configManager.RemoteHostUrl} [Bind Address: 0.0.0.0]");
            }
            else
            {
                _configManager.CurrentNetworkState = Logic.System.Config.ConfigManager.NetworkState.StrictLocalhost;
                _configManager.IsLanConnection = false;
                _configManager.RemoteHostUrl = $"http://127.0.0.1:{_configManager.CustomPort}";
                GD.Print($"SetupWizard: Localhost routing properties initialized. Subprocess visibility restricted to internal loopback socket matrices: {_configManager.RemoteHostUrl}");
            }

            _configManager.SaveConfiguration();
            UpdateNetworkUIFeedbackPanel(_configManager.RemoteHostUrl);
        }

        /// <summary>
        /// Validates pre-execution dependencies and passes startup flags down to backend systems.
        /// </summary>
        private void ExecuteFinalDeploymentPipeline()
        {
            if (_backendLauncher == null)
            {
                _backendLauncher = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
            }

            if (_backendLauncher != null)
            {
                _configManager.SetupCompleted = true;
                _configManager.SaveConfiguration();

                GD.Print("SetupWizard: All initialization conditions confirmed. Routing startup sequences to BackendLauncher.");
                _backendLauncher.StartBackend();
            }
            else
            {
                GD.PrintErr("SetupWizard: Fatal execution fault. Unable to locate the operational BackendLauncher component path.");
            }
        }

        /// <summary>
        /// Evaluates active state indexes to synchronize interface panel visibility settings.
        /// Automatically manages the layout allocation and disposal lifecycle of the transient selection panel sub-scene.
        /// </summary>
        private void UpdateWizardUIOverview()
        {
            if (PanelWelcome != null)
                PanelWelcome.Visible = (_currentWizardState == WizardState.Welcome);

            if (PanelDependencies != null)
                PanelDependencies.Visible = (_currentWizardState == WizardState.Dependencies);

            if (PanelModeSelection != null)
                PanelModeSelection.Visible = (_currentWizardState == WizardState.ModeSelection || _currentWizardState == WizardState.SelectNetworkTopology);

            if (PanelPerformanceProfile != null)
                PanelPerformanceProfile.Visible = (_currentWizardState == WizardState.SelectPerformance);

            bool isSelectionState = (_currentWizardState == WizardState.ModelSelection || _currentWizardState == WizardState.SelectLLM || _currentWizardState == WizardState.SelectSTT || _currentWizardState == WizardState.SelectTTS);

            if (isSelectionState)
            {
                if (_activeSelectionPanel != null)
                {
                    _activeSelectionPanel.Visible = true;
                }
            }
            else
            {
                if (_activeSelectionPanel != null)
                {
                    _activeSelectionPanel.ModelConfirmed -= OnDynamicModelConfirmed;
                    _activeSelectionPanel.QueueFree();
                    _activeSelectionPanel = null;
                }
            }

            if (PanelDownloading != null)
                PanelDownloading.Visible = (_currentWizardState == WizardState.Downloading || _currentWizardState == WizardState.StartingServer);

            HandleStateInitialization(_currentWizardState);
        }

        /// <summary>
        /// Populates active network connection values across corresponding interface layouts.
        /// </summary>
        private void UpdateNetworkUIFeedbackPanel(string hostUrl)
        {
            if (TxtLanIpInput != null)
            {
                TxtLanIpInput.Text = hostUrl;
            }

            if (ModelDownloadStatus != null)
            {
                ModelDownloadStatus.Text = $"[center]Network socket mapping initialized at: {hostUrl}[/center]";
            }
        }

        /// <summary>
        /// Validates metadata tokens forwarded by shared layout panels and caches target parameters locally.
        /// Manual UI button updates have been stripped out to ensure a fully automated pipeline traversal.
        /// </summary>
        private async void OnDynamicModelConfirmed(int categoryIndex, string modelName, string targetExecutable)
        {
            List<ConfigManager.ModelPreset> presets = await _configManager.GetOrDownloadPresetsAsync();
            ConfigManager.ModelPreset verifiedPreset = presets.Find(targetPreset => targetPreset.Name == modelName);

            if (verifiedPreset != null)
            {
                Logic.UI.ModelCategory category = (Logic.UI.ModelCategory)categoryIndex;
                switch (category)
                {
                    case Logic.UI.ModelCategory.LLM:
                        _selectedLLM = verifiedPreset;
                        GD.Print($"SetupWizard: Language model reference assigned successfully: {modelName}");
                        break;

                    case Logic.UI.ModelCategory.STT:
                        _selectedSTT = verifiedPreset;
                        GD.Print($"SetupWizard: Speech-to-text validation parameters bound: {modelName}");
                        break;

                    case Logic.UI.ModelCategory.TTS:
                        _selectedTTS = verifiedPreset;
                        GD.Print($"SetupWizard: Audio generation tensor parameters bound: {modelName}");
                        break;
                }
            }

            TransitionToNextState();
        }

        /// <summary>
        /// Invokes scene swap operations targeting core execution scene files.
        /// </summary>
        private void TransitionToMainScene()
        {
            GetTree().ChangeSceneToFile(MainChatScenePath);
        }

        private void OnPerformanceNextPressed()
        {
            ApplyPerformanceTierConfiguration(_currentPerformanceSelection);
        }

        private void SetPerformanceSelection(int tierIndex)
        {
            _currentPerformanceSelection = tierIndex;

            if (BtnLowPerf != null) BtnLowPerf.ButtonPressed = (tierIndex == 0);
            if (BtnMedPerf != null) BtnMedPerf.ButtonPressed = (tierIndex == 1);
            if (BtnHighPerf != null) BtnHighPerf.ButtonPressed = (tierIndex == 2);
        }

        /// <summary>
        /// Validates operational availability across targeted remote server addresses.
        /// </summary>
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
        /// Commits target aesthetic variations directly to configuration files and applies UI updates.
        /// </summary>
        private void SeleccionarTema(bool esOscuro)
        {
            _esModoOscuro = esOscuro;

            if (Logic.System.Config.ConfigManager.Instance != null)
            {
                Logic.System.Config.ConfigManager.Instance.DarkMode = esOscuro;
                Logic.System.Config.ConfigManager.Instance.SaveConfiguration();
            }

            // Sync with global ThemeManager autoload and apply globally to tree root
            if (Logic.UI.ThemeManager.Instance != null)
            {
                GetTree().Root.Theme = Logic.UI.ThemeManager.Instance.ObtenerTemaGlobal(esOscuro);
            }
            else
            {
                string path = esOscuro ? "res://Resources/UI_Themes/minimal_theme.tres" : "res://Resources/UI_Themes/tema_claro.tres";
                Theme temaCorrecto = ResourceLoader.Load<Theme>(path);
                this.Theme = temaCorrecto;
                GetTree().Root.Theme = temaCorrecto;
            }

            // Update local ColorRect/Panel background
            if (SetupBackground is ColorRect bgRect)
            {
                bgRect.Color = esOscuro ? new Color("#131313") : new Color("#f5f5f7");
            }
            else if (SetupBackground is PanelContainer bgPanel)
            {
                var style = new StyleBoxFlat { BgColor = esOscuro ? new Color("#131313") : new Color("#f5f5f7") };
                bgPanel.AddThemeStyleboxOverride("panel", style);
            }

            // --- Real-time Local Overrides and Aesthetic Hardening ---

            // Retrieve and update the Glass Panel StyleBox Flat properties
            if (PanelWelcome != null && PanelWelcome.GetThemeStylebox("panel") is StyleBoxFlat glassStyle)
            {
                if (esOscuro)
                {
                    glassStyle.BgColor = new Color(0.12f, 0.12f, 0.14f, 0.85f);
                    glassStyle.BorderColor = new Color(1f, 1f, 1f, 0.08f);
                    glassStyle.ShadowColor = new Color(0f, 0f, 0f, 0.45f);
                }
                else
                {
                    glassStyle.BgColor = new Color(1f, 1f, 1f, 0.92f);
                    glassStyle.BorderColor = new Color(0f, 0f, 0f, 0.08f);
                    glassStyle.ShadowColor = new Color(0f, 0f, 0f, 0.08f);
                }
            }

            // Retrieve and update the Card & Input Field StyleBox Flat properties
            if (TxtLanIpInput != null && TxtLanIpInput.GetThemeStylebox("normal") is StyleBoxFlat inputStyle)
            {
                if (esOscuro)
                {
                    inputStyle.BgColor = new Color(0f, 0f, 0f, 0.2f);
                    inputStyle.BorderColor = new Color(1f, 1f, 1f, 0.05f);
                    inputStyle.BorderWidthLeft = 0;
                    inputStyle.BorderWidthTop = 0;
                    inputStyle.BorderWidthRight = 0;
                    inputStyle.BorderWidthBottom = 0;
                }
                else
                {
                    inputStyle.BgColor = new Color(0.95f, 0.95f, 0.96f, 1f);
                    inputStyle.BorderColor = new Color(0.89f, 0.89f, 0.91f, 1f);
                    inputStyle.BorderWidthLeft = 1;
                    inputStyle.BorderWidthTop = 1;
                    inputStyle.BorderWidthRight = 1;
                    inputStyle.BorderWidthBottom = 1;
                }
            }

            // Retrieve and update the Button normal/hover stylebox properties
            if (BtnTemaOscuro != null && BtnTemaOscuro.GetThemeStylebox("normal") is StyleBoxFlat btnNormalStyle)
            {
                if (esOscuro)
                {
                    btnNormalStyle.BgColor = new Color(0.5f, 0.5f, 0.5f, 0.15f);
                }
                else
                {
                    btnNormalStyle.BgColor = new Color(0.90f, 0.90f, 0.92f, 1f);
                }
            }

            if (BtnTemaOscuro != null && BtnTemaOscuro.GetThemeStylebox("hover") is StyleBoxFlat btnHoverStyle)
            {
                if (esOscuro)
                {
                    btnHoverStyle.BgColor = new Color(0.5f, 0.5f, 0.5f, 0.25f);
                }
                else
                {
                    btnHoverStyle.BgColor = new Color(0.85f, 0.85f, 0.87f, 1f);
                }
            }

            // High contrast text colors for toggle and primary buttons
            var whiteColor = new Color(1f, 1f, 1f, 1f);
            var darkColor = new Color(0.114f, 0.114f, 0.122f, 1f);

            BtnTemaOscuro?.AddThemeColorOverride("font_pressed_color", whiteColor);
            BtnTemaClaro?.AddThemeColorOverride("font_pressed_color", whiteColor);
            BtnTemaOscuro?.AddThemeColorOverride("font_focus_color", esOscuro ? whiteColor : darkColor);
            BtnTemaClaro?.AddThemeColorOverride("font_focus_color", esOscuro ? whiteColor : darkColor);

            BtnLowPerf?.AddThemeColorOverride("font_pressed_color", whiteColor);
            BtnMedPerf?.AddThemeColorOverride("font_pressed_color", whiteColor);
            BtnHighPerf?.AddThemeColorOverride("font_pressed_color", whiteColor);
            
            BtnLowPerf?.AddThemeColorOverride("font_focus_color", esOscuro ? whiteColor : darkColor);
            BtnMedPerf?.AddThemeColorOverride("font_focus_color", esOscuro ? whiteColor : darkColor);
            BtnHighPerf?.AddThemeColorOverride("font_focus_color", esOscuro ? whiteColor : darkColor);
        }
    }
}