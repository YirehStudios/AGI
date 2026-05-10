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
            if (miBurbuja == null) return;

            Theme temaActivo = this.Theme ?? ResourceLoader.Load<Theme>("res://Resources/UI_Themes/TemaClaro.theme");

            if (temaActivo != null && temaActivo.HasStylebox("panel", "BubbleBot2"))
            {
                StyleBox estiloPuro = temaActivo.GetStylebox("panel", "BubbleBot2");
                miBurbuja.AddThemeStyleboxOverride("panel", estiloPuro);
            }
        }

        public void IniciarEstadoPensando(string accion = "Pensando")
        {
            _textoCompleto = "";
            _baseActionText = accion;
            if (_messageBody != null) _messageBody.Set("markdown_text", "");
            if (_botActionsContainer != null) _botActionsContainer.Visible = true;
            _dotsTimer.Start();
        }

        public void AgregarToken(string token)
        {
            _textoCompleto += token;
            if (_messageBody != null) _messageBody.Set("markdown_text", _textoCompleto);
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