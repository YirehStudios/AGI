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
    private HBoxContainer _currentBotMessageNode;

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

        // Suscripción de evento de tokens al NetworkManager
        Node networkManager = GetNode("/root/NetworkManager");
        if (networkManager != null)
        {
            networkManager.Connect("TokenReceived", new Callable(this, MethodName.OnTokenReceived));
        }
        else
        {
            GD.PrintErr("ChatbotMain: NetworkManager singleton not found in tree.");
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

    // Instancia los mensajes y emite la orden de streaming en el backend
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
        
        // Almacena la referencia del contenedor actual para la concatenación en tiempo real
        _currentBotMessageNode = newBotMsg;

        ScrollToBottom();

        Node networkManager = GetNode("/root/NetworkManager");
        if (networkManager != null)
        {
            networkManager.Call("StreamChatCompletion", text);
        }
    }

    // Recibe los tokens del flujo en vivo y los inyecta al nodo activo actual
    private void OnTokenReceived(string token)
    {
        if (_currentBotMessageNode == null) return;

        RichTextLabel messageBody = _currentBotMessageNode.GetNode<RichTextLabel>("MessageBubble/MessageBody");
        messageBody.Text += token;

        ScrollToBottom();

        // Lógica de validación de sintaxis para disparar la ejecución del motor de síntesis de voz
        if (token.Contains(".") || token.Contains("!") || token.Contains("?"))
        {
            DispatchPiperSpeech();
        }
    }

    private void DispatchPiperSpeech()
    {
        GD.Print("ChatbotMain: Punctuation syntax detected. Triggering Piper TTS dispatch.");
        // Estructura reservada para la conexión directa con el manejador del proceso Piper
    }

    private async void ScrollToBottom()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        
        ScrollBar vScroll = ChatScrollContainer.GetVScrollBar();
        vScroll.Value = vScroll.MaxValue;
    }
}