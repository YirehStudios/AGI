using Godot;
using System;

namespace Logic.UI
{
    public partial class MainApp : Panel
    {
        [Export] public PackedScene ChatbotScene;
        [Export] public PackedScene LivemodeScene;
        
        [Export] public Control SidebarWrapper;
        [Export] public Control ContentContainer;
        
        [Export] public TextureRect CompanyLogo;
        [Export] public Button MenuToggleButton;
        [Export] public Button ChatBotModeButton;
        [Export] public Button LiveModeButton;
        [Export] public RichTextLabel HeaderTitle;

        private Node _currentView;
        private bool _isSidebarOpen = true;

        public override void _Ready()
        {
            if (MenuToggleButton != null) MenuToggleButton.Pressed += ToggleSidebar;
            if (ChatBotModeButton != null) ChatBotModeButton.Pressed += OnChatbotModePressed;
            if (LiveModeButton != null) LiveModeButton.Pressed += OnLiveModePressed;

            LoadMode(ChatbotScene);

            if (CompanyLogo != null)
            {
                CompanyLogo.PivotOffset = CompanyLogo.Size / 2;

                Tween logoTween = GetTree().CreateTween().SetLoops();
                
                // Animación estable usando Modulate y Escala
                // Fase 1: Crece sutil y se ilumina natural
                logoTween.Parallel().TweenProperty(CompanyLogo, "scale", new Vector2(1.03f, 1.03f), 2.0f)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                logoTween.Parallel().TweenProperty(CompanyLogo, "modulate", new Color(1.15f, 1.15f, 1.15f, 1.0f), 2.0f)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

                // Fase 2: Regresa a la normalidad
                logoTween.Chain().TweenProperty(CompanyLogo, "scale", new Vector2(1.0f, 1.0f), 2.0f)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                logoTween.Parallel().TweenProperty(CompanyLogo, "modulate", new Color(1.0f, 1.0f, 1.0f, 1.0f), 2.0f)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            }
        }

        private void OnChatbotModePressed()
        {
            if (HeaderTitle != null) HeaderTitle.Text = "Chat";
            LoadMode(ChatbotScene);
        }

        private void OnLiveModePressed()
        {
            if (HeaderTitle != null) HeaderTitle.Text = "Live";
            LoadMode(LivemodeScene);
        }

        public void LoadMode(PackedScene sceneToLoad)
        {
            if (sceneToLoad == null) return;

            if (_currentView != null)
            {
                _currentView.QueueFree();
            }

            _currentView = sceneToLoad.Instantiate();
            ContentContainer.AddChild(_currentView);

            if (_currentView is Control controlView)
            {
                controlView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                controlView.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                controlView.SizeFlagsVertical = SizeFlags.ExpandFill;
            }
        }

        public void ToggleSidebar()
        {
            _isSidebarOpen = !_isSidebarOpen;
            Tween tween = GetTree().CreateTween();
            tween.SetParallel(true);
            
            float targetWidth = _isSidebarOpen ? 250.0f : 0.0f;
            float targetAlpha = _isSidebarOpen ? 1.0f : 0.0f;
            
            tween.TweenProperty(SidebarWrapper, "custom_minimum_size:x", targetWidth, 0.5f)
                 .SetTrans(Tween.TransitionType.Expo)
                 .SetEase(Tween.EaseType.Out);
                 
            tween.TweenProperty(SidebarWrapper, "modulate:a", targetAlpha, 0.4f)
                 .SetTrans(Tween.TransitionType.Linear)
                 .SetEase(Tween.EaseType.InOut);
        }
    }
}