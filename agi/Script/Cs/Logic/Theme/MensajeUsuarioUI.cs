using Godot;
using Logic.UI.Effects;

namespace Logic.UI.Components
{
    public partial class MensajeUsuarioUI : HBoxContainer
    {
        [Export] private TextContainer _messageBody;
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

            bool isDark = ThemeManager.Instance.EsModoOscuro;
            Theme temaActivo = ThemeManager.Instance.ObtenerTemaGlobal(isDark);

            if (temaActivo != null)
            {
                var panelStyle = new StyleBoxFlat();
                panelStyle.BgColor = isDark ? new Color(0.15f, 0.15f, 0.2f, 0.7f) : new Color(0.9f, 0.9f, 0.95f, 0.7f);
                panelStyle.CornerRadiusTopLeft = 16;
                panelStyle.CornerRadiusTopRight = 16;
                panelStyle.CornerRadiusBottomLeft = 16;
                panelStyle.CornerRadiusBottomRight = 16;
                panelStyle.BorderWidthBottom = 1;
                panelStyle.BorderWidthTop = 1;
                panelStyle.BorderWidthLeft = 1;
                panelStyle.BorderWidthRight = 1;
                panelStyle.BorderColor = new Color(1, 1, 1, 0.15f);
                panelStyle.ContentMarginLeft = 15;
                panelStyle.ContentMarginRight = 15;
                panelStyle.ContentMarginTop = 12;
                panelStyle.ContentMarginBottom = 12;
                
                miBurbuja.AddThemeStyleboxOverride("panel", panelStyle);
                
                // Aplicar el Shader de Liquid Glass
                var effects = GetNodeOrNull<EffectsEngine>("/root/ThemeManager/EffectsEngine");
                if (effects == null)
                    effects = EffectsEngine.Instance;

                if (effects != null)
                {
                    Color mixColor = isDark ? new Color(0.15f, 0.15f, 0.2f, 0.6f) : new Color(0.9f, 0.9f, 0.95f, 0.5f);
                    effects.ApplyFrostedGlass(miBurbuja, 2.5f, mixColor, true);
                }
            }

            if (_messageBody != null)
            {
                _messageBody.AddThemeColorOverride("default_color", isDark ? new Color(1, 1, 1, 1) : new Color(0.1f, 0.1f, 0.1f, 1));
                string text = _messageBody.MarkdownText;
                _messageBody.MarkdownText = text; // Force re-parse to update inline chips colors
            }
        }

        public void ConfigurarMensaje(string texto)
        {
            _textoOriginal = texto;
            if (_messageBody != null)
            {
                _messageBody.MarkdownText = texto;
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
                _minimizeBtn.Text = "";
                _minimizeBtn.Icon = ResourceLoader.Load<Texture2D>("res://Resources/Images/Icons/Util/expand.svg");
            }
            else
            {
                _messageBody.CustomMinimumSize = new Vector2(_messageBody.CustomMinimumSize.X, 0);
                _messageBody.FitContent = true;
                _messageBody.ClipContents = false;
                _minimizeBtn.Text = "";
                _minimizeBtn.Icon = ResourceLoader.Load<Texture2D>("res://Resources/Images/Icons/Util/collapse.svg");
            }
        }
    }
}