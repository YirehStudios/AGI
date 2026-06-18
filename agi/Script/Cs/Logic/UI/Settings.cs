using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        [Export] public Button WorkspacesBtn { get; set; }
        /// <summary>Navigation button for the Privacy view.</summary>
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
        [Export] public Texture2D WorkspacesIcon { get; set; }
        /// <summary>Icon for the Privacy category.</summary>
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
        [Export] public Container WorkspacesViewContainer { get; set; }
        /// <summary>Container rendered when the Privacy category is active.</summary>
        [Export] public Container PrivacyViewContainer { get; set; }

        // ─────────────────────────────────────────────────────────────
        //  MODELS CONTROLS
        // ─────────────────────────────────────────────────────────────

        [ExportGroup("Models Controls")]
        /// <summary>Dropdown populated from user://models/ directory JSON manifests.</summary>
        [Export] public OptionButton ActiveModelSelector { get; set; }
        
        [Export] public OptionButton ActiveImageModelSelector { get; set; }
        [Export] public OptionButton ActiveVideoModelSelector { get; set; }

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
        
        // Modular specific profiles for future expansion
        [Export] public SpinBox LlmCpuThreadsSpinBox { get; set; }
        [Export] public SpinBox LlmGpuLayersSpinBox { get; set; }
        [Export] public SpinBox ImageCpuThreadsSpinBox { get; set; }
        [Export] public SpinBox VideoCpuThreadsSpinBox { get; set; }
        [Export] public SpinBox WhisperCpuThreadsSpinBox { get; set; }
        [Export] public SpinBox PyScriptsCpuThreadsSpinBox { get; set; }

        /// <summary>Selector for the hardware accelerator to use for Llama, Whisper and ONNX.</summary>
        [Export] public OptionButton GpuSelector { get; set; }

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
        public CheckButton TransModeToggle { get; set; }
        public HSlider TransBlurSlider { get; set; }
        public HSlider TransOpacitySlider { get; set; }
        public CheckButton TransPopupsToggle { get; set; }
        public HSlider TransPopupsBlurSlider { get; set; }
        public HSlider TransPopupsOpacitySlider { get; set; }
        public CheckButton TransSubWindowsToggle { get; set; }
        public HSlider TransSubWindowsBlurSlider { get; set; }
        public HSlider TransSubWindowsOpacitySlider { get; set; }
        [Export] public Container WorkspaceListContainer { get; set; }
        [Export] public Container WorkspaceItemTemplate { get; set; }

        /// <summary>Trigger button that initiates the workspace directory selection dialog.</summary>
        [Export] public Button WorkspaceBrowseBtn { get; set; }

        /// <summary>Native FileDialog for selecting the workspace.</summary>
        [Export] public FileDialog WorkspaceFileDialog { get; set; }

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
        private Logic.Lite.ChatManager _chatManager;
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
            _chatManager     = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");

            if (_configManager == null)
                GD.PrintErr("[SETTINGS] ConfigManager dependency missing.");
            if (_backendLauncher == null)
                GD.PrintErr("[SETTINGS] BackendLauncher dependency missing.");

            if (GpuSelector == null)
            {
                GpuSelector = GetNodeOrNull<OptionButton>("%GpuSelector");
                if (GpuSelector == null)
                    GD.PrintErr("[SETTINGS] Fallback: No se pudo enlazar %GpuSelector dinámicamente.");
            }

            TransModeToggle = GetNodeOrNull<CheckButton>("%TransModeToggle");
            TransBlurSlider = GetNodeOrNull<HSlider>("%TransBlurSlider");
            TransOpacitySlider = GetNodeOrNull<HSlider>("%TransOpacitySlider");
            TransPopupsToggle = GetNodeOrNull<CheckButton>("%TransPopupsToggle");
            TransPopupsBlurSlider = GetNodeOrNull<HSlider>("%TransPopupsBlurSlider");
            TransPopupsOpacitySlider = GetNodeOrNull<HSlider>("%TransPopupsOpacitySlider");
            TransSubWindowsToggle = GetNodeOrNull<CheckButton>("%TransSubWindowsToggle");
            TransSubWindowsBlurSlider = GetNodeOrNull<HSlider>("%TransSubWindowsBlurSlider");
            TransSubWindowsOpacitySlider = GetNodeOrNull<HSlider>("%TransSubWindowsOpacitySlider");
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
            if (WorkspacesIcon != null && WorkspacesBtn != null) WorkspacesBtn.Icon = WorkspacesIcon;
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
            if (WorkspacesBtn         == null) GD.PrintErr("[SETTINGS] Export not wired: WorkspacesBtn");
            if (PrivacyBtn            == null) GD.PrintErr("[SETTINGS] Export not wired: PrivacyBtn");
            if (ModelsViewContainer      == null) GD.PrintErr("[SETTINGS] Export not wired: ModelsViewContainer");
            if (PerformanceViewContainer == null) GD.PrintErr("[SETTINGS] Export not wired: PerformanceViewContainer");
            if (ToolsViewContainer       == null) GD.PrintErr("[SETTINGS] Export not wired: ToolsViewContainer");
            if (PreferencesViewContainer == null) GD.PrintErr("[SETTINGS] Export not wired: PreferencesViewContainer");
            if (WorkspacesViewContainer  == null) GD.PrintErr("[SETTINGS] Export not wired: WorkspacesViewContainer");
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
            if (WorkspacesBtn  != null) WorkspacesBtn.Pressed  += () => SwitchActiveView(4);
            if (PrivacyBtn     != null) PrivacyBtn.Pressed     += () => SwitchActiveView(5);

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
                WorkspacesViewContainer,
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
            if (ActiveImageModelSelector != null) ActiveImageModelSelector.ItemSelected += OnImageModelSelected;
            if (ActiveVideoModelSelector != null) ActiveVideoModelSelector.ItemSelected += OnVideoModelSelected;
            if (TxtIaDisplayNameInput != null) TxtIaDisplayNameInput.TextChanged  += OnDisplayNameChanged;
            if (NumInputTokensLimit   != null) NumInputTokensLimit.ValueChanged   += v => UpdateInputTokenLimit((int)v);
            if (NumOutputTokensLimit  != null) NumOutputTokensLimit.ValueChanged  += v => UpdateOutputTokenLimit((int)v);
            if (NumContextWindowSize != null) NumContextWindowSize.ValueChanged += (val) =>
            {
                // We only mark unsaved if it differs
            };

            if (WeightImportBtn != null)
            {
                var modelsDialog = GetNodeOrNull<FileDialog>("%ModelsFileDialog");
                if (modelsDialog != null)
                {
                    WeightImportBtn.Pressed += () => modelsDialog.PopupCentered(new Vector2I(800, 600));
                    modelsDialog.FileSelected += (path) =>
                    {
                        if (WeightImportLineEdit != null) WeightImportLineEdit.Text = path;
                    };
                }
            }
            // ── Performance ─────────────────────────────────────────
            if (CpuThreadsSpinBox   != null) CpuThreadsSpinBox.ValueChanged   += v => UpdateCpuThreads((int)v, "Llm");
            if (GpuLayersSpinBox    != null) GpuLayersSpinBox.ValueChanged    += v => UpdateGpuLayers((int)v, "Llm");
            
            if (LlmCpuThreadsSpinBox != null) LlmCpuThreadsSpinBox.ValueChanged += v => UpdateCpuThreads((int)v, "Llm");
            if (LlmGpuLayersSpinBox != null) LlmGpuLayersSpinBox.ValueChanged += v => UpdateGpuLayers((int)v, "Llm");
            if (ImageCpuThreadsSpinBox != null) ImageCpuThreadsSpinBox.ValueChanged += v => UpdateCpuThreads((int)v, "Image");
            if (VideoCpuThreadsSpinBox != null) VideoCpuThreadsSpinBox.ValueChanged += v => UpdateCpuThreads((int)v, "Video");
            if (WhisperCpuThreadsSpinBox != null) WhisperCpuThreadsSpinBox.ValueChanged += v => UpdateCpuThreads((int)v, "Whisper");
            if (PyScriptsCpuThreadsSpinBox != null) PyScriptsCpuThreadsSpinBox.ValueChanged += v => UpdateCpuThreads((int)v, "PyScripts");
            if (RamSaturationSpinBox!= null) RamSaturationSpinBox.ValueChanged += v => UpdateRamSaturation((int)v);
            if (GpuSelector         != null) GpuSelector.ItemSelected         += OnGpuSelected;

            // ── Preferences ─────────────────────────────────────────
            if (DarkModeToggle != null) DarkModeToggle.Toggled += OnDarkModeToggled;
            
            if (TransModeToggle != null) TransModeToggle.Toggled += OnTransModeToggled;
            if (TransBlurSlider != null) TransBlurSlider.DragEnded += (v) => OnTransBlurChanged(TransBlurSlider.Value);
            if (TransOpacitySlider != null) TransOpacitySlider.DragEnded += (v) => OnTransOpacityChanged(TransOpacitySlider.Value);
            if (TransPopupsToggle != null) TransPopupsToggle.Toggled += OnTransPopupsToggled;
            if (TransPopupsBlurSlider != null) TransPopupsBlurSlider.DragEnded += (v) => OnTransPopupsBlurChanged(TransPopupsBlurSlider.Value);
            if (TransPopupsOpacitySlider != null) TransPopupsOpacitySlider.DragEnded += (v) => OnTransPopupsOpacityChanged(TransPopupsOpacitySlider.Value);
            if (TransSubWindowsToggle != null) TransSubWindowsToggle.Toggled += OnTransSubWindowsToggled;
            if (TransSubWindowsBlurSlider != null) TransSubWindowsBlurSlider.DragEnded += (v) => OnTransSubWindowsBlurChanged(TransSubWindowsBlurSlider.Value);
            if (TransSubWindowsOpacitySlider != null) TransSubWindowsOpacitySlider.DragEnded += (v) => OnTransSubWindowsOpacityChanged(TransSubWindowsOpacitySlider.Value);

            if (WorkspaceBrowseBtn != null && WorkspaceFileDialog != null)
            {
                WorkspaceBrowseBtn.Pressed += () => WorkspaceFileDialog.PopupCentered(new Vector2I(800, 600));
                WorkspaceFileDialog.DirSelected += OnWorkspaceDirSelected;
            }

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
            CpuThreadsSpinBox?.SetValueNoSignal(_configManager.PerformanceProfile.Llm.CpuThreads);
            GpuLayersSpinBox?.SetValueNoSignal(_configManager.PerformanceProfile.Llm.GpuLayers);
            
            LlmCpuThreadsSpinBox?.SetValueNoSignal(_configManager.PerformanceProfile.Llm.CpuThreads);
            LlmGpuLayersSpinBox?.SetValueNoSignal(_configManager.PerformanceProfile.Llm.GpuLayers);
            ImageCpuThreadsSpinBox?.SetValueNoSignal(_configManager.PerformanceProfile.Image.CpuThreads);
            VideoCpuThreadsSpinBox?.SetValueNoSignal(_configManager.PerformanceProfile.Video.CpuThreads);
            WhisperCpuThreadsSpinBox?.SetValueNoSignal(_configManager.PerformanceProfile.Whisper.CpuThreads);
            PyScriptsCpuThreadsSpinBox?.SetValueNoSignal(_configManager.PerformanceProfile.PyScripts.CpuThreads);
            RamSaturationSpinBox?.SetValueNoSignal(_configManager.PerformanceProfile.RamSaturationCeilingMB);

            // ── Preferences ─────────────────────────────────────────
            bool isDark = _configManager.DarkMode;
            DarkModeToggle?.SetPressedNoSignal(_configManager.DarkMode);
            TransModeToggle?.SetPressedNoSignal(_configManager.TransModeEnabled);
            TransBlurSlider?.SetValueNoSignal(_configManager.TransModeBlur);
            TransOpacitySlider?.SetValueNoSignal(_configManager.TransModeOpacity);
            TransPopupsToggle?.SetPressedNoSignal(_configManager.TransModeApplyToPopups);
            TransPopupsBlurSlider?.SetValueNoSignal(_configManager.TransModePopupsBlur);
            TransPopupsOpacitySlider?.SetValueNoSignal(_configManager.TransModePopupsOpacity);
            TransSubWindowsToggle?.SetPressedNoSignal(_configManager.TransModeApplyToSubWindows);
            TransSubWindowsBlurSlider?.SetValueNoSignal(_configManager.TransModeSubWindowsBlur);
            TransSubWindowsOpacitySlider?.SetValueNoSignal(_configManager.TransModeSubWindowsOpacity);
            
            RefreshWorkspaceUI();
            
            Material = null;
            
            ThemeManager.Instance?.ApplyTransMode();
            UpdateThemeCornerRadius(isDark ? 16 : 8);
            UpdateTransSlidersVisibility();

            // ── Performance (Dynamic Population) ────────────────────
            PopulateGpuSelector();

            // ── Models ──────────────────────────────────────────────
            CargarModelosEnMenu();
            CargarModelosVisualesEnMenu();
            SyncModelFieldsFromActiveProfile();

            // ── Tools ───────────────────────────────────────────────
            PopulateToolsGrid();
        }

        /// <summary>
        /// Pushes values from the currently-loaded <see cref="ConfigManager.ActiveProfile"/>
        /// into the Models view controls without triggering reactive writes back to disk.
        /// </summary>
        public void RefreshWorkspaceUI()
        {
            if (_chatManager == null || _chatManager.CurrentSession == null || WorkspaceListContainer == null || WorkspaceItemTemplate == null) return;

            foreach (Node child in WorkspaceListContainer.GetChildren())
            {
                if (child != WorkspaceItemTemplate)
                {
                    child.QueueFree();
                }
            }

            foreach (var ws in _chatManager.CurrentSession.Workspaces.ToList())
            {
                var row = WorkspaceItemTemplate.Duplicate() as Container;
                row.Visible = true;
                
                var lineEdit = row.GetNode<LineEdit>("PathEdit");
                bool isActive = !ws.StartsWith("!");
                string cleanPath = isActive ? ws : ws.Substring(1);

                if (lineEdit != null)
                {
                    lineEdit.Text = cleanPath;
                }

                var deactivateToggle = row.GetNode<CheckBox>("DeactivateToggle");
                if (deactivateToggle != null)
                {
                    deactivateToggle.SetPressedNoSignal(isActive);
                    deactivateToggle.Toggled += (isPressed) => {
                        int index = _chatManager.CurrentSession.Workspaces.IndexOf(ws);
                        if (index != -1)
                        {
                            _chatManager.CurrentSession.Workspaces[index] = isPressed ? cleanPath : "!" + cleanPath;
                            _chatManager.SaveSession();
                            RefreshWorkspaceUI();
                        }
                    };
                }
                
                var delBtn = row.GetNode<Button>("DeleteBtn");
                if (delBtn != null)
                {
                    delBtn.Pressed += () => {
                        _chatManager.CurrentSession.Workspaces.Remove(ws);
                        _chatManager.SaveSession();
                        RefreshWorkspaceUI();
                    };
                }
                
                WorkspaceListContainer.AddChild(row);
            }
        }

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
            if (ActiveImageModelSelector != null) ActiveImageModelSelector.Clear();
            if (ActiveVideoModelSelector != null) ActiveVideoModelSelector.Clear();

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

            int textIndex = 0;
            int imageIndex = 0;
            int videoIndex = 0;

            while (fileName != string.Empty)
            {
                if (!dir.CurrentIsDir() && fileName.EndsWith("_profile.json"))
                {
                    string fullPath = $"{rutaModelos}/{fileName}";
                    try 
                    {
                        string raw = global::System.IO.File.ReadAllText(ProjectSettings.GlobalizePath(fullPath));
                        var profile = JsonSerializer.Deserialize<ConfigManager.ModelProfile>(raw);
                        string displayName = !string.IsNullOrEmpty(profile?.Nombre) ? profile.Nombre : fileName;
                        
                        string cat = profile?.Category ?? "LLM";

                        if (cat == "Image")
                        {
                            if (ActiveImageModelSelector != null)
                            {
                                ActiveImageModelSelector.AddItem(displayName);
                                if (displayName == _configManager?.ActiveImageModel) ActiveImageModelSelector.Select(imageIndex);
                                imageIndex++;
                            }
                        }
                        else if (cat == "Video")
                        {
                            if (ActiveVideoModelSelector != null)
                            {
                                ActiveVideoModelSelector.AddItem(displayName);
                                if (displayName == _configManager?.ActiveVideoModel) ActiveVideoModelSelector.Select(videoIndex);
                                videoIndex++;
                            }
                        }
                        else
                        {
                            ActiveModelSelector.AddItem(displayName);
                            _rutasModelos[textIndex] = fullPath;
                            textIndex++;
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore invalid profiles
                    }
                }
                fileName = dir.GetNext();
            }

            if (ActiveModelSelector.ItemCount == 0)
            {
                ActiveModelSelector.AddItem("Sin modelos instalados", 0);
                ActiveModelSelector.Disabled = true;
            }
            else
            {
                ActiveModelSelector.Disabled = false;
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

            if (ActiveImageModelSelector != null)
            {
                if (ActiveImageModelSelector.ItemCount == 0)
                {
                    ActiveImageModelSelector.AddItem("Sin modelos de imagen", 0);
                    ActiveImageModelSelector.Disabled = true;
                }
                else ActiveImageModelSelector.Disabled = false;
            }

            if (ActiveVideoModelSelector != null)
            {
                if (ActiveVideoModelSelector.ItemCount == 0)
                {
                    ActiveVideoModelSelector.AddItem("Sin modelos de video", 0);
                    ActiveVideoModelSelector.Disabled = true;
                }
                else ActiveVideoModelSelector.Disabled = false;
            }
        }

        private async void CargarModelosVisualesEnMenu()
        {
            // Migrated to CargarModelosEnMenu to strictly load locally installed profiles
        }

        private void OnImageModelSelected(long idx)
        {
            if (ActiveImageModelSelector == null || _configManager == null) return;
            string selectedName = ActiveImageModelSelector.GetItemText((int)idx);
            _configManager.ActiveImageModel = selectedName;
            _configManager.SaveConfiguration();
        }

        private void OnVideoModelSelected(long idx)
        {
            if (ActiveVideoModelSelector == null || _configManager == null) return;
            string selectedName = ActiveVideoModelSelector.GetItemText((int)idx);
            _configManager.ActiveVideoModel = selectedName;
            _configManager.SaveConfiguration();
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

        private void UpdateCpuThreads(int threads, string engine)
        {
            if (_configManager == null) return;
            switch(engine) {
                case "Llm": _configManager.PerformanceProfile.Llm.CpuThreads = threads; break;
                case "Image": _configManager.PerformanceProfile.Image.CpuThreads = threads; break;
                case "Video": _configManager.PerformanceProfile.Video.CpuThreads = threads; break;
                case "Whisper": _configManager.PerformanceProfile.Whisper.CpuThreads = threads; break;
                case "PyScripts": _configManager.PerformanceProfile.PyScripts.CpuThreads = threads; break;
            }
            SaveAndRestartBackend();
        }

        private void UpdateGpuLayers(int layers, string engine)
        {
            if (_configManager == null) return;
            switch(engine) {
                case "Llm": _configManager.PerformanceProfile.Llm.GpuLayers = layers; break;
                case "Image": _configManager.PerformanceProfile.Image.GpuLayers = layers; break;
                case "Video": _configManager.PerformanceProfile.Video.GpuLayers = layers; break;
                case "Whisper": _configManager.PerformanceProfile.Whisper.GpuLayers = layers; break;
                case "PyScripts": _configManager.PerformanceProfile.PyScripts.GpuLayers = layers; break;
            }
            SaveAndRestartBackend();
        }

        private void UpdateRamSaturation(int saturationMB)
        {
            if (_configManager == null) return;
            _configManager.PerformanceProfile.RamSaturationCeilingMB = saturationMB;
            SaveAndRestartBackend();
        }

        private void OnGpuSelected(long index)
        {
            if (_configManager == null) return;
            
            // Item 0 is usually CPU (-1). Real GPUs start at item 1 (index 0).
            int gpuId = (int)index - 1;
            _configManager.SelectedGpuIndex = gpuId;
            _configManager.SaveConfiguration();
            SaveAndRestartBackend();
        }

        private void PopulateGpuSelector()
        {
            if (GpuSelector == null) return;

            GpuSelector.Clear();
            GpuSelector.AddItem("Procesador Central (CPU) - Seguro");

            try
            {
                var output = new Godot.Collections.Array();
                int result = OS.Execute("nvidia-smi", new string[] { "--query-gpu=index,name", "--format=csv,noheader" }, output, true);
                
                if (result == 0 && output.Count > 0)
                {
                    string stdout = output[0].ToString();
                    string[] lines = stdout.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            string gpuName = parts[1].Trim();
                            GpuSelector.AddItem($"GPU: {gpuName}");
                        }
                    }
                }
                else
                {
                    // Fallback to Vulkan query or generic names
                    // This could be expanded if non-NVIDIA GPUs are targeted
                    GD.Print("[SETTINGS] No se detectó nvidia-smi o falló. Mostrando opciones genéricas.");
                    GpuSelector.AddItem("Acelerador Primario (GPU 0)");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SETTINGS] Error listando GPUs: {ex.Message}");
                GpuSelector.AddItem("Acelerador Primario (GPU 0)");
            }

            int selectedIndex = 0;
            if (_configManager != null && _configManager.SelectedGpuIndex >= 0)
            {
                selectedIndex = _configManager.SelectedGpuIndex + 1;
            }

            if (selectedIndex < GpuSelector.ItemCount)
            {
                GpuSelector.Select(selectedIndex);
            }
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
        private void OnWorkspaceDirSelected(string dirPath)
        {
            if (_chatManager == null || _chatManager.CurrentSession == null) return;
            
            if (!_chatManager.CurrentSession.Workspaces.Contains(dirPath))
            {
                _chatManager.CurrentSession.Workspaces.Add(dirPath);
            }
            
            _chatManager.SaveSession();
            RefreshWorkspaceUI();
        }

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
                ThemeManager.Instance.ApplyTransMode();
            }

            // Notify MainApp to update the active view/Chatbot theme dynamically
            var mainApp = GetTree().Root.FindChild("MainApp", true, false) as MainApp;
            if (mainApp != null)
            {
                mainApp.SetThemeMode(isPressed);
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

        private void OnTransModeToggled(bool isPressed)
        {
            if (_configManager != null) { _configManager.TransModeEnabled = isPressed; _configManager.SaveConfiguration(); }
            ThemeManager.Instance?.ApplyTransMode();
            UpdateTransSlidersVisibility();
        }

        private void OnTransBlurChanged(double value)
        {
            if (_configManager != null) { _configManager.TransModeBlur = (float)value; _configManager.SaveConfiguration(); }
            ThemeManager.Instance?.ApplyTransMode();
        }

        private void OnTransOpacityChanged(double value)
        {
            if (_configManager != null) { _configManager.TransModeOpacity = (float)value; _configManager.SaveConfiguration(); }
            ThemeManager.Instance?.ApplyTransMode();
        }

        private void OnTransPopupsToggled(bool isPressed)
        {
            if (_configManager != null) { _configManager.TransModeApplyToPopups = isPressed; _configManager.SaveConfiguration(); }
            ThemeManager.Instance?.ApplyTransMode();
            UpdateTransSlidersVisibility();
        }

        private void OnTransPopupsBlurChanged(double value)
        {
            if (_configManager != null) { _configManager.TransModePopupsBlur = (float)value; _configManager.SaveConfiguration(); }
            ThemeManager.Instance?.ApplyTransMode();
        }

        private void OnTransPopupsOpacityChanged(double value)
        {
            if (_configManager != null) { _configManager.TransModePopupsOpacity = (float)value; _configManager.SaveConfiguration(); }
            ThemeManager.Instance?.ApplyTransMode();
        }

        private void OnTransSubWindowsToggled(bool isPressed)
        {
            if (_configManager != null) { _configManager.TransModeApplyToSubWindows = isPressed; _configManager.SaveConfiguration(); }
            ThemeManager.Instance?.ApplyTransMode();
            UpdateTransSlidersVisibility();
        }

        private void OnTransSubWindowsBlurChanged(double value)
        {
            if (_configManager != null) { _configManager.TransModeSubWindowsBlur = (float)value; _configManager.SaveConfiguration(); }
            ThemeManager.Instance?.ApplyTransMode();
        }

        private void OnTransSubWindowsOpacityChanged(double value)
        {
            if (_configManager != null) { _configManager.TransModeSubWindowsOpacity = (float)value; _configManager.SaveConfiguration(); }
            ThemeManager.Instance?.ApplyTransMode();
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

        private void UpdateTransSlidersVisibility()
        {
            if (_configManager == null) return;

            // Detección del Gestor de Ventanas:
            // KWin (Linux) y DWM (Windows 11) gestionan el radio de desenfoque de la ventana principal globalmente.
            // Para la ventana principal (Fondo Principal), si estamos en Linux o Windows, ocultamos el HSlider de Blur.
            bool isNativeOSBlur = OS.GetName() == "Linux" || OS.GetName() == "FreeBSD" || OS.GetName() == "Windows";
            
            // Mostramos siempre los sliders de Blur y Opacidad de la Ventana Principal
            // si la transparencia está activa. Para KWin/DWM, un valor de Blur = 0 desactiva
            // el efecto, y > 0 lo activa (ignorando la intensidad numérica, ya que el SO lo gestiona).
            if (TransBlurSlider != null) TransBlurSlider.GetParent<Control>().Visible = _configManager.TransModeEnabled;
            if (TransOpacitySlider != null) TransOpacitySlider.GetParent<Control>().Visible = _configManager.TransModeEnabled;
            
            // Para "Pestañas" y "Subventanas/Emergentes", usamos el shader interno de Godot (frosted_glass.gdshader).
            // Este shader SÍ soporta cambiar el radio de desenfoque interno, por lo que SIEMPRE mostramos sus sliders.
            if (TransPopupsBlurSlider != null) TransPopupsBlurSlider.GetParent<Control>().Visible = _configManager.TransModeApplyToPopups;
            if (TransPopupsOpacitySlider != null) TransPopupsOpacitySlider.GetParent<Control>().Visible = _configManager.TransModeApplyToPopups;
            
            if (TransSubWindowsBlurSlider != null) TransSubWindowsBlurSlider.GetParent<Control>().Visible = _configManager.TransModeApplyToSubWindows;
            if (TransSubWindowsOpacitySlider != null) TransSubWindowsOpacitySlider.GetParent<Control>().Visible = _configManager.TransModeApplyToSubWindows;
        }
    }
}
