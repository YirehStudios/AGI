using Godot;
using System;

/// <summary>
/// Core controller for the Chatbot interface.
/// Manages user interactions, message rendering, and automated responses.
/// </summary>
public partial class ChatbotMain : Control
{
    private ScrollContainer _chatScroll;
    private VBoxContainer _messageList;
    private LineEdit _textInput;
    private Button _sendButton;
    private ScrollBar _vScrollBar;

    /// <summary>
    /// Lifecycle method called when the node enters the scene tree.
    /// Initializes UI references and establishes event subscriptions.
    /// </summary>
    public override void _Ready()
    {
        _chatScroll = GetNode<ScrollContainer>("Background/MainLayout/ChatScroll");
        _messageList = GetNode<VBoxContainer>("Background/MainLayout/ChatScroll/ChatMargin/MessageList");
        _textInput = GetNode<LineEdit>("Background/MainLayout/InputPanel/InputMargin/InputHBox/TextInput");
        _sendButton = GetNode<Button>("Background/MainLayout/InputPanel/InputMargin/InputHBox/SendButton");
        
        _vScrollBar = _chatScroll.GetVScrollBar();
        
        // Subscription to scrollbar changes to maintain visibility of new content
        _vScrollBar.Changed += ScrollToBottom;

        // User input signal connections
        _sendButton.Pressed += OnSendPressed;
        _textInput.TextSubmitted += OnTextSubmitted;
    }

    /// <summary>
    /// Event handler for manual button press.
    /// </summary>
    private void OnSendPressed()
    {
        SendMessage(_textInput.Text);
    }

    /// <summary>
    /// Event handler for text submission via the Enter key.
    /// </summary>
    /// <param name="text">The submitted string from the input field.</param>
    private void OnTextSubmitted(string text)
    {
        SendMessage(text);
    }

    /// <summary>
    /// Processes the outbound message and triggers the automated response sequence.
    /// Implements asynchronous delays to enhance user experience realism.
    /// </summary>
    /// <param name="text">Input text to be displayed and processed.</param>
    private async void SendMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        
        _textInput.Clear();
        AddMessageNode(text, true);
        
        // Simulated latency for the automated assistant response
        await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);
        
        const string botResponse = "Gracias por tu mensaje. Como asistente virtual de YirehStudios, estoy procesando tu solicitud para brindarte la mejor solución tecnológica.";
        AddMessageNode(botResponse, false);
    }

    /// <summary>
    /// Handles the structural creation of a message row.
    /// Dynamically aligns content and adds spacers based on the message source.
    /// </summary>
    /// <param name="text">Textual content of the message.</param>
    /// <param name="isUser">True if the sender is the client; false for the system/bot.</param>
    private void AddMessageNode(string text, bool isUser)
    {
        HBoxContainer messageRow = new HBoxContainer();
        messageRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        
        if (isUser)
        {
            messageRow.Alignment = BoxContainer.AlignmentMode.End;
            
            // Layout spacer to maintain message width constraints on the left
            MarginContainer spacer = new MarginContainer();
            spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            spacer.SizeFlagsStretchRatio = 0.2f;
            messageRow.AddChild(spacer);
            
            messageRow.AddChild(CreateBubble(text, true));
            messageRow.AddChild(CreateAvatar("U", new Color(0.2f, 0.2f, 0.2f)));
        }
        else
        {
            messageRow.Alignment = BoxContainer.AlignmentMode.Begin;
            messageRow.AddChild(CreateAvatar("Y", new Color(0.102f, 0.451f, 0.910f)));
            messageRow.AddChild(CreateBubble(text, false));
            
            // Layout spacer to maintain message width constraints on the right
            MarginContainer spacer = new MarginContainer();
            spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            spacer.SizeFlagsStretchRatio = 0.2f;
            messageRow.AddChild(spacer);
        }
        
        _messageList.AddChild(messageRow);
    }

    /// <summary>
    /// Component factory for chat bubbles.
    /// Configures visual presentation, including corner radii and text wrapping behaviors.
    /// </summary>
    /// <param name="text">The text to be rendered.</param>
    /// <param name="isUser">The context defining the color palette and bubble pointer side.</param>
    /// <returns>A configured PanelContainer representing the message bubble.</returns>
    private PanelContainer CreateBubble(string text, bool isUser)
    {
        PanelContainer bubble = new PanelContainer();
        bubble.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        
        StyleBoxFlat style = new StyleBoxFlat();
        style.ContentMarginLeft = 16;
        style.ContentMarginRight = 16;
        style.ContentMarginTop = 12;
        style.ContentMarginBottom = 12;
        
        if (isUser)
        {
            style.BgColor = new Color(0.910f, 0.941f, 0.996f);
            style.SetCornerRadiusAll(16);
            style.CornerRadiusTopRight = 4;
        }
        else
        {
            style.BgColor = new Color(0.941f, 0.957f, 0.976f);
            style.SetCornerRadiusAll(16);
            style.CornerRadiusTopLeft = 4;
        }
        
        bubble.AddThemeStyleboxOverride("panel", style);
        
        RichTextLabel label = new RichTextLabel();
        label.BbcodeEnabled = true;
        label.Text = text;
        label.FitContent = true;
        label.ScrollActive = false;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SelectionEnabled = true;
        
        Color textColor = isUser ? new Color(0.04f, 0.04f, 0.04f) : new Color(0.12f, 0.12f, 0.12f);
        label.AddThemeColorOverride("default_color", textColor);
        
        bubble.AddChild(label);
        return bubble;
    }

    /// <summary>
    /// Component factory for user/bot identifiers.
    /// Creates a circular container with a centered label.
    /// </summary>
    /// <param name="letter">The character displayed.</param>
    /// <param name="bgColor">The background color of the circle.</param>
    /// <returns>A styled PanelContainer.</returns>
    private PanelContainer CreateAvatar(string letter, Color bgColor)
    {
        PanelContainer avatar = new PanelContainer();
        avatar.CustomMinimumSize = new Vector2(36, 36);
        avatar.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        
        StyleBoxFlat style = new StyleBoxFlat();
        style.BgColor = bgColor;
        style.SetCornerRadiusAll(18);
        
        avatar.AddThemeStyleboxOverride("panel", style);
        
        Label label = new Label();
        label.Text = letter;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        
        avatar.AddChild(label);
        return avatar;
    }

    /// <summary>
    /// Initiates a scroll adjustment to the bottom of the chat history.
    /// Defers execution to ensure node sizes are calculated after layout updates.
    /// </summary>
    private void ScrollToBottom()
    {
        CallDeferred(nameof(DeferredScroll));
    }

    /// <summary>
    /// Performs the vertical scroll update based on the current scrollbar maximum.
    /// </summary>
    private void DeferredScroll()
    {
        _chatScroll.ScrollVertical = (int)_vScrollBar.MaxValue;
    }
}