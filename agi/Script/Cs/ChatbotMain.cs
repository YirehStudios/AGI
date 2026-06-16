using Godot;
using System;
using System.IO;
using System.Collections.Generic;
using global::System.Threading.Tasks;

namespace Logic.UI
{
    public partial class ChatbotMain : Control
    {
        [Export] public ScrollContainer ChatScrollContainer;
        [Export] public VBoxContainer MessagesContainer;
        [Export] public TextContainer TextInputField;
        [Export] public float MinInputHeight = 45f;
        [Export] public float MaxInputHeight = 150f;
        [Export] public Button SendButton;

        private List<string> _attachedFiles = new List<string>();

        [Export] public PackedScene EscenaMensajeUsuario;
        [Export] public PackedScene EscenaMensajeBot;

        [ExportCategory("Attachments UI")]
        [Export] public Button AttachmentMenuBtn;
        [Export] public FileDialog AttachmentFileDialog;

        [ExportCategory("Nodes Cheados Dinamicamente")]
        [Export] public Button ToolsMenuButton;
        [Export] public Control ToolsMenuPanel;
        [Export] public Panel panel;
        [Export] public VBoxContainer MenuLayout;
        [Export] public Label Label;
        [Export] public PanelContainer InputPanel;


        
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
                TextInputField.Connect("TextChanged", Callable.From(OnInputTextChanged));
            }

            var canDropCall = Callable.From<Vector2, Variant, bool>(_CanDropDataForward);
            var dropCall = Callable.From<Vector2, Variant>(_DropDataForward);
            
            if (TextInputField != null) TextInputField.Call("SetInputDragForwarding", new Callable(), canDropCall, dropCall);
            if (ChatScrollContainer != null) 
            {
                ChatScrollContainer.SetDragForwarding(new Callable(), canDropCall, dropCall);
                ChatScrollContainer.GetVScrollBar().Modulate = new Color(1, 1, 1, 0);
            }
            if (MessagesContainer != null) MessagesContainer.SetDragForwarding(new Callable(), canDropCall, dropCall);
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
            var toolsMenuButton = ToolsMenuButton;
            var toolsMenuPanel = ToolsMenuPanel;


            if (ToolsMenuPanel != null && ToolsMenuButton != null)
            {
                ToolsMenuPanel.Visible = false;
                ToolsMenuButton.Pressed += () =>
                {
                    GD.Print("ToolsMenuButton clicked! Toggling panel visibility.");
                    ToolsMenuPanel.Visible = !ToolsMenuPanel.Visible;
                };
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

            SetupAttachmentMenu();
            // Dynamically load active messages from global ChatManager memory on startup
            LoadActiveMessagesIntoUI();
        }

        private void SetupAttachmentMenu()
        {

            if (AttachmentMenuBtn != null && AttachmentFileDialog != null)
            {
                var fd = AttachmentFileDialog;
                
                fd.FileMode = FileDialog.FileModeEnum.OpenFiles;
                fd.Access = FileDialog.AccessEnum.Filesystem;
                fd.UseNativeDialog = true;
                
                fd.ClearFilters();
                fd.AddFilter("*.txt, *.md, *.json, *.xml, *.cs, *.py, *.js, *.html, *.css, *.gd, *.cpp, *.h", "Archivos de Texto y Código");
                fd.AddFilter("*.pdf", "Documentos PDF");
                fd.AddFilter("*.xlsx, *.xls, *.csv", "Hojas de Cálculo");
                fd.AddFilter("*.doc, *.docx, *.odt", "Documentos de Texto");
                
                // Disconnect to avoid multiple triggers if Setup is called again
                var callable = new Callable(this, MethodName.HandleFilesDropped);
                if (fd.IsConnected(FileDialog.SignalName.FilesSelected, callable))
                {
                    fd.Disconnect(FileDialog.SignalName.FilesSelected, callable);
                }
                fd.Connect(FileDialog.SignalName.FilesSelected, callable);
                
                AttachmentMenuBtn.Pressed += () => {
                    fd.PopupCentered(new Vector2I(800, 600));
                };
            }

        }

        private void AttachFileToMessage(string filePath)
        {
            if (_attachedFiles.Contains(filePath)) return;
            _attachedFiles.Add(filePath);
            
            if (TextInputField != null)
            {
                string fName = Path.GetFileName(filePath);
                string bbcode = $"[file]{fName}[/file]    "; // Agregamos 4 espacios para padding del chip visual
                TextInputField.InsertTextAtCaret(bbcode);
            }
            else
            {
                string fName = Path.GetFileName(filePath);
                TextInputField.MarkdownText += $"[file]{fName}[/file]    ";
            }
        }

                private async void HandleFilesDropped(string[] files)
        {
            var mainApp = GetNodeOrNull<Node>("/root/MainApp");
            if (mainApp == null) mainApp = GetParent().GetParent().GetParent().GetParent(); // Fallback to relative path if not root
            
            var filesPanel = mainApp?.GetNodeOrNull<Control>("FilesOverlay/FilesPanel");
            bool droppedInFilesPanel = false;
            if (filesPanel != null && filesPanel.Visible)
            {
                var mousePos = GetViewport().GetMousePosition();
                if (filesPanel.GetGlobalRect().HasPoint(mousePos))
                {
                    droppedInFilesPanel = true;
                }
            }

            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            string chatId = chatManager?.CurrentSession?.SessionName ?? "default_chat";
            string historyDir = Path.Combine(
                global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData),
                "agi", "history", chatId
            );

            if (!global::System.IO.Directory.Exists(historyDir))
            {
                global::System.IO.Directory.CreateDirectory(historyDir);
            }

            foreach (var file in files)
            {
                string targetPath = Path.Combine(historyDir, Path.GetFileName(file));
                try
                {
                    if (file != targetPath)
                    {
                        global::System.IO.File.Copy(file, targetPath, true);
                    }
                    _ = RunExtractor(targetPath);
                    AttachFileToMessage(targetPath);
                }
                catch (global::System.Exception ex)
                {
                    GD.PrintErr($"Failed to copy dropped file: {ex.Message}");
                }
            }
            
            if (droppedInFilesPanel && mainApp != null && mainApp.HasMethod("OpenFilesPanelIfNotOpen"))
            {
                mainApp.Call("OpenFilesPanelIfNotOpen");
            }
        }

        private async global::System.Threading.Tasks.Task RunExtractor(string targetPath)
        {
            string ext = Path.GetExtension(targetPath).ToLower();
            string[] supportedExts = { ".pdf", ".xlsx", ".xls", ".csv", ".mp4", ".avi", ".mkv", ".mov", ".mp3", ".wav", ".m4a" };
            
            if (global::System.Array.IndexOf(supportedExts, ext) >= 0)
            {
                var envManager = GetNodeOrNull<global::EnvironmentManager>("/root/EnvironmentManager");
                if (envManager?.Bridge != null)
                {
                    string scriptPath = Path.Combine(envManager.BinPath, "file_extractor.py");
                    if (!global::System.IO.File.Exists(scriptPath))
                    {
                        string resPath = ProjectSettings.GlobalizePath("res://Script/Cs/System/Drivers/file_extractor.py");
                        if (global::System.IO.File.Exists(resPath)) scriptPath = resPath;
                    }
                    
                    string outPath = targetPath + ".extracted.txt";
                    if (ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".mp3" || ext == ".wav" || ext == ".m4a")
                    {
                        outPath = targetPath + "_meta.json";
                    }

                    string args = $"\"{targetPath}\" \"{outPath}\"";
                    var startInfo = envManager.Bridge.ConfigurePythonMicroservice(scriptPath, args, ProjectSettings.GlobalizePath("res://"));
                    startInfo.CreateNoWindow = true;
                    startInfo.UseShellExecute = false;
                    
                    try
                    {
                        using (var process = new global::System.Diagnostics.Process { StartInfo = startInfo })
                        {
                            process.Start();
                            await global::System.Threading.Tasks.Task.Run(() => process.WaitForExit(15000));
                        }
                        
                        // Refrescar panel de archivos si se requiere para que detecte el cambio en el historial
                        var filesPanel = GetNodeOrNull<Node>("/root/MainApp/FilesOverlay/FilesPanel/Files");
                        if (filesPanel != null && filesPanel.HasMethod("LoadWorkspace"))
                        {
                            filesPanel.CallDeferred("LoadWorkspace");
                        }
                    }
                    catch (global::System.Exception ex)
                    {
                        GD.PrintErr($"Failed to run Python file extractor from Chatbot: {ex.Message}");
                    }
                }
            }
        }

        private bool IsAllowedExtension(string path)
        {
            string ext = global::System.IO.Path.GetExtension(path).ToLower();
            string[] allowed = { ".txt", ".md", ".json", ".xml", ".cs", ".py", ".js", ".html", ".css", ".gd", ".cpp", ".h", ".pdf", ".xlsx", ".xls", ".csv", ".doc", ".docx", ".odt" };
            return global::System.Array.IndexOf(allowed, ext) >= 0;
        }

        public override bool _CanDropData(Vector2 atPosition, Variant data)
        {
            if (data.VariantType == Variant.Type.Dictionary)
            {
                var dict = data.AsGodotDictionary();
                if (dict.ContainsKey("files"))
                {
                    var filePaths = dict["files"].AsStringArray();
                    foreach (var path in filePaths)
                    {
                        if (IsAllowedExtension(path)) return true;
                    }
                }
            }
            return false;
        }

        public override void _DropData(Vector2 atPosition, Variant data)
        {
            if (data.VariantType == Variant.Type.Dictionary)
            {
                var dict = data.AsGodotDictionary();
                if (dict.ContainsKey("files"))
                {
                    var filePaths = dict["files"].AsStringArray();
                    foreach (var path in filePaths)
                    {
                        if (IsAllowedExtension(path))
                        {
                            AttachFileToMessage(path);
                        }
                    }
                }
            }
        }

        private bool _CanDropDataForward(Vector2 atPos, Variant data) => _CanDropData(atPos, data);
        private void _DropDataForward(Vector2 atPos, Variant data) => _DropData(atPos, data);

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
            if (_isWaitingForResponse)
            {
                var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
                if (chatManager != null)
                {
                    chatManager.CancelGeneration();
                    
                    // Forcefully terminate UI waiting state so user can type again
                    _isWaitingForResponse = false;
                    if (SendButton != null)
                    {
                        SendButton.Disabled = false;
                        SendButton.Modulate = new Color(1, 1, 1, 1);
                    }
                    if (TextInputField != null) TextInputField.SetEditable(true);
                    
                    var cancelMsg = EscenaMensajeBot.Instantiate<Logic.UI.Components.MensajeBotUI>();
                    if (MessagesContainer.Theme != null) cancelMsg.Theme = MessagesContainer.Theme;
                    MessagesContainer.AddChild(cancelMsg);
                    cancelMsg.ConfigurarMensaje("\n[color=red][i]Generación detenida por el usuario.[/i][/color]\n");
                    ScrollToBottom();
                }
                return;
            }

            if (TextInputField != null)
            {
                _ = ProcessMessage(TextInputField.MarkdownText);
            }
        }

        private void OnTextInputGuiInput(InputEvent @event)
        {
            if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Enter && !keyEvent.ShiftPressed)
            {
                GetViewport().SetInputAsHandled();
                _ = ProcessMessage(TextInputField.MarkdownText);
            }
        }

        private PopupMenu _autocompletePopup;
        private int _atSymbolIndex = -1;

        private void OnInputTextChanged()
        {
            if (TextInputField == null) return;

            // Sync attached files with text content
            string text = TextInputField.MarkdownText;
            var toRemove = new List<string>();
            foreach (var path in _attachedFiles)
            {
                string tag = $"[file]{Path.GetFileName(path)}[/file]";
                if (!text.Contains(tag))
                {
                    toRemove.Add(path);
                }
            }
            foreach (var path in toRemove)
            {
                _attachedFiles.Remove(path);
            }

            int totalLines = 0;
            for (int i = 0; i < TextInputField.GetLineCount(); i++)
            {
                totalLines += 1 + TextInputField.GetLineWrapCount(i);
            }

            float contentHeight = (totalLines * 24f) + 20f;
            contentHeight = Mathf.Clamp(contentHeight, MinInputHeight, MaxInputHeight);

            TextInputField.CustomMinimumSize = new Vector2(TextInputField.CustomMinimumSize.X, contentHeight);

            HandleAutocomplete();
        }

        private void HandleAutocomplete()
        {
            if (TextInputField == null) return;
            string text = TextInputField.MarkdownText;
            int caretCol = TextInputField.GetCaretColumn();
            
            // Find if we are typing a word starting with @
            int lastAt = text.LastIndexOf('@', Math.Max(0, caretCol - 1));
            
            if (lastAt != -1 && (lastAt == 0 || char.IsWhiteSpace(text[lastAt - 1])))
            {
                string searchStr = text.Substring(lastAt + 1, caretCol - lastAt - 1).ToLower();
                // Avoid matching if there's a space after @
                if (searchStr.Contains(" "))
                {
                    HideAutocomplete();
                    return;
                }
                
                ShowAutocomplete(searchStr, lastAt);
            }
            else
            {
                HideAutocomplete();
            }
        }

        private void ShowAutocomplete(string searchStr, int atIndex)
        {
            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            string chatId = chatManager?.CurrentSession?.SessionName ?? "default_chat";
            string historyDir = Path.Combine(
                global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData),
                "agi", "history", chatId
            );

            if (!Directory.Exists(historyDir))
            {
                HideAutocomplete();
                return;
            }

            var files = Directory.GetFiles(historyDir);
            var matches = new List<string>();
            foreach (var f in files)
            {
                string fName = Path.GetFileName(f);
                if (fName == "id.txt") continue;
                if (fName.ToLower().Contains(searchStr))
                {
                    matches.Add(fName);
                }
            }

            if (matches.Count == 0)
            {
                HideAutocomplete();
                return;
            }

            if (_autocompletePopup == null)
            {
                _autocompletePopup = new PopupMenu();
                _autocompletePopup.IndexPressed += OnAutocompleteSelected;
                AddChild(_autocompletePopup);
            }

            _autocompletePopup.Clear();
            foreach (var match in matches)
            {
                _autocompletePopup.AddItem(match);
            }

            _atSymbolIndex = atIndex;

            // Position popup near the caret roughly
            var rect = TextInputField.GetGlobalRect();
            Vector2 popupPos = new Vector2(rect.Position.X + 20, rect.Position.Y - _autocompletePopup.Size.Y);
            _autocompletePopup.Position = new Vector2I((int)popupPos.X, (int)popupPos.Y);
            _autocompletePopup.Popup();
            _autocompletePopup.SetFocusedItem(0);
        }

        private void HideAutocomplete()
        {
            if (_autocompletePopup != null && _autocompletePopup.Visible)
            {
                _autocompletePopup.Hide();
            }
            _atSymbolIndex = -1;
        }

        private void OnAutocompleteSelected(long index)
        {
            if (_autocompletePopup == null || TextInputField == null || _atSymbolIndex == -1) return;
            string selectedFile = _autocompletePopup.GetItemText((int)index);
            
            string currentText = TextInputField.MarkdownText;
            int caretCol = TextInputField.GetCaretColumn();
            
            string beforeAt = currentText.Substring(0, _atSymbolIndex);
            
            string afterCaret = currentText.Substring(caretCol);
            TextInputField.MarkdownText = beforeAt + afterCaret;
            TextInputField.SetCaretColumn(beforeAt.Length);
            TextInputField.GrabFocus();

            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            string chatId = chatManager?.CurrentSession?.SessionName ?? "default_chat";
            string historyDir = Path.Combine(
                global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData),
                "agi", "history", chatId
            );
            string targetPath = Path.Combine(historyDir, selectedFile);
            AttachFileToMessage(targetPath);
        }

        /// <summary>
        /// Orchestrates the comprehensive lifecycle of a user message submission using components.
        /// </summary>
        private async Task ProcessMessage(string text)
        {
            if (_attachedFiles.Count > 0)
            {
                _attachedFiles.Clear();
            }

            if (string.IsNullOrWhiteSpace(text) || _isWaitingForResponse) return;

            var configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");

            if (ToggleToolMCP != null && ToggleToolMCP.ButtonPressed && configManager != null && chatManager != null)
            {
                if (string.IsNullOrEmpty(configManager.PersistedWorkspacePath) && string.IsNullOrEmpty(chatManager.CurrentSession.WorkspacePath))
                {
                }
            }

            _isWaitingForResponse = true;

            TextInputField.MarkdownText = string.Empty;
            TextInputField.CustomMinimumSize = new Vector2(TextInputField.CustomMinimumSize.X, MinInputHeight);

            if (SendButton != null) 
            {
                SendButton.Disabled = false; // Mantenemos habilitado para poder usarlo como STOP
                SendButton.Modulate = new Color(0.8f, 0.2f, 0.2f, 1.0f); // Color rojo para indicar "Stop"
            }

            var nuevoMsgUsuario = EscenaMensajeUsuario.Instantiate<Logic.UI.Components.MensajeUsuarioUI>();

            if (MessagesContainer.Theme != null) nuevoMsgUsuario.Theme = MessagesContainer.Theme;

            MessagesContainer.AddChild(nuevoMsgUsuario);
            nuevoMsgUsuario.ConfigurarMensaje(text);

            ScrollToBottom(true);

            // Capture Mode (Default to 1: Focus)
            int selectedMode = ModeSelector != null ? ModeSelector.Selected : 1;

            // Build Active Tools List
            var activeTools = new List<string>();
            if (ToggleToolTime != null && ToggleToolTime.ButtonPressed) activeTools.Add("Time");
            if (ToggleToolWebSearch != null && ToggleToolWebSearch.ButtonPressed) activeTools.Add("Web Search");
            if (ToggleToolMCP != null && ToggleToolMCP.ButtonPressed) activeTools.Add("MCP");

            chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager != null)
            {
                await chatManager.SendToAI(text, selectedMode, activeTools);
            }
            else
            {
                _isWaitingForResponse = false;
                if (SendButton != null) 
                {
                    SendButton.Disabled = false;
                    SendButton.Modulate = new Color(1, 1, 1, 1);
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

            ScrollToBottom(true);
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
            if (SendButton != null)
            {
                SendButton.Disabled = false;
                SendButton.Modulate = new Color(1, 1, 1, 1);
            }
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
            if (TextInputField != null) TextInputField.SetEditable(true);
            if (SendButton != null)
            {
                SendButton.Disabled = false;
                SendButton.Modulate = new Color(1, 1, 1, 1);
            }

            if (_mensajeBotActual != null)
            {
                _mensajeBotActual.FinalizarRespuesta();
            }
        }

        private bool _isScrolling = false;
        private async void ScrollToBottom(bool force = false)
        {
            if (_isScrolling && !force) return;
            _isScrolling = true;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ChatScrollContainer != null)
            {
                ScrollBar vScroll = ChatScrollContainer.GetVScrollBar();
                
                if (force)
                {
                    vScroll.Value = vScroll.MaxValue;
                    _isScrolling = false;
                    return;
                }
                
                // Smart auto-scroll: Si el usuario ha scrolleado hacia arriba más de 100px, no lo forzamos a bajar.
                double distanceToBottom = vScroll.MaxValue - vScroll.Page - vScroll.Value;
                if (distanceToBottom < 150) // Tolerancia un poco mayor
                {
                    vScroll.Value = vScroll.MaxValue;
                }
            }
            _isScrolling = false;
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
            var toolsMenuPanel = ToolsMenuPanel;
            var toolsPanelChild = panel;

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
            var toolsLabel = Label;
            if (toolsLabel != null)
            {
                toolsLabel.AddThemeColorOverride("font_color", isDark ? new Color(0.9f, 0.9f, 0.95f) : new Color(0.15f, 0.15f, 0.2f));
            }

            var menuLayout = MenuLayout;
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

            if (ModeSelector != null)
            {
                var popup = ModeSelector.GetPopup();
                if (popup != null)
                {
                    Color popupIconColor = isDark ? new Color(0.85f, 0.85f, 0.9f) : new Color(0.15f, 0.15f, 0.2f);
                    for (int i = 0; i < popup.ItemCount; i++)
                    {
                        popup.SetItemIconModulate(i, popupIconColor);
                    }
                }
            }

            // 5. InputPanel Container Style (Outer background of input area)
            var inputPanel = InputPanel;
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
            var toolsMenuButton = ToolsMenuButton;
            if (toolsMenuButton != null)
            {
                Color btnColor = isDark ? new Color(0.85f, 0.85f, 0.9f) : new Color(0.2f, 0.2f, 0.25f);
                Color btnHoverColor = isDark ? new Color(1.0f, 1.0f, 1.0f) : new Color(0.05f, 0.05f, 0.1f);

                toolsMenuButton.AddThemeColorOverride("icon_normal_color", btnColor);
                toolsMenuButton.AddThemeColorOverride("icon_hover_color", btnHoverColor);
                toolsMenuButton.AddThemeColorOverride("icon_pressed_color", new Color(0.274f, 0.623f, 0.924f));
                toolsMenuButton.AddThemeColorOverride("icon_focus_color", btnHoverColor);
            }
            
            if (AttachmentMenuBtn != null)
            {
                Color btnColor = isDark ? new Color(0.85f, 0.85f, 0.9f) : new Color(0.2f, 0.2f, 0.25f);
                Color btnHoverColor = isDark ? new Color(1.0f, 1.0f, 1.0f) : new Color(0.05f, 0.05f, 0.1f);

                AttachmentMenuBtn.AddThemeColorOverride("font_color", btnColor);
                AttachmentMenuBtn.AddThemeColorOverride("font_hover_color", btnHoverColor);
                AttachmentMenuBtn.AddThemeColorOverride("font_pressed_color", new Color(0.274f, 0.623f, 0.924f));
                AttachmentMenuBtn.AddThemeColorOverride("font_focus_color", btnHoverColor);
                AttachmentMenuBtn.AddThemeColorOverride("icon_normal_color", btnColor);
                AttachmentMenuBtn.AddThemeColorOverride("icon_hover_color", btnHoverColor);
                AttachmentMenuBtn.AddThemeColorOverride("icon_pressed_color", new Color(0.274f, 0.623f, 0.924f));
                AttachmentMenuBtn.AddThemeColorOverride("icon_focus_color", btnHoverColor);
            }

            if (ModeSelector != null)
            {
                Color fontColor = isDark ? new Color(0.85f, 0.85f, 0.9f) : new Color(0.2f, 0.2f, 0.25f);
                Color fontHoverColor = isDark ? new Color(1.0f, 1.0f, 1.0f) : new Color(0.05f, 0.05f, 0.1f);

                ModeSelector.AddThemeColorOverride("font_color", fontColor);
                ModeSelector.AddThemeColorOverride("font_hover_color", fontHoverColor);
                ModeSelector.AddThemeColorOverride("font_pressed_color", new Color(0.274f, 0.623f, 0.924f));
                ModeSelector.AddThemeColorOverride("font_focus_color", fontHoverColor);
                
                // Icon styling for the OptionButton
                ModeSelector.AddThemeColorOverride("icon_normal_color", fontColor);
                ModeSelector.AddThemeColorOverride("icon_hover_color", fontHoverColor);
                ModeSelector.AddThemeColorOverride("icon_pressed_color", new Color(0.274f, 0.623f, 0.924f));
                ModeSelector.AddThemeColorOverride("icon_focus_color", fontHoverColor);
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
        private void RenderDynamicBlocks(Logic.UI.Components.MensajeBotUI bubble, string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return;
            if (bubble == null) return;
            if (CodeBlockTemplate == null) return;

            Control messageLayout = bubble.FindChild("MessageLayout", true, false) as Control;
            RichTextLabel originalMarkdownNode = bubble.FindChild("MessageBody", true, false) as RichTextLabel;
            if (messageLayout == null || originalMarkdownNode == null) return;

            if (!rawText.Contains("```"))
            {
                originalMarkdownNode.Visible = true;
                originalMarkdownNode.Text = rawText;
                originalMarkdownNode.Set("markdown_text", rawText);
                return;
            }

            originalMarkdownNode.Visible = false;

            string[] separator = { "```" };
            string[] blocks = rawText.Split(separator, StringSplitOptions.None);

            // 1. Ensure we have exactly the right number of dynamic controls instantiated
            while (bubble.DynamicBlocks.Count < blocks.Length)
            {
                int blockIndex = bubble.DynamicBlocks.Count;
                if (blockIndex % 2 == 0)
                {
                    RichTextLabel textBlock = (RichTextLabel)originalMarkdownNode.Duplicate();
                    textBlock.Visible = true;
                    messageLayout.AddChild(textBlock);
                    bubble.DynamicBlocks.Add(textBlock);
                }
                else
                {
                    PanelContainer newCodeBlock = (PanelContainer)CodeBlockTemplate.Duplicate();
                    newCodeBlock.Visible = true;
                    newCodeBlock.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

                    // Theme styling
                    StyleBoxFlat codePanelStyle = new StyleBoxFlat();
                    codePanelStyle.CornerRadiusTopLeft = 12;
                    codePanelStyle.CornerRadiusTopRight = 12;
                    codePanelStyle.CornerRadiusBottomLeft = 12;
                    codePanelStyle.CornerRadiusBottomRight = 12;
                    codePanelStyle.SetContentMarginAll(14);
                    codePanelStyle.BgColor = new Color(0.09f, 0.09f, 0.11f);
                    codePanelStyle.BorderWidthLeft = 1;
                    codePanelStyle.BorderWidthTop = 1;
                    codePanelStyle.BorderWidthRight = 1;
                    codePanelStyle.BorderWidthBottom = 1;
                    codePanelStyle.BorderColor = new Color(0.20f, 0.20f, 0.25f, 0.6f);
                    codePanelStyle.ShadowColor = new Color(0, 0, 0, 0.25f);
                    codePanelStyle.ShadowSize = 6;
                    newCodeBlock.AddThemeStyleboxOverride("panel", codePanelStyle);

                    var codeEdits = newCodeBlock.FindChildren("*", "CodeEdit", true, false);
                    if (codeEdits.Count > 0 && codeEdits[0] is CodeEdit editNode)
                    {
                        StyleBoxFlat editStyle = new StyleBoxFlat();
                        editStyle.BgColor = new Color(0, 0, 0, 0);
                        editStyle.BorderWidthLeft = 0;
                        editStyle.BorderWidthTop = 0;
                        editStyle.BorderWidthRight = 0;
                        editStyle.BorderWidthBottom = 0;
                        editStyle.SetContentMarginAll(8);
                        
                        editNode.AddThemeStyleboxOverride("normal", editStyle);
                        editNode.AddThemeStyleboxOverride("focus", editStyle);
                        editNode.AddThemeStyleboxOverride("read_only", editStyle);

                        editNode.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.95f));
                        editNode.AddThemeColorOverride("font_readonly_color", new Color(0.92f, 0.92f, 0.95f));
                        editNode.AddThemeColorOverride("background_color", new Color(0, 0, 0, 0));
                        editNode.AddThemeFontSizeOverride("font_size", 14);
                        editNode.WrapMode = TextEdit.LineWrappingMode.Boundary;

                        if (editNode.GetHScrollBar() != null) editNode.GetHScrollBar().Visible = false;
                        if (editNode.GetVScrollBar() != null) editNode.GetVScrollBar().Visible = false;

                        var highlighter = new CodeHighlighter();
                        highlighter.NumberColor = new Color(0.92f, 0.77f, 0.51f);
                        highlighter.SymbolColor = new Color(0.80f, 0.80f, 0.80f);
                        highlighter.FunctionColor = new Color(0.38f, 0.69f, 0.93f);
                        highlighter.MemberVariableColor = new Color(0.48f, 0.82f, 0.64f);

                        highlighter.AddColorRegion("\"", "\"", new Color(0.48f, 0.75f, 0.48f), false);
                        highlighter.AddColorRegion("'", "'", new Color(0.48f, 0.75f, 0.48f), false);
                        highlighter.AddColorRegion("#", "", new Color(0.45f, 0.45f, 0.50f), true);
                        highlighter.AddColorRegion("//", "", new Color(0.45f, 0.45f, 0.50f), true);
                        highlighter.AddColorRegion("/*", "*/", new Color(0.45f, 0.45f, 0.50f), false);

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
                        langLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f));
                        langLabel.AddThemeFontSizeOverride("font_size", 12);
                    }

                    var buttons = newCodeBlock.FindChildren("*", "Button", true, false);
                    if (buttons.Count > 0 && buttons[0] is Button copyBtn && codeEdits.Count > 0)
                    {
                        copyBtn.Text = "Copy";
                        copyBtn.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
                        copyBtn.AddThemeColorOverride("font_hover_color", new Color(1.0f, 1.0f, 1.0f));
                        
                        var targetEdit = codeEdits[0] as CodeEdit;
                        copyBtn.Pressed += () =>
                        {
                            DisplayServer.ClipboardSet(targetEdit.Text);
                            copyBtn.Text = "¡Copiado!";
                            GetTree().CreateTimer(1.5f).Timeout += () =>
                            {
                                if (GodotObject.IsInstanceValid(copyBtn)) copyBtn.Text = "Copy";
                            };
                        };
                    }

                    messageLayout.AddChild(newCodeBlock);
                    bubble.DynamicBlocks.Add(newCodeBlock);
                }
            }

            // 2. Update the text/content of each block dynamically
            for (int i = 0; i < blocks.Length; i++)
            {
                string content = blocks[i];
                Control node = bubble.DynamicBlocks[i];

                if (i % 2 == 0)
                {
                    RichTextLabel textBlock = (RichTextLabel)node;
                    textBlock.Set("markdown_text", content.Trim());
                    textBlock.Text = content.Trim();
                }
                else
                {
                    PanelContainer codeBlock = (PanelContainer)node;
                    string language = "code";
                    int firstNewline = content.IndexOf('\n');

                    if (firstNewline != -1 && firstNewline < 20)
                    {
                        language = content.Substring(0, firstNewline).Trim();
                        content = content.Substring(firstNewline + 1);
                    }

                    var labels = codeBlock.FindChildren("*", "Label", true, false);
                    if (labels.Count > 0 && labels[0] is Label langLabel)
                    {
                        langLabel.Text = string.IsNullOrEmpty(language) ? "CODE" : language.ToUpper();
                    }

                    var codeEdits = codeBlock.FindChildren("*", "CodeEdit", true, false);
                    if (codeEdits.Count > 0 && codeEdits[0] is CodeEdit editNode)
                    {
                        editNode.Text = content.Trim();

                        int totalLines = 0;
                        for (int lineIndex = 0; lineIndex < editNode.GetLineCount(); lineIndex++)
                        {
                            totalLines += 1 + editNode.GetLineWrapCount(lineIndex);
                        }

                        float fontHeight = editNode.GetLineHeight();
                        if (fontHeight <= 0) fontHeight = 24f; // Fallback
                        float contentHeight = (totalLines * fontHeight) + 30f;
                        
                        editNode.CustomMinimumSize = new Vector2(editNode.CustomMinimumSize.X, contentHeight);
                        editNode.ScrollPastEndOfFile = false;
                    }
                }
            }
        }

        /// <summary>
        /// Handles global input events to hide the ToolsMenuPanel when clicking outside of it.
        /// </summary>
        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
            {
                var toolsMenuPanel = ToolsMenuPanel;
                var toolsMenuButton = ToolsMenuButton;

                if (toolsMenuPanel != null && toolsMenuPanel.Visible)
                {
                    Vector2 mousePos = GetViewport().GetMousePosition();
                    bool isInsidePanel = toolsMenuPanel.GetGlobalRect().HasPoint(mousePos);
                    bool isInsideButton = toolsMenuButton != null && toolsMenuButton.GetGlobalRect().HasPoint(mousePos);

                    if (!isInsidePanel && !isInsideButton)
                    {
                        toolsMenuPanel.Visible = false;
                    }
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