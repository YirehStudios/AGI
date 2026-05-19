using Godot;
using System;
using System.Threading.Tasks;

namespace Logic.UI
{
    public partial class ChatbotMain : Control
    {
        [Export] public ScrollContainer ChatScrollContainer;
        [Export] public VBoxContainer MessagesContainer;
        [Export] public TextEdit TextInputField;
        [Export] public float MinInputHeight = 45f;
        [Export] public float MaxInputHeight = 150f;
        [Export] public Button SendButton;

        [Export] public PackedScene EscenaMensajeUsuario;
        [Export] public PackedScene EscenaMensajeBot;

        [ExportCategory("AI Execution State")]
        [Export] public OptionButton ModeSelector; // 0 = Flash, 1 = Focus, 2 = Deep
        [Export] public Button ToggleToolTime;
        [Export] public Button ToggleToolWebSearch;
        [Export] public Button ToggleToolMCP; // For Filesystem/OS operations

        [Export] public PanelContainer CodeBlockTemplate;
        [Export] public Texture2D RandomImage1;
        [Export] public Texture2D RandomImage2;
        [Export] public Texture2D RandomImage3;
        [Export] public Texture2D RandomImage4;

        [Export] public VideoStream RandomVideo1;
        [Export] public VideoStream RandomVideo2;
        [Export] public VideoStream RandomVideo3;
        [Export] public VideoStream RandomVideo4;

        [ExportCategory("Hot-Swap UI")]
        [Export] public Control WelcomeOverlay;

        private AudioEffectRecord _recorder;
        private float _silenceTimer = 0.0f;
        private const float SilenceThreshold = 0.05f;
        private bool _isRecording = false;

        private Logic.UI.Components.MensajeBotUI _mensajeBotActual;

        private bool _isLiveModeEnabled = true;
        private bool _isWaitingForResponse = false;
        private string _fullMessageBuffer = string.Empty;
        private Random _randomGenerator = new Random();

        /// <summary>
        /// Initializes UI component event subscriptions and establishes event delegates for network and system workflows.
        /// </summary>
        public override void _Ready()
        {

            if (SendButton != null)
            {
                SendButton.Pressed += OnSendPressed;
            }

            if (TextInputField != null)
            {
                TextInputField.GuiInput += OnTextInputGuiInput;
                TextInputField.TextChanged += OnInputTextChanged;
            }

            if (CodeBlockTemplate != null) CodeBlockTemplate.Visible = false;

            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager != null)
            {
                chatManager.OnBotStartedThinking += OnBotStartedThinking;
                chatManager.OnBotMessageTokenReceived += OnTokenReceived;
                chatManager.OnBotFinishedSpeaking += OnBotFinishedSpeaking;
                chatManager.OnBotToolExecutionStarted += OnBotToolExecutionStarted;
                chatManager.OnBotToolApprovalRequired += OnBotToolApprovalRequired;
            }

            var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (networkManager != null)
            {
                networkManager.STTCompleted += OnSTTCompleted;
            }

            // Wire up the ToolsMenuPanel toggle logic
            var toolsMenuButton = GetNodeOrNull<Button>("MainContainer/ChatAreaContainer/InputAreaMargin/InputPanel/InputLayout/ToolsMenuButton");
            var toolsMenuPanel = GetNodeOrNull<Control>("ToolsMenuPanel");

            if (toolsMenuPanel != null)
            {
                toolsMenuPanel.Visible = false; // Start hidden
                if (toolsMenuButton != null)
                {
                    toolsMenuButton.Pressed += () =>
                    {
                        toolsMenuPanel.Visible = !toolsMenuPanel.Visible;
                    };
                }
            }

            // Set default tool active states on start from persisted settings
            var configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            if (configManager != null)
            {
                // Load persisted settings
                if (ModeSelector != null)
                {
                    ModeSelector.Selected = configManager.PersistedSelectedAiMode;
                }
                if (ToggleToolTime != null)
                {
                    ToggleToolTime.ButtonPressed = configManager.PersistedToolTimeActive == 1;
                }
                if (ToggleToolWebSearch != null)
                {
                    ToggleToolWebSearch.ButtonPressed = configManager.PersistedToolWebSearchActive == 1;
                }
                if (ToggleToolMCP != null)
                {
                    ToggleToolMCP.ButtonPressed = configManager.PersistedToolMcpActive == 1;
                }

                // Connect signals to save settings when modified
                if (ModeSelector != null)
                {
                    ModeSelector.ItemSelected += (long index) =>
                    {
                        configManager.PersistedSelectedAiMode = (int)index;
                        configManager.SaveConfiguration();
                    };
                }
                if (ToggleToolTime != null)
                {
                    ToggleToolTime.Toggled += (bool toggledOn) =>
                    {
                        configManager.PersistedToolTimeActive = toggledOn ? 1 : 0;
                        configManager.SaveConfiguration();
                    };
                }
                if (ToggleToolWebSearch != null)
                {
                    ToggleToolWebSearch.Toggled += (bool toggledOn) =>
                    {
                        configManager.PersistedToolWebSearchActive = toggledOn ? 1 : 0;
                        configManager.SaveConfiguration();
                    };
                }
                if (ToggleToolMCP != null)
                {
                    ToggleToolMCP.Toggled += (bool toggledOn) =>
                    {
                        configManager.PersistedToolMcpActive = toggledOn ? 1 : 0;
                        configManager.SaveConfiguration();
                    };
                }
            }
            else
            {
                if (ToggleToolTime != null) ToggleToolTime.ButtonPressed = true;
                if (ToggleToolMCP != null) ToggleToolMCP.ButtonPressed = true;
            }

            // Dynamically load active messages from global ChatManager memory on startup
            LoadActiveMessagesIntoUI();
        }

        /// <summary>
        /// Evaluates microphone input frame-by-frame by querying the peak volume.
        /// </summary>
        public override void _Process(double delta)
        {
            if (!_isLiveModeEnabled || _recorder == null) return;

            int recordBusIndex = AudioServer.GetBusIndex("Record");
            float currentDb = AudioServer.GetBusPeakVolumeLeftDb(recordBusIndex, 0);
            float linearVolume = Mathf.DbToLinear(currentDb);

            if (linearVolume > SilenceThreshold)
            {
                if (!_isRecording) StartRecording();
                _silenceTimer = 0.0f;
            }
            else if (_isRecording)
            {
                _silenceTimer += (float)delta;
                if (_silenceTimer >= 3.0f)
                {
                    StopAndSendRecording();
                }
            }
        }

        private void StartRecording()
        {
            _isRecording = true;
            _recorder.SetRecordingActive(true);
        }

        private void StopAndSendRecording()
        {
            _isRecording = false;
            _recorder.SetRecordingActive(false);
            AudioStreamWav recording = _recorder.GetRecording();

            if (recording != null)
            {
                string path = ProjectSettings.GlobalizePath("user://audio/chat_input.wav");
                recording.SaveToWav(path);

                var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
                if (networkManager != null)
                {
                    _ = networkManager.RequestSTT(path);
                }
            }
            _silenceTimer = 0.0f;
        }

        private void OnSTTCompleted(string recognizedText)
        {
            if (string.IsNullOrWhiteSpace(recognizedText)) return;
            _ = ProcessMessage(recognizedText);
        }

        private void OnSendPressed()
        {
            if (TextInputField != null)
            {
                _ = ProcessMessage(TextInputField.Text);
            }
        }

        private void OnTextInputGuiInput(InputEvent @event)
        {
            if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Enter && !keyEvent.ShiftPressed)
            {
                GetViewport().SetInputAsHandled();
                _ = ProcessMessage(TextInputField.Text);
            }
        }

        private void OnInputTextChanged()
        {
            if (TextInputField == null) return;

            int totalLines = 0;
            for (int i = 0; i < TextInputField.GetLineCount(); i++)
            {
                totalLines += 1 + TextInputField.GetLineWrapCount(i);
            }

            float contentHeight = (totalLines * 24f) + 20f;
            contentHeight = Mathf.Clamp(contentHeight, MinInputHeight, MaxInputHeight);

            TextInputField.CustomMinimumSize = new Vector2(TextInputField.CustomMinimumSize.X, contentHeight);
        }

        /// <summary>
        /// Orchestrates the comprehensive lifecycle of a user message submission using components.
        /// </summary>
        private async Task ProcessMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _isWaitingForResponse) return;

            _isWaitingForResponse = true;

            if (WelcomeOverlay != null)
            {
                WelcomeOverlay.Visible = false;
            }

            TextInputField.Text = string.Empty;
            TextInputField.CustomMinimumSize = new Vector2(TextInputField.CustomMinimumSize.X, MinInputHeight);

            if (SendButton != null) SendButton.Disabled = true;

            var nuevoMsgUsuario = EscenaMensajeUsuario.Instantiate<Logic.UI.Components.MensajeUsuarioUI>();

            if (MessagesContainer.Theme != null) nuevoMsgUsuario.Theme = MessagesContainer.Theme;

            MessagesContainer.AddChild(nuevoMsgUsuario);
            nuevoMsgUsuario.ConfigurarMensaje(text);

            ScrollToBottom();

            // Capture Mode (Default to 1: Focus)
            int selectedMode = ModeSelector != null ? ModeSelector.Selected : 1;

            // Build Active Tools List
            var activeTools = new global::System.Collections.Generic.List<string>();
            if (ToggleToolTime != null && ToggleToolTime.ButtonPressed) activeTools.Add("Time");
            if (ToggleToolWebSearch != null && ToggleToolWebSearch.ButtonPressed) activeTools.Add("Web Search");
            if (ToggleToolMCP != null && ToggleToolMCP.ButtonPressed) activeTools.Add("MCP");

            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager != null)
            {
                await chatManager.SendToAI(text, selectedMode, activeTools);
            }
            else
            {
                _isWaitingForResponse = false;
                if (SendButton != null) SendButton.Disabled = false;
            }
        }

        /// <summary>
        /// Prepares the scene tree for an incoming bot response using the Bot Component.
        /// </summary>
        private void OnBotStartedThinking()
        {
            _fullMessageBuffer = string.Empty;

            // --- INSTANCIACIÓN DEL COMPONENTE DE BOT ---
            _mensajeBotActual = EscenaMensajeBot.Instantiate<Logic.UI.Components.MensajeBotUI>();

            // AGREGAR ESTA LÍNEA AQUÍ:
            if (MessagesContainer.Theme != null) _mensajeBotActual.Theme = MessagesContainer.Theme;

            MessagesContainer.AddChild(_mensajeBotActual);
            _mensajeBotActual.IniciarEstadoPensando();

            ScrollToBottom();
        }

        private async Task GenerateMockMediaResponse(bool isVideo)
        {
            OnBotStartedThinking();
            await ToSignal(GetTree().CreateTimer(3.0f), SceneTreeTimer.SignalName.Timeout);

            if (_mensajeBotActual != null) _mensajeBotActual.FinalizarRespuesta();

            Control messageLayout = _mensajeBotActual?.FindChild("MessageLayout", true, false) as Control;

            if (messageLayout != null)
            {
                int rand = _randomGenerator.Next(1, 5);

                if (isVideo)
                {
                    VideoStreamPlayer videoPlayer = new VideoStreamPlayer();
                    videoPlayer.CustomMinimumSize = new Vector2(400, 300);
                    videoPlayer.Expand = true;
                    videoPlayer.Autoplay = true;
                    videoPlayer.Loop = true;

                    // Seleccionamos el video aleatorio
                    if (rand == 1) videoPlayer.Stream = RandomVideo1;
                    else if (rand == 2) videoPlayer.Stream = RandomVideo2;
                    else if (rand == 3) videoPlayer.Stream = RandomVideo3;
                    else videoPlayer.Stream = RandomVideo4;

                    messageLayout.AddChild(videoPlayer);
                }
                else
                {
                    TextureRect imageRect = new TextureRect();
                    imageRect.CustomMinimumSize = new Vector2(400, 300); // Tamaño de la imagen
                    imageRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                    imageRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;

                    // Seleccionamos la imagen aleatoria
                    if (rand == 1) imageRect.Texture = RandomImage1;
                    else if (rand == 2) imageRect.Texture = RandomImage2;
                    else if (rand == 3) imageRect.Texture = RandomImage3;
                    else imageRect.Texture = RandomImage4;

                    messageLayout.AddChild(imageRect);
                }
            }

            _isWaitingForResponse = false;
            if (SendButton != null) SendButton.Disabled = false;
            if (TextInputField != null) TextInputField.GrabFocus();
        }

        /// <summary>
        /// Streams tokens directly to the active Bot Component.
        /// </summary>
        private void OnTokenReceived(string token)
        {
            if (_mensajeBotActual == null) return;
            _mensajeBotActual.AgregarToken(token);
            ScrollToBottom();
        }

        /// <summary>
        /// Resolves the bot response state machine.
        /// </summary>
        private void OnBotFinishedSpeaking(string fullResponse)
        {
            _isWaitingForResponse = false;
            if (TextInputField != null) TextInputField.Editable = true;
            if (SendButton != null) SendButton.Disabled = false;

            if (_mensajeBotActual != null)
            {
                _mensajeBotActual.FinalizarRespuesta();
                InjectCodeBlocks(_mensajeBotActual.ObtenerTextoCompleto());
            }
        }

        private async void ScrollToBottom()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ChatScrollContainer != null)
            {
                ScrollBar vScroll = ChatScrollContainer.GetVScrollBar();
                vScroll.Value = vScroll.MaxValue;
            }
        }

        public void UpdateTheme(bool isDark)
        {
            string path = isDark ? "res://Resources/UI_Themes/minimal_theme.tres" : "res://Resources/UI_Themes/tema_claro.tres";
            Theme temaCorrecto = ResourceLoader.Load<Theme>(path);

            this.Theme = temaCorrecto;

            if (MessagesContainer != null)
            {
                foreach (Node child in MessagesContainer.GetChildren())
                {
                    if (child is Control controlChild)
                    {
                        controlChild.Theme = temaCorrecto;
                    }
                }
            }

            // ─────────────────────────────────────────────────────────────
            //  DYNAMIC STYLE OVERRIDES FOR PREMIUM UI VISIBILITY
            // ─────────────────────────────────────────────────────────────

            // 1. ToolsMenuPanel Style (Characterized by HSL Charcoal in Dark and soft White in Light)
            var toolsMenuPanel = GetNodeOrNull<PanelContainer>("ToolsMenuPanel");
            var toolsPanelChild = GetNodeOrNull<Panel>("ToolsMenuPanel/panel");

            StyleBoxFlat toolsPanelStyle = new StyleBoxFlat();
            toolsPanelStyle.CornerRadiusTopLeft = 12;
            toolsPanelStyle.CornerRadiusTopRight = 12;
            toolsPanelStyle.CornerRadiusBottomLeft = 12;
            toolsPanelStyle.CornerRadiusBottomRight = 12;
            toolsPanelStyle.SetContentMarginAll(12);

            if (isDark)
            {
                toolsPanelStyle.BgColor = new Color(0.12f, 0.12f, 0.16f, 0.95f); // Premium dark glass
                toolsPanelStyle.BorderWidthLeft = 1;
                toolsPanelStyle.BorderWidthTop = 1;
                toolsPanelStyle.BorderWidthRight = 1;
                toolsPanelStyle.BorderWidthBottom = 1;
                toolsPanelStyle.BorderColor = new Color(0.25f, 0.25f, 0.32f, 0.4f);
                toolsPanelStyle.ShadowColor = new Color(0, 0, 0, 0.3f);
                toolsPanelStyle.ShadowSize = 8;
            }
            else
            {
                toolsPanelStyle.BgColor = new Color(0.98f, 0.98f, 1.0f, 0.98f); // Soft light glass
                toolsPanelStyle.BorderWidthLeft = 1;
                toolsPanelStyle.BorderWidthTop = 1;
                toolsPanelStyle.BorderWidthRight = 1;
                toolsPanelStyle.BorderWidthBottom = 1;
                toolsPanelStyle.BorderColor = new Color(0.8f, 0.8f, 0.85f, 0.5f);
                toolsPanelStyle.ShadowColor = new Color(0, 0, 0, 0.1f);
                toolsPanelStyle.ShadowSize = 6;
            }

            if (toolsMenuPanel != null)
            {
                toolsMenuPanel.AddThemeStyleboxOverride("panel", toolsPanelStyle);
            }
            if (toolsPanelChild != null)
            {
                toolsPanelChild.AddThemeStyleboxOverride("panel", toolsPanelStyle);
            }

            // 2. Tools Menu Typography & Flat Button Overrides
            var toolsLabel = GetNodeOrNull<Label>("ToolsMenuPanel/MenuMargin/MenuLayout/Label");
            if (toolsLabel != null)
            {
                toolsLabel.AddThemeColorOverride("font_color", isDark ? new Color(0.9f, 0.9f, 0.95f) : new Color(0.15f, 0.15f, 0.2f));
            }

            var menuLayout = GetNodeOrNull<VBoxContainer>("ToolsMenuPanel/MenuMargin/MenuLayout");
            if (menuLayout != null)
            {
                foreach (Node child in menuLayout.GetChildren())
                {
                    if (child is Button btn && child != toolsLabel)
                    {
                        Color normalColor = isDark ? new Color(0.85f, 0.85f, 0.9f) : new Color(0.2f, 0.2f, 0.25f);
                        Color hoverColor = isDark ? new Color(1.0f, 1.0f, 1.0f) : new Color(0.05f, 0.05f, 0.1f);
                        Color pressedColor = new Color(0.274f, 0.623f, 0.924f); // Brand Active Blue

                        btn.AddThemeColorOverride("font_color", normalColor);
                        btn.AddThemeColorOverride("font_hover_color", hoverColor);
                        btn.AddThemeColorOverride("font_pressed_color", pressedColor);
                        btn.AddThemeColorOverride("font_focus_color", hoverColor);

                        // Dynamic native icon coloring for hover/normal/pressed button events
                        btn.AddThemeColorOverride("icon_normal_color", normalColor);
                        btn.AddThemeColorOverride("icon_hover_color", hoverColor);
                        btn.AddThemeColorOverride("icon_pressed_color", pressedColor);
                        btn.AddThemeColorOverride("icon_hover_pressed_color", pressedColor);
                        btn.AddThemeColorOverride("icon_focus_color", hoverColor);
                    }
                }
            }

            // 3. TextEdit (TextInputField) Premium Styling
            if (TextInputField != null)
            {
                StyleBoxFlat textInputStyle = new StyleBoxFlat();
                textInputStyle.CornerRadiusTopLeft = 8;
                textInputStyle.CornerRadiusTopRight = 8;
                textInputStyle.CornerRadiusBottomLeft = 8;
                textInputStyle.CornerRadiusBottomRight = 8;
                textInputStyle.SetContentMarginAll(10);

                if (isDark)
                {
                    textInputStyle.BgColor = new Color(0.12f, 0.12f, 0.15f);
                    textInputStyle.BorderWidthLeft = 1;
                    textInputStyle.BorderWidthTop = 1;
                    textInputStyle.BorderWidthRight = 1;
                    textInputStyle.BorderWidthBottom = 1;
                    textInputStyle.BorderColor = new Color(0.22f, 0.22f, 0.28f);

                    TextInputField.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
                    TextInputField.AddThemeColorOverride("font_placeholder_color", new Color(0.5f, 0.5f, 0.55f));
                    TextInputField.AddThemeColorOverride("caret_color", new Color(0.9f, 0.9f, 0.95f));
                }
                else
                {
                    textInputStyle.BgColor = new Color(0.96f, 0.96f, 0.98f);
                    textInputStyle.BorderWidthLeft = 1;
                    textInputStyle.BorderWidthTop = 1;
                    textInputStyle.BorderWidthRight = 1;
                    textInputStyle.BorderWidthBottom = 1;
                    textInputStyle.BorderColor = new Color(0.85f, 0.85f, 0.9f);

                    TextInputField.AddThemeColorOverride("font_color", new Color(0.15f, 0.15f, 0.2f));
                    TextInputField.AddThemeColorOverride("font_placeholder_color", new Color(0.6f, 0.6f, 0.65f));
                    TextInputField.AddThemeColorOverride("caret_color", new Color(0.15f, 0.15f, 0.2f));
                }

                TextInputField.AddThemeStyleboxOverride("normal", textInputStyle);
                TextInputField.AddThemeStyleboxOverride("focus", textInputStyle);
            }

            // 4. OptionButton (ModeSelector) Premium Styling
            if (ModeSelector != null)
            {
                StyleBoxFlat modeSelectStyleNormal = new StyleBoxFlat();
                modeSelectStyleNormal.CornerRadiusTopLeft = 8;
                modeSelectStyleNormal.CornerRadiusTopRight = 8;
                modeSelectStyleNormal.CornerRadiusBottomLeft = 8;
                modeSelectStyleNormal.CornerRadiusBottomRight = 8;
                modeSelectStyleNormal.SetContentMarginAll(8);

                StyleBoxFlat modeSelectStyleHover = modeSelectStyleNormal.Duplicate() as StyleBoxFlat;

                if (isDark)
                {
                    modeSelectStyleNormal.BgColor = new Color(0.16f, 0.16f, 0.2f);
                    modeSelectStyleNormal.BorderWidthLeft = 1;
                    modeSelectStyleNormal.BorderWidthTop = 1;
                    modeSelectStyleNormal.BorderWidthRight = 1;
                    modeSelectStyleNormal.BorderWidthBottom = 1;
                    modeSelectStyleNormal.BorderColor = new Color(0.26f, 0.26f, 0.32f);

                    modeSelectStyleHover.BgColor = new Color(0.2f, 0.2f, 0.25f);
                    modeSelectStyleHover.BorderWidthLeft = 1;
                    modeSelectStyleHover.BorderWidthTop = 1;
                    modeSelectStyleHover.BorderWidthRight = 1;
                    modeSelectStyleHover.BorderWidthBottom = 1;
                    modeSelectStyleHover.BorderColor = new Color(0.35f, 0.35f, 0.42f);

                    Color textColor = new Color(0.9f, 0.9f, 0.95f);
                    ModeSelector.AddThemeColorOverride("font_color", textColor);
                    ModeSelector.AddThemeColorOverride("font_hover_color", new Color(1.0f, 1.0f, 1.0f));
                    ModeSelector.AddThemeColorOverride("font_pressed_color", textColor);
                    ModeSelector.AddThemeColorOverride("font_focus_color", textColor);
                    ModeSelector.AddThemeColorOverride("icon_normal_color", textColor);
                    ModeSelector.AddThemeColorOverride("icon_hover_color", new Color(1.0f, 1.0f, 1.0f));
                }
                else
                {
                    modeSelectStyleNormal.BgColor = new Color(0.92f, 0.92f, 0.95f);
                    modeSelectStyleNormal.BorderWidthLeft = 1;
                    modeSelectStyleNormal.BorderWidthTop = 1;
                    modeSelectStyleNormal.BorderWidthRight = 1;
                    modeSelectStyleNormal.BorderWidthBottom = 1;
                    modeSelectStyleNormal.BorderColor = new Color(0.8f, 0.8f, 0.85f);

                    modeSelectStyleHover.BgColor = new Color(0.86f, 0.86f, 0.9f);
                    modeSelectStyleHover.BorderWidthLeft = 1;
                    modeSelectStyleHover.BorderWidthTop = 1;
                    modeSelectStyleHover.BorderWidthRight = 1;
                    modeSelectStyleHover.BorderWidthBottom = 1;
                    modeSelectStyleHover.BorderColor = new Color(0.7f, 0.7f, 0.75f);

                    Color textColor = new Color(0.15f, 0.15f, 0.2f);
                    ModeSelector.AddThemeColorOverride("font_color", textColor);
                    ModeSelector.AddThemeColorOverride("font_hover_color", new Color(0.05f, 0.05f, 0.1f));
                    ModeSelector.AddThemeColorOverride("font_pressed_color", textColor);
                    ModeSelector.AddThemeColorOverride("font_focus_color", textColor);
                    ModeSelector.AddThemeColorOverride("icon_normal_color", textColor);
                    ModeSelector.AddThemeColorOverride("icon_hover_color", new Color(0.05f, 0.05f, 0.1f));
                }

                ModeSelector.AddThemeStyleboxOverride("normal", modeSelectStyleNormal);
                ModeSelector.AddThemeStyleboxOverride("hover", modeSelectStyleHover);
                ModeSelector.AddThemeStyleboxOverride("pressed", modeSelectStyleNormal);
                ModeSelector.AddThemeStyleboxOverride("focus", modeSelectStyleNormal);

                var popup = ModeSelector.GetPopup();
                if (popup != null)
                {
                    StyleBoxFlat popupStyle = new StyleBoxFlat();
                    popupStyle.CornerRadiusTopLeft = 10;
                    popupStyle.CornerRadiusTopRight = 10;
                    popupStyle.CornerRadiusBottomLeft = 10;
                    popupStyle.CornerRadiusBottomRight = 10;
                    popupStyle.SetContentMarginAll(8);

                    if (isDark)
                    {
                        popupStyle.BgColor = new Color(0.12f, 0.12f, 0.15f);
                        popupStyle.BorderWidthLeft = 1;
                        popupStyle.BorderWidthTop = 1;
                        popupStyle.BorderWidthRight = 1;
                        popupStyle.BorderWidthBottom = 1;
                        popupStyle.BorderColor = new Color(0.22f, 0.22f, 0.28f);

                        popup.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.9f));
                        popup.AddThemeColorOverride("font_hover_color", new Color(1.0f, 1.0f, 1.0f));
                    }
                    else
                    {
                        popupStyle.BgColor = new Color(0.98f, 0.98f, 1.0f);
                        popupStyle.BorderWidthLeft = 1;
                        popupStyle.BorderWidthTop = 1;
                        popupStyle.BorderWidthRight = 1;
                        popupStyle.BorderWidthBottom = 1;
                        popupStyle.BorderColor = new Color(0.8f, 0.8f, 0.85f);

                        popup.AddThemeColorOverride("font_color", new Color(0.15f, 0.15f, 0.2f));
                        popup.AddThemeColorOverride("font_hover_color", new Color(0.05f, 0.05f, 0.1f));
                    }

                    popup.AddThemeStyleboxOverride("panel", popupStyle);
                }
            }

            // 5. InputPanel Container Style (Outer background of input area)
            var inputPanel = GetNodeOrNull<PanelContainer>("MainContainer/ChatAreaContainer/InputAreaMargin/InputPanel");
            if (inputPanel != null)
            {
                StyleBoxFlat inputPanelStyle = new StyleBoxFlat();
                inputPanelStyle.CornerRadiusTopLeft = 12;
                inputPanelStyle.CornerRadiusTopRight = 12;
                inputPanelStyle.CornerRadiusBottomLeft = 12;
                inputPanelStyle.CornerRadiusBottomRight = 12;
                inputPanelStyle.SetContentMarginAll(8);

                if (isDark)
                {
                    inputPanelStyle.BgColor = new Color(0.07f, 0.07f, 0.09f);
                    inputPanelStyle.BorderWidthLeft = 1;
                    inputPanelStyle.BorderWidthTop = 1;
                    inputPanelStyle.BorderWidthRight = 1;
                    inputPanelStyle.BorderWidthBottom = 1;
                    inputPanelStyle.BorderColor = new Color(0.18f, 0.18f, 0.22f);
                }
                else
                {
                    inputPanelStyle.BgColor = new Color(1.0f, 1.0f, 1.0f);
                    inputPanelStyle.BorderWidthLeft = 1;
                    inputPanelStyle.BorderWidthTop = 1;
                    inputPanelStyle.BorderWidthRight = 1;
                    inputPanelStyle.BorderWidthBottom = 1;
                    inputPanelStyle.BorderColor = new Color(0.85f, 0.85f, 0.9f);
                    inputPanelStyle.ShadowColor = new Color(0, 0, 0, 0.05f);
                    inputPanelStyle.ShadowSize = 6;
                }

                inputPanel.AddThemeStyleboxOverride("panel", inputPanelStyle);
            }

            // 6. ToolsMenuButton Style (Briefcase icon toggle)
            var toolsMenuButton = GetNodeOrNull<Button>("MainContainer/ChatAreaContainer/InputAreaMargin/InputPanel/InputLayout/ToolsMenuButton");
            if (toolsMenuButton != null)
            {
                Color btnColor = isDark ? new Color(0.85f, 0.85f, 0.9f) : new Color(0.2f, 0.2f, 0.25f);
                Color btnHoverColor = isDark ? new Color(1.0f, 1.0f, 1.0f) : new Color(0.05f, 0.05f, 0.1f);

                toolsMenuButton.AddThemeColorOverride("icon_normal_color", btnColor);
                toolsMenuButton.AddThemeColorOverride("icon_hover_color", btnHoverColor);
                toolsMenuButton.AddThemeColorOverride("icon_pressed_color", new Color(0.274f, 0.623f, 0.924f));
                toolsMenuButton.AddThemeColorOverride("icon_focus_color", btnHoverColor);
            }

            // 7. SendButton Style
            if (SendButton != null)
            {
                StyleBoxFlat sendStyleNormal = new StyleBoxFlat();
                sendStyleNormal.CornerRadiusTopLeft = 12;
                sendStyleNormal.CornerRadiusTopRight = 12;
                sendStyleNormal.CornerRadiusBottomLeft = 12;
                sendStyleNormal.CornerRadiusBottomRight = 12;
                sendStyleNormal.BgColor = new Color(0.274f, 0.623f, 0.924f);

                StyleBoxFlat sendStyleHover = sendStyleNormal.Duplicate() as StyleBoxFlat;
                sendStyleHover.BgColor = new Color(0.35f, 0.7f, 1.0f);

                SendButton.AddThemeStyleboxOverride("normal", sendStyleNormal);
                SendButton.AddThemeStyleboxOverride("hover", sendStyleHover);
                SendButton.AddThemeStyleboxOverride("pressed", sendStyleNormal);
                SendButton.AddThemeStyleboxOverride("focus", sendStyleNormal);
            }
        }

        public void LoadActiveMessagesIntoUI()
        {
            if (MessagesContainer != null)
            {
                foreach (Node child in MessagesContainer.GetChildren())
                {
                    child.QueueFree();
                }
            }

            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager == null || chatManager.CurrentSession == null) return;

            foreach (var msg in chatManager.CurrentSession.Messages)
            {
                if (msg.Role == "user")
                {
                    if (EscenaMensajeUsuario != null)
                    {
                        var nuevoMsgUsuario = EscenaMensajeUsuario.Instantiate<Logic.UI.Components.MensajeUsuarioUI>();
                        MessagesContainer.AddChild(nuevoMsgUsuario);
                        if (MessagesContainer.Theme != null) nuevoMsgUsuario.Theme = MessagesContainer.Theme;
                        nuevoMsgUsuario.ConfigurarMensaje(msg.Content);
                    }
                }
                else if (msg.Role == "assistant")
                {
                    if (EscenaMensajeBot != null)
                    {
                        var nuevoMsgBot = EscenaMensajeBot.Instantiate<Logic.UI.Components.MensajeBotUI>();
                        MessagesContainer.AddChild(nuevoMsgBot);
                        if (MessagesContainer.Theme != null) nuevoMsgBot.Theme = MessagesContainer.Theme;
                        
                        nuevoMsgBot.AgregarToken(msg.Content);
                        nuevoMsgBot.FinalizarRespuesta();
                    }
                }
            }

            ScrollToBottom();
        }

        // Model selection and hot-swap logic has been migrated to Settings.cs.

        /// <summary>
        /// Processes tool execution tracking to update the current message UI container based on the current active MCP schema key.
        /// </summary>
        private void OnBotToolExecutionStarted(string toolName)
        {
            if (_mensajeBotActual == null) return;

            string accionTexto = "Pensando";
            switch (toolName)
            {
                case "web_search": accionTexto = "Buscando"; break;
                case "os_command": accionTexto = "Ejecutando comando"; break;
                case "file_read": accionTexto = "Leyendo archivo"; break;
                default: accionTexto = $"Usando {toolName}"; break;
            }

            _mensajeBotActual.CambiarEstadoAccion(accionTexto);
        }

        /// <summary>
        /// Instantiates an interactive intercept UI container within the chat layout to allow human-in-the-loop validation of autonomous actions.
        /// Overhauled to dynamically parse tool arguments and generate independent input text fields for a clean, non-JSON display.
        /// Automatically reconstructs the correct structural schema layout sequence upon submission.
        /// </summary>
        private void OnBotToolApprovalRequired(string toolName, string toolArgsJson)
        {
            if (_mensajeBotActual == null) return;

            if (_mensajeBotActual.HasMethod("CambiarEstadoAccion"))
                _mensajeBotActual.Call("CambiarEstadoAccion", "Esperando autorización...");

            Control messageLayout = _mensajeBotActual.FindChild("MessageLayout", true, false) as Control;
            if (messageLayout == null) return;

            VBoxContainer approvalContainer = new VBoxContainer();

            Label title = new Label { Text = $"⚠️ La IA quiere usar: {toolName}" };
            title.AddThemeColorOverride("font_color", new Color(1, 0.8f, 0.2f));
            approvalContainer.AddChild(title);

            var parsedArgs = new global::System.Collections.Generic.Dictionary<string, string>();
            try
            {
                using var doc = global::System.Text.Json.JsonDocument.Parse(toolArgsJson);
                global::System.Text.Json.JsonElement argsElement;

                if (!doc.RootElement.TryGetProperty("arguments", out argsElement))
                    argsElement = doc.RootElement;

                foreach (var prop in argsElement.EnumerateObject())
                {
                    if (prop.Name != "tool") parsedArgs[prop.Name] = prop.Value.ToString();
                }
            }
            catch { /* Fallback mapping routine targeting unparsed layout definitions */ }

            var inputs = new global::System.Collections.Generic.Dictionary<string, TextEdit>();

            if (parsedArgs.Count > 0)
            {
                foreach (var kvp in parsedArgs)
                {
                    Label argLabel = new Label { Text = kvp.Key.ToUpper() + ":" };
                    argLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                    approvalContainer.AddChild(argLabel);

                    TextEdit argInput = new TextEdit
                    {
                        Text = kvp.Value,
                        CustomMinimumSize = new Vector2(0, kvp.Key == "content" || kvp.Key == "command" ? 150 : 40),
                        WrapMode = TextEdit.LineWrappingMode.Boundary
                    };
                    approvalContainer.AddChild(argInput);
                    inputs[kvp.Key] = argInput;
                }
            }
            else
            {
                TextEdit fallbackInput = new TextEdit
                {
                    Text = toolArgsJson,
                    CustomMinimumSize = new Vector2(0, 100),
                    WrapMode = TextEdit.LineWrappingMode.Boundary
                };
                approvalContainer.AddChild(fallbackInput);
                inputs["_raw_"] = fallbackInput;
            }

            HBoxContainer btnContainer = new HBoxContainer();
            Button acceptBtn = new Button { Text = "Aceptar y Ejecutar" };
            Button cancelBtn = new Button { Text = "Cancelar" };

            btnContainer.AddChild(acceptBtn);
            btnContainer.AddChild(cancelBtn);
            approvalContainer.AddChild(btnContainer);

            messageLayout.AddChild(approvalContainer);

            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");

            acceptBtn.Pressed += () =>
            {
                approvalContainer.QueueFree();

                if (_mensajeBotActual.HasMethod("CambiarEstadoAccion"))
                    _mensajeBotActual.Call("CambiarEstadoAccion", $"Ejecutando {toolName}...");

                string finalJson;
                if (inputs.TryGetValue("_raw_", out TextEdit value))
                {
                    finalJson = value.Text;
                }
                else
                {
                    var resultArgs = new global::System.Collections.Generic.Dictionary<string, string>();
                    foreach (var kvp in inputs) resultArgs[kvp.Key] = kvp.Value.Text;

                    var payloadObj = new { tool = toolName, arguments = resultArgs };
                    finalJson = global::System.Text.Json.JsonSerializer.Serialize(payloadObj);
                }

                chatManager?.EmitSignal(Logic.Lite.ChatManager.SignalName.OnUserToolApprovalResponse, true, finalJson);
            };

            cancelBtn.Pressed += () =>
            {
                approvalContainer.QueueFree();
                if (_mensajeBotActual.HasMethod("CambiarEstadoAccion"))
                    _mensajeBotActual.Call("CambiarEstadoAccion", "Herramienta cancelada.");

                chatManager?.EmitSignal(Logic.Lite.ChatManager.SignalName.OnUserToolApprovalResponse, false, toolArgsJson);
            };

            ScrollToBottom();
        }

        /// <summary>
        /// Parses the raw text block to distinguish standard markdown content from code segments, 
        /// programmatically instantiating structural code editor templates dynamically.
        /// Integrates programmatic layout duplication, runtime syntax color rule initialization, 
        /// and secure window clipboard routing bindings.
        /// </summary>
        private void InjectCodeBlocks(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText) || !rawText.Contains("```")) return;
            if (_mensajeBotActual == null) return;

            if (CodeBlockTemplate == null)
            {
                GD.PrintErr("[UI ERROR] CodeBlockTemplate es nulo. ¡Asigna tu PanelContainer de plantilla de código en el Inspector de Godot (ChatbotMain) para que puedan dibujarse!");
                return;
            }

            Control messageLayout = _mensajeBotActual.FindChild("MessageLayout", true, false) as Control;
            RichTextLabel originalMarkdownNode = _mensajeBotActual.FindChild("MessageBody", true, false) as RichTextLabel;

            if (messageLayout == null || originalMarkdownNode == null) return;
            originalMarkdownNode.Visible = false;

            string[] separator = { "```" };
            string[] blocks = rawText.Split(separator, StringSplitOptions.None);

            for (int i = 0; i < blocks.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(blocks[i])) continue;

                if (i % 2 == 0)
                {
                    RichTextLabel textBlock = (RichTextLabel)originalMarkdownNode.Duplicate();
                    textBlock.Visible = true;
                    textBlock.Set("markdown_text", blocks[i].Trim());
                    textBlock.Text = blocks[i].Trim();
                    messageLayout.AddChild(textBlock);
                }
                else
                {
                    PanelContainer newCodeBlock = (PanelContainer)CodeBlockTemplate.Duplicate();
                    newCodeBlock.Visible = true;

                    string codeContent = blocks[i];
                    string language = "code";
                    int firstNewline = codeContent.IndexOf('\n');

                    if (firstNewline != -1 && firstNewline < 20)
                    {
                        language = codeContent.Substring(0, firstNewline).Trim();
                        codeContent = codeContent.Substring(firstNewline + 1);
                    }

                    CodeEdit editNode = null;
                    var codeEdits = newCodeBlock.FindChildren("*", "CodeEdit", true, false);
                    if (codeEdits.Count > 0 && codeEdits[0] is CodeEdit foundEdit)
                    {
                        editNode = foundEdit;
                        editNode.Text = codeContent.Trim();

                        var highlighter = new CodeHighlighter();
                        highlighter.NumberColor = new Color(0.92f, 0.77f, 0.51f);
                        highlighter.SymbolColor = new Color(0.80f, 0.80f, 0.80f);
                        highlighter.FunctionColor = new Color(0.38f, 0.69f, 0.93f);
                        highlighter.MemberVariableColor = new Color(0.48f, 0.82f, 0.64f);

                        var coreKeywords = new string[] {
                            "public", "private", "protected", "class", "void", "string", "int", "float", "bool",
                            "var", "return", "if", "else", "for", "while", "foreach", "import", "def", "from",
                            "as", "print", "async", "await", "using", "namespace", "new", "true", "false"
                        };

                        Color keywordColor = new Color(0.85f, 0.43f, 0.58f);
                        foreach (var keyword in coreKeywords)
                        {
                            highlighter.AddKeywordColor(keyword, keywordColor);
                        }

                        editNode.SyntaxHighlighter = highlighter;
                    }

                    var labels = newCodeBlock.FindChildren("*", "Label", true, false);
                    if (labels.Count > 0 && labels[0] is Label langLabel)
                    {
                        langLabel.Text = string.IsNullOrEmpty(language) ? "CODE" : language.ToUpper();
                    }

                    var buttons = newCodeBlock.FindChildren("*", "Button", true, false);
                    if (buttons.Count > 0 && buttons[0] is Button copyBtn && editNode != null)
                    {
                        copyBtn.Pressed += () =>
                        {
                            DisplayServer.ClipboardSet(editNode.Text);
                            copyBtn.Text = "¡Copiado!";

                            GetTree().CreateTimer(1.5f).Timeout += () =>
                            {
                                if (GodotObject.IsInstanceValid(copyBtn)) copyBtn.Text = "Copy";
                            };
                        };
                    }

                    messageLayout.AddChild(newCodeBlock);
                }
            }
        }

        /// <summary>
        /// Handles cleanup operations for active event subscriptions when the node is processed out of the scene tree.
        /// </summary>
        public override void _ExitTree()
        {
            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager != null)
            {
                chatManager.OnBotStartedThinking -= OnBotStartedThinking;
                chatManager.OnBotMessageTokenReceived -= OnTokenReceived;
                chatManager.OnBotFinishedSpeaking -= OnBotFinishedSpeaking;
                chatManager.OnBotToolExecutionStarted -= OnBotToolExecutionStarted;
                chatManager.OnBotToolApprovalRequired -= OnBotToolApprovalRequired;
            }

            var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (networkManager != null)
            {
                networkManager.STTCompleted -= OnSTTCompleted;
            }
        }
    }

}