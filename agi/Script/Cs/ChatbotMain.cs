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
        [Export] public HBoxContainer UserMessageTemplate;
        [Export] public HBoxContainer BotMessageTemplate;
        [Export] public RichTextLabel UserMessageMarkdownNode;
        [Export] public RichTextLabel BotMessageMarkdownNode;
        [Export] public Control BotMessageLayoutNode;
        [Export] public OptionButton ToolSelector; 
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
        [Export] public HBoxContainer BotActionsContainer;
        [Export] public Label BotActions;
        [Export] public Control BotAvatarContainer;
        [Export] public Label LanguageLabel;
        [Export] public CodeEdit CodeEditor;
        [Export] public Button CopyBtn;
        [Export] public TextureRect GotaCuerpo;
        [Export] public TextureRect AlaIzquierda;
        [Export] public TextureRect AlaDerecha;
        [Export] public Control Pupilas;
        [Export] public Control Equis;
        [Export] public Control BottomInputPanel;
        [Export] public Control ChatBackgroundPanel;

        private AudioEffectRecord _recorder;
        private float _silenceTimer = 0.0f;
        private const float SilenceThreshold = 0.05f;
        private bool _isRecording = false;
        private HBoxContainer _currentBotMessageNode;
        private bool _isLiveModeEnabled = true;
        private bool _isWaitingForResponse = false;
        private Godot.Timer _dotsAnimationTimer;
        private string _ttsBuffer = string.Empty;
        private string _fullMessageBuffer = string.Empty;
        private Random _randomGenerator = new Random();

        /// <summary>
        /// Initializes UI component event subscriptions, hides base templates to prevent rendering artifacts, 
        /// and establishes event delegates with global manager singletons via absolute paths.
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
            
            if (UserMessageTemplate != null) UserMessageTemplate.Visible = false;
            if (BotMessageTemplate != null) BotMessageTemplate.Visible = false;
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
        }

        /// <summary>
        /// Evaluates microphone input frame-by-frame by querying the peak volume of the designated recording audio bus.
        /// Triggers recording state transitions based on linear volume thresholds and an accumulated silence timer.
        /// </summary>
        /// <param name="delta">The time elapsed since the previous frame.</param>
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

        /// <summary>
        /// Activates the AudioEffectRecord instance on the audio bus to begin buffering audio data.
        /// </summary>
        private void StartRecording()
        {
            _isRecording = true;
            _recorder.SetRecordingActive(true);
        }

        /// <summary>
        /// Deactivates the recording effect, extracts the buffered AudioStreamWav, serializes it to the local user directory,
        /// and dispatches an asynchronous network request for Speech-To-Text translation.
        /// </summary>
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

        /// <summary>
        /// Receives the parsed string from the Speech-To-Text service and forwards it to the message processing pipeline.
        /// </summary>
        /// <param name="recognizedText">The output string returned from the STT endpoint.</param>
        private void OnSTTCompleted(string recognizedText)
        {
            if (string.IsNullOrWhiteSpace(recognizedText)) return;
            _ = ProcessMessage(recognizedText);
        }

        /// <summary>
        /// Captures the current string from the input field and initiates message processing.
        /// </summary>
        private void OnSendPressed()
        {
            if (TextInputField != null)
            {
                _ = ProcessMessage(TextInputField.Text);
            }
        }

        /// <summary>
        /// Injects a direct string payload into the message processing pipeline.
        /// </summary>
        /// <param name="newText">The raw string to be processed as a user message.</param>
        private void OnTextSubmitted(string newText)
        {
            _ = ProcessMessage(newText);
        }

        /// <summary>
        /// Intercepts GUI events on the text input node to detect unshifted Enter key presses.
        /// Consumes the input event to prevent newline injection and triggers message submission.
        /// </summary>
        /// <param name="event">The input event captured by the Godot input system.</param>
        private void OnTextInputGuiInput(InputEvent @event)
        {
            if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Enter && !keyEvent.ShiftPressed)
            {
                GetViewport().SetInputAsHandled();
                _ = ProcessMessage(TextInputField.Text);
            }
        }

        /// <summary>
        /// Computes the required height of the text input node by calculating line counts and text wraps.
        /// Clamps the resulting value within predefined boundary constraints and applies the custom minimum size vector.
        /// </summary>
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
        /// Orchestrates the comprehensive lifecycle of a user message submission. Clears existing UI states, 
        /// instantiates and injects the user message template into the scene tree, binds copy/minimize delegate actions, 
        /// evaluates debug commands, and routes the payload to the selected backend endpoint.
        /// </summary>
        /// <param name="text">The raw message payload to be processed.</param>
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

            HBoxContainer newUserMsg = (HBoxContainer)UserMessageTemplate.Duplicate();
            string relativePath = UserMessageTemplate.GetPathTo(UserMessageMarkdownNode);
            RichTextLabel userMarkdownLabel = newUserMsg.GetNode<RichTextLabel>(relativePath);
            
            userMarkdownLabel.Set("markdown_text", text);
            newUserMsg.Visible = true;

            Button copyBtn = newUserMsg.GetNodeOrNull<Button>("UserActions/CopyUserBtn");
            Button minimizeBtn = newUserMsg.GetNodeOrNull<Button>("UserActions/MinimizeUserBtn");

            if (copyBtn != null)
            {
                string textToCopy = text;
                copyBtn.Pressed += () => {
                    DisplayServer.ClipboardSet(textToCopy);
                };
            }

            if (minimizeBtn != null)
            {
                bool isMinimized = false;
                minimizeBtn.Pressed += () => {
                    isMinimized = !isMinimized;
                    if (isMinimized)
                    {
                        userMarkdownLabel.CustomMinimumSize = new Vector2(userMarkdownLabel.CustomMinimumSize.X, 30);
                        userMarkdownLabel.FitContent = false;
                        userMarkdownLabel.ClipContents = true;
                        minimizeBtn.Text = "Maximizar";
                    }
                    else
                    {
                        userMarkdownLabel.CustomMinimumSize = new Vector2(userMarkdownLabel.CustomMinimumSize.X, 0);
                        userMarkdownLabel.FitContent = true;
                        userMarkdownLabel.ClipContents = false;
                        minimizeBtn.Text = "Minimizar";
                    }
                };
            }

            MessagesContainer.AddChild(newUserMsg);
            ScrollToBottom();

            if (text.Trim().ToLower() == "/error")
            {
                OnBotStartedThinking(); 
                await ToSignal(GetTree().CreateTimer(1.5f), SceneTreeTimer.SignalName.Timeout);
                TriggerConnectionErrorAnimation(); 
                _isWaitingForResponse = false;
                if (SendButton != null) SendButton.Disabled = false;
                if (TextInputField != null) TextInputField.Editable = true;
                return; 
            }

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
        /// Prepares the scene tree for an incoming bot response. Allocates a new instance of the bot message template, 
        /// initializes UI loading states, and starts the asynchronous animation routine.
        /// </summary>
        private void OnBotStartedThinking()
        {
            _fullMessageBuffer = string.Empty;

            HBoxContainer newBotMsg = (HBoxContainer)BotMessageTemplate.Duplicate();
            string relativePath = BotMessageTemplate.GetPathTo(BotMessageMarkdownNode);
            RichTextLabel botTextLabel = newBotMsg.GetNode<RichTextLabel>(relativePath);
            
            botTextLabel.Set("markdown_text", "");
            newBotMsg.Visible = true;
            MessagesContainer.AddChild(newBotMsg);
            
            _currentBotMessageNode = newBotMsg;

            if (BotActionsContainer != null)
            {
                string containerPath = BotMessageTemplate.GetPathTo(BotActionsContainer);
                HBoxContainer actionsContainer = newBotMsg.GetNodeOrNull<HBoxContainer>(containerPath);
                if (actionsContainer != null) actionsContainer.Visible = true;
            }

            if (BotActions != null)
            {
                string path = BotMessageTemplate.GetPathTo(BotActions);
                Label actionsLabel = newBotMsg.GetNodeOrNull<Label>(path);
                if (actionsLabel != null)
                {
                    actionsLabel.Text = "Pensando";
                    StartDotsAnimation(actionsLabel, "Pensando");
                }
            }
            
            ScrollToBottom();
        }

        /// <summary>
        /// Constructs a synthetic bot response containing embedded multimedia components. 
        /// Dynamically builds layout containers, instantiates video players or texture rects, 
        /// assigns randomly pooled assets, and resolves playback controls at runtime.
        /// </summary>
        /// <param name="prompt">The initial string trigger associated with this response.</param>
        /// <param name="isVideo">Boolean flag dictating the instantiation of a VideoStreamPlayer versus a TextureRect.</param>
        private async Task GenerateMockMediaResponse(string prompt, bool isVideo)
        {
            HBoxContainer newBotMsg = (HBoxContainer)BotMessageTemplate.Duplicate();
            string mdRelativePath = BotMessageTemplate.GetPathTo(BotMessageMarkdownNode);
            RichTextLabel botTextLabel = newBotMsg.GetNode<RichTextLabel>(mdRelativePath);
            string layoutRelativePath = BotMessageTemplate.GetPathTo(BotMessageLayoutNode);
            Control messageLayout = newBotMsg.GetNode<Control>(layoutRelativePath);

            if (BotActionsContainer != null)
            {
                string containerPath = BotMessageTemplate.GetPathTo(BotActionsContainer);
                HBoxContainer actionsContainer = newBotMsg.GetNodeOrNull<HBoxContainer>(containerPath);
                if (actionsContainer != null) actionsContainer.Visible = true;
            }

            Label actionsLabel = null;
            if (BotActions != null)
            {
                string path = BotMessageTemplate.GetPathTo(BotActions);
                actionsLabel = newBotMsg.GetNodeOrNull<Label>(path);
                if (actionsLabel != null)
                {
                    string baseText = isVideo ? "Generando video" : "Generando imagen";
                    actionsLabel.Text = baseText;
                    StartDotsAnimation(actionsLabel, baseText);
                }
            }

            botTextLabel.Set("markdown_text", "");
            newBotMsg.Visible = true;
            MessagesContainer.AddChild(newBotMsg);
            ScrollToBottom();

            await ToSignal(GetTree().CreateTimer(3.0f), SceneTreeTimer.SignalName.Timeout);

            StopDotsAnimation();
            
            if (BotActionsContainer != null)
            {
                string containerPath = BotMessageTemplate.GetPathTo(BotActionsContainer);
                HBoxContainer actionsContainer = newBotMsg.GetNodeOrNull<HBoxContainer>(containerPath);
                if (actionsContainer != null) actionsContainer.Visible = false;
            }

            if (isVideo)
            {
                VBoxContainer videoWrapper = new VBoxContainer();
                videoWrapper.CustomMinimumSize = new Vector2(480, 0);
                videoWrapper.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                videoWrapper.AddThemeConstantOverride("separation", 15);

                AspectRatioContainer aspectContainer = new AspectRatioContainer();
                aspectContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                aspectContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                aspectContainer.Ratio = 1.7778f;
                aspectContainer.CustomMinimumSize = new Vector2(0, 270);

                VideoStreamPlayer videoPlayer = new VideoStreamPlayer();
                videoPlayer.Expand = true;
                videoPlayer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                videoPlayer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                videoPlayer.Autoplay = true;
                videoPlayer.Loop = true;

                VideoStream[] availableVideos = new VideoStream[] { RandomVideo1, RandomVideo2, RandomVideo3, RandomVideo4 };
                VideoStream chosenVideo = null;
                int attempts = 0;
                
                while (chosenVideo == null && attempts < 10)
                {
                    int randomIndex = _randomGenerator.Next(0, 4);
                    chosenVideo = availableVideos[randomIndex];
                    attempts++;
                }

                if (chosenVideo != null) videoPlayer.Stream = chosenVideo;
                
                aspectContainer.AddChild(videoPlayer);
                videoWrapper.AddChild(aspectContainer);

                HBoxContainer controlsLayout = new HBoxContainer();
                controlsLayout.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                controlsLayout.AddThemeConstantOverride("separation", 15);

                Button playPauseBtn = new Button();
                playPauseBtn.Text = "⏸ Pausar";
                
                StyleBoxFlat btnNormal = new StyleBoxFlat();
                btnNormal.BgColor = new Color(0.373f, 0.502f, 0.357f, 1.0f);
                playPauseBtn.AddThemeStyleboxOverride("normal", btnNormal);
                playPauseBtn.Pressed += () => {
                    videoPlayer.Paused = !videoPlayer.Paused;
                    playPauseBtn.Text = videoPlayer.Paused ? "▶ Reproducir" : "⏸ Pausar";
                };

                controlsLayout.AddChild(playPauseBtn);
                videoWrapper.AddChild(controlsLayout);
                messageLayout.AddChild(videoWrapper);
                videoPlayer.Play();
            }
            else
            {
                TextureRect mediaRect = new TextureRect();
                mediaRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                mediaRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                mediaRect.CustomMinimumSize = new Vector2(250, 250);

                Texture2D[] availableImages = new Texture2D[] { RandomImage1, RandomImage2, RandomImage3, RandomImage4 };
                Texture2D chosenImage = null;
                int attempts = 0;
                
                while (chosenImage == null && attempts < 10)
                {
                    int randomIndex = _randomGenerator.Next(0, 4);
                    chosenImage = availableImages[randomIndex];
                    attempts++;
                }

                if (chosenImage != null) mediaRect.Texture = chosenImage;
                messageLayout.AddChild(mediaRect);
            }
            
            ScrollToBottom();
            _isWaitingForResponse = false;
            if (TextInputField != null) TextInputField.Editable = true;
            if (SendButton != null) SendButton.Disabled = false;
            if (TextInputField != null) TextInputField.GrabFocus();
        }

        /// <summary>
        /// Concatenates partial string sequences retrieved asynchronously from the network into the main text buffer, 
        /// triggering a runtime update of the Markdown label component to stream output to the UI.
        /// </summary>
        /// <param name="token">The latest string segment received from the backend generation node.</param>
        private void OnTokenReceived(string token)
        {
            if (_currentBotMessageNode == null) return;

            string relativePath = BotMessageTemplate.GetPathTo(BotMessageMarkdownNode);
            RichTextLabel messageBody = _currentBotMessageNode.GetNode<RichTextLabel>(relativePath);
            
            _fullMessageBuffer += token;
            messageBody.Set("markdown_text", _fullMessageBuffer);
            
            ScrollToBottom();
        }

        /// <summary>
        /// Resolves the bot response state machine by resetting interactable elements, purging active processing timers, 
        /// restoring default avatar modulation, and executing the secondary text-parsing routine for code extraction.
        /// </summary>
        /// <param name="fullResponse">The complete compiled string output from the LLM.</param>
        private void OnBotFinishedSpeaking(string fullResponse)
        {
            _isWaitingForResponse = false;
            if (TextInputField != null) TextInputField.Editable = true;
            if (SendButton != null) SendButton.Disabled = false;

            StopDotsAnimation();

            if (_currentBotMessageNode != null)
            {
                if (BotActionsContainer != null)
                {
                    string path = BotMessageTemplate.GetPathTo(BotActionsContainer);
                    HBoxContainer actionsContainer = _currentBotMessageNode.GetNodeOrNull<HBoxContainer>(path);
                    if (actionsContainer != null) actionsContainer.Visible = false;
                }

                if (BotAvatarContainer != null)
                {
                    string path = BotMessageTemplate.GetPathTo(BotAvatarContainer);
                    Control avatarNode = _currentBotMessageNode.GetNodeOrNull<Control>(path);
                    if (avatarNode != null)
                    {
                        avatarNode.Scale = new Vector2(1.0f, 1.0f);
                        avatarNode.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f);
                    }
                }

                InjectCodeBlocks(_fullMessageBuffer);
            }
        }

        /// <summary>
        /// Parses the raw string payload to identify standard markdown codeblock delimiters. 
        /// Suppresses the generic text node and dynamically interleaves syntax-highlighted code panels 
        /// and distinct text block instances based on parsed array indices.
        /// </summary>
        /// <param name="rawText">The aggregated response string containing potential markdown syntax.</param>
        private void InjectCodeBlocks(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText) || !rawText.Contains("```")) return;
            if (_currentBotMessageNode == null || CodeBlockTemplate == null) return;

            string layoutRelativePath = BotMessageTemplate.GetPathTo(BotMessageLayoutNode);
            Control messageLayout = _currentBotMessageNode.GetNode<Control>(layoutRelativePath);
            
            string mdRelativePath = BotMessageTemplate.GetPathTo(BotMessageMarkdownNode);
            RichTextLabel originalMarkdownNode = _currentBotMessageNode.GetNode<RichTextLabel>(mdRelativePath);
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
                    messageLayout.AddChild(textBlock);
                }
                else 
                {
                    string codeContent = blocks[i];
                    string language = "code";
                    int firstNewline = codeContent.IndexOf('\n');
                    if (firstNewline != -1)
                    {
                        language = codeContent.Substring(0, firstNewline).Trim();
                        codeContent = codeContent.Substring(firstNewline + 1);
                    }

                    PanelContainer newCodeBlock = (PanelContainer)CodeBlockTemplate.Duplicate();
                    newCodeBlock.Visible = true;

                    if (LanguageLabel != null)
                    {
                        string langPath = CodeBlockTemplate.GetPathTo(LanguageLabel);
                        Label langLabel = newCodeBlock.GetNode<Label>(langPath);
                        if (langLabel != null) langLabel.Text = language.ToUpper();
                    }

                    if (CodeEditor != null)
                    {
                        string editorPath = CodeBlockTemplate.GetPathTo(CodeEditor);
                        CodeEdit codeEditor = newCodeBlock.GetNode<CodeEdit>(editorPath);
                        if (codeEditor != null) codeEditor.Text = codeContent.TrimEnd();
                    }

                    if (CopyBtn != null)
                    {
                        string copyBtnPath = CodeBlockTemplate.GetPathTo(CopyBtn);
                        Button copyBtn = newCodeBlock.GetNode<Button>(copyBtnPath);
                        if (copyBtn != null)
                        {
                            string finalCopyContent = codeContent;
                            copyBtn.Pressed += async () => {
                                DisplayServer.ClipboardSet(finalCopyContent);
                                copyBtn.Modulate = new Color(0, 1, 0, 1);
                                await ToSignal(GetTree().CreateTimer(0.5f), SceneTreeTimer.SignalName.Timeout);
                                if (GodotObject.IsInstanceValid(copyBtn))
                                {
                                    copyBtn.Modulate = new Color(1, 1, 1, 1);
                                }
                            };
                        }
                    }

                    messageLayout.AddChild(newCodeBlock);
                }
            }
        }

        /// <summary>
        /// Defers execution until the main thread processes the current frame to ensure Godot's UI layout logic 
        /// has correctly updated dimensions. Afterwards, assigns the maximum value to the VScrollBar to follow content.
        /// </summary>
        private async void ScrollToBottom()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (ChatScrollContainer != null)
            {
                ScrollBar vScroll = ChatScrollContainer.GetVScrollBar();
                vScroll.Value = vScroll.MaxValue;
            }
        }

        /// <summary>
        /// Instantiates a transient Timer object to modulate the `Text` property of a given label on an interval callback.
        /// Simulates asynchronous loading state by appending trailing periods recursively.
        /// </summary>
        /// <param name="label">The target UI node to receive the string updates.</param>
        /// <param name="baseText">The immutable prefix string to prepend to the animation sequence.</param>
        private void StartDotsAnimation(Label label, string baseText)
        {
            if (_dotsAnimationTimer != null) return; 

            _dotsAnimationTimer = new Godot.Timer();
            _dotsAnimationTimer.WaitTime = 0.4f;
            _dotsAnimationTimer.OneShot = false;
            
            int dotCount = 0;
            _dotsAnimationTimer.Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(label)) return;
                dotCount = (dotCount + 1) % 4;
                string dots = new string('.', dotCount);
                label.Text = baseText + dots;
            };
            
            AddChild(_dotsAnimationTimer);
            _dotsAnimationTimer.Start();
        }

        /// <summary>
        /// Executes a safe destruction pattern on the active animation timer. Stops the internal clock and 
        /// pushes the node to the engine's defer queue for memory deallocation.
        /// </summary>
        private void StopDotsAnimation()
        {
            if (_dotsAnimationTimer != null)
            {
                _dotsAnimationTimer.Stop();
                _dotsAnimationTimer.QueueFree();
                _dotsAnimationTimer = null;
            }
        }

        /// <summary>
        /// Orchestrates a multi-track property animation via Godot's Tween API to visually indicate a connection fault.
        /// Sets parallel interpolation parameters to modify positional offsets, transform rotations, and structural modulations
        /// of various UI avatar components synchronously.
        /// </summary>
        public void TriggerConnectionErrorAnimation()
        {
            if (_currentBotMessageNode == null || BotAvatarContainer == null) return;

            string avatarPath = BotMessageTemplate.GetPathTo(BotAvatarContainer);
            Control avatarContainer = _currentBotMessageNode.GetNodeOrNull<Control>(avatarPath);
            if (avatarContainer == null) return;

            TextureRect gotaCuerpo = GotaCuerpo != null ? _currentBotMessageNode.GetNodeOrNull<TextureRect>(BotMessageTemplate.GetPathTo(GotaCuerpo)) : null;
            TextureRect alaIzquierda = AlaIzquierda != null ? _currentBotMessageNode.GetNodeOrNull<TextureRect>(BotMessageTemplate.GetPathTo(AlaIzquierda)) : null;
            TextureRect alaDerecha = AlaDerecha != null ? _currentBotMessageNode.GetNodeOrNull<TextureRect>(BotMessageTemplate.GetPathTo(AlaDerecha)) : null;
            Control pupilas = Pupilas != null ? _currentBotMessageNode.GetNodeOrNull<Control>(BotMessageTemplate.GetPathTo(Pupilas)) : null;
            Control equis = Equis != null ? _currentBotMessageNode.GetNodeOrNull<Control>(BotMessageTemplate.GetPathTo(Equis)) : null;

            if (alaIzquierda != null && WingLeftBaseTexture != null) alaIzquierda.Texture = WingLeftBaseTexture;
            if (alaDerecha != null && WingRightBaseTexture != null) alaDerecha.Texture = WingRightBaseTexture;

            Tween sequence = GetTree().CreateTween();

            sequence.TweenCallback(Callable.From(() => {
                if (pupilas != null) pupilas.Visible = false;
                if (equis != null) equis.Visible = true;
            }));

            sequence.TweenInterval(0.2f); 
            if (gotaCuerpo != null)
            {
                sequence.TweenProperty(gotaCuerpo, "position", gotaCuerpo.Position + new Vector2(0, 15f), 0.5f)
                    .SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
            }

            sequence.TweenInterval(0.1f);
            sequence.SetParallel(true);
            
            if (alaIzquierda != null)
            {
                sequence.TweenProperty(alaIzquierda, "modulate", new Color(1.0f, 0.2f, 0.2f, 1.0f), 0.4f);
                sequence.TweenProperty(alaIzquierda, "position", alaIzquierda.Position + new Vector2(0, 20f), 0.6f)
                    .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                sequence.TweenProperty(alaIzquierda, "rotation_degrees", -75f, 0.6f)
                    .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            }

            if (alaDerecha != null)
            {
                sequence.TweenProperty(alaDerecha, "modulate", new Color(1.0f, 0.2f, 0.2f, 1.0f), 0.4f);
                sequence.TweenProperty(alaDerecha, "position", alaDerecha.Position + new Vector2(0, 20f), 0.6f)
                    .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                sequence.TweenProperty(alaDerecha, "rotation_degrees", 75f, 0.6f)
                    .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            }

            sequence.SetParallel(false);
            
            string relativePath = BotMessageTemplate.GetPathTo(BotMessageMarkdownNode);
            RichTextLabel botTextLabel = _currentBotMessageNode.GetNode<RichTextLabel>(relativePath);
            botTextLabel.Set("markdown_text", "[color=red]Error de conexión crítico con Eden Core...[/color]");
            StopDotsAnimation();
            
            if (BotActions != null)
            {
                string actionsPath = BotMessageTemplate.GetPathTo(BotActions);
                Label actionsLabel = _currentBotMessageNode.GetNodeOrNull<Label>(actionsPath);
                if (actionsLabel != null) actionsLabel.Text = "Error";
            }
        }

        /// <summary>
        /// Injects a system-wide color palette dynamically onto the UI hierarchy. Constructs necessary background matrices
        /// at runtime, instantiates localized instances of `StyleBoxFlat` to manipulate borders and margins independently, 
        /// and delegates a recursive tree traversal to update textual node modulations.
        /// </summary>
        /// <param name="isDark">A boolean evaluating to true for dark palette matrices, false otherwise.</param>
        public void UpdateTheme(bool isDark)
{
    Color primaryText = isDark ? new Color(1f, 1f, 1f) : new Color(0.2f, 0.2f, 0.2f);
    Color inputBg = isDark ? new Color(0.25f, 0.25f, 0.28f) : new Color(1f, 1f, 1f);
    Color mainBg = isDark ? new Color(0.12f, 0.12f, 0.14f) : new Color(0.95f, 0.95f, 0.97f);
    Color bottomBarBg = isDark ? new Color(0.06f, 0.06f, 0.07f) : new Color(0.95f, 0.95f, 0.95f);
    Color botBubbleBg = isDark ? new Color(0.18f, 0.18f, 0.20f) : new Color(0.92f, 0.92f, 0.93f);
    Color userTextColor = new Color(1f, 1f, 1f);

    ColorRect floorBg = GetNodeOrNull<ColorRect>("DynamicFloorBg");
    if (floorBg == null)
    {
        floorBg = new ColorRect();
        floorBg.Name = "DynamicFloorBg";
        AddChild(floorBg);
        MoveChild(floorBg, 0);
    }
    floorBg.Color = mainBg;
    floorBg.SetAnchorsPreset(LayoutPreset.FullRect);
    floorBg.OffsetBottom = 0;
    floorBg.OffsetTop = 0;
    floorBg.OffsetLeft = 0;
    floorBg.OffsetRight = 0;

    if (ChatScrollContainer != null)
    {
        StyleBoxFlat sBg = new StyleBoxFlat();
        sBg.BgColor = mainBg;
        ChatScrollContainer.AddThemeStyleboxOverride("panel", sBg);
    }

    if (BottomInputPanel != null)
    {
        StyleBoxFlat newStyle = new StyleBoxFlat();
        newStyle.BgColor = bottomBarBg;
        
        if (BottomInputPanel is Panel panel)
        {
            if (panel.HasThemeStylebox("panel") && panel.GetThemeStylebox("panel") is StyleBoxFlat existingStyle)
            {
                newStyle = (StyleBoxFlat)existingStyle.Duplicate();
                newStyle.BgColor = bottomBarBg;
                newStyle.BorderWidthBottom = 0;
                newStyle.BorderWidthTop = 0;
                newStyle.BorderWidthLeft = 0;
                newStyle.BorderWidthRight = 0;
            }
            panel.AddThemeStyleboxOverride("panel", newStyle);
        }
        else if (BottomInputPanel is PanelContainer pContainer)
        {
            if (pContainer.HasThemeStylebox("panel") && pContainer.GetThemeStylebox("panel") is StyleBoxFlat existingStyle)
            {
                newStyle = (StyleBoxFlat)existingStyle.Duplicate();
                newStyle.BgColor = bottomBarBg;
                newStyle.BorderWidthBottom = 0;
                newStyle.BorderWidthTop = 0;
                newStyle.BorderWidthLeft = 0;
                newStyle.BorderWidthRight = 0;
            }
            pContainer.AddThemeStyleboxOverride("panel", newStyle);
        }
        else if (BottomInputPanel is ColorRect colorRect)
        {
            colorRect.Color = bottomBarBg;
        }
    }

    if (TextInputField != null)
    {
        TextInputField.AddThemeColorOverride("font_color", primaryText);
        StyleBoxFlat editStyle = new StyleBoxFlat();
        editStyle.BgColor = inputBg;
        editStyle.CornerRadiusTopLeft = 15; 
        editStyle.CornerRadiusTopRight = 15;
        editStyle.CornerRadiusBottomLeft = 15; 
        editStyle.CornerRadiusBottomRight = 15;
        editStyle.ContentMarginLeft = 12;
        editStyle.ContentMarginTop = 12;
        TextInputField.AddThemeStyleboxOverride("normal", editStyle);
        TextInputField.AddThemeStyleboxOverride("focus", editStyle);
    }

    if (ToolSelector != null)
    {
        ToolSelector.Alignment = HorizontalAlignment.Center;
        ToolSelector.AddThemeColorOverride("font_color", primaryText);
        ToolSelector.AddThemeColorOverride("font_hover_color", primaryText);
        ToolSelector.AddThemeColorOverride("font_focus_color", primaryText);
        ToolSelector.AddThemeColorOverride("font_pressed_color", primaryText);
        
        StyleBoxFlat toolStyle = new StyleBoxFlat();
        toolStyle.BgColor = inputBg;
        toolStyle.CornerRadiusTopLeft = 8;
        toolStyle.CornerRadiusTopRight = 8;
        toolStyle.CornerRadiusBottomLeft = 8;
        toolStyle.CornerRadiusBottomRight = 8;
        ToolSelector.AddThemeStyleboxOverride("normal", toolStyle);
        ToolSelector.AddThemeStyleboxOverride("hover", toolStyle);
        ToolSelector.AddThemeStyleboxOverride("focus", toolStyle);
        ToolSelector.AddThemeStyleboxOverride("pressed", toolStyle);

        PopupMenu popup = ToolSelector.GetPopup();
        if (popup != null)
        {
            StyleBoxFlat popupStyle = new StyleBoxFlat();
            popupStyle.BgColor = inputBg;
            popupStyle.CornerRadiusTopLeft = 8;
            popupStyle.CornerRadiusTopRight = 8;
            popupStyle.CornerRadiusBottomLeft = 8;
            popupStyle.CornerRadiusBottomRight = 8;
            popup.AddThemeStyleboxOverride("panel", popupStyle);

            popup.AddThemeColorOverride("font_color", primaryText);
            popup.AddThemeColorOverride("font_hover_color", primaryText);
            popup.AddThemeColorOverride("font_focus_color", primaryText);

            StyleBoxFlat popupHoverStyle = new StyleBoxFlat();
            popupHoverStyle.BgColor = new Color(0.35f, 0.35f, 0.38f);
            popupHoverStyle.CornerRadiusTopLeft = 4;
            popupHoverStyle.CornerRadiusTopRight = 4;
            popupHoverStyle.CornerRadiusBottomLeft = 4;
            popupHoverStyle.CornerRadiusBottomRight = 4;
            popup.AddThemeStyleboxOverride("hover", popupHoverStyle);
        }
    }

    MarginContainer inputMargin = GetNodeOrNull<MarginContainer>("MainContainer/ChatAreaContainer/InputAreaMargin");
    if (inputMargin != null)
    {
        ColorRect marginBg = inputMargin.GetNodeOrNull<ColorRect>("DarkMarginBg");
        if (marginBg == null)
        {
            marginBg = new ColorRect();
            marginBg.Name = "DarkMarginBg";
            inputMargin.AddChild(marginBg);
            inputMargin.MoveChild(marginBg, 0); 
        }
        marginBg.Color = mainBg; 
    }

    if (BotMessageMarkdownNode != null)
        BotMessageMarkdownNode.AddThemeColorOverride("default_color", primaryText);
    
    if (UserMessageMarkdownNode != null)
        UserMessageMarkdownNode.AddThemeColorOverride("default_color", userTextColor);
    
    ApplyBubbleStyle(BotMessageTemplate, botBubbleBg);

    if (MessagesContainer != null)
    {
        foreach (Node child in MessagesContainer.GetChildren())
        {
            string childName = child.Name.ToString();
            
            bool esMensajeDeIA = childName.Contains("Bot") || 
                                 child.FindChild("BotActions", true, false) != null || 
                                 child.FindChild("ThinkingIcon", true, false) != null;

            if (esMensajeDeIA)
            {
                ApplyTextThemeToNode(child, primaryText);
                ApplyBubbleStyle(child, botBubbleBg);
            }
            else
            {
                ApplyTextThemeToNode(child, userTextColor);
            }
        }
    }
}

private void ApplyBubbleStyle(Node messageNode, Color bgColor)
{
    if (messageNode == null) return;
    
    PanelContainer bubble = messageNode.FindChild("MessageBubble", true, false) as PanelContainer;
    
    if (bubble != null)
    {
        StyleBoxFlat newStyle = new StyleBoxFlat();
        if (bubble.HasThemeStylebox("panel") && bubble.GetThemeStylebox("panel") is StyleBoxFlat existingStyle)
        {
            newStyle = (StyleBoxFlat)existingStyle.Duplicate();
            newStyle.BorderWidthBottom = 0;
            newStyle.BorderWidthTop = 0;
            newStyle.BorderWidthLeft = 0;
            newStyle.BorderWidthRight = 0;
        }
        newStyle.BgColor = bgColor;
        bubble.AddThemeStyleboxOverride("panel", newStyle);
    }
}
        /// <summary>
        /// Performs an asynchronous recursive traversal over the structural layout tree to typecast controls 
        /// and enforce strict foreground color modifications against inherited standard controls.
        /// </summary>
        /// <param name="node">The initial node vertex point evaluated within the traversal algorithm.</param>
        /// <param name="textColor">The mapped layout color strictly instantiated for textual data components.</param>
        private void ApplyTextThemeToNode(Node node, Color textColor)
        {
            if (node is RichTextLabel richText)
            {
                richText.AddThemeColorOverride("default_color", textColor);
            }
            else if (node is Label label)
            {
                if (label.Name == "BotActions")
                {
                    label.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f, 1f));
                }
                else
                {
                    label.AddThemeColorOverride("font_color", textColor);
                }
            }
            else if (node is TextureRect textureRect && textureRect.Name == "ThinkingIcon")
            {
                textureRect.SelfModulate = new Color(0.5f, 0.5f, 0.5f, 1f);
            }
            
            foreach (Node child in node.GetChildren())
            {
                ApplyTextThemeToNode(child, textColor);
            }
        }

        /// <summary>
        /// Executed natively prior to memory de-allocation; systematically isolates node instances from singleton structures 
        /// by decoupling active delegate hooks thereby mitigating NullReferenceExceptions upon node cleanup.
        /// </summary>
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
    }
}