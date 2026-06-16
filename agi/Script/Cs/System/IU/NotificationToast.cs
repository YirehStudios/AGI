using Godot;
using System;

namespace Logic.System.UI
{
    public partial class NotificationToast : MarginContainer
    {
        private Label _messageLabel;
        private TextureRect _iconRect;
        private Button _copyButton;
        private string _technicalDetails;

        public override void _Ready()
        {
            _messageLabel = GetNode<Label>("%MessageLabel");
            _iconRect = GetNode<TextureRect>("%IconRect");
            _copyButton = GetNode<Button>("%CopyButton");

            if (_copyButton != null)
            {
                _copyButton.Pressed += OnCopyPressed;
                _copyButton.Hide();
            }
            
            Modulate = new Color(1, 1, 1, 0);
        }

        public void Setup(string message, string type, string details = "")
        {
            _messageLabel.Text = message;
            _technicalDetails = details;

            if (!string.IsNullOrEmpty(details) && _copyButton != null)
            {
                _copyButton.Show();
            }

            string iconPath = "res://Resources/Images/Icons/Util/info.svg";
            switch (type.ToLower())
            {
                case "success": iconPath = "res://Resources/Images/Icons/Util/success.svg"; break;
                case "warning": iconPath = "res://Resources/Images/Icons/Util/warning.svg"; break;
                case "error": iconPath = "res://Resources/Images/Icons/Util/error.svg"; break;
            }
            
            if (ResourceLoader.Exists(iconPath))
            {
                _iconRect.Texture = GD.Load<Texture2D>(iconPath);
            }

            AnimateIn(type.ToLower() != "error");
        }

        public void UpdateMessage(string newMessage)
        {
            _messageLabel.Text = newMessage;
        }

        private void OnCopyPressed()
        {
            DisplayServer.ClipboardSet(_technicalDetails);
            GD.Print("[NotificationToast] Technical details copied to clipboard.");
        }

        private void AnimateIn(bool autoDismiss)
        {
            Tween tween = CreateTween();
            // Slide in from top and fade in
            Vector2 targetPos = Position;
            Position = new Vector2(Position.X, Position.Y - 50);
            
            tween.Parallel().TweenProperty(this, "position:y", targetPos.Y, 0.4f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            tween.Parallel().TweenProperty(this, "modulate:a", 1.0f, 0.4f).SetTrans(Tween.TransitionType.Cubic);

            if (autoDismiss)
            {
                tween.Chain().TweenInterval(5.0f);
                tween.Chain().TweenProperty(this, "modulate:a", 0.0f, 0.3f).SetTrans(Tween.TransitionType.Cubic);
                tween.TweenCallback(Callable.From(QueueFree));
            }
        }
    }
}
