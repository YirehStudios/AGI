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
        [Export] private string LlamaServerUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b8770/llama-b8770-bin-ubuntu-vulkan-x64.tar.gz";
        [Export] private string WhisperServerUrl = "https://raw.githubusercontent.com/YirehStudios/AGI/refs/heads/main/whisper-server-vulkan-linux/whisper-server-vulkan-linux.tar.gz";
        [Export] private string SherpaServerUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.10.32/sherpa-onnx-v1.10.32-linux-x64.tar.bz2";
        [Export] private string PiperModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-piper-es_ES-upc_ona-high.tar.bz2";
        private ConfigManager.ModelPreset _selectedLLM;
        private ConfigManager.ModelPreset _selectedSTT;
        private ConfigManager.ModelPreset _selectedTTS;

        [Export] public Button BtnStartBatchDownload;

        private DownloadManager _downloadManager;
        private WizardState _currentState;
        private DependencyInstaller _dependencyInstaller;
        private ConfigManager _configManager;

        /// <summary>
        /// Initializes the core managers and child nodes, and binds the UI signals to their respective handlers.
        /// Evaluates the underlying platform to determine the initial routing logic.
        /// </summary>
        public override void _Ready()
        {
            _configManager = GetNode<ConfigManager>("/root/ConfigManager");

            _dependencyInstaller = new DependencyInstaller();
            AddChild(_dependencyInstaller);

            _downloadManager = new DownloadManager();
            AddChild(_downloadManager);

            // Subscribes local handlers to the real-time progress events emitted by DownloadManager.
            _downloadManager.DownloadCompleted += OnModelDownloadCompleted;
            _downloadManager.DownloadProgress += OnModelDownloadProgress;

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
                    // Extracts and sanitizes string values from the text inputs.
                    string urlIngresada = TxtRemoteUrlInput != null ? TxtRemoteUrlInput.Text.Trim() : "";
                    string puerto = TxtCustomPort != null && !string.IsNullOrWhiteSpace(TxtCustomPort.Text) ? TxtCustomPort.Text.Trim() : "8080";
                    bool isLan = ChkIsLan != null && ChkIsLan.ButtonPressed;

                    // Assigns a default local IP address fallback if the parsed string is empty.
                    if (string.IsNullOrWhiteSpace(urlIngresada))
                    {
                        urlIngresada = "192.168.1.100";
                    }

                    // Validates that the user input contains a protocol schema, appending http:// if missing.
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

            // Evaluates the configuration flag. Bypasses the initial UI setup if the environment is already configured.
            if (_configManager.SetupCompleted)
            {
                FastBootSequence();
                return;
            }

            string osName = OS.GetName();
            bool isMobile = osName == "Android" || osName == "iOS";

            // Evaluates the operating system to conditionally render the LocalHost button and route the initial state.
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
        /// Bypasses the setup UI if the configuration is already completed.
        /// Triggers the local server instantiation or performs a remote handshake based on the saved application mode.
        /// </summary>
        private async void FastBootSequence()
        {
            PanelWelcome.Visible = false;

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
                    // Resets the configuration completion flag and routes back to the mode selection state upon connection failure.
                    _configManager.SetupCompleted = false;
                    _configManager.SaveConfiguration();

                    SwitchState(WizardState.ModeSelection);
                    if (ModelDownloadStatus != null)
                        ModelDownloadStatus.Text = "Se perdió la conexión con el servidor guardado. Configura uno nuevo.";
                }
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
        private void OnModelSelected(ConfigManager.ModelPreset preset, Button clickedButton)
        {
            // Retroalimentación visual
            clickedButton.Text = "¡Seleccionado!";
            clickedButton.Disabled = true;

            if (preset.Name.Contains("Whisper"))
            {
                _selectedSTT = preset;
            }
            else if (preset.Name.Contains("Sherpa") || preset.Name.Contains("Kokoro"))
            {
                _selectedTTS = preset;
            }
            else
            {
                _selectedLLM = preset;
            }

            if (_selectedLLM != null && _selectedSTT != null && _selectedTTS != null)
            {
                if (BtnStartBatchDownload != null)
                {
                    BtnStartBatchDownload.Disabled = false;
                    BtnStartBatchDownload.Text = "Todo listo. Iniciar Sistema";
                }
            }
        }

        /// <summary>
        /// Orchestrates the asynchronous retrieval, extraction, and initialization of native C++ engines.
        /// Isolates binary dependency validation to prevent redundant network invocations, dynamically resolves
        /// TTS dictionary structures, and coordinates file system mappings via the configuration orchestrator.
        /// </summary>
        private async void StartModelDownload()
        {
            SwitchState(WizardState.Downloading);

            string binDir = ProjectSettings.GlobalizePath("user://bin");
            global::System.IO.Directory.CreateDirectory(binDir);

            string llamaBinPath = global::System.IO.Path.Combine(binDir, "llama-b8770/llama-server");
            string whisperBinPath = global::System.IO.Path.Combine(binDir, "whisper-server");
            string sherpaBinPath = global::System.IO.Path.Combine(binDir, "sherpa-onnx/sherpa-onnx-tts-server");
            string binDirAbs = ProjectSettings.GlobalizePath("user://bin");

            // Interroga el sistema de archivos local de forma aislada para resolver el contenedor Llama.
            if (!global::System.IO.File.Exists(llamaBinPath))
            {
                if (ModelDownloadStatus != null) ModelDownloadStatus.Text = "[center]Descargando Llama Server...[/center]";
                await _downloadManager.DownloadFileAsync(LlamaServerUrl, "user://bin", "llama-server.tar.gz");
                OS.Execute("tar", new string[] { "-xf", global::System.IO.Path.Combine(binDirAbs, "llama-server.tar.gz"), "-C", binDirAbs }, new Godot.Collections.Array(), true);
                OS.Execute("chmod", new string[] { "+x", llamaBinPath }, new Godot.Collections.Array(), true);
            }

            // Interroga el sistema de archivos local de forma aislada para resolver el contenedor Whisper.
            if (!global::System.IO.File.Exists(whisperBinPath))
            {
                if (ModelDownloadStatus != null) ModelDownloadStatus.Text = "[center]Descargando Whisper Server...[/center]";
                await _downloadManager.DownloadFileAsync(WhisperServerUrl, "user://bin", "whisper-server.tar.gz");
                OS.Execute("tar", new string[] { "-xf", global::System.IO.Path.Combine(binDirAbs, "whisper-server.tar.gz"), "-C", binDirAbs }, new Godot.Collections.Array(), true);
                OS.Execute("chmod", new string[] { "+x", whisperBinPath }, new Godot.Collections.Array(), true);
            }

            // Interroga el sistema de archivos local de forma aislada para resolver el contenedor Sherpa-ONNX.
            if (!global::System.IO.File.Exists(sherpaBinPath))
            {
                if (ModelDownloadStatus != null) ModelDownloadStatus.Text = "[center]Descargando Sherpa-ONNX Server...[/center]";
                await _downloadManager.DownloadFileAsync(SherpaServerUrl, "user://bin", "sherpa-onnx-linux.tar.bz2");
                OS.Execute("tar", new string[] { "-xjf", global::System.IO.Path.Combine(binDirAbs, "sherpa-onnx-linux.tar.bz2"), "-C", binDirAbs }, new Godot.Collections.Array(), true);
                
                string extractedSherpaDir = global::System.IO.Path.Combine(binDirAbs, "sherpa-onnx-v1.10.32-linux-x64");
                string targetSherpaDir = global::System.IO.Path.Combine(binDirAbs, "sherpa-onnx");
                if (global::System.IO.Directory.Exists(extractedSherpaDir))
                {
                    if (global::System.IO.Directory.Exists(targetSherpaDir)) global::System.IO.Directory.Delete(targetSherpaDir, true);
                    global::System.IO.Directory.Move(extractedSherpaDir, targetSherpaDir);
                }
                OS.Execute("chmod", new string[] { "+x", sherpaBinPath }, new Godot.Collections.Array(), true);
            }

            string piperDir = ProjectSettings.GlobalizePath("user://models");
            global::System.IO.Directory.CreateDirectory(piperDir);

            List<ConfigManager.ModelPreset> presetsToDownload = new List<ConfigManager.ModelPreset> { _selectedLLM, _selectedSTT, _selectedTTS };

            foreach (ConfigManager.ModelPreset preset in presetsToDownload)
            {
                if (preset == null) continue;

                string safeFileName = preset.Name.Replace(" ", "_");
                
                // Extrae los descriptores de archivo basándose en la clase de modelo y su topología de red de origen.
                if (preset.Name.Contains("Whisper")) safeFileName += ".bin";
                else if (preset.Name.Contains("Piper") || preset.Name.Contains("Sherpa")) 
                {
                    safeFileName = global::System.IO.Path.GetFileName(new global::System.Uri(preset.DownloadLinks[0]).LocalPath);
                }
                else safeFileName += ".gguf";

                // Delega las asignaciones persistentes en el gestor de configuración evaluando las clasificaciones semánticas.
                if (preset.Name.Contains("Piper") || preset.Name.Contains("Sherpa"))
                {
                    _configManager.ActiveTTSEngine = "sherpa-onnx";
                    _configManager.ActiveTTSModel = safeFileName.Replace(".tar.bz2", ""); 
                }
                else if (preset.Name.Contains("Whisper"))
                {
                    _configManager.ActiveSTTModel = safeFileName;
                }
                else
                {
                    _configManager.ActiveModelName = preset.Name;
                    _configManager.ActiveModelPath = ProjectSettings.GlobalizePath("user://models/" + safeFileName);
                }

                _configManager.ActiveModelUrl = preset.DownloadLinks[0];
                _configManager.SaveConfiguration();

                // Implementa resolución de caché dinámico diferenciando entre contenedores simples y directorios extraídos.
                string globalPath = ProjectSettings.GlobalizePath("user://models/" + safeFileName);
                bool isAlreadyExtracted = false;
                
                if (preset.Name.Contains("Piper") || preset.Name.Contains("Sherpa"))
                {
                    string extractedFolderPath = ProjectSettings.GlobalizePath("user://models/" + _configManager.ActiveTTSModel);
                    if (global::System.IO.Directory.Exists(extractedFolderPath)) isAlreadyExtracted = true;
                }

                if (global::System.IO.File.Exists(globalPath) || isAlreadyExtracted)
                {
                    GD.Print($"SetupWizard: Cache local validado para {safeFileName}");
                    if (ModelDownloadStatus != null) ModelDownloadStatus.Text = $"[center]El modelo {preset.Name} ya está listo. Omitiendo...[/center]";
                    await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout); 
                    continue; 
                }

                if (ModelDownloadStatus != null) ModelDownloadStatus.Text = $"[center]Descargando tensores: {preset.Name}...[/center]";

                bool success = await _downloadManager.DownloadFileAsync(preset.DownloadLinks[0], "user://models", safeFileName);

                // Rompe el ciclo e interrumpe la transición de arranque si la transferencia binaria arroja falsos positivos.
                if (!success)
                {
                    GD.PrintErr($"SetupWizard: Fallo de red con {preset.Name}");
                    if (ModelDownloadStatus != null) ModelDownloadStatus.Text = $"[center][color=red]Error de red descargando {preset.Name}.[/color][/center]";
                    return;
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
        /// Instantiates the monitoring phase for the Docker container and the Llama server instances.
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
                ModelDownloadStatus.Text = "Error crítico al iniciar Docker o Llama Server.\nRevisa la consola o asegúrate de que Docker esté corriendo.";
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

			List<ConfigManager.ModelPreset> presets = await _configManager.GetOrDownloadPresetsAsync();

			if (presets == null || presets.Count == 0) return;

			// Creamos un estilo de tarjeta claro y limpio desde C#
			StyleBoxFlat cardStyle = new StyleBoxFlat
			{
				BgColor = new Color(1, 1, 1, 1),
				BorderColor = new Color(0.85f, 0.85f, 0.85f, 1),
				BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
				CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
				ContentMarginLeft = 20, ContentMarginTop = 20, ContentMarginRight = 20, ContentMarginBottom = 20
			};

			foreach (ConfigManager.ModelPreset preset in presets)
			{
				PanelContainer cardPanel = new PanelContainer();
				cardPanel.AddThemeStyleboxOverride("panel", cardStyle);

				HBoxContainer cardLayout = new HBoxContainer();
				VBoxContainer textContainer = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

				Label nameLabel = new Label { Text = preset.Name };
				// LÍNEA CORREGIDA: Se eliminó la búsqueda de la fuente inexistente que causaba el Crash.
				nameLabel.AddThemeColorOverride("font_color", new Color(0.1f, 0.1f, 0.1f, 1));

				Label descLabel = new Label { Text = preset.Description };
				descLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.4f, 0.4f, 1));

				Button actionButton = new Button { Text = "Seleccionar" };

				// Pasamos el botón mismo al evento para poder cambiarle el texto al hacer clic
				actionButton.Pressed += () => OnModelSelected(preset, actionButton);

				textContainer.AddChild(nameLabel);
				textContainer.AddChild(descLabel);
				cardLayout.AddChild(textContainer);
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
    }
}