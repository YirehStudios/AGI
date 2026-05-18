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
        [Export] public PanelContainer LoadingOverlay;
        [Export] public ProgressBar SwapProgressBar;
        private global::System.Collections.Generic.Dictionary<int, string> _rutasModelos = new global::System.Collections.Generic.Dictionary<int, string>();

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
            if (WelcomeOverlay != null)
    {
        WelcomeOverlay.Visible = false;
    }

    TextInputField.Text = string.Empty;

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
                                
                                ModelSelector.AddItem(nombreModelo);
                                _rutasModelos[ModelSelector.GetItemCount() - 1] = $"{rutaModelos}/{fileName}";
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
                else
                {
                    var configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
                    bool matchFound = false;

                    if (configManager != null && !string.IsNullOrEmpty(configManager.ActiveProfilePath))
                    {
                        foreach (var kvp in _rutasModelos)
                        {
                            if (ProjectSettings.GlobalizePath(kvp.Value) == configManager.ActiveProfilePath)
                            {
                                ModelSelector.Select(ModelSelector.GetItemIndex(kvp.Key));
                                matchFound = true;
                                break;
                            }
                        }
                    }

                    if (!matchFound)
                    {
                        ModelSelector.Select(0);
                    }
                }
            }
            else
            {
                GD.PrintErr($"[SISTEMA] No se encontró la carpeta de modelos en: {rutaModelos}");
            }
        }
        

        private async void OnModelSelected(long index)
        {
            int itemId = ModelSelector.GetItemId((int)index);
            if (_rutasModelos.TryGetValue(itemId, out string rutaJsonSeleccionada))
            {
                string globalPath = ProjectSettings.GlobalizePath(rutaJsonSeleccionada);
                GD.Print($"[IA] Preparando modelo desde configuración: {globalPath}");
                
                try
                {
                    if (TextInputField != null) TextInputField.Editable = false;
                    if (SendButton != null) SendButton.Disabled = true;

                    if (LoadingOverlay != null) LoadingOverlay.Visible = true;
                    if (SwapProgressBar != null) SwapProgressBar.Value = 0;

                    string json = global::System.IO.File.ReadAllText(globalPath);
                    var configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
                    
                    if (configManager != null)
                    {
                        var profile = global::System.Text.Json.JsonSerializer.Deserialize<Logic.System.Config.ConfigManager.ModelProfile>(json);
                        configManager.ActiveProfile = profile;
                        configManager.ActiveProfilePath = globalPath;

                        var backend = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");

                        if (profile.Tipo == 2) // LocalHost
                        {
                            configManager.CurrentMode = Logic.System.Config.ConfigManager.AppMode.LocalHost;
                            if (backend != null)
                            {
                                Logic.Backend.BackendLauncher.BuildLogReceivedEventHandler onLogReceived = (log) => {
                                    if (SwapProgressBar != null && SwapProgressBar.Value < 90) SwapProgressBar.Value += 5;
                                };

                                backend.BuildLogReceived += onLogReceived;

                                backend.TerminateOrphanedResources();
                                backend.StartBackend();
                                await ToSignal(backend, Logic.Backend.BackendLauncher.SignalName.BackendReady);
                                
                                backend.BuildLogReceived -= onLogReceived;
                                
                                if (SwapProgressBar != null) SwapProgressBar.Value = 100;
                            }
                        }
                        else if (profile.Tipo == 3) // Cloud API
                        {
                            configManager.CurrentMode = Logic.System.Config.ConfigManager.AppMode.CloudAPI;
                            if (backend != null)
                            {
                                backend.TerminateOrphanedResources();
                            }
                            
                            if (SwapProgressBar != null)
                            {
                                SwapProgressBar.Value = 50;
                                await ToSignal(GetTree().CreateTimer(0.3f), "timeout");
                                SwapProgressBar.Value = 100;
                            }
                        }

                        if (LoadingOverlay != null) LoadingOverlay.Visible = false;


                        GD.Print($"[IA] Perfil de modelo '{profile.Nombre}' cargado exitosamente.");
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[ERROR] Falla al cargar el perfil de modelo: {ex.Message}");
                    if (LoadingOverlay != null) LoadingOverlay.Visible = false;
                    
                    var errorMsg = new RichTextLabel();
                    errorMsg.BbcodeEnabled = true;
                    errorMsg.Text = $"[center][color=red][ERROR] Falla al cambiar modelo: {ex.Message}[/color][/center]";
                    errorMsg.FitContent = true;
                    MessagesContainer.AddChild(errorMsg);
                    ScrollToBottom();
                }
                finally
                {
                    if (TextInputField != null) TextInputField.Editable = true;
                    if (SendButton != null) SendButton.Disabled = false;
                }
            }
        }

        /// <summary>
        /// Processes tool execution tracking to update the current message UI container based on the current active MCP schema key.
        /// </summary>
        private void OnBotToolExecutionStarted(string toolName)
        {
            if (_mensajeBotActual == null) return;
            
            string accionTexto = "Pensando";
            switch(toolName)
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
                if (inputs.ContainsKey("_raw_"))
                {
                    finalJson = inputs["_raw_"].Text;
                }
                else
                {
                    var resultArgs = new global::System.Collections.Generic.Dictionary<string, string>();
                    foreach(var kvp in inputs) resultArgs[kvp.Key] = kvp.Value.Text;
                    
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