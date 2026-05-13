using Godot;
using System;

namespace Logic.UI
{
    public partial class MainApp : Panel
    {
        [Export] public PackedScene ChatbotScene;
        [Export] public PackedScene LivemodeScene;
        [Export] public Panel SidebarWrapper;
        [Export] public Panel ContentContainer;
        [Export] public CenterContainer WelcomeOverlay;
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
        [Export] public Button SettingsTabBtn;
        [Export] public Panel SettingsPanel;
        [Export] public CheckButton DarkModeToggle;
        [Export] public Label SettingsTitle;
        [Export] public Label DarkSettingsLabel;
        [Export] public PanelContainer SidebarContainer;
        [Export] public Button SidebarSettingsButton;

        private Node _currentView;
        private bool _isSidebarOpen = true;
        private bool _isSettingsOpen = false;
        private Tween _settingsTabTween;
        private Tween _settingsPanelTween;
        private const float SettingsWidth = 640.0f;
        private bool _isCurrentlyDark = false;

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
                AgiModeButton.Pressed += () => GD.Print("AGI Mode Pressed - Not Implemented");
            if (HistoryButton != null)
            {
                HistoryButton.Pressed += () =>
                {
                    if (HistoryScroll != null)
                        HistoryScroll.Visible = !HistoryScroll.Visible;
                };
            } 

            if (SettingsTabBtn != null)
            {
                SettingsTabBtn.MouseEntered += OnSettingsTabHovered;
                SettingsTabBtn.MouseExited += OnSettingsTabUnhovered;
                SettingsTabBtn.Pressed += ToggleSettingsPanel;
            }

            if (SidebarSettingsButton != null)
            {
                SidebarSettingsButton.Pressed += ToggleSettingsPanel;
            }

            if (DarkModeToggle != null)
            {
                DarkModeToggle.Toggled += OnDarkModeToggled;
            }

            if (Logic.Config.ConfigManager.Instance != null && DarkModeToggle != null)
            {
                DarkModeToggle.ButtonPressed = Logic.Config.ConfigManager.Instance.DarkMode;
                OnDarkModeToggled(Logic.Config.ConfigManager.Instance.DarkMode);
            }
            else if (DarkModeToggle != null)
            {
                OnDarkModeToggled(DarkModeToggle.ButtonPressed);
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

        public void HideWelcomeMessage()
        {
            if (WelcomeOverlay != null)
            {
                WelcomeOverlay.Visible = false;
            }
        }

        private void ChangeMode(PackedScene sceneToLoad, string titleText)
        {
            if (ContentContainer != null)
            {
                foreach (Node child in ContentContainer.GetChildren())
                {
                    child.QueueFree();
                }
            }

            if (sceneToLoad == null) return;

            // Instanciamos la nueva
            _currentView = sceneToLoad.Instantiate();
            ContentContainer.AddChild(_currentView);

            if (_currentView is Control controlView)
            {
                controlView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                controlView.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                controlView.SizeFlagsVertical = SizeFlags.ExpandFill;
            }

            if (_currentView.HasMethod("UpdateTheme"))
            {
                _currentView.Call("UpdateTheme", _isCurrentlyDark);
            }
        }

        public void ToggleSidebar()
        {
            if (UiAnimator == null) return;
            
            _isSidebarOpen = !_isSidebarOpen;
            if (_isSidebarOpen) UiAnimator.Play("sidebar_open");
            else UiAnimator.Play("sidebar_close");
        }

        private void LoadHistoryFiles()
        {
            if (HistoryListContainer == null) return;
            foreach (Node child in HistoryListContainer.GetChildren()) child.QueueFree();

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
                        historyBtn.ClipText = true;
                        historyBtn.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                        historyBtn.CustomMinimumSize = new Vector2(10, 0);
                        
                        
                        string capturedFileName = fileName;
                        historyBtn.Pressed += () => GD.Print(capturedFileName);
                        HistoryListContainer.AddChild(historyBtn);
                    }
                    fileName = dir.GetNext();
                }
            }
        }

        private void OnSettingsTabHovered()
        {
            if (_isSettingsOpen) return;
            _settingsTabTween?.Kill();
            _settingsTabTween = GetTree().CreateTween();
            _settingsTabTween.Parallel().TweenProperty(SettingsTabBtn, "offset_left", -60.0f, 0.2f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            _settingsTabTween.Parallel().TweenProperty(SettingsTabBtn, "offset_right", 0.0f, 0.2f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }

        private void OnSettingsTabUnhovered()
        {
            if (_isSettingsOpen) return;
            _settingsTabTween?.Kill();
            _settingsTabTween = GetTree().CreateTween();
            _settingsTabTween.Parallel().TweenProperty(SettingsTabBtn, "offset_left", -20.0f, 0.3f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
            _settingsTabTween.Parallel().TweenProperty(SettingsTabBtn, "offset_right", 40.0f, 0.3f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        }

        private void ToggleSettingsPanel()
        {
            _isSettingsOpen = !_isSettingsOpen;
            _settingsPanelTween?.Kill();
            _settingsPanelTween = GetTree().CreateTween();
            
            if (_isSettingsOpen)
            {
                _settingsTabTween?.Kill();
                if (SettingsTabBtn != null) 
                {
                    SettingsTabBtn.OffsetLeft = -60.0f;
                    SettingsTabBtn.OffsetRight = 0.0f;
                }
                _settingsPanelTween.Parallel().TweenProperty(SettingsPanel, "offset_left", -SettingsWidth, 0.6f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                _settingsPanelTween.Parallel().TweenProperty(SettingsPanel, "offset_right", 0.0f, 0.6f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
            }
            else
            {
                _settingsPanelTween.Parallel().TweenProperty(SettingsPanel, "offset_left", 0.0f, 0.5f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                _settingsPanelTween.Parallel().TweenProperty(SettingsPanel, "offset_right", SettingsWidth, 0.5f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                _settingsTabTween?.Kill();
                _settingsTabTween = GetTree().CreateTween();
                if (SettingsTabBtn != null) 
                {
                    _settingsTabTween.Parallel().TweenProperty(SettingsTabBtn, "offset_left", -20.0f, 0.5f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                    _settingsTabTween.Parallel().TweenProperty(SettingsTabBtn, "offset_right", 40.0f, 0.5f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                }
            }
        }

        private void OnDarkModeToggled(bool isPressed)
        {
            _isCurrentlyDark = isPressed;
            
            if (ThemeManager.Instance != null)
            {
                this.Theme = ThemeManager.Instance.ObtenerTemaGlobal(isPressed);
                
                if (Logic.Config.ConfigManager.Instance != null)
                {
                    Logic.Config.ConfigManager.Instance.DarkMode = isPressed;
                    Logic.Config.ConfigManager.Instance.SaveSettings();
                }

                if (_currentView != null && _currentView.HasMethod("UpdateTheme"))
                {
                    _currentView.Call("UpdateTheme", isPressed);
                }
            }
        }
    }
}