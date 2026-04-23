using Godot;
using System;
using System.Threading.Tasks;

namespace Logic.UI
{
    /// <summary>
    /// Core controller for the Chatbot user interface. Handles user input, network communication formatting,
    /// audio recording for Speech-to-Text, UI animations, and dynamic markdown/code-block rendering.
    /// </summary>
    public partial class ChatbotMain : Control
    {
        [Export] public ScrollContainer ChatScrollContainer;
        
        [Export] public VBoxContainer MessagesContainer;
        [Export] public LineEdit TextInputField;
        [Export] public Button SendButton;
        [Export] public HBoxContainer UserMessageTemplate;
        [Export] public HBoxContainer BotMessageTemplate;
        [Export] public RichTextLabel UserMessageMarkdownNode;
        [Export] public RichTextLabel BotMessageMarkdownNode;
        [Export] public Control BotMessageLayoutNode;
        [Export] public OptionButton ToolSelector; 
        [Export] public PanelContainer CodeBlockTemplate;
        
        // --- Mock Media Resources ---
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

        // Internal State Variables
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
        /// Called when the node enters the scene tree for the first time.
        /// Initializes UI components, hides templates, and subscribes to required event streams from Chat and Network managers.
        /// </summary>
        public override void _Ready()
        {
            if (TextInputField == null) return;

            SendButton.Pressed += OnSendPressed;
            TextInputField.TextSubmitted += OnTextSubmitted;
            
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
        /// Called every frame. 
        /// Processes audio input levels continuously to detect voice activity and triggers Speech-to-Text payload generation upon silence detection.
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

        /// <summary>
        /// Activates the audio recorder effect when voice is detected over the threshold.
        /// </summary>
        private void StartRecording()
        {
            _isRecording = true;
            _recorder.SetRecordingActive(true);
        }

        /// <summary>
        /// Deactivates the audio recorder effect, saves the buffer to a local WAV file, and dispatches an STT request.
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
        /// Callback invoked upon successful transcription of the recorded audio. Triggers the standard message processing pipeline.
        /// </summary>
        private void OnSTTCompleted(string recognizedText)
        {
            if (string.IsNullOrWhiteSpace(recognizedText)) return;
            _ = ProcessMessage(recognizedText);
        }

        /// <summary>
        /// Handles the UI event when the user clicks the physical send button.
        /// </summary>
        private void OnSendPressed()
        {
            _ = ProcessMessage(TextInputField.Text);
        }

        /// <summary>
        /// Handles the UI event when the user submits text via the 'Enter' key.
        /// </summary>
        private void OnTextSubmitted(string newText)
        {
            _ = ProcessMessage(newText);
        }

        /// <summary>
        /// Orchestrates the instantiation of the user message UI node, blocks input to prevent race conditions, and routes the query.
        /// </summary>
        private async Task ProcessMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _isWaitingForResponse) return;

            _isWaitingForResponse = true;
            TextInputField.Text = string.Empty;
            SendButton.Disabled = true;

            HBoxContainer newUserMsg = (HBoxContainer)UserMessageTemplate.Duplicate();
            string relativePath = UserMessageTemplate.GetPathTo(UserMessageMarkdownNode);
            RichTextLabel userMarkdownLabel = newUserMsg.GetNode<RichTextLabel>(relativePath);
            
            userMarkdownLabel.Set("markdown_text", text);
            newUserMsg.Visible = true;
            MessagesContainer.AddChild(newUserMsg);
            ScrollToBottom();

            if (text.Trim().ToLower() == "/error")
            {
                OnBotStartedThinking(); 
                await ToSignal(GetTree().CreateTimer(1.5f), SceneTreeTimer.SignalName.Timeout);
                TriggerConnectionErrorAnimation(); 
                _isWaitingForResponse = false;
                SendButton.Disabled = false;
                TextInputField.Editable = true;
                return; 
            }

            int selectedTool = ToolSelector != null ? ToolSelector.Selected : 0;

            if (selectedTool == 1)
            {
                await GenerateMockMediaResponse(text, isVideo: false);
            }
            else if (selectedTool == 2)
            {
                await GenerateMockMediaResponse(text, isVideo: true);
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
                    SendButton.Disabled = false;
                }
            }
        }

        /// <summary>
        /// Prepares the UI for incoming AI tokens by creating a new Bot message layout and starting the idle "Thinking" animation.
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
        /// Simulates media generation processes locally (image or video generation) based on the user's prompt tool selection.
        /// </summary>
        private async Task GenerateMockMediaResponse(string prompt, bool isVideo)
        {
            HBoxContainer newBotMsg = (HBoxContainer)BotMessageTemplate.Duplicate();
            string mdRelativePath = BotMessageTemplate.GetPathTo(BotMessageMarkdownNode);
            RichTextLabel botTextLabel = newBotMsg.GetNode<RichTextLabel>(mdRelativePath);
            string layoutRelativePath = BotMessageTemplate.GetPathTo(BotMessageLayoutNode);
            Control messageLayout = newBotMsg.GetNode<Control>(layoutRelativePath);

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
            if (actionsLabel != null) actionsLabel.Text = string.Empty;

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
            TextInputField.Editable = true;
            SendButton.Disabled = false;
            TextInputField.GrabFocus();
        }

        /// <summary>
        /// Real-time stream handler appending parsed language tokens dynamically onto the designated RichTextLabel node.
        /// </summary>
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
        /// Finalizes the AI conversation turn, halts asynchronous animations, evaluates custom markdown blocks, 
        /// hides the entire bot actions container, and restores user controls.
        /// </summary>
        private void OnBotFinishedSpeaking(string fullResponse)
        {
            _isWaitingForResponse = false;
            TextInputField.Editable = true;
            SendButton.Disabled = false;

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
        /// Parses the final message buffer for markdown code segments.
        /// Splits the content by block delimiters and dynamically injects interactive UI Godot panels for code, while reconstructing standard markdown nodes.
        /// </summary>
        private void InjectCodeBlocks(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText) || !rawText.Contains("```")) return;
            if (_currentBotMessageNode == null || CodeBlockTemplate == null) return;

            string layoutRelativePath = BotMessageTemplate.GetPathTo(BotMessageLayoutNode);
            Control messageLayout = _currentBotMessageNode.GetNode<Control>(layoutRelativePath);
            
            string mdRelativePath = BotMessageTemplate.GetPathTo(BotMessageMarkdownNode);
            RichTextLabel originalMarkdownNode = _currentBotMessageNode.GetNode<RichTextLabel>(mdRelativePath);
            originalMarkdownNode.Visible = false; 

            string[] blocks = rawText.Split(new string[] { "```" }, StringSplitOptions.None);
            
            for (int i = 0; i < blocks.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(blocks[i])) continue;

                // Even indices represent normal text outside code blocks.
                if (i % 2 == 0) 
                {
                    RichTextLabel textBlock = (RichTextLabel)originalMarkdownNode.Duplicate();
                    textBlock.Visible = true;
                    textBlock.Set("markdown_text", blocks[i].Trim());
                    messageLayout.AddChild(textBlock);
                }
                // Odd indices represent content inside a code block.
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
        /// Enqueues an execution frame to ensure the UI has resolved node heights before forcing the ScrollContainer to the bottom limit.
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
        /// Instantiates and loops a dynamically generated timer to append trailing dots to a label string asynchronously.
        /// </summary>
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
        /// Disposes the dots animation timer safely and releases resources from the scene tree.
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
        /// Executes a complex visual tween sequence updating textures, scale, and color modulation natively to reflect a critical systemic error state.
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
        /// Safely unsubscribes event handlers before the node is disposed from the memory tree to prevent GC memory leakages.
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