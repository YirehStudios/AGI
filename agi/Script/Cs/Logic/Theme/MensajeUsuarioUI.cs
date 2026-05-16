using Godot;

namespace Logic.UI.Components
{
    public partial class MensajeUsuarioUI : HBoxContainer
    {
        [Export] private RichTextLabel _messageBody;
        [Export] private Button _copyBtn;
        [Export] private Button _minimizeBtn;

        private string _textoOriginal = "";
        private bool _estaMinimizado = false;

        public override void _Ready()
        {
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

            if (temaActivo != null && temaActivo.HasStylebox("panel", "BubbleUser2"))
            {
                miBurbuja.AddThemeStyleboxOverride("panel", temaActivo.GetStylebox("panel", "BubbleUser2"));
            }
        }

        public void ConfigurarMensaje(string texto)
        {
            _textoOriginal = texto;
            if (_messageBody != null) 
            {
                _messageBody.Set("markdown_text", texto);
                _messageBody.Text = texto;
            }
            if (_copyBtn != null) _copyBtn.Pressed += () => DisplayServer.ClipboardSet(_textoOriginal);
            if (_minimizeBtn != null) _minimizeBtn.Pressed += AlternarMinimizado;
        }

        private void AlternarMinimizado()
        {
            _estaMinimizado = !_estaMinimizado;
            if (_messageBody == null || _minimizeBtn == null) return;

            if (_estaMinimizado)
            {
                _messageBody.CustomMinimumSize = new Vector2(_messageBody.CustomMinimumSize.X, 30);
                _messageBody.FitContent = false;
                _messageBody.ClipContents = true;
                _minimizeBtn.Text = "Maximizar";
            }
            else
            {
                _messageBody.CustomMinimumSize = new Vector2(_messageBody.CustomMinimumSize.X, 0);
                _messageBody.FitContent = true;
                _messageBody.ClipContents = false;
                _minimizeBtn.Text = "Minimizar";
            }
        }
    }
}