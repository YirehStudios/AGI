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

        [Export] public OptionButton ToolSelector; 
        [Export] public OptionButton ModelSelector;
        private global::System.Collections.Generic.Dictionary<int, string> _rutasModelos = new global::System.Collections.Generic.Dictionary<int, string>();

        [Export] public PanelContainer CodeBlockTemplate;
        [Export] public Texture2D RandomImage1;
        [Export] public Texture2D RandomImage2;
        [Export] public Texture2D RandomImage3;
        [Export] public Texture2D RandomImage4;
        [Export] public Texture2D WingLeftBaseTexture;
        [Export] public Texture2D WingRightBaseTexture;
        [Export] public VideoStream RandomVideo1;
        [Export] public VideoStream RandomVideo2;
        [Export] public VideoStream RandomVideo3;
        [Export] public VideoStream RandomVideo4;
        [Export] public AudioStreamPlayer MicroRecorderPlayer; 
        
        [Export] public Label LanguageLabel;
        [Export] public CodeEdit CodeEditor;
        [Export] public Button CopyBtn;
        [Export] public Control BottomInputPanel;
        [Export] public Control ChatBackgroundPanel;

        private AudioEffectRecord _recorder;
        private float _silenceTimer = 0.0f;
        private const float SilenceThreshold = 0.05f;
        private bool _isRecording = false;
        
        private Logic.UI.Components.MensajeBotUI _mensajeBotActual;

        private bool _isLiveModeEnabled = true;
        private bool _isWaitingForResponse = false;
        private string _ttsBuffer = string.Empty;
        private string _fullMessageBuffer = string.Empty;
        private Random _randomGenerator = new Random();

        /// <summary>
        /// Initializes UI component event subscriptions and establishes event delegates.
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
            }

            var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (networkManager != null) 
            {
                networkManager.STTCompleted += OnSTTCompleted;
            }
            
            CargarModelosEnMenu();
            if (ModelSelector != null)
            {
                ModelSelector.ItemSelected += OnModelSelected;
            }
            
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

            Node current = this;
            while (current != null)
            {
                if (current.HasMethod("HideWelcomeMessage"))
                {
                    current.Call("HideWelcomeMessage");
                    break;
                }
                current = current.GetParent();
            }

            TextInputField.Text = string.Empty;
            TextInputField.CustomMinimumSize = new Vector2(TextInputField.CustomMinimumSize.X, MinInputHeight);
            if (SendButton != null) SendButton.Disabled = true;

            var nuevoMsgUsuario = EscenaMensajeUsuario.Instantiate<Logic.UI.Components.MensajeUsuarioUI>();
            
            if (MessagesContainer.Theme != null) nuevoMsgUsuario.Theme = MessagesContainer.Theme;
            
            MessagesContainer.AddChild(nuevoMsgUsuario);
            nuevoMsgUsuario.ConfigurarMensaje(text);

            ScrollToBottom();

            int selectedTool = ToolSelector != null ? ToolSelector.Selected : 0;

            if (selectedTool == 1)
            {
                await GenerateMockMediaResponse(text, false);
            }
            else if (selectedTool == 2)
            {
                await GenerateMockMediaResponse(text, true);
            }
            else
            {
                var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
                if (chatManager != null) 
                {
                    await chatManager.SendToAI(text);
                }
                else
                {
                    _isWaitingForResponse = false;
                    if (SendButton != null) SendButton.Disabled = false;
                }
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

                private async Task GenerateMockMediaResponse(string prompt, bool isVideo)
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

        /// <summary>
        /// Parses the raw string payload to identify standard markdown codeblocks.
        /// </summary>
        private void InjectCodeBlocks(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText) || !rawText.Contains("```")) return;
            if (_mensajeBotActual == null || CodeBlockTemplate == null) return;

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
                    
                }
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

        public override void _ExitTree()
        {
            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager != null) 
            {
                chatManager.OnBotStartedThinking -= OnBotStartedThinking;
                chatManager.OnBotMessageTokenReceived -= OnTokenReceived;
                chatManager.OnBotFinishedSpeaking -= OnBotFinishedSpeaking;
            }

            var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (networkManager != null) 
            {
                networkManager.STTCompleted -= OnSTTCompleted;
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
        }

        private void CargarModelosEnMenu()
        {
            if (ModelSelector == null) return;
            ModelSelector.Clear();
            _rutasModelos.Clear();

            string rutaModelos = "user://models"; 
            
            using var dir = DirAccess.Open(rutaModelos);
            if (dir != null)
            {
                dir.ListDirBegin();
                string fileName = dir.GetNext();
                int index = 0;

                while (fileName != "")
                {
                    if (!dir.CurrentIsDir() && fileName.EndsWith(".json"))
                    {
                        using var file = FileAccess.Open($"{rutaModelos}/{fileName}", FileAccess.ModeFlags.Read);
                        if (file != null)
                        {
                            string contenidoJson = file.GetAsText();
                            var json = new Json();
                            
                            if (json.Parse(contenidoJson) == Error.Ok)
                            {
                                var data = json.Data.AsGodotDictionary();
                                string nombreModelo = data.ContainsKey("nombre") ? (string)data["nombre"] : fileName.Replace(".json", "");
                                
                                ModelSelector.AddItem(nombreModelo, index);
                                _rutasModelos[index] = $"{rutaModelos}/{fileName}";
                                index++;
                            }
                        }
                    }
                    fileName = dir.GetNext();
                }
                
                if (ModelSelector.ItemCount == 0)
                {
                    ModelSelector.AddItem("Sin modelos instalados", 0);
                    ModelSelector.Disabled = true;
                }
            }
            else
            {
                GD.PrintErr($"[SISTEMA] No se encontró la carpeta de modelos en: {rutaModelos}");
            }
        }

        private void OnModelSelected(long index)
        {
            int intIndex = (int)index;
            if (_rutasModelos.ContainsKey(intIndex))
            {
                string rutaJsonSeleccionada = _rutasModelos[intIndex];
                GD.Print($"[IA] Preparando modelo desde configuración: {rutaJsonSeleccionada}");
                
            }
        }
    }
    
}