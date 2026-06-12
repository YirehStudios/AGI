using Godot;

namespace Logic.UI.Components
{
    public partial class MensajeBotUI : HBoxContainer
    {
        [Export] private TextContainer _messageBody;
        [Export] private HBoxContainer _botActionsContainer;
        [Export] private Label _botActionsLabel;

        private string _textoCompleto = "";
        private Timer _dotsTimer;
        private int _dotCount = 0;
        private string _baseActionText = "Pensando";
        public global::System.Collections.Generic.List<Control> DynamicBlocks = new global::System.Collections.Generic.List<Control>();

        public override void _Ready()
        {
            _dotsTimer = new Timer();
            _dotsTimer.WaitTime = 0.4f;
            _dotsTimer.Timeout += ActualizarPuntos;
            AddChild(_dotsTimer);

            CallDeferred(nameof(ForzarPinturaNativa));
        }

        public override void _Notification(int what)
        {
            if (what == NotificationThemeChanged)
            {
                CallDeferred(nameof(ForzarPinturaNativa));
            }
        }

        private void ForzarPinturaNativa()
        {
            var miBurbuja = GetNodeOrNull<PanelContainer>("MessageBubble");
            if (miBurbuja == null || ThemeManager.Instance == null) return;

            bool esOscuro = ThemeManager.Instance.EsModoOscuro;
            Theme temaActivo = ThemeManager.Instance.ObtenerTemaGlobal(esOscuro);

            if (temaActivo != null && temaActivo.HasStylebox("panel", "BubbleBot2"))
            {
                miBurbuja.AddThemeStyleboxOverride("panel", temaActivo.GetStylebox("panel", "BubbleBot2"));
            }

            if (_messageBody != null)
            {
                string text = _messageBody.MarkdownText;
                _messageBody.MarkdownText = text; // Force re-parse to update inline chips colors
            }

            Color colorTexto = esOscuro ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.15f, 0.15f, 0.15f);
            if (_messageBody != null)
            {
                _messageBody.AddThemeColorOverride("default_color", colorTexto);
            }

            Color colorPensando = esOscuro ? new Color("e0e0e0") : new Color("808080");
            if (_botActionsLabel != null) _botActionsLabel.AddThemeColorOverride("font_color", colorPensando);
        }

        /// <summary>
        /// Prepares the message component for the initial processing state, resetting text fields and starting the processing timer.
        /// </summary>
        public void IniciarEstadoPensando(string accion = "Pensando")
        {
            _textoCompleto = "";
            _baseActionText = accion;
            if (_messageBody != null)
            {
                _messageBody.MarkdownText = "";
            }
            if (_botActionsContainer != null) _botActionsContainer.Visible = true;
            _dotsTimer.Start();
        }

        /// <summary>
        /// Updates the execution state text, ensures tracking timers are active, and clears 
        /// residual token string allocations to prevent JSON payload leakage in the user interface.
        /// </summary>
        public void CambiarEstadoAccion(string nuevaAccion)
        {
            _baseActionText = nuevaAccion;
            if (_botActionsContainer != null) _botActionsContainer.Visible = true;
            if (_dotsTimer.IsStopped()) _dotsTimer.Start();

            _textoCompleto = "";
            if (_messageBody != null)
            {
                _messageBody.MarkdownText = "";
            }
        }

        private PackedScene _codeEditScene = ResourceLoader.Load<PackedScene>("res://Scenes/IAScene/CodeEdit.tscn");

        public void AgregarToken(string token)
        {
            _textoCompleto += token;
            ActualizarBloques();
        }

        public void FinalizarRespuesta()
        {
            _dotsTimer.Stop();
            if (_botActionsContainer != null) _botActionsContainer.Visible = false;
        }

        public void ConfigurarMensaje(string texto)
        {
            _textoCompleto = texto;
            ActualizarBloques();
            FinalizarRespuesta();
        }

        private void ActualizarBloques()
        {
            if (_messageBody == null) return;
            var layout = GetNodeOrNull<VBoxContainer>("MessageBubble/MessageLayout");
            if (layout == null) return;

            if (DynamicBlocks.Count == 0)
            {
                DynamicBlocks.Add(_messageBody);
            }

            var parts = _textoCompleto.Split("```");
            int requiredChildren = parts.Length;

            while (DynamicBlocks.Count < requiredChildren)
            {
                int index = DynamicBlocks.Count;
                if (index % 2 == 0)
                {
                    // Text block
                    var newText = (TextContainer)_messageBody.Duplicate(0);
                    layout.AddChild(newText);
                    DynamicBlocks.Add(newText);
                }
                else
                {
                    // Code block
                    var newCode = _codeEditScene.Instantiate<Control>();
                    layout.AddChild(newCode);
                    DynamicBlocks.Add(newCode);
                }
            }

            for (int i = 0; i < DynamicBlocks.Count; i++)
            {
                DynamicBlocks[i].Visible = i < requiredChildren;
            }

            for (int i = 0; i < requiredChildren; i++)
            {
                string part = parts[i];
                if (i % 2 == 0)
                {
                    // Text block
                    var tc = (TextContainer)DynamicBlocks[i];
                    tc.MarkdownText = part;
                }
                else
                {
                    // Code block
                    var codeBlock = DynamicBlocks[i];
                    var lines = part.Split(new[] { '\n' }, 2);
                    string lang = lines[0].Trim();
                    string code = lines.Length > 1 ? lines[1] : "";

                    var langLabel = codeBlock.GetNodeOrNull<Label>("ContentLayout/HeaderBar/HeaderMargin/HeaderLayout/LanguageIndicator");
                    if (langLabel != null) langLabel.Text = string.IsNullOrEmpty(lang) ? "code" : lang;

                    var codeEdit = codeBlock.GetNodeOrNull<CodeEdit>("ContentLayout/CodeMargin/CodeEditorNode");
                    if (codeEdit != null)
                    {
                        codeEdit.Text = code;
                        // Dynamically adjust height to content
                        int lineCount = codeEdit.GetLineCount();
                        float newHeight = (lineCount * 24.0f) + 40.0f; // Approx height per line
                        if (newHeight > 500) newHeight = 500;
                        if (newHeight < 120) newHeight = 120;
                        codeBlock.CustomMinimumSize = new Vector2(0, newHeight);
                    }

                    var copyBtn = codeBlock.GetNodeOrNull<Button>("ContentLayout/HeaderBar/HeaderMargin/HeaderLayout/CopyButton");
                    if (copyBtn != null && !copyBtn.HasMeta("connected"))
                    {
                        copyBtn.SetMeta("connected", true);
                        copyBtn.Pressed += () => { DisplayServer.ClipboardSet(codeEdit?.Text ?? ""); };
                    }
                }
            }
        }

        public string ObtenerTextoCompleto() => _textoCompleto;

        private void ActualizarPuntos()
        {
            _dotCount = (_dotCount + 1) % 4;
            if (_botActionsLabel != null) _botActionsLabel.Text = _baseActionText + new string('.', _dotCount);
        }
    }
}