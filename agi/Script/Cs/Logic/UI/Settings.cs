using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Logic.System.Config;
using Logic.Backend;
using Logic.Network;

namespace Logic.UI
{
    /// <summary>
    /// Standalone controller for the configuration subsystem.
    /// Manages five functional views (Models, Performance, Tools, Preferences, Privacy),
    /// drives model directory scanning, and synchronizes all state mutations to
    /// <see cref="ConfigManager"/> with immediate disk serialization.
    /// </summary>
    public partial class Settings : PanelContainer
    {
        // ─────────────────────────────────────────────────────────────
        //  NAVIGATION EXPORTS
        // ─────────────────────────────────────────────────────────────

        [ExportGroup("Navigation")]
        /// <summary>Primary navigation button that activates the Models view.</summary>
        [Export] public Button ModelsBtn { get; set; }

        /// <summary>Primary navigation button that activates the Performance view.</summary>
        [Export] public Button PerformanceBtn { get; set; }

        /// <summary>Primary navigation button that activates the MCP Tools view.</summary>
        [Export] public Button ToolsBtn { get; set; }

        /// <summary>Primary navigation button that activates the Preferences view (theme, appearance).</summary>
        [Export] public Button PreferencesBtn { get; set; }

        /// <summary>Primary navigation button that activates the Privacy and Data view.</summary>
        [Export] public Button PrivacyBtn { get; set; }

        /// <summary>System-info button pinned at the bottom of the sidebar below the spacer.</summary>
        [Export] public Button InfoBtn { get; set; }

        /// <summary>
        /// Update trigger button. Hidden by default; visibility is set programmatically
        /// when an upstream version footprint mismatch is detected.
        /// </summary>
        [Export] public Button UpdateBtn { get; set; }

        // ─────────────────────────────────────────────────────────────
        //  VIEW CONTAINER EXPORTS
        // ─────────────────────────────────────────────────────────────

        [ExportGroup("View Containers")]
        /// <summary>Container rendered when the Models navigation category is active.</summary>
        [Export] public Container ModelsViewContainer { get; set; }

        /// <summary>Container rendered when the Performance navigation category is active.</summary>
        [Export] public Container PerformanceViewContainer { get; set; }

        /// <summary>Container rendered when the Tools navigation category is active.</summary>
        [Export] public Container ToolsViewContainer { get; set; }

        /// <summary>Container rendered when the Preferences navigation category is active.</summary>
        [Export] public Container PreferencesViewContainer { get; set; }

        /// <summary>Container rendered when the Privacy navigation category is active.</summary>
        [Export] public Container PrivacyViewContainer { get; set; }

        // ─────────────────────────────────────────────────────────────
        //  MODELS CONTROLS
        // ─────────────────────────────────────────────────────────────

        [ExportGroup("Models Controls")]
        /// <summary>Dropdown populated from user://models/ directory JSON manifests.</summary>
        [Export] public OptionButton ActiveModelSelector { get; set; }

        /// <summary>
        /// Editable display name for the currently loaded model profile.
        /// Mutating this field immediately rewrites <c>Nombre</c> inside the active JSON card.
        /// </summary>
        [Export] public LineEdit TxtIaDisplayNameInput { get; set; }

        /// <summary>Maximum tokens accepted by the model in a single inference request.</summary>
        [Export] public SpinBox NumInputTokensLimit { get; set; }

        /// <summary>Maximum tokens the model may produce per response generation cycle.</summary>
        [Export] public SpinBox NumOutputTokensLimit { get; set; }

        /// <summary>Filesystem path or URL for importing raw GGUF weight files.</summary>
        [Export] public LineEdit WeightImportLineEdit { get; set; }

        /// <summary>Trigger button that initiates the weight import pipeline.</summary>
        [Export] public Button WeightImportBtn { get; set; }

        // ─────────────────────────────────────────────────────────────
        //  PERFORMANCE CONTROLS
        // ─────────────────────────────────────────────────────────────

        [ExportGroup("Performance Controls")]
        /// <summary>Determines the number of CPU worker threads allocated to the inference engine.</summary>
        [Export] public SpinBox CpuThreadsSpinBox { get; set; }

        /// <summary>Number of neural network layers offloaded to the GPU via CUDA/Vulkan.</summary>
        [Export] public SpinBox GpuLayersSpinBox { get; set; }

        /// <summary>Hard RAM saturation ceiling in megabytes before the engine triggers memory relief.</summary>
        [Export] public SpinBox RamSaturationSpinBox { get; set; }

        // ─────────────────────────────────────────────────────────────
        //  TOOLS CONTROLS
        // ─────────────────────────────────────────────────────────────

        [ExportGroup("Tools Controls")]
        /// <summary>
        /// Dynamic vertical container populated at runtime with one row per registered MCP tool,
        /// each row containing a name label and a permission OptionButton.
        /// </summary>
        [Export] public VBoxContainer ToolsGrid { get; set; }

        // ─────────────────────────────────────────────────────────────
        //  PREFERENCES CONTROLS
        // ─────────────────────────────────────────────────────────────

        [ExportGroup("Preferences Controls")]
        /// <summary>
        /// Theme toggle. Relocated from Privacy into the dedicated Preferences view.
        /// Triggers immediate global theme re-evaluation via <see cref="ThemeManager"/>.
        /// </summary>
        [Export] public CheckButton DarkModeToggle { get; set; }

        // ─────────────────────────────────────────────────────────────
        //  PRIVACY CONTROLS
        // ─────────────────────────────────────────────────────────────

        [ExportGroup("Privacy Controls")]
        /// <summary>Editable path controlling where conversation history JSON files are persisted.</summary>
        [Export] public LineEdit StoragePathLineEdit { get; set; }

        /// <summary>Executes an irreversible wipe of all files in the configured history directory.</summary>
        [Export] public Button PurgeDataBtn { get; set; }

        /// <summary>Toggle that enables strict network isolation (telemetry disabled) mode.</summary>
        [Export] public CheckButton TelemetryToggle { get; set; }

        // ─────────────────────────────────────────────────────────────
        //  PRIVATE STATE
        // ─────────────────────────────────────────────────────────────

        private ConfigManager _configManager;
        private BackendLauncher _backendLauncher;
        private NetworkManager _networkManager;

        /// <summary>
        /// Maps dropdown item index → absolute globalized path of the corresponding model JSON manifest.
        /// Populated exclusively by <see cref="CargarModelosEnMenu"/>.
        /// </summary>
        private readonly Dictionary<int, string> _rutasModelos = new();

        // ─────────────────────────────────────────────────────────────
        //  GODOT LIFECYCLE
        // ─────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public override void _Ready()
        {
            InitializeSystemLinks();
            ApplyStartupTheme();
            BindNavigationSignals();
            BindControlSignals();
            LoadActiveConfiguration();

            // Default landing view is Models.
            SwitchActiveView(0);
        }

        // ─────────────────────────────────────────────────────────────
        //  INITIALIZATION
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves all AutoLoad singleton dependencies via safe NodePath lookup.
        /// Logs a diagnostic error if any required dependency is absent.
        /// </summary>
        private void InitializeSystemLinks()
        {
            _configManager   = GetNodeOrNull<ConfigManager>("/root/ConfigManager");
            _backendLauncher = GetNodeOrNull<BackendLauncher>("/root/BackendLauncher");
            _networkManager  = GetNodeOrNull<NetworkManager>("/root/NetworkManager");

            if (_configManager == null)
                GD.PrintErr("[SETTINGS] ConfigManager dependency missing.");
            if (_backendLauncher == null)
                GD.PrintErr("[SETTINGS] BackendLauncher dependency missing.");
        }

        /// <summary>
        /// Reads the persisted <see cref="ConfigManager.DarkMode"/> flag and applies the
        /// matching global <see cref="Theme"/> via <see cref="ThemeManager"/> immediately on boot,
        /// resolving the startup theme initialization deficit.
        /// </summary>
        private void ApplyStartupTheme()
        {
            if (ThemeManager.Instance == null || _configManager == null) return;
            GetTree().Root.Theme = ThemeManager.Instance.ObtenerTemaGlobal(_configManager.DarkMode);
        }

        // ─────────────────────────────────────────────────────────────
        //  NAVIGATION
        // ─────────────────────────────────────────────────────────────

        /// <summary>Connects every sidebar navigation button to the <see cref="SwitchActiveView"/> dispatcher.</summary>
        private void BindNavigationSignals()
        {
            if (ModelsBtn      != null) ModelsBtn.Pressed      += () => SwitchActiveView(0);
            if (PerformanceBtn != null) PerformanceBtn.Pressed += () => SwitchActiveView(1);
            if (ToolsBtn       != null) ToolsBtn.Pressed       += () => SwitchActiveView(2);
            if (PreferencesBtn != null) PreferencesBtn.Pressed += () => SwitchActiveView(3);
            if (PrivacyBtn     != null) PrivacyBtn.Pressed     += () => SwitchActiveView(4);
            if (InfoBtn        != null) InfoBtn.Pressed        += ShowInfoPanel;
        }

        /// <summary>
        /// Mutually-exclusive view toggler. Exactly one view container is visible at any time.
        /// </summary>
        /// <param name="viewIndex">
        /// 0 = Models, 1 = Performance, 2 = Tools, 3 = Preferences, 4 = Privacy.
        /// </param>
        private void SwitchActiveView(int viewIndex)
        {
            if (ModelsViewContainer      != null) ModelsViewContainer.Visible      = (viewIndex == 0);
            if (PerformanceViewContainer != null) PerformanceViewContainer.Visible = (viewIndex == 1);
            if (ToolsViewContainer       != null) ToolsViewContainer.Visible       = (viewIndex == 2);
            if (PreferencesViewContainer != null) PreferencesViewContainer.Visible = (viewIndex == 3);
            if (PrivacyViewContainer     != null) PrivacyViewContainer.Visible     = (viewIndex == 4);
        }

        /// <summary>
        /// Displays a minimal version/info overlay. Implementation prints to output for now;
        /// a dedicated popup can be wired in a future polish pass.
        /// </summary>
        private void ShowInfoPanel()
        {
            GD.Print("[SETTINGS] AGI Platform — Yireh Studios — Build: vE 0.1");
        }

        // ─────────────────────────────────────────────────────────────
        //  SIGNAL BINDING
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Registers all control-level event handlers for the five module views.
        /// Each handler delegates to a typed mutation method to keep signal closures thin.
        /// </summary>
        private void BindControlSignals()
        {
            // ── Models ──────────────────────────────────────────────
            if (ActiveModelSelector   != null) ActiveModelSelector.ItemSelected   += OnModelSelected;
            if (TxtIaDisplayNameInput != null) TxtIaDisplayNameInput.TextChanged  += OnDisplayNameChanged;
            if (NumInputTokensLimit   != null) NumInputTokensLimit.ValueChanged   += v => UpdateInputTokenLimit((int)v);
            if (NumOutputTokensLimit  != null) NumOutputTokensLimit.ValueChanged  += v => UpdateOutputTokenLimit((int)v);

            // ── Performance ─────────────────────────────────────────
            if (CpuThreadsSpinBox   != null) CpuThreadsSpinBox.ValueChanged   += v => UpdateCpuThreads((int)v);
            if (GpuLayersSpinBox    != null) GpuLayersSpinBox.ValueChanged    += v => UpdateGpuLayers((int)v);
            if (RamSaturationSpinBox!= null) RamSaturationSpinBox.ValueChanged += v => UpdateRamSaturation((int)v);

            // ── Preferences ─────────────────────────────────────────
            if (DarkModeToggle != null) DarkModeToggle.Toggled += OnDarkModeToggled;

            // ── Privacy ─────────────────────────────────────────────
            if (PurgeDataBtn != null) PurgeDataBtn.Pressed += PurgeLocalData;
        }

        // ─────────────────────────────────────────────────────────────
        //  CONFIGURATION LOADING
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the current <see cref="ConfigManager"/> state and populates every
        /// control with its persisted value without firing reactive signal handlers.
        /// </summary>
        private void LoadActiveConfiguration()
        {
            if (_configManager == null) return;

            // ── Performance ─────────────────────────────────────────
            if (CpuThreadsSpinBox    != null) CpuThreadsSpinBox.SetValueNoSignal(_configManager.PerformanceProfile.CpuThreads);
            if (GpuLayersSpinBox     != null) GpuLayersSpinBox.SetValueNoSignal(_configManager.PerformanceProfile.GpuLayers);
            if (RamSaturationSpinBox != null) RamSaturationSpinBox.SetValueNoSignal(_configManager.PerformanceProfile.RamSaturationCeilingMB);

            // ── Preferences ─────────────────────────────────────────
            if (DarkModeToggle != null) DarkModeToggle.SetPressedNoSignal(_configManager.DarkMode);

            // ── Models ──────────────────────────────────────────────
            CargarModelosEnMenu();
            SyncModelFieldsFromActiveProfile();

            // ── Tools ───────────────────────────────────────────────
            PopulateToolsGrid();
        }

        /// <summary>
        /// Pushes values from the currently-loaded <see cref="ConfigManager.ActiveProfile"/>
        /// into the Models view controls without triggering reactive writes back to disk.
        /// </summary>
        private void SyncModelFieldsFromActiveProfile()
        {
            var profile = _configManager?.ActiveProfile;
            if (profile == null) return;

            if (TxtIaDisplayNameInput != null)
                TxtIaDisplayNameInput.Text = profile.Nombre ?? string.Empty;

            if (NumInputTokensLimit != null)
                NumInputTokensLimit.SetValueNoSignal(profile.MaxInputTokens > 0 ? profile.MaxInputTokens : 4096);

            if (NumOutputTokensLimit != null)
                NumOutputTokensLimit.SetValueNoSignal(profile.MaxOutputTokens > 0 ? profile.MaxOutputTokens : 2048);
        }

        // ─────────────────────────────────────────────────────────────
        //  MODEL DIRECTORY SCANNING  (ported from ChatbotMain.cs)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Scans <c>user://models/</c> for <c>.json</c> profile manifests, populates
        /// <see cref="ActiveModelSelector"/>, and selects the entry that matches
        /// <see cref="ConfigManager.ActiveProfilePath"/>.
        /// </summary>
        private void CargarModelosEnMenu()
        {
            if (ActiveModelSelector == null) return;

            ActiveModelSelector.Clear();
            _rutasModelos.Clear();

            const string rutaModelos = "user://models";
            using var dir = DirAccess.Open(rutaModelos);

            if (dir == null)
            {
                GD.PrintErr($"[SETTINGS] Carpeta de modelos no encontrada: {rutaModelos}");
                ActiveModelSelector.AddItem("Sin modelos instalados", 0);
                ActiveModelSelector.Disabled = true;
                return;
            }

            dir.ListDirBegin();
            string fileName = dir.GetNext();

            while (fileName != string.Empty)
            {
                if (!dir.CurrentIsDir() && fileName.EndsWith(".json"))
                {
                    string fullPath = $"{rutaModelos}/{fileName}";
                    using var file = Godot.FileAccess.Open(fullPath, Godot.FileAccess.ModeFlags.Read);
                    if (file != null)
                    {
                        var json = new Json();
                        if (json.Parse(file.GetAsText()) == Error.Ok)
                        {
                            var data = json.Data.AsGodotDictionary();
                            string displayName = data.ContainsKey("nombre")
                                ? (string)data["nombre"]
                                : fileName.Replace(".json", string.Empty);

                            ActiveModelSelector.AddItem(displayName);
                            _rutasModelos[ActiveModelSelector.GetItemCount() - 1] = fullPath;
                        }
                    }
                }
                fileName = dir.GetNext();
            }

            if (ActiveModelSelector.ItemCount == 0)
            {
                ActiveModelSelector.AddItem("Sin modelos instalados", 0);
                ActiveModelSelector.Disabled = true;
                return;
            }

            ActiveModelSelector.Disabled = false;

            // Restore selection matching the persisted ActiveProfilePath.
            if (_configManager != null && !string.IsNullOrEmpty(_configManager.ActiveProfilePath))
            {
                bool matched = false;
                foreach (var kvp in _rutasModelos)
                {
                    if (ProjectSettings.GlobalizePath(kvp.Value) == _configManager.ActiveProfilePath)
                    {
                        ActiveModelSelector.Select(kvp.Key);
                        matched = true;
                        break;
                    }
                }

                if (!matched) ActiveModelSelector.Select(0);
            }
            else
            {
                ActiveModelSelector.Select(0);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  TOOLS GRID POPULATION
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Dynamically constructs the MCP permission matrix inside <see cref="ToolsGrid"/>.
        /// Each row contains a tool name label and a three-option permission selector
        /// (Automatic = 0, Ask First = 1, Excluded = 2).
        /// </summary>
        private void PopulateToolsGrid()
        {
            if (ToolsGrid == null) return;
            foreach (Node child in ToolsGrid.GetChildren()) child.QueueFree();

            string[] registeredTools =
            {
                "web_search", "os_command", "create_new_file", "read_file",
                "edit_existing_file", "single_find_and_replace",
                "fetch_url_content", "delete_file", "rename_file"
            };

            foreach (string tool in registeredTools)
            {
                HBoxContainer row = new HBoxContainer();

                Label nameLabel = new Label
                {
                    Text = tool,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill
                };

                OptionButton permSelector = new OptionButton();
                permSelector.AddItem("Automatic",  0);
                permSelector.AddItem("Ask First",  1);
                permSelector.AddItem("Excluded",   2);

                int savedPermission = _configManager != null
                    && _configManager.ToolPermissions.TryGetValue(tool, out int p) ? p : 0;
                permSelector.Select(savedPermission);

                string capturedTool = tool;
                permSelector.ItemSelected += idx => OnToolPermissionChanged(capturedTool, (int)idx);

                row.AddChild(nameLabel);
                row.AddChild(permSelector);
                ToolsGrid.AddChild(row);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  MODELS HANDLERS
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Deserializes the selected model JSON manifest into <see cref="ConfigManager.ActiveProfile"/>,
        /// updates <see cref="ConfigManager.ActiveProfilePath"/>, refreshes model field controls,
        /// and conditionally restarts the local backend engine.
        /// </summary>
        private async void OnModelSelected(long index)
        {
            int itemId = ActiveModelSelector.GetItemId((int)index);
            if (!_rutasModelos.TryGetValue(itemId, out string rutaJson)) return;

            string globalPath = ProjectSettings.GlobalizePath(rutaJson);

            try
            {
                string raw     = File.ReadAllText(globalPath);
                var profile    = JsonSerializer.Deserialize<ConfigManager.ModelProfile>(raw);

                if (_configManager != null)
                {
                    _configManager.ActiveProfile     = profile;
                    _configManager.ActiveProfilePath = globalPath;
                    _configManager.SaveConfiguration();
                }

                SyncModelFieldsFromActiveProfile();

                if (profile?.Tipo == 2 /* LocalHost */ && _backendLauncher != null)
                {
                    _configManager.CurrentMode = ConfigManager.AppMode.LocalHost;
                    _backendLauncher.TerminateOrphanedResources();
                    _backendLauncher.StartBackend();
                    await ToSignal(_backendLauncher, BackendLauncher.SignalName.BackendReady);
                }
                else if (profile?.Tipo == 3 /* CloudAPI */)
                {
                    _configManager.CurrentMode = ConfigManager.AppMode.CloudAPI;
                    _backendLauncher?.TerminateOrphanedResources();
                }

                GD.Print($"[SETTINGS] Perfil cargado: {profile?.Nombre}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SETTINGS] Error al cargar perfil: {ex.Message}");
            }
        }

        /// <summary>
        /// Rewrites the <c>Nombre</c> display field in the active model profile and
        /// immediately persists the change to its corresponding JSON file on disk.
        /// </summary>
        private void OnDisplayNameChanged(string newName)
        {
            if (_configManager?.ActiveProfile == null) return;
            _configManager.ActiveProfile.Nombre = newName;
            SaveActiveProfileToDisk();
        }

        /// <summary>
        /// Updates <see cref="ConfigManager.ModelProfile.MaxInputTokens"/> and
        /// serializes the change to the active profile JSON immediately.
        /// </summary>
        private void UpdateInputTokenLimit(int limit)
        {
            if (_configManager?.ActiveProfile == null) return;
            _configManager.ActiveProfile.MaxInputTokens = limit;
            SaveActiveProfileToDisk();
        }

        /// <summary>
        /// Updates <see cref="ConfigManager.ModelProfile.MaxOutputTokens"/> and
        /// serializes the change to the active profile JSON immediately.
        /// </summary>
        private void UpdateOutputTokenLimit(int limit)
        {
            if (_configManager?.ActiveProfile == null) return;
            _configManager.ActiveProfile.MaxOutputTokens = limit;
            SaveActiveProfileToDisk();
        }

        /// <summary>
        /// Serializes the current <see cref="ConfigManager.ActiveProfile"/> to its corresponding
        /// JSON manifest file at <see cref="ConfigManager.ActiveProfilePath"/>.
        /// Performs an early-out if either the profile or path is null/empty.
        /// </summary>
        private void SaveActiveProfileToDisk()
        {
            if (_configManager?.ActiveProfile == null) return;
            if (string.IsNullOrEmpty(_configManager.ActiveProfilePath)) return;

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_configManager.ActiveProfile, options);
                File.WriteAllText(_configManager.ActiveProfilePath, json);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SETTINGS] Error al guardar perfil en disco: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  PERFORMANCE HANDLERS
        // ─────────────────────────────────────────────────────────────

        private void UpdateCpuThreads(int threads)
        {
            if (_configManager == null) return;
            _configManager.PerformanceProfile.CpuThreads = threads;
            SaveAndRestartBackend();
        }

        private void UpdateGpuLayers(int layers)
        {
            if (_configManager == null) return;
            _configManager.PerformanceProfile.GpuLayers = layers;
            SaveAndRestartBackend();
        }

        private void UpdateRamSaturation(int saturationMB)
        {
            if (_configManager == null) return;
            _configManager.PerformanceProfile.RamSaturationCeilingMB = saturationMB;
            SaveAndRestartBackend();
        }

        // ─────────────────────────────────────────────────────────────
        //  TOOLS HANDLER
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Persists the updated MCP tool permission level (0 = Automatic, 1 = Ask First,
        /// 2 = Excluded) directly to <see cref="ConfigManager.ToolPermissions"/> and
        /// triggers an immediate configuration save.
        /// </summary>
        private void OnToolPermissionChanged(string toolName, int permissionLevel)
        {
            if (_configManager == null) return;
            _configManager.ToolPermissions[toolName] = permissionLevel;
            _configManager.SaveConfiguration();
            GD.Print($"[MCP] {toolName} → permission {permissionLevel}");
        }

        // ─────────────────────────────────────────────────────────────
        //  PREFERENCES HANDLER
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Toggles the application-wide theme by delegating to <see cref="ThemeManager.ObtenerTemaGlobal"/>.
        /// Immediately propagates the new <see cref="Theme"/> to the entire scene tree root
        /// and persists the preference to <see cref="ConfigManager"/>.
        /// </summary>
        private void OnDarkModeToggled(bool isPressed)
        {
            if (_configManager != null)
            {
                _configManager.DarkMode = isPressed;
                _configManager.SaveConfiguration();
            }

            if (ThemeManager.Instance != null)
            {
                GetTree().Root.Theme = ThemeManager.Instance.ObtenerTemaGlobal(isPressed);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  PRIVACY HANDLERS
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Iterates the configured history storage directory and removes all <c>.json</c>
        /// conversation files. Path is read from <see cref="StoragePathLineEdit"/> if available,
        /// falling back to <c>user://history/</c>.
        /// </summary>
        private void PurgeLocalData()
        {
            string historyPath = StoragePathLineEdit?.Text ?? "user://history/";
            if (!DirAccess.DirExistsAbsolute(historyPath)) return;

            using var dir = DirAccess.Open(historyPath);
            if (dir == null) return;

            dir.ListDirBegin();
            string fileName = dir.GetNext();
            while (fileName != string.Empty)
            {
                if (!dir.CurrentIsDir() && fileName.EndsWith(".json"))
                    dir.Remove(fileName);

                fileName = dir.GetNext();
            }

            GD.Print($"[SETTINGS] Purga completada: {historyPath}");
        }

        // ─────────────────────────────────────────────────────────────
        //  SHARED HELPERS
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Persists current <see cref="ConfigManager"/> state and, when in LocalHost mode,
        /// issues a graceful backend stop followed by a fresh boot cycle.
        /// </summary>
        private void SaveAndRestartBackend()
        {
            _configManager?.SaveConfiguration();

            if (_configManager?.CurrentMode == ConfigManager.AppMode.LocalHost
                && _backendLauncher != null)
            {
                GD.Print("[SETTINGS] Restarting local backend after configuration change.");
                _backendLauncher.StopBackend();
                // StartBackend is called after StopBackend completes its synchronous teardown.
                _backendLauncher.StartBackend();
            }
        }
    }
}
