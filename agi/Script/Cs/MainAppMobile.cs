using Godot;
using System;

namespace Logic.UI
{
    public partial class MainAppMobile : Panel
    {
        [Export] public PackedScene ChatbotScene;
        [Export] public PackedScene LivemodeScene;
       
        [Export] public Control SidebarWrapper;
        [Export] public Control ContentContainer;
       
        private Node _currentView;
        private bool _isSidebarOpen = true;

        private RichTextLabel _headerTitle;

        public override void _Ready()
        {
            _headerTitle = GetNodeOrNull<RichTextLabel>("MainLayout/RightColumn/HeaderPanel/HeaderMargin/HeaderLayout/HeaderTitle");

            Button menuBtn = GetNodeOrNull<Button>("MainLayout/RightColumn/HeaderPanel/HeaderMargin/HeaderLayout/MenuToggleButton");
            if (menuBtn != null) menuBtn.Pressed += ToggleSidebar;

            Button chatBtn = GetNodeOrNull<Button>("MainLayout/RightColumn/HeaderPanel/HeaderMargin/HeaderLayout/ChatBotModeButton");
            if (chatBtn != null) chatBtn.Pressed += OnChatbotModePressed;

            Button liveBtn = GetNodeOrNull<Button>("MainLayout/RightColumn/HeaderPanel/HeaderMargin/HeaderLayout/LiveModeButton");
            if (liveBtn != null) liveBtn.Pressed += OnLiveModePressed;

            // Iniciar por defecto en Chat
            LoadMode(ChatbotScene);
        }

        private void OnChatbotModePressed()
        {
            if (_headerTitle != null) _headerTitle.Text = "Chat";
            LoadMode(ChatbotScene);
        }

        private void OnLiveModePressed()
        {
            if (_headerTitle != null) _headerTitle.Text = "Live";
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
           
            float targetAlpha = _isSidebarOpen ? 1.0f : 0.0f;
            
            // Lógica exclusiva para celular: animar el ALTO (eje Y) de la barra inferior (de 60px a 0px)
            float targetHeight = _isSidebarOpen ? 60.0f : 0.0f;
            
            tween.TweenProperty(SidebarWrapper, "custom_minimum_size:y", targetHeight, 0.4f)
                 .SetTrans(Tween.TransitionType.Quart)
                 .SetEase(Tween.EaseType.Out);
                
            tween.TweenProperty(SidebarWrapper, "modulate:a", targetAlpha, 0.3f)
                 .SetTrans(Tween.TransitionType.Linear)
                 .SetEase(Tween.EaseType.InOut);
        }
    }
}