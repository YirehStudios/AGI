using Godot;
using System;
using System.Threading.Tasks;

namespace Logic.UI
{
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
        
        [Export] public Texture2D RandomImage1;
        [Export] public Texture2D RandomImage2;
        [Export] public Texture2D RandomImage3;
        [Export] public Texture2D RandomImage4;

        [Export] public VideoStream RandomVideo1;
        [Export] public VideoStream RandomVideo2;
        [Export] public VideoStream RandomVideo3;
        [Export] public VideoStream RandomVideo4;
        [Export] public AudioStreamPlayer MicroRecorderPlayer; 

        private AudioEffectRecord _recorder;
        private float _silenceTimer = 0.0f;
        private const float SilenceThreshold = 0.05f;
        private bool _isRecording = false;
        
        private HBoxContainer _currentBotMessageNode;
        private bool _isLiveModeEnabled = true;
        private bool _isWaitingForResponse = false;
        private Godot.Timer _typingAnimationTimer;
        
        private string _ttsBuffer = string.Empty;
        private string _fullMessageBuffer = string.Empty;
        private Random _randomGenerator = new Random();

        /// <summary>
        /// Configures UI delegates and initializes subscriptions to network and processing signals 
        /// upon node attachment to the scene tree. Also configures the native audio recording bus.
        /// </summary>
        public override void _Ready()
        {
            if (TextInputField == null) return;

            SendButton.Pressed += OnSendPressed;
            TextInputField.TextSubmitted += OnTextSubmitted;
            
            if (UserMessageTemplate != null) UserMessageTemplate.Visible = false;
            if (BotMessageTemplate != null) BotMessageTemplate.Visible = false;

            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager != null) 
            {
                chatManager.OnBotStartedThinking += OnBotStartedThinking;
                chatManager.OnBotMessageTokenReceived += OnTokenReceived;
                chatManager.OnBotFinishedSpeaking += OnBotFinishedSpeaking;
            }

            // CRITICAL FIX: Routed STT event subscription to the Network layer.
            var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (networkManager != null) 
            {
                networkManager.STTCompleted += OnSTTCompleted;
            }
        }

        /// <summary>
        /// Asynchronously monitors the audio recording bus to evaluate sound thresholds.
        /// Triggers recording and audio segment dispatch upon exceeding silence tolerance.
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
        /// Enables active capture state on the bus assigned to the recording effect.
        /// </summary>
        private void StartRecording()
        {
            _isRecording = true;
            _recorder.SetRecordingActive(true);
            GD.Print("ChatBot: Voice detected, recording...");
        }

        /// <summary>
        /// Finalizes capture of the current voice segment, serializes it to a binary WAV file 
        /// within the user partition, and yields processing to the background STT pipeline.
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
                
                // CRITICAL FIX: Routed inference execution to the Network layer.
                var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
                if (networkManager != null) 
                {
                    _ = networkManager.RequestSTT(path);
                }
            }
            _silenceTimer = 0.0f;
        }

        /// <summary>
        /// Captures the signal emitted upon completion of audio transcription, validating the resulting 
        /// string and routing it to the main chatbot message processing flow.
        /// </summary>
        private void OnSTTCompleted(string recognizedText)
        {
            if (string.IsNullOrWhiteSpace(recognizedText)) return;
            GD.Print("[FLOW] Initiating LLM response chain...");
            _ = ProcessMessage(recognizedText);
        }

        /// <summary>
        /// Delegates synthesis of a text string to the underlying WebSocket audio engine.
        /// </summary>
        private void DispatchSherpaSpeech(string textToSynthesize)
        {
            GD.Print($"[TTS] Requesting speech synthesis via WebSocket: {textToSynthesize.Substring(0, Math.Min(20, textToSynthesize.Length))}...");
            
            // CRITICAL FIX: Routed synthesis execution to the Network WebSocket stream.
            var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (networkManager != null) 
            {
                _ = networkManager.RequestTTSWebSocket(textToSynthesize);
            }
        }

        private void OnSendPressed()
        {
            _ = ProcessMessage(TextInputField.Text);
        }

        private void OnTextSubmitted(string newText)
        {
            _ = ProcessMessage(newText);
        }

        /// <summary>
        /// Processes user input and delegates to the appropriate logic pipeline.
        /// Utilizes relative topological paths to assign markdown content to cloned instances.
        /// </summary>
        private async Task ProcessMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _isWaitingForResponse) return;

            _isWaitingForResponse = true;
            TextInputField.Text = string.Empty;
            SendButton.Disabled = true;

            HBoxContainer newUserMsg = (HBoxContainer)UserMessageTemplate.Duplicate();
            
            // Resolve node relative path to assign properties safely to the duplicated instance
            string relativePath = UserMessageTemplate.GetPathTo(UserMessageMarkdownNode);
            RichTextLabel userMarkdownLabel = newUserMsg.GetNode<RichTextLabel>(relativePath);
            
            // Interface with the markdown plugin using the exposed GDScript property
            userMarkdownLabel.Set("markdown_text", text);
            
            newUserMsg.Visible = true;
            MessagesContainer.AddChild(newUserMsg);
            ScrollToBottom();

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
        /// Prepares the UI state for incoming token streaming by deploying a new message node.
        /// </summary>
        private void OnBotStartedThinking()
        {
            _fullMessageBuffer = string.Empty;

            HBoxContainer newBotMsg = (HBoxContainer)BotMessageTemplate.Duplicate();
            
            string relativePath = BotMessageTemplate.GetPathTo(BotMessageMarkdownNode);
            RichTextLabel botTextLabel = newBotMsg.GetNode<RichTextLabel>(relativePath);
            
            botTextLabel.Set("markdown_text", ".");
            newBotMsg.Visible = true;
            MessagesContainer.AddChild(newBotMsg);
            
            _currentBotMessageNode = newBotMsg;
            ScrollToBottom();
            StartTypingAnimation(botTextLabel);
        }

        /// <summary>
        /// Mocks media generation utilizing dynamically resolved topology paths instead of hardcoded strings.
        /// </summary>
        private async Task GenerateMockMediaResponse(string prompt, bool isVideo)
        {
            HBoxContainer newBotMsg = (HBoxContainer)BotMessageTemplate.Duplicate();
            
            string mdRelativePath = BotMessageTemplate.GetPathTo(BotMessageMarkdownNode);
            RichTextLabel botTextLabel = newBotMsg.GetNode<RichTextLabel>(mdRelativePath);
            
            string layoutRelativePath = BotMessageTemplate.GetPathTo(BotMessageLayoutNode);
            Control messageLayout = newBotMsg.GetNode<Control>(layoutRelativePath);

            botTextLabel.Set("markdown_text", isVideo ? "Generando video para: " + prompt : "Generando imagen para: " + prompt);
            
            newBotMsg.Visible = true;
            MessagesContainer.AddChild(newBotMsg);
            ScrollToBottom();
            StartTypingAnimation(botTextLabel);

            await ToSignal(GetTree().CreateTimer(3.0f), SceneTreeTimer.SignalName.Timeout);

            StopTypingAnimation();
            botTextLabel.Set("markdown_text", isVideo ? "¡Aquí tienes tu video!" : "¡Aquí tienes tu imagen!");

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

                if (chosenVideo != null)
                {
                    videoPlayer.Stream = chosenVideo;
                }
                
                aspectContainer.AddChild(videoPlayer);
                videoWrapper.AddChild(aspectContainer);

                HBoxContainer controlsLayout = new HBoxContainer();
                controlsLayout.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                controlsLayout.AddThemeConstantOverride("separation", 15);

                Button playPauseBtn = new Button();
                playPauseBtn.Text = "⏸ Pausar";
                playPauseBtn.AddThemeColorOverride("font_color", new Color(1, 1, 1, 1));
                playPauseBtn.AddThemeColorOverride("font_hover_color", new Color(1, 1, 1, 1));
                playPauseBtn.AddThemeColorOverride("font_pressed_color", new Color(1, 1, 1, 1));
                playPauseBtn.AddThemeColorOverride("font_focus_color", new Color(1, 1, 1, 1));

                StyleBoxFlat btnNormal = new StyleBoxFlat();
                btnNormal.BgColor = new Color(0.373f, 0.502f, 0.357f, 1.0f);
                btnNormal.CornerRadiusTopLeft = 10;
                btnNormal.CornerRadiusTopRight = 10;
                btnNormal.CornerRadiusBottomLeft = 10;
                btnNormal.CornerRadiusBottomRight = 10;
                btnNormal.ContentMarginTop = 8;
                btnNormal.ContentMarginBottom = 8;
                btnNormal.ContentMarginLeft = 18;
                btnNormal.ContentMarginRight = 18;
                btnNormal.AntiAliasing = true;

                StyleBoxFlat btnHover = (StyleBoxFlat)btnNormal.Duplicate();
                btnHover.BgColor = new Color(0.42f, 0.55f, 0.4f, 1.0f);

                StyleBoxFlat btnPressed = (StyleBoxFlat)btnNormal.Duplicate();
                btnPressed.BgColor = new Color(0.25f, 0.35f, 0.24f, 1.0f);

                playPauseBtn.AddThemeStyleboxOverride("normal", btnNormal);
                playPauseBtn.AddThemeStyleboxOverride("hover", btnHover);
                playPauseBtn.AddThemeStyleboxOverride("pressed", btnPressed);
                playPauseBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

                playPauseBtn.Pressed += () =>
                {
                    videoPlayer.Paused = !videoPlayer.Paused;
                    playPauseBtn.Text = videoPlayer.Paused ? "▶ Reproducir" : "⏸ Pausar";
                };

                HSlider progressSlider = new HSlider();
                progressSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                progressSlider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

                StyleBoxFlat sliderBg = new StyleBoxFlat();
                sliderBg.BgColor = new Color(0.85f, 0.85f, 0.85f, 1.0f);
                sliderBg.CornerRadiusTopLeft = 4;
                sliderBg.CornerRadiusTopRight = 4;
                sliderBg.CornerRadiusBottomLeft = 4;
                sliderBg.CornerRadiusBottomRight = 4;
                sliderBg.ExpandMarginTop = 4;
                sliderBg.ExpandMarginBottom = 4;
                sliderBg.AntiAliasing = true;

                StyleBoxFlat sliderFill = new StyleBoxFlat();
                sliderFill.BgColor = new Color(0.373f, 0.502f, 0.357f, 1.0f);
                sliderFill.CornerRadiusTopLeft = 4;
                sliderFill.CornerRadiusTopRight = 4;
                sliderFill.CornerRadiusBottomLeft = 4;
                sliderFill.CornerRadiusBottomRight = 4;
                sliderFill.ExpandMarginTop = 4;
                sliderFill.ExpandMarginBottom = 4;
                sliderFill.AntiAliasing = true;

                progressSlider.AddThemeStyleboxOverride("slider", sliderBg);
                progressSlider.AddThemeStyleboxOverride("grabber_area", sliderFill);
                progressSlider.AddThemeStyleboxOverride("grabber_area_highlight", sliderFill);

                controlsLayout.AddChild(playPauseBtn);
                controlsLayout.AddChild(progressSlider);

                videoWrapper.AddChild(controlsLayout);
                messageLayout.AddChild(videoWrapper);

                Godot.Timer syncTimer = new Godot.Timer();
                syncTimer.WaitTime = 0.1f;
                syncTimer.Autostart = true;
                
                bool isDragging = false;
                progressSlider.DragStarted += () => isDragging = true;
                progressSlider.DragEnded += (bool valueChanged) =>
                {
                    isDragging = false;
                    videoPlayer.StreamPosition = progressSlider.Value;
                };

                syncTimer.Timeout += () =>
                {
                    if (IsInstanceValid(videoPlayer))
                    {
                        if (!isDragging && !videoPlayer.Paused)
                        {
                            double len = videoPlayer.GetStreamLength();
                            if (len > 0) progressSlider.MaxValue = len;
                            progressSlider.Value = videoPlayer.StreamPosition;
                        }
                    }
                    else
                    {
                        syncTimer.Stop();
                        syncTimer.QueueFree();
                    }
                };

                videoWrapper.AddChild(syncTimer);
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

                if (chosenImage != null)
                {
                    mediaRect.Texture = chosenImage;
                }

                messageLayout.AddChild(mediaRect);
            }
            
            ScrollToBottom();

            _isWaitingForResponse = false;
            TextInputField.Editable = true;
            SendButton.Disabled = false;
            TextInputField.GrabFocus();
        }

        /// <summary>
        /// Processes the incoming real-time token stream from the LLM.
        /// It builds a buffer to ensure markdown integrity and invokes the parser sequentially.
        /// </summary>
        private void OnTokenReceived(string token)
        {
            if (_currentBotMessageNode == null) return;

            string relativePath = BotMessageTemplate.GetPathTo(BotMessageMarkdownNode);
            RichTextLabel messageBody = _currentBotMessageNode.GetNode<RichTextLabel>(relativePath);
            
            if (_typingAnimationTimer != null)
            {
                StopTypingAnimation();
                _fullMessageBuffer = string.Empty;
            }

            _fullMessageBuffer += token;
            messageBody.Set("markdown_text", _fullMessageBuffer);
            
            ScrollToBottom();
        }

        private void OnBotFinishedSpeaking(string fullResponse)
        {
            _isWaitingForResponse = false;
            TextInputField.Editable = true;
            SendButton.Disabled = false;
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

        /// <summary>
        /// Animates a typing state reading directly from the markdown property.
        /// </summary>
        private void StartTypingAnimation(RichTextLabel label)
        {
            if (_typingAnimationTimer != null) return; 

            _typingAnimationTimer = new Godot.Timer();
            _typingAnimationTimer.WaitTime = 0.4f;
            _typingAnimationTimer.OneShot = false;
            
            _typingAnimationTimer.Timeout += () =>
            {
                string currentText = label.Get("markdown_text").AsString();
                if (currentText.EndsWith("...")) label.Set("markdown_text", currentText.Substring(0, currentText.Length - 3) + ".");
                else if (currentText.EndsWith("..")) label.Set("markdown_text", currentText.Substring(0, currentText.Length - 2) + "...");
                else if (currentText.EndsWith(".")) label.Set("markdown_text", currentText.Substring(0, currentText.Length - 1) + "..");
            };
            
            AddChild(_typingAnimationTimer);
            _typingAnimationTimer.Start();
        }

        private void StopTypingAnimation()
        {
            if (_typingAnimationTimer != null)
            {
                _typingAnimationTimer.Stop();
                _typingAnimationTimer.QueueFree();
                _typingAnimationTimer = null;
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

            // CRITICAL FIX: Unsubscribed from NetworkManager instead of BackendLauncher.
            var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (networkManager != null) 
            {
                networkManager.STTCompleted -= OnSTTCompleted;
            }
        }
    }
}