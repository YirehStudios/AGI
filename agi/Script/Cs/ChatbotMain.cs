using Godot;
using System;
using System.Threading.Tasks;

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

    /// <summary>
    /// Godot lifecycle initialization method.
    /// Subscribes to UI events using the Exported references linked in the editor.
    /// </summary>
    public override void _Ready()
    {
        // Validation to ensure nodes are assigned in the inspector
        if (SidebarContainer == null || MenuToggleButton == null || TextInputField == null)
        {
            GD.PrintErr("ChatbotMain: One or more Exported nodes are not assigned in the Inspector.");
            return;
        }

        // UI Event Subscriptions using the Exported variables
        SendButton.Pressed += OnSendPressed;
        TextInputField.TextSubmitted += OnTextSubmitted;
        MenuToggleButton.Pressed += OnMenuTogglePressed;
        
        // Ensure templates are hidden by default if not already
        if (UserMessageTemplate != null) UserMessageTemplate.Visible = false;
        if (BotMessageTemplate != null) BotMessageTemplate.Visible = false;
    }

    /// <summary>
    /// Handles toggling the sidebar visibility using interpolation (Tween).
    /// Animates the custom_minimum_size property of the SidebarContainer.
    /// </summary>
    private void OnMenuTogglePressed()
    {
        // Invert current sidebar state
        _isSidebarOpen = !_isSidebarOpen;
        
        // Create a tween to animate the 'custom_minimum_size:x' property
        Tween tween = GetTree().CreateTween();
        float targetWidth = _isSidebarOpen ? _sidebarWidth : 0.0f;
        
        // Execute animation with a duration of 0.3 seconds and a smooth cubic curve
        tween.TweenProperty(SidebarContainer, "custom_minimum_size:x", targetWidth, 0.3f)
             .SetTrans(Tween.TransitionType.Cubic)
             .SetEase(Tween.EaseType.InOut);
    }

    /// <summary>
    /// Handler for the send button press event.
    /// Initiates message processing asynchronously.
    /// </summary>
    private void OnSendPressed()
    {
        // Task is intentionally discarded as this is a UI event handler (fire-and-forget)
        _ = ProcessMessage(TextInputField.Text);
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
        TextInputField.Text = string.Empty;

        // Instantiation and configuration of the user's message
        HBoxContainer newUserMsg = (HBoxContainer)UserMessageTemplate.Duplicate();
        
        // Accessing internal components of the clone by relative path
        newUserMsg.GetNode<RichTextLabel>("MessageBubble/MessageBody").Text = text;
        newUserMsg.Visible = true; // Ensure the copy is visible
        MessagesContainer.AddChild(newUserMsg);

        // Force scroll down after adding content
        ScrollToBottom();

        // Simulate network delay or processing time (0.6 seconds)
        await ToSignal(GetTree().CreateTimer(0.6f), SceneTreeTimer.SignalName.Timeout);

        // Instantiation and configuration of the bot's message
        HBoxContainer newBotMsg = (HBoxContainer)BotMessageTemplate.Duplicate();
        
        // Accessing internal components of the clone by relative path
        newBotMsg.GetNode<RichTextLabel>("MessageBubble/MessageBody").Text = "¡Hola! Todavía estoy trabajando en mis conexiones y funciones de backend, pero mi interfaz y diseño ya son totalmente funcionales. ¿En qué más te puedo ayudar?";
        newBotMsg.Visible = true;
        MessagesContainer.AddChild(newBotMsg);

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
        ScrollBar vScroll = ChatScrollContainer.GetVScrollBar();
        vScroll.Value = vScroll.MaxValue;
    }
}