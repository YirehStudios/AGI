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

        [ExportGroup("Navigation Icons")]
        /// <summary>Icon for the Models navigation category.</summary>
        [Export] public Texture2D ModelsIcon { get; set; }

        /// <summary>Icon for the Performance navigation category.</summary>
        [Export] public Texture2D PerformanceIcon { get; set; }

        /// <summary>Icon for the MCP Tools navigation category.</summary>
        [Export] public Texture2D ToolsIcon { get; set; }

        /// <summary>Icon for the Preferences navigation category.</summary>
        [Export] public Texture2D PreferencesIcon { get; set; }

        /// <summary>Icon for the Privacy navigation category.</summary>
        [Export] public Texture2D PrivacyIcon { get; set; }

        /// <summary>Icon for the Info button.</summary>
        [Export] public Texture2D InfoIcon { get; set; }

        /// <summary>Icon for the Update button.</summary>
        [Export] public Texture2D UpdateIcon { get; set; }

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

        /// <summary>
        /// Ventana de Contexto: combined token budget (input history + current turn).
        /// Range 2048–200000. Immediately persisted to <see cref="ConfigManager.ChatTemplate.ContextCeiling"/>.
        /// </summary>
        [Export] public SpinBox NumContextWindowSize { get; set; }

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
        /// Index of the currently active sidebar view (0–4).
        /// Tracked to avoid redundant re-renders when the user clicks the already-active button.
        /// </summary>
        private int _activeViewIndex = -1;

        /// <summary>
        /// Maps dropdown item index → absolute globalized path of the corresponding model JSON manifest.
        /// Populated exclusively by <see cref="CargarModelosEnMenu"/>.
        /// </summary>
        private readonly Dictionary<int, string> _rutasModelos = [];

        // ─────────────────────────────────────────────────────────────
        //  GODOT LIFECYCLE
        // ─────────────────────────────────────────────────────────────

        /// <inheritdoc/>
        public override void _Ready()
        {
            InitializeSystemLinks();
            ApplyStartupTheme();
            SetupIcons();
            ValidateExports();
            BindNavigationSignals();
            BindControlSignals();
            SubscribeTTSPipeline();
            LoadActiveConfiguration();

            // Defer the initial view switch by one frame.
            // This guarantees that QueueFree() calls issued during LoadActiveConfiguration()
            // (e.g., inside PopulateToolsGrid()) are fully resolved before we touch
            // visibility flags — preventing a deferred-deletion/layout re-entrancy freeze.
            CallDeferred(MethodName.SwitchActiveView, 0);
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
        /// Dynamically assigns the navigation icon resources specified via the inspector properties 
        /// directly onto the active texture parameters of their corresponding sidebar buttons.
        /// </summary>
        private void SetupIcons()
        {
            if (ModelsIcon != null && ModelsBtn != null) ModelsBtn.Icon = ModelsIcon;
            if (PerformanceIcon != null && PerformanceBtn != null) PerformanceBtn.Icon = PerformanceIcon;
            if (ToolsIcon != null && ToolsBtn != null) ToolsBtn.Icon = ToolsIcon;
            if (PreferencesIcon != null && PreferencesBtn != null) PreferencesBtn.Icon = PreferencesIcon;
            if (PrivacyIcon != null && PrivacyBtn != null) PrivacyBtn.Icon = PrivacyIcon;
            if (InfoIcon != null && InfoBtn != null) InfoBtn.Icon = InfoIcon;
            if (UpdateIcon != null && UpdateBtn != null) UpdateBtn.Icon = UpdateIcon;
        }

        /// <summary>
        /// Iterates through the children of the unique navigation sidebar node, purging the string text
        /// properties and scaling layout bounding fields to render a compact, icon-only control array.
        /// </summary>
        private void AdjustSidebarToIconOnly()
        {
            var sidebar = GetNodeOrNull<VBoxContainer>("%NavigationSidebar");
            if (sidebar == null) return;

            foreach (Node child in sidebar.GetChildren())
            {
                if (child is Button btn)
                {
                    btn.Text = string.Empty;
                    btn.CustomMinimumSize = new Vector2(48, 48);
                }
            }
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

        /// <summary>
        /// Scans all seven navigation Export properties and emits a <see cref="GD.PrintErr"/> for
        /// each one that is null. A null export means the NodePath in the scene was not wired,
        /// which is the most common source of silent navigation failures.
        /// </summary>
        private void ValidateExports()
        {
            if (ModelsBtn             == null) GD.PrintErr("[SETTINGS] Export not wired: ModelsBtn");
            if (PerformanceBtn        == null) GD.PrintErr("[SETTINGS] Export not wired: PerformanceBtn");
            if (ToolsBtn              == null) GD.PrintErr("[SETTINGS] Export not wired: ToolsBtn");
            if (PreferencesBtn        == null) GD.PrintErr("[SETTINGS] Export not wired: PreferencesBtn");
            if (PrivacyBtn            == null) GD.PrintErr("[SETTINGS] Export not wired: PrivacyBtn");
            if (ModelsViewContainer      == null) GD.PrintErr("[SETTINGS] Export not wired: ModelsViewContainer");
            if (PerformanceViewContainer == null) GD.PrintErr("[SETTINGS] Export not wired: PerformanceViewContainer");
            if (ToolsViewContainer       == null) GD.PrintErr("[SETTINGS] Export not wired: ToolsViewContainer");
            if (PreferencesViewContainer == null) GD.PrintErr("[SETTINGS] Export not wired: PreferencesViewContainer");
            if (PrivacyViewContainer     == null) GD.PrintErr("[SETTINGS] Export not wired: PrivacyViewContainer");
        }

        // ─────────────────────────────────────────────────────────────
        //  NAVIGATION
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Connects every sidebar navigation button to the <see cref="SwitchActiveView"/> dispatcher.
        /// Uses index-captured lambdas so each button closure captures its own immutable integer.
        /// </summary>
        private void BindNavigationSignals()
        {
            // Primary navigation — index is captured by value in each lambda.
            if (ModelsBtn      != null) ModelsBtn.Pressed      += () => SwitchActiveView(0);
            if (PerformanceBtn != null) PerformanceBtn.Pressed += () => SwitchActiveView(1);
            if (ToolsBtn       != null) ToolsBtn.Pressed       += () => SwitchActiveView(2);
            if (PreferencesBtn != null) PreferencesBtn.Pressed += () => SwitchActiveView(3);
            if (PrivacyBtn     != null) PrivacyBtn.Pressed     += () => SwitchActiveView(4);

            // System buttons.
            if (InfoBtn   != null) InfoBtn.Pressed   += ShowInfoPanel;
            // UpdateBtn has no pressed handler by design — it is shown programmatically
            // by the version-check subsystem when a footprint mismatch is detected.
        }

        /// <summary>
        /// Mutually-exclusive view switcher. Iterates the full container array unconditionally
        /// so every container receives an explicit Visible assignment on every call —
        /// preventing the partial-visible state that caused the PrivacyBtn soft-lock.
        /// </summary>
        /// <remarks>
        /// CONTRACT:
        /// - Array indices map 1-to-1 to button indices: 0=Models, 1=Performance, 2=Tools,
        ///   3=Preferences, 4=Privacy.
        /// - Every container's MouseFilter is forced to Ignore on every call, ensuring that
        ///   invisible containers can never intercept mouse events from the sidebar buttons.
        /// - Early-out if <paramref name="viewIndex"/> equals <see cref="_activeViewIndex"/>.
        /// </remarks>
        /// <param name="viewIndex">Target view index in [0, 4].</param>
        private void SwitchActiveView(int viewIndex)
        {
            if (viewIndex == _activeViewIndex) return;
            _activeViewIndex = viewIndex;

            // Build the dispatch table inline — zero allocations on repeated calls
            // because the array only lives on the stack within this frame.
            Container[] views =
            [
                ModelsViewContainer,
                PerformanceViewContainer,
                ToolsViewContainer,
                PreferencesViewContainer,
                PrivacyViewContainer,
            ];

            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] == null) continue;
                // Explicit unconditional assignment — no short-circuit, no partial state.
                views[i].Visible     = (i == viewIndex);
                views[i].MouseFilter = MouseFilterEnum.Ignore;
            }

            GD.Print($"[SETTINGS] SwitchActiveView → {viewIndex}");
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
            if (NumContextWindowSize  != null) NumContextWindowSize.ValueChanged  += v => UpdateContextWindow((int)v);

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
            CpuThreadsSpinBox?.SetValueNoSignal(_configManager.PerformanceProfile.CpuThreads);
            GpuLayersSpinBox?.SetValueNoSignal(_configManager.PerformanceProfile.GpuLayers);
            RamSaturationSpinBox?.SetValueNoSignal(_configManager.PerformanceProfile.RamSaturationCeilingMB);

            // ── Preferences ─────────────────────────────────────────
            bool isDark = _configManager.DarkMode;
            DarkModeToggle?.SetPressedNoSignal(_configManager.DarkMode);
            
            if (Material is ShaderMaterial glassMat)
            {
                Color blendColor = isDark ? new Color(0.06f, 0.06f, 0.09f, 0.45f) : new Color(0.95f, 0.95f, 0.98f, 0.30f);
                glassMat.SetShaderParameter("mix_color", blendColor);
                glassMat.SetShaderParameter("blur_amount", isDark ? 2.0f : 1.5f);
            }
            UpdateThemeCornerRadius(isDark ? 16 : 8);

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

            NumInputTokensLimit?.SetValueNoSignal(profile.MaxInputTokens > 0 ? profile.MaxInputTokens : 4096);

            NumOutputTokensLimit?.SetValueNoSignal(profile.MaxOutputTokens > 0 ? profile.MaxOutputTokens : 2048);

            if (profile.Template != null)
                NumContextWindowSize?.SetValueNoSignal(profile.Template.ContextCeiling > 0 ? profile.Template.ContextCeiling : 4096);
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
        /// The tool list is sourced verbatim from the Python backend's <c>/call_tool</c> dispatch
        /// table in <c>mcp_server.py</c> — any name not present there will silently fail at
        /// runtime, so this list must be kept in sync with the backend.
        /// </summary>
        /// <remarks>
        /// Permission levels:
        ///   0 = Automatic  — the agent executes without user interruption.
        ///   1 = Ask First  — the agent pauses and the user must approve each call.
        ///   2 = Excluded   — the tool is stripped from the MCP schema sent to the LLM.
        ///
        /// Destructive tools (marked ⚠) default to "Ask First" on first run when no
        /// saved preference exists, protecting the user from unintentional mutations.
        /// </remarks>
        private void PopulateToolsGrid()
        {
            if (ToolsGrid == null) return;
            foreach (Node child in ToolsGrid.GetChildren()) child.QueueFree();

            // ── Clean Canonical Registry (Human-readable only) ──────────────────
            // Technical prefixes removed to prevent layout collapse.
            (string Key, string Label, bool Destructive)[] toolDefinitions =
            [
                // ── Network ─────────────────────────────────────────────────────
                ("web_search",             "Búsqueda Web",                        false),
                ("fetch_url_content",      "Leer URL Externa",                    false),

                // ── Filesystem: Read ────────────────────────────────────────────
                ("read_file",              "Leer Archivo",                        false),
                ("read_multiple_files",    "Leer Múltiples Archivos",             false),
                ("file_glob_search",       "Búsqueda Glob de Archivos",            false),
                ("grep_search",            "Búsqueda Regex en Directorio",        false),
                ("ls",                     "Listar Directorio",                   false),

                // ── Filesystem: Write ───────────────────────────────────────────
                ("create_new_file",        "⚠ Crear Archivo",                     true),
                ("create_directory",       "⚠ Crear Directorio",                  true),
                ("edit_existing_file",     "⚠ Sobreescribir Archivo",             true),
                ("single_find_and_replace","⚠ Reemplazar en Archivo",             true),

                // ── Filesystem: Destructive ─────────────────────────────────────
                ("delete_file",            "⚠ Eliminar Archivo",                  true),
                ("rename_file",            "⚠ Mover/Renombrar Archivo",           true),

                // ── System ──────────────────────────────────────────────────────
                ("os_command",             "⚠ Ejecutar Comando del Sistema",      true),
            ];

            foreach (var (key, label, isDestructive) in toolDefinitions)
            {
                HBoxContainer row = new();
                row.AddThemeConstantOverride("separation", 12);

                Label nameLabel = new()
                {
                    Text                = label,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill
                };

                // Aesthetic amber tint for destructive tools
                // Tint para herramientas destructivas adaptable al tema actual
                if (isDestructive)
                {
                    bool isCurrentlyDark = DarkModeToggle != null && DarkModeToggle.ButtonPressed;
                    
                    // Si el DarkModeToggle está presionado, usa amarillo brillante; si no, un ámbar oscuro legible
                    Color destructiveColor = (DarkModeToggle != null && DarkModeToggle.ButtonPressed) 
                        ? new Color(1.0f, 0.78f, 0.22f)  // Amarillo brillante (Modo Noche)
                        : new Color(0.72f, 0.43f, 0.0f);  // Ámbar quemado/Marrón dorado (Modo Día)
                        
                    nameLabel.AddThemeColorOverride("font_color", destructiveColor);
                }

                OptionButton permSelector = new();
                permSelector.AddItem("Automático",   0);
                permSelector.AddItem("Preguntar",    1);
                permSelector.AddItem("Excluir",      2);

                int defaultPerm = isDestructive ? 1 : 0;
                int savedPermission = _configManager != null
                    && _configManager.ToolPermissions.TryGetValue(key, out int p) ? p : defaultPerm;
                permSelector.Select(savedPermission);

                if (_configManager != null && !_configManager.ToolPermissions.ContainsKey(key))
                    OnToolPermissionChanged(key, savedPermission);

                string capturedKey = key;
                permSelector.ItemSelected += idx => OnToolPermissionChanged(capturedKey, (int)idx);

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

        /// <summary>
        /// Updates the context ceiling field in the active profile's <see cref="ConfigManager.ChatTemplate"/>
        /// and immediately serializes the change to disk — no backend restart needed for this parameter.
        /// </summary>
        private void UpdateContextWindow(int tokens)
        {
            if (_configManager?.ActiveProfile?.Template == null) return;
            _configManager.ActiveProfile.Template.ContextCeiling = tokens;
            SaveActiveProfileToDisk();
        }

        // ─────────────────────────────────────────────────────────────
        //  TTS PIPELINE SUBSCRIPTION (Phase 5)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Subscribes to the <see cref="Logic.Lite.ChatManager.OnBotFinishedSpeaking"/> signal so that
        /// every completed bot response is routed through the Kokoro WebSocket TTS pipeline in
        /// <see cref="NetworkManager.RequestTTSWebSocket"/>.
        /// The subscription lives in Settings because ChatbotMain is a pure messaging view with no audio coupling.
        /// </summary>
        private void SubscribeTTSPipeline()
        {
            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager != null)
                chatManager.OnBotFinishedSpeaking += OnBotFinishedSpeakingTTS;
        }

        /// <summary>
        /// Receives the final cleaned bot response and asynchronously dispatches it to the
        /// Kokoro ONNX TTS WebSocket server at <c>ws://127.0.0.1:8888</c>.
        /// The raw WAV bytes returned by the server are handled downstream by
        /// <see cref="NativeTTSManager"/> via <c>TTSAudioChunkReceived</c>.
        /// </summary>
        private async void OnBotFinishedSpeakingTTS(string spokenText)
        {
            if (string.IsNullOrWhiteSpace(spokenText)) return;
            if (_networkManager == null) return;

            try
            {
                await _networkManager.RequestTTSWebSocket(spokenText);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SETTINGS/TTS] WebSocket dispatch failed: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public override void _ExitTree()
        {
            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager != null)
                chatManager.OnBotFinishedSpeaking -= OnBotFinishedSpeakingTTS;
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
        /// Immediately propagates the new <see cref="Theme"/> to the entire scene tree root, updates
        /// real-time glass shader uniform parameters, and transitions the frame corner radius configuration.
        /// </summary>
        /// <param name="isPressed">Indicates whether dark mode is currently active.</param>
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

            if (Material is ShaderMaterial glassMat)
            {
                Color blendColor = isPressed ? new Color(0.06f, 0.06f, 0.09f, 0.45f) : new Color(0.95f, 0.95f, 0.98f, 0.30f);
                glassMat.SetShaderParameter("mix_color", blendColor);
                glassMat.SetShaderParameter("blur_amount", isPressed ? 2.0f : 1.5f);
            }

            UpdateThemeCornerRadius(isPressed ? 16 : 8);
            PopulateToolsGrid();
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


        /// <summary>
        /// Locates the active flat StyleBox architecture belonging to the target panel control 
        /// and updates the numerical corner rounding values uniformly across all edges.
        /// </summary>
        /// <param name="radius">The precise perimeter border radius layout dimension in pixels.</param>
        public void UpdateThemeCornerRadius(int radius)
        {
            if (GetThemeStylebox("panel") is StyleBoxFlat panelStyle)
            {
                panelStyle.SetCornerRadiusAll(radius);
            }
        }
    }
}
