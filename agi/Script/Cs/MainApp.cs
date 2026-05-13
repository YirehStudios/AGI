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
                AgiModeButton.Pressed += () => ChangeMode(null, "Modo AGI");

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
    if (HeaderTitle != null) HeaderTitle.Text = titleText;
    
    
    if (WelcomeOverlay != null)
    {
        WelcomeOverlay.Visible = (sceneToLoad == ChatbotScene);
    }
    
    if (sceneToLoad == null) return;
    
    if (_currentView != null) _currentView.QueueFree();

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
        }

        private void OnDarkModeToggled(bool isPressed)
        {
            _isCurrentlyDark = isPressed;
            
            if (ThemeManager.Instance != null)
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
                        
                        if (HistoryButton != null)
                        {
                            historyBtn.AddThemeStyleboxOverride("normal", HistoryButton.GetThemeStylebox("normal"));
                            historyBtn.AddThemeStyleboxOverride("hover", HistoryButton.GetThemeStylebox("hover"));
                            historyBtn.AddThemeStyleboxOverride("pressed", HistoryButton.GetThemeStylebox("pressed"));
                            historyBtn.AddThemeStyleboxOverride("focus", HistoryButton.GetThemeStylebox("focus"));
                        }

                        string capturedFileName = fileName;
                        historyBtn.Pressed += () => GD.Print(capturedFileName);
                        HistoryListContainer.AddChild(historyBtn);
                    }
                    fileName = dir.GetNext();
                }
            }
            
            ApplyTheme(_isCurrentlyDark);
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
            ApplyTheme(_isCurrentlyDark);
        }

        private void ApplyTheme(bool isDark)
{
    Color primaryText = isDark ? new Color(1f, 1f, 1f) : new Color(0.2f, 0.2f, 0.2f);
    Color bgMain = isDark ? new Color(0.12f, 0.12f, 0.14f) : new Color(0.949f, 0.949f, 0.969f);
    Color bgSurface = isDark ? new Color(0.18f, 0.18f, 0.20f) : new Color(1f, 1f, 1f);

    if (this.HasThemeStylebox("panel") && this.GetThemeStylebox("panel") is StyleBoxFlat existingMainStyle)
    {
        StyleBoxFlat newMain = (StyleBoxFlat)existingMainStyle.Duplicate();
        newMain.BgColor = bgMain;
        this.AddThemeStyleboxOverride("panel", newMain);
    }
    else
    {
        StyleBoxFlat newMain = new StyleBoxFlat();
        newMain.BgColor = bgMain;
        this.AddThemeStyleboxOverride("panel", newMain);
    }

    if (SidebarContainer != null)
    {
        if (SidebarContainer.HasThemeStylebox("panel") && SidebarContainer.GetThemeStylebox("panel") is StyleBoxFlat sbBox)
        {
            StyleBoxFlat newSb = (StyleBoxFlat)sbBox.Duplicate();
            newSb.BgColor = bgSurface;
            SidebarContainer.AddThemeStyleboxOverride("panel", newSb);
        }
    }

    if (SettingsPanel != null)
    {
        if (SettingsPanel.HasThemeStylebox("panel") && SettingsPanel.GetThemeStylebox("panel") is StyleBoxFlat spBox)
        {
            StyleBoxFlat newSp = (StyleBoxFlat)spBox.Duplicate();
            newSp.BgColor = isDark ? new Color(0.15f, 0.15f, 0.18f, 0.85f) : new Color(0.92f, 0.92f, 0.95f, 0.65f);
            SettingsPanel.AddThemeStyleboxOverride("panel", newSp);
        }
    }

    if (SettingsTabBtn != null)
    {
        if (isDark)
        {
            SettingsTabBtn.AddThemeColorOverride("icon_normal_color", new Color(0.95f, 0.95f, 0.95f));
            SettingsTabBtn.AddThemeColorOverride("icon_hover_color", new Color(1f, 1f, 1f));
            SettingsTabBtn.AddThemeColorOverride("icon_pressed_color", new Color(1f, 1f, 1f));
        }
        else
        {
            SettingsTabBtn.AddThemeColorOverride("icon_normal_color", new Color(0.2f, 0.2f, 0.2f));
            SettingsTabBtn.AddThemeColorOverride("icon_hover_color", new Color(0.1f, 0.1f, 0.1f));
            SettingsTabBtn.AddThemeColorOverride("icon_pressed_color", new Color(1f, 1f, 1f));
        }
    }

    Button[] sidebarBtns = { ChatBotModeButton, LiveModeButton, AgiModeButton, HistoryButton, SidebarSettingsButton };
    foreach (var btn in sidebarBtns)
    {
        if (btn == null) continue;

        if (btn.HasThemeStylebox("hover") && btn.GetThemeStylebox("hover") is StyleBoxFlat hBox)
        {
            StyleBoxFlat newHov = (StyleBoxFlat)hBox.Duplicate();
            newHov.BgColor = isDark ? new Color(0.25f, 0.25f, 0.28f) : new Color(0.898f, 0.898f, 0.902f);
            btn.AddThemeStyleboxOverride("hover", newHov);
            btn.AddThemeStyleboxOverride("pressed", newHov);
        }
    }

    if (HistoryListContainer != null)
    {
        foreach (Node child in HistoryListContainer.GetChildren())
        {
            if (child is Button histBtn)
            {
                if (histBtn.HasThemeStylebox("hover") && histBtn.GetThemeStylebox("hover") is StyleBoxFlat hBox)
                {
                    StyleBoxFlat newHov = (StyleBoxFlat)hBox.Duplicate();
                    newHov.BgColor = isDark ? new Color(0.25f, 0.25f, 0.28f) : new Color(0.898f, 0.898f, 0.902f);
                    histBtn.AddThemeStyleboxOverride("hover", newHov);
                    histBtn.AddThemeStyleboxOverride("pressed", newHov);
                }
            }
        }
    }

    TintAllButtonsAndLabels(SidebarWrapper, primaryText);
    TintAllButtonsAndLabels(MenuToggleButton, primaryText);

    if (HeaderTitle != null) HeaderTitle.AddThemeColorOverride("default_color", primaryText);
    if (SettingsTitle != null) SettingsTitle.AddThemeColorOverride("font_color", primaryText);
    if (DarkSettingsLabel != null) DarkSettingsLabel.AddThemeColorOverride("font_color", primaryText);

    if (_currentView != null && _currentView.HasMethod("UpdateTheme"))
    {
        _currentView.Call("UpdateTheme", isDark);
    }
}
        private void TintAllButtonsAndLabels(Node node, Color color)
        {
            if (node == null) return;

            if (node is Button btn)
            {
                btn.AddThemeColorOverride("font_color", color);
                btn.AddThemeColorOverride("icon_normal_color", color);
                btn.AddThemeColorOverride("font_hover_color", color);
                btn.AddThemeColorOverride("icon_hover_color", color);
                btn.AddThemeColorOverride("font_focus_color", color);
                btn.AddThemeColorOverride("icon_focus_color", color);
                btn.AddThemeColorOverride("font_pressed_color", color);
                btn.AddThemeColorOverride("icon_pressed_color", color);
            }
            else if (node is Label lbl)
            {
                lbl.AddThemeColorOverride("font_color", color);
            }

            foreach (Node child in node.GetChildren())
            {
                TintAllButtonsAndLabels(child, color);
            }
        }
    }
}