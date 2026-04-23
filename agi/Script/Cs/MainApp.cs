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
        [Export] public Button AgiModeButton;
        
        [Export] public RichTextLabel HeaderTitle;
        
        [Export] public Button HistoryButton;
        [Export] public ScrollContainer HistoryScroll;
        [Export] public VBoxContainer HistoryListContainer;
        
        [Export] public AnimationPlayer UiAnimator;

        private Node _currentView;
        private bool _isSidebarOpen = true;

        public override void _Ready()
        {
            if (SidebarWrapper != null)
            {
                SidebarWrapper.CustomMinimumSize = new Vector2(250, 0);
                SidebarWrapper.Modulate = new Color(1, 1, 1, 1);
                _isSidebarOpen = true;
            }

            if (MenuToggleButton != null) 
                MenuToggleButton.Pressed += ToggleSidebar;

            if (ChatBotModeButton != null) 
                ChatBotModeButton.Pressed += () => ChangeMode(ChatbotScene, "Modo Chat Bot");
            if (LiveModeButton != null) 
                LiveModeButton.Pressed += () => ChangeMode(LivemodeScene, "Modo Live");
            if (AgiModeButton != null) 
                AgiModeButton.Pressed += () => ChangeMode(null, "Modo AGI");

            if (HistoryButton != null)
            {
                HistoryButton.Pressed += () => 
                {
                    if (HistoryScroll != null) 
                        HistoryScroll.Visible = !HistoryScroll.Visible;
                };
            }

            InitializeLogoAnimation();
            ChangeMode(ChatbotScene, "Modo Chat Bot");
            LoadHistoryFiles();
        }

        private void InitializeLogoAnimation()
        {
            if (CompanyLogo == null) return;

            CompanyLogo.PivotOffset = CompanyLogo.Size / 2;
            Tween logoTween = GetTree().CreateTween().SetLoops();
            
            logoTween.Parallel().TweenProperty(CompanyLogo, "scale", new Vector2(1.03f, 1.03f), 2.0f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            logoTween.Parallel().TweenProperty(CompanyLogo, "modulate", new Color(1.15f, 1.15f, 1.15f, 1.0f), 2.0f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

            logoTween.Chain().TweenProperty(CompanyLogo, "scale", new Vector2(1.0f, 1.0f), 2.0f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            logoTween.Parallel().TweenProperty(CompanyLogo, "modulate", new Color(1.0f, 1.0f, 1.0f, 1.0f), 2.0f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }

        private void ChangeMode(PackedScene sceneToLoad, string titleText)
        {
            if (HeaderTitle != null) 
            {
                HeaderTitle.Text = titleText;
            }

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
            if (UiAnimator == null) return;

            _isSidebarOpen = !_isSidebarOpen;
            
            if (_isSidebarOpen)
            {
                UiAnimator.Play("sidebar_open");
            }
            else
            {
                UiAnimator.Play("sidebar_close");
            }
        }

        private void LoadHistoryFiles()
        {
            if (HistoryListContainer == null) return;

            foreach (Node child in HistoryListContainer.GetChildren())
            {
                child.QueueFree();
            }

            string historyPath = "user://history/";

            if (!DirAccess.DirExistsAbsolute(historyPath))
            {
                DirAccess.MakeDirAbsolute(historyPath);
                return; 
            }

            using var dir = DirAccess.Open(historyPath);
            if (dir != null)
            {
                dir.ListDirBegin();
                string fileName = dir.GetNext();
                
                while (fileName != "")
                {
                    if (!dir.CurrentIsDir() && fileName.EndsWith(".json"))
                    {
                        string chatName = fileName.Replace(".json", "");
                        
                        Button historyBtn = new Button();
                        historyBtn.Text = chatName;
                        historyBtn.Alignment = HorizontalAlignment.Left;
                        
                        if (HistoryButton != null)
                        {
                            historyBtn.AddThemeStyleboxOverride("normal", HistoryButton.GetThemeStylebox("normal"));
                            historyBtn.AddThemeStyleboxOverride("hover", HistoryButton.GetThemeStylebox("hover"));
                            historyBtn.AddThemeStyleboxOverride("pressed", HistoryButton.GetThemeStylebox("pressed"));
                            historyBtn.AddThemeStyleboxOverride("focus", HistoryButton.GetThemeStylebox("focus"));
                        }
                        
                        string capturedFileName = fileName;
                        historyBtn.Pressed += () => GD.Print($"Cargado: {capturedFileName}");
                        
                        HistoryListContainer.AddChild(historyBtn);
                    }
                    fileName = dir.GetNext();
                }
            }
        }
    }
}