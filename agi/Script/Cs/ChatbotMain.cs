using Godot;
using System;
using System.Threading.Tasks;

namespace Logic.UI
{
    /// <summary>
    /// Orchestrates the main chat interface, processes response streams to trigger TTS,
    /// and coordinates conversational memory.
    /// </summary>
    public partial class ChatbotMain : Control
    {
        // UI Node References via Export (Must match Node names in Editor exactly)
        [Export] public PanelContainer SidebarContainer;
        [Export] public Button MenuToggleButton;
        [Export] public ScrollContainer ChatScrollContainer;
        [Export] public VBoxContainer MessagesContainer;
        [Export] public LineEdit TextInputField;
        [Export] public Button SendButton;
        [Export] public HBoxContainer UserMessageTemplate;
        [Export] public HBoxContainer BotMessageTemplate;
        
        // State and configuration
        private bool _isSidebarOpen = true;
        private float _sidebarWidth = 250.0f;
        private HBoxContainer _currentBotMessageNode;
        private bool _isLiveModeEnabled = false; // Controls whether TTS is triggered automatically
        
        // Processing buffers for the TTS engine and LLM memory
        private string _ttsBuffer = string.Empty;
        private string _fullMessageBuffer = string.Empty;

        /// <summary>
        /// Godot lifecycle initialization method.
        /// Subscribes to UI events and internal NetworkManager signals.
        /// </summary>
        public override void _Ready()
        {
            // Validation to ensure nodes are assigned in the inspector
            if (SidebarContainer == null || MenuToggleButton == null || TextInputField == null)
            {
                GD.PrintErr("ChatbotMain: Exported nodes validation failed.");
                return;
            }

            // UI Event Subscriptions using the Exported variables
            SendButton.Pressed += OnSendPressed;
            TextInputField.TextSubmitted += OnTextSubmitted;
            MenuToggleButton.Pressed += OnMenuTogglePressed;
            
            // Ensure templates are hidden by default if not already
            if (UserMessageTemplate != null) UserMessageTemplate.Visible = false;
            if (BotMessageTemplate != null) BotMessageTemplate.Visible = false;

            // Subscription to network events and prompt generation
            Node networkManager = GetNodeOrNull("/root/NetworkManager");
            if (networkManager != null)
            {
                networkManager.Connect("TokenReceived", new Callable(this, MethodName.OnTokenReceived));
            }
            else
            {
                GD.PrintErr("ChatbotMain: NetworkManager singleton not found in tree.");
            }

            Node chatManager = GetNodeOrNull("/root/ChatManager");
            if (chatManager != null)
            {
                chatManager.Connect("MessageReady", new Callable(this, MethodName.OnMessageReady));
            }
            else
            {
                GD.PrintErr("ChatbotMain: ChatManager singleton not found in tree.");
            }
        }

        private void OnMenuTogglePressed()
        {
            _isSidebarOpen = !_isSidebarOpen;
            
            Tween tween = GetTree().CreateTween();
            float targetWidth = _isSidebarOpen ? _sidebarWidth : 0.0f;
            
            tween.TweenProperty(SidebarContainer, "custom_minimum_size:x", targetWidth, 0.3f)
                 .SetTrans(Tween.TransitionType.Cubic)
                 .SetEase(Tween.EaseType.InOut);
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
        /// Instantiates visual messages and commands the memory manager to create the prompt.
        /// </summary>
        private async Task ProcessMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            TextInputField.Text = string.Empty;

            HBoxContainer newUserMsg = (HBoxContainer)UserMessageTemplate.Duplicate();
            newUserMsg.GetNode<RichTextLabel>("MessageBubble/MessageBody").Text = text;
            newUserMsg.Visible = true;
            MessagesContainer.AddChild(newUserMsg);

            ScrollToBottom();

            HBoxContainer newBotMsg = (HBoxContainer)BotMessageTemplate.Duplicate();
            newBotMsg.GetNode<RichTextLabel>("MessageBubble/MessageBody").Text = ""; 
            newBotMsg.Visible = true;
            MessagesContainer.AddChild(newBotMsg);
            
            // Stores the reference of the current container for real-time concatenation
            _currentBotMessageNode = newBotMsg;
            
            // Resets session accumulators
            _ttsBuffer = string.Empty;
            _fullMessageBuffer = string.Empty;

            ScrollToBottom();

            // Strong typing to avoid issues with Godot.Variant
            Logic.Lite.ChatManager chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager != null)
            {
                chatManager.GeneratePrompt(text);
            }
        }

        /// <summary>
        /// Receives the formatted Mistral prompt and triggers the Streaming HTTP request.
        /// Resolves CS0030 error via explicit casting.
        /// </summary>
        private async void OnMessageReady(string formattedMistralPrompt)
        {
            // Explicit casting to the NetworkManager class to access the asynchronous Task method directly
            Logic.Network.NetworkManager networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (networkManager != null)
            {
                // Explicitly wait for the streaming from the server to finish ([DONE])
                await networkManager.StreamChatCompletion(formattedMistralPrompt);

                // Memory insertion: Persists the complete response in the ChatManager to maintain historical context
                Logic.Lite.ChatManager chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
                if (chatManager != null)
                {
                    chatManager.RegisterAssistantReply(_fullMessageBuffer);
                    GD.Print("ChatbotMain: Response successfully persisted in dynamic memory.");
                }
            }
        }

        /// <summary>
        /// Receives live stream tokens (SSE), injects them into the UI, and checks punctuation for speech.
        /// </summary>
        private void OnTokenReceived(string token)
        {
            if (_currentBotMessageNode == null) return;

            RichTextLabel messageBody = _currentBotMessageNode.GetNode<RichTextLabel>("MessageBubble/MessageBody");
            messageBody.Text += token;

            ScrollToBottom();

            // Accumulation in processing and memory buffers
            _ttsBuffer += token;
            _fullMessageBuffer += token;

            // Syntax validation logic (Trigger TTS) upon detecting final punctuation
            if (token.Contains(".") || token.Contains("!") || token.Contains("?"))
            {
                // Only dispatch speech if Live Mode is active
                if (_isLiveModeEnabled)
                {
                    DispatchSherpaSpeech(_ttsBuffer.Trim());
                }

                // Always clear the acoustic buffer to prevent memory leaks in text-only mode
                _ttsBuffer = string.Empty; 
            }
        }

        /// <summary>
        /// Audits the intercepted sentence and delegates the string to the TTS orchestrator process.
        /// </summary>
        private void DispatchSherpaSpeech(string textToSynthesize)
        {
            // Prevents native engine invocation with empty streams that cause segmentation faults
            if (string.IsNullOrWhiteSpace(textToSynthesize)) return;

            GD.Print("ChatbotMain: Syntactic punctuation detected. Executing Sherpa-ONNX TTS.");
            
            // Retrieves the transport layer to the backend via global node injection
            Logic.Backend.BackendLauncher backendLauncher = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
            if (backendLauncher != null)
            {
                // Transmits the synthesis instruction to the native binary
                backendLauncher.StartSherpaTTS(textToSynthesize);
            }
        }

        /// <summary>
        /// Scrolls the view area to the bottom so that new text is always visible.
        /// </summary>
        private async void ScrollToBottom()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            
            ScrollBar vScroll = ChatScrollContainer.GetVScrollBar();
            vScroll.Value = vScroll.MaxValue;
        }
    }
}