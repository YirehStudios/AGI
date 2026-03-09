using Godot;
using System;
using System.Threading.Tasks;

namespace Logic.UI
{
    public partial class ChatbotMain : Control
    {
        [Export] public Button MenuToggleButton;
        [Export] public ScrollContainer ChatScrollContainer;
        [Export] public VBoxContainer MessagesContainer;
        [Export] public LineEdit TextInputField;
        [Export] public Button SendButton;
        [Export] public HBoxContainer UserMessageTemplate;
        [Export] public HBoxContainer BotMessageTemplate;
        
        private HBoxContainer _currentBotMessageNode;
        private bool _isLiveModeEnabled = false; 
        private bool _isWaitingForResponse = false;
        private Godot.Timer _typingAnimationTimer;
        
        private string _ttsBuffer = string.Empty;
        private string _fullMessageBuffer = string.Empty;

        public override void _Ready()
        {
            if (MenuToggleButton == null || TextInputField == null)
            {
                GD.PrintErr("ChatbotMain: Exported nodes validation failed.");
                return;
            }

            SendButton.Pressed += OnSendPressed;
            TextInputField.TextSubmitted += OnTextSubmitted;
            MenuToggleButton.Pressed += OnMenuTogglePressed;
            
            Button liveModeBtn = GetNodeOrNull<Button>("MainContainer/ChatAreaContainer/HeaderPanel/HeaderMargin/HeaderLayout/LiveModeButton");
            if (liveModeBtn != null) liveModeBtn.Pressed += OnLiveModePressed;
            
            if (UserMessageTemplate != null) UserMessageTemplate.Visible = false;
            if (BotMessageTemplate != null) BotMessageTemplate.Visible = false;

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
        }

        private void OnMenuTogglePressed()
        {
            MainApp mainApp = GetNodeOrNull<MainApp>("/root/MainApp");
            if (mainApp != null) mainApp.ToggleSidebar();
        }

        private void OnLiveModePressed()
        {
            MainApp mainApp = GetNodeOrNull<MainApp>("/root/MainApp");
            if (mainApp != null) mainApp.LoadMode(mainApp.LivemodeScene);
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

            HBoxContainer newBotMsg = (HBoxContainer)BotMessageTemplate.Duplicate();
            RichTextLabel botTextLabel = newBotMsg.GetNode<RichTextLabel>("MessageBubble/MessageBody");
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

        private void OnTokenReceived(string token)
        {
            if (_currentBotMessageNode == null) return;

            RichTextLabel messageBody = _currentBotMessageNode.GetNode<RichTextLabel>("MessageBubble/MessageBody");
            
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
                if (_isLiveModeEnabled) DispatchSherpaSpeech(_ttsBuffer.Trim());
                _ttsBuffer = string.Empty; 
            }
        }

        private void DispatchSherpaSpeech(string textToSynthesize)
        {
            if (string.IsNullOrWhiteSpace(textToSynthesize)) return;
            
            Logic.Backend.BackendLauncher backendLauncher = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
            if (backendLauncher != null) backendLauncher.StartSherpaTTS(textToSynthesize);
        }

        private async void ScrollToBottom()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ScrollBar vScroll = ChatScrollContainer.GetVScrollBar();
            vScroll.Value = vScroll.MaxValue;
        }

        private void StartTypingAnimation(RichTextLabel label)
        {
            _typingAnimationTimer = new Godot.Timer();
            _typingAnimationTimer.WaitTime = 0.4f;
            _typingAnimationTimer.OneShot = false;
            
            _typingAnimationTimer.Timeout += () => 
            {
                if (label.Text == "...") label.Text = ".";
                else if (label.Text == "..") label.Text = "...";
                else if (label.Text == ".") label.Text = "..";
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