using Godot;

namespace Logic.UI.Components
{
    public partial class MensajeBotUI : HBoxContainer
    {
        [Export] private RichTextLabel _messageBody;
        [Export] private HBoxContainer _botActionsContainer;
        [Export] private Label _botActionsLabel;

        private string _textoCompleto = "";
        private Timer _dotsTimer;
        private int _dotCount = 0;
        private string _baseActionText = "Pensando";

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

            Theme temaActivo = ThemeManager.Instance.ObtenerTemaGlobal(ThemeManager.Instance.EsModoOscuro);

            if (temaActivo != null && temaActivo.HasStylebox("panel", "BubbleBot2"))
            {
                miBurbuja.AddThemeStyleboxOverride("panel", temaActivo.GetStylebox("panel", "BubbleBot2"));
            }

            Color colorPensando = ThemeManager.Instance.EsModoOscuro ? new Color("e0e0e0") : new Color("808080");
            if (_botActionsLabel != null) _botActionsLabel.AddThemeColorOverride("font_color", colorPensando);
        }

        public void IniciarEstadoPensando(string accion = "Pensando")
        {
            _textoCompleto = "";
            _baseActionText = accion;
            if (_messageBody != null) 
            {
                _messageBody.Set("markdown_text", "");
                _messageBody.Text = "";
            }
            if (_botActionsContainer != null) _botActionsContainer.Visible = true;
            _dotsTimer.Start();
        }

        public void AgregarToken(string token)
        {
            _textoCompleto += token;
            if (_messageBody != null) 
            {
                _messageBody.Set("markdown_text", _textoCompleto);
                _messageBody.Text = _textoCompleto;
            }
        }

        public void FinalizarRespuesta()
        {
            _dotsTimer.Stop();
            if (_botActionsContainer != null) _botActionsContainer.Visible = false;
        }

        public string ObtenerTextoCompleto() => _textoCompleto;

        private void ActualizarPuntos()
        {
            _dotCount = (_dotCount + 1) % 4;
            if (_botActionsLabel != null) _botActionsLabel.Text = _baseActionText + new string('.', _dotCount);
        }
    }
}