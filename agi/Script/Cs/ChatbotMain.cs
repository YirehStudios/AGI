using Godot;
using System;
using System.Threading.Tasks;

public partial class ChatbotMain : Control
{
    // UI Node References
    private VBoxContainer _messagesVBox;
    private LineEdit _chatInput;
    private Button _btnSend;
    private ScrollContainer _chatScroll;
    private HBoxContainer _userMsgTemplate;
    private HBoxContainer _botMsgTemplate;
    
    // References for Sidebar logic
    private Control _sidebarWrapper;
    private Button _btnMenuToggle;
    
    // State and configuration
    private bool _isSidebarOpen = true;
    private float _sidebarWidth = 250.0f;

    /// <summary>
    /// Godot lifecycle initialization method.
    /// Responsible for obtaining references to scene tree nodes and subscribing to events.
    /// </summary>
    public override void _Ready()
    {
        // Direct retrieval of UI node references
        _messagesVBox = GetNode<VBoxContainer>("MainLayout/ChatAreaVBox/ChatScroll/ScrollMargin/MessagesVBox");
        _chatInput = GetNode<LineEdit>("MainLayout/ChatAreaVBox/InputMargin/InputContainer/InputHBox/ChatInput");
        _btnSend = GetNode<Button>("MainLayout/ChatAreaVBox/InputMargin/InputContainer/InputHBox/BtnSend");
        _chatScroll = GetNode<ScrollContainer>("MainLayout/ChatAreaVBox/ChatScroll");
        
        _sidebarWrapper = GetNode<Control>("MainLayout/SidebarWrapper");
        _btnMenuToggle = GetNode<Button>("MainLayout/ChatAreaVBox/HeaderContainer/HeaderMargin/HeaderHBox/BtnMenuToggle");

        // Retrieval of message templates for cloning
        _userMsgTemplate = _messagesVBox.GetNode<HBoxContainer>("UserMsg1");
        _botMsgTemplate = _messagesVBox.GetNode<HBoxContainer>("BotMsg1");

        // UI Event Subscriptions
        _btnSend.Pressed += OnSendPressed;
        _chatInput.TextSubmitted += OnTextSubmitted;
        _btnMenuToggle.Pressed += OnMenuTogglePressed;
    }

    /// <summary>
    /// Handles toggling the sidebar visibility using interpolation (Tween).
    /// </summary>
    private void OnMenuTogglePressed()
    {
        // Invert current sidebar state
        _isSidebarOpen = !_isSidebarOpen;
        
        // Create a tween to animate the 'custom_minimum_size:x' property
        Tween tween = GetTree().CreateTween();
        float targetWidth = _isSidebarOpen ? _sidebarWidth : 0.0f;
        
        // Execute animation with a duration of 0.3 seconds and a smooth cubic curve
        tween.TweenProperty(_sidebarWrapper, "custom_minimum_size:x", targetWidth, 0.3f)
             .SetTrans(Tween.TransitionType.Cubic)
             .SetEase(Tween.EaseType.InOut);
    }

    /// <summary>
    /// Handler for the send button press event.
    /// Initiates message processing asynchronously.
    /// </summary>
    private void OnSendPressed()
    {
        // Task is intentionally discarded as this is a UI event handler
        _ = ProcessMessage(_chatInput.Text);
    }

    /// <summary>
    /// Handler for text submission from the LineEdit (Enter key).
    /// </summary>
    /// <param name="newText">The text entered by the user.</param>
    private void OnTextSubmitted(string newText)
    {
        _ = ProcessMessage(newText);
    }

    /// <summary>
    /// Processes the core chat logic: validates input, updates user UI,
    /// simulates latency, and generates the bot response.
    /// </summary>
    /// <param name="text">The message to process.</param>
    private async Task ProcessMessage(string text)
    {
        // Validate empty or whitespace input
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Immediate cleanup of the input field to improve UX
        _chatInput.Text = string.Empty;

        // Instantiation and configuration of the user's message
        HBoxContainer newUserMsg = (HBoxContainer)_userMsgTemplate.Duplicate();
        newUserMsg.GetNode<Label>("Bubble/Text").Text = text;
        newUserMsg.Visible = true; // Ensure the copy is visible
        _messagesVBox.AddChild(newUserMsg);

        // Force scroll down after adding content
        ScrollToBottom();

        // Simulate network delay or processing time (0.6 seconds)
        await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);

        // Instantiation and configuration of the bot's message
        HBoxContainer newBotMsg = (HBoxContainer)_botMsgTemplate.Duplicate();
        newBotMsg.GetNode<Label>("Bubble/Text").Text = "¡Hola! Todavía estoy trabajando en mis conexiones y funciones de backend, pero mi interfaz y diseño ya son totalmente funcionales. ¿En qué más te puedo ayudar?";
        newBotMsg.Visible = true;
        _messagesVBox.AddChild(newBotMsg);

        // Final scroll update
        ScrollToBottom();
    }

    /// <summary>
    /// Forces the scroll container to scroll to the absolute bottom.
    /// Waits for the next process frame to ensure the UI engine has recalculated sizes.
    /// </summary>
    private async void ScrollToBottom()
    {
        // Wait for a process frame to ensure VBoxContainer has updated its height with new children
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        
        // Set vertical scroll value to the maximum possible
        ScrollBar vScroll = _chatScroll.GetVScrollBar();
        vScroll.Value = vScroll.MaxValue;
    }
}