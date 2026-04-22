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

        [Export] public Texture2D WingLeftBaseTexture;
        [Export] public Texture2D WingRightBaseTexture;
        
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

            var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (networkManager != null) 
            {
                networkManager.STTCompleted += OnSTTCompleted;
            }
        }

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
            GD.Print("ChatBot: Voice detected, recording...");
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
            GD.Print("[FLOW] Initiating LLM response chain...");
            _ = ProcessMessage(recognizedText);
        }

        private void OnSendPressed()
        {
            _ = ProcessMessage(TextInputField.Text);
        }

        private void OnTextSubmitted(string newText)
        {
            _ = ProcessMessage(newText);
        }

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

            // =========================================================
            // 🛠️ MODO DE PRUEBA: COMANDO SECRETO PARA VER EL ERROR
            // =========================================================
            if (text.Trim().ToLower() == "/error")
            {
                OnBotStartedThinking(); // Aparece Eden en pantalla
                
                // Esperamos 1.5 segundos para crear suspenso
                await ToSignal(GetTree().CreateTimer(1.5f), SceneTreeTimer.SignalName.Timeout);
                
                // ¡PUM! Detonamos la animación
                TriggerConnectionErrorAnimation(); 
                
                _isWaitingForResponse = false;
                SendButton.Disabled = false;
                TextInputField.Editable = true;
                return; // Cortamos aquí para que no intente buscar a la IA
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

            // ANIMACIONES VIEJAS ELIMINADAS: Ahora el logo es estático y profesional.
        }

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
                // (Código de video se mantiene intacto)
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

            if (_currentBotMessageNode != null)
            {
                Control avatarNode = _currentBotMessageNode.GetNodeOrNull<Control>("AvatarPanel/BotAvatarContainer");
                if (avatarNode != null)
                {
                    avatarNode.Scale = new Vector2(1.0f, 1.0f);
                    avatarNode.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f);
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

        public void TriggerConnectionErrorAnimation()
        {
            if (_currentBotMessageNode == null) return;

            Control avatarContainer = _currentBotMessageNode.GetNodeOrNull<Control>("AvatarPanel/BotAvatarContainer");
            if (avatarContainer == null) return;

            TextureRect gotaCuerpo = avatarContainer.GetNode<TextureRect>("GotaCuerpo");
            TextureRect alaIzquierda = avatarContainer.GetNode<TextureRect>("AlaIzquierda");
            TextureRect alaDerecha = avatarContainer.GetNode<TextureRect>("AlaDerecha");
            Control pupilas = avatarContainer.GetNode<Control>("Pupilas");
            Control equis = avatarContainer.GetNode<Control>("Equis");

            if (WingLeftBaseTexture != null) alaIzquierda.Texture = WingLeftBaseTexture;
            if (WingRightBaseTexture != null) alaDerecha.Texture = WingRightBaseTexture;

            Tween sequence = GetTree().CreateTween();

            sequence.TweenCallback(Callable.From(() => {
                pupilas.Visible = false;
                equis.Visible = true;
            }));

            sequence.TweenInterval(0.2f); 
            sequence.TweenProperty(gotaCuerpo, "position", gotaCuerpo.Position + new Vector2(0, 15f), 0.5f)
                .SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);

            sequence.TweenInterval(0.1f);
            sequence.SetParallel(true);
            
            sequence.TweenProperty(alaIzquierda, "modulate", new Color(1.0f, 0.2f, 0.2f, 1.0f), 0.4f);
            sequence.TweenProperty(alaIzquierda, "position", alaIzquierda.Position + new Vector2(0, 20f), 0.6f)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            sequence.TweenProperty(alaIzquierda, "rotation_degrees", -75f, 0.6f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

            sequence.TweenProperty(alaDerecha, "modulate", new Color(1.0f, 0.2f, 0.2f, 1.0f), 0.4f);
            sequence.TweenProperty(alaDerecha, "position", alaDerecha.Position + new Vector2(0, 20f), 0.6f)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            sequence.TweenProperty(alaDerecha, "rotation_degrees", 75f, 0.6f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

            sequence.SetParallel(false);
            
            string relativePath = BotMessageTemplate.GetPathTo(BotMessageMarkdownNode);
            RichTextLabel botTextLabel = _currentBotMessageNode.GetNode<RichTextLabel>(relativePath);
            botTextLabel.Set("markdown_text", "[color=red]Error de conexión crítico con Eden Core...[/color]");
            StopTypingAnimation();
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
    }
}