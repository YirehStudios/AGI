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
        private bool _isLiveModeEnabled = false;
        private bool _isWaitingForResponse = false;
        private Godot.Timer _typingAnimationTimer;
        
        private string _ttsBuffer = string.Empty;
        private string _fullMessageBuffer = string.Empty;
        private Random _randomGenerator = new Random();

        /// <summary>
        /// Configura los delegados de la interfaz de usuario e inicializa las suscripciones a las señales de red y procesamiento 
        /// en el momento en que el nodo se acopla al árbol de escena, incluyendo la escucha de la finalización del STT
        /// y la configuración del bus nativo de grabación.
        /// </summary>
        public override void _Ready()
        {
            if (TextInputField == null) return;

            SendButton.Pressed += OnSendPressed;
            TextInputField.TextSubmitted += OnTextSubmitted;
            
            if (UserMessageTemplate != null) UserMessageTemplate.Visible = false;
            if (BotMessageTemplate != null) BotMessageTemplate.Visible = false;

            int recordBusIndex = AudioServer.GetBusIndex("Record");
            if (recordBusIndex != -1)
            {
                _recorder = (AudioEffectRecord)AudioServer.GetBusEffect(recordBusIndex, 0);
            }

            Node networkManager = GetNodeOrNull("/root/NetworkManager");
            if (networkManager != null)
            {
                networkManager.Connect("TokenReceived", new Callable(this, MethodName.OnTokenReceived));
            }

            Node chatManager = GetNodeOrNull("/root/ChatManager");
            if (chatManager != null)
            {
                chatManager.Connect("MessageReady", new Callable(this, MethodName.OnMessageReady));
            }

            Node backendLauncher = GetNodeOrNull("/root/BackendLauncher");
            if (backendLauncher != null)
            {
                backendLauncher.Connect("STTCompleted", new Callable(this, MethodName.OnSTTCompleted));
            }
        }

        /// <summary>
        /// Monitoriza asíncronamente el bus de grabación de audio para evaluar los umbrales de sonido.
        /// Desencadena el registro y el envío del segmento de audio tras superar la tolerancia de silencio.
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
        /// Habilita el estado de captura activa sobre el bus asignado al efecto de grabación.
        /// </summary>
        private void StartRecording()
        {
            _isRecording = true;
            _recorder.SetRecordingActive(true);
            GD.Print("ChatBot: Detectada voz, grabando...");
        }

        /// <summary>
        /// Finaliza la captura del segmento actual de voz, lo serializa en un archivo binario WAV dentro 
        /// de la partición de usuario y cede el procesamiento a la canalización STT en segundo plano.
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
                
                Logic.Backend.BackendLauncher backend = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
                if (backend != null) backend.ProcessSpeechToText(path);
            }
            _silenceTimer = 0.0f;
        }

        /// <summary>
        /// Captura la señal emitida al finalizar la transcripción de audio, validando la cadena resultante 
        /// y derivándola al flujo principal de procesamiento de mensajes del chatbot.
        /// </summary>
        /// <param name="recognizedText">Cadena de texto generada por el motor STT.</param>
        private void OnSTTCompleted(string recognizedText)
        {
            if (string.IsNullOrWhiteSpace(recognizedText)) return;
            
            GD.Print($"LiveMode: Escuché: {recognizedText}");
            _ = ProcessMessage(recognizedText);
        }

        /// <summary>
        /// Delega la síntesis de una cadena de texto al motor de audio subyacente. 
        /// Inyecta la instrucción asíncrona hacia el proceso en contenedor.
        /// </summary>
        /// <param name="textToSynthesize">Cadena que requiere conversión a flujo de audio.</param>
        private void DispatchSherpaSpeech(string textToSynthesize)
        {
            if (string.IsNullOrWhiteSpace(textToSynthesize)) return;
            
            Logic.Backend.BackendLauncher backend = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
            if (backend != null) 
            {
                backend.GenerateTextToSpeech(textToSynthesize);
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

        private async Task ProcessMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _isWaitingForResponse) return;

            _isWaitingForResponse = true;
            TextInputField.Text = string.Empty;
            SendButton.Disabled = true;

            HBoxContainer newUserMsg = (HBoxContainer)UserMessageTemplate.Duplicate();
            newUserMsg.GetNode<RichTextLabel>("MessageBubble/MessageBody").Text = text;
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
                ProcessNormalChatMessage(text);
            }
        }

        private void ProcessNormalChatMessage(string text)
        {
            HBoxContainer newBotMsg = (HBoxContainer)BotMessageTemplate.Duplicate();
            RichTextLabel botTextLabel = newBotMsg.GetNode<RichTextLabel>("MessageBubble/MessageLayout/MessageBody");
            botTextLabel.Text = ".";
            newBotMsg.Visible = true;
            MessagesContainer.AddChild(newBotMsg);
            
            _currentBotMessageNode = newBotMsg;
            _ttsBuffer = string.Empty;
            _fullMessageBuffer = string.Empty;
            ScrollToBottom();

            StartTypingAnimation(botTextLabel);

            Logic.Lite.ChatManager chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager != null) chatManager.GeneratePrompt(text);
        }

        private async Task GenerateMockMediaResponse(string prompt, bool isVideo)
        {
            HBoxContainer newBotMsg = (HBoxContainer)BotMessageTemplate.Duplicate();
            RichTextLabel botTextLabel = newBotMsg.GetNode<RichTextLabel>("MessageBubble/MessageLayout/MessageBody");
            botTextLabel.Text = isVideo ? "Generando video para: " + prompt : "Generando imagen para: " + prompt;
            newBotMsg.Visible = true;
            MessagesContainer.AddChild(newBotMsg);
            ScrollToBottom();
            StartTypingAnimation(botTextLabel);

            await ToSignal(GetTree().CreateTimer(3.0f), SceneTreeTimer.SignalName.Timeout);

            StopTypingAnimation();
            botTextLabel.Text = isVideo ? "¡Aquí tienes tu video!" : "¡Aquí tienes tu imagen!";

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
                newBotMsg.GetNode<Control>("MessageBubble/MessageLayout").AddChild(videoWrapper);

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
                mediaRect.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
                mediaRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
                mediaRect.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                mediaRect.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

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

                newBotMsg.GetNode<Control>("MessageBubble/MessageLayout").AddChild(mediaRect);
            }
            
            ScrollToBottom();

            _isWaitingForResponse = false;
            TextInputField.Editable = true;
            SendButton.Disabled = false;
            TextInputField.GrabFocus();
        }

        /// <summary>
        /// Procesa el flujo de texto entrante del LLM en tiempo real.
        /// Garantiza la separación de canales: actualiza la UI visualmente de forma incondicional,
        /// pero delega la síntesis de voz (TTS) única y exclusivamente si el contexto es interactivo (LiveMode).
        /// </summary>
        private void OnTokenReceived(string token)
        {
            if (_currentBotMessageNode == null) return;

            RichTextLabel messageBody = _currentBotMessageNode.GetNode<RichTextLabel>("MessageBubble/MessageLayout/MessageBody");
            
            if (_typingAnimationTimer != null)
            {
                StopTypingAnimation();
                messageBody.Text = "";
            }

            messageBody.Text += token;
            ScrollToBottom();

            _ttsBuffer += token;
            _fullMessageBuffer += token;

            if (token.Contains(".") || token.Contains("!") || token.Contains("?"))
            {
                // Evaluación de contexto estricta: El bot permanece mudo en chat regular.
                if (_isLiveModeEnabled) 
                {
                    DispatchSherpaSpeech(_ttsBuffer.Trim());
                }
                
                _ttsBuffer = string.Empty;
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
                if (label.Text.EndsWith("...")) label.Text = label.Text.Substring(0, label.Text.Length - 3) + ".";
                else if (label.Text.EndsWith("..")) label.Text = label.Text.Substring(0, label.Text.Length - 2) + "...";
                else if (label.Text.EndsWith(".")) label.Text = label.Text.Substring(0, label.Text.Length - 1) + "..";
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

        private async void OnMessageReady(string formattedMistralPrompt)
        {
            Logic.Network.NetworkManager networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (networkManager != null)
            {
                await networkManager.StreamChatCompletion(formattedMistralPrompt);

                Logic.Lite.ChatManager chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
                if (chatManager != null) chatManager.RegisterAssistantReply(_fullMessageBuffer);

                _isWaitingForResponse = false;
                TextInputField.Editable = true;
                SendButton.Disabled = false;
                TextInputField.GrabFocus();
            }
        }
    }
}