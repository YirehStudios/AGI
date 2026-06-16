using Godot;
using System;

namespace Logic.System.UI
{
    public partial class NotificationManager : Node
    {
        private VBoxContainer _toastContainer;
        private PackedScene _toastScene;

        public override void _Ready()
        {
            _toastScene = GD.Load<PackedScene>("res://Scenes/Config/Noti.tscn");

            CanvasLayer layer = new CanvasLayer { Layer = 100 };
            AddChild(layer);

            MarginContainer margin = new MarginContainer();
            margin.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            margin.AddThemeConstantOverride("margin_top", 20);
            margin.AddThemeConstantOverride("margin_right", 20);
            layer.AddChild(margin);

            _toastContainer = new VBoxContainer();
            _toastContainer.AddThemeConstantOverride("separation", 10);
            margin.AddChild(_toastContainer);
        }

        public NotificationToast NotifyInfo(string message) => ShowToast(message, "info");
        public NotificationToast NotifySuccess(string message) => ShowToast(message, "success");
        public NotificationToast NotifyWarning(string message) => ShowToast(message, "warning");
        public NotificationToast NotifyError(string message, string technicalDetails = "") => ShowToast(message, "error", technicalDetails);

        private NotificationToast ShowToast(string message, string type, string details = "")
        {
            if (_toastScene == null) return null;

            NotificationToast toast = _toastScene.Instantiate<NotificationToast>();
            _toastContainer.AddChild(toast);
            toast.Setup(message, type, details);
            return toast;
        }
    }
}
