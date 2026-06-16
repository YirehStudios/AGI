using Godot;
using System;

namespace Logic.UI
{
    public partial class MainApp : Panel
    {
        [Export] public PackedScene ChatbotScene;
        [Export] public PackedScene LivemodeScene;
        [Export] public PackedScene AgiModeScene; // Nueva escena para el modelo 3D (Kipfel3D)
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
        [Export] public PanelContainer SidebarContainer;
        [Export] public Button SidebarSettingsButton;

        [ExportCategory("Files UI")]
        [Export] public Button SidebarFilesButton;
        [Export] public Panel FilesPanel;
        [Export] public Button FilesTabBtn;
        [Export] public VBoxContainer FilesListContainer;
        [Export] public PanelContainer AttachmentChipTemplate;

        private Node _currentView;
        private bool _isSidebarOpen = true;
        private bool _isSettingsOpen = false;
        private bool _isFilesOpen = false;
        private Tween _settingsTabTween;
        private Tween _settingsPanelTween;
        private Tween _filesTabTween;
        private Tween _filesPanelTween;
        private const float SettingsWidth = 640.0f;
        private const float FilesWidth = 400.0f;
        private bool _isCurrentlyDark = false;
        private readonly global::System.Collections.Generic.HashSet<string> _displayedHistoryFiles = new();
        private readonly global::System.Collections.Generic.Dictionary<string, MarginContainer> _historyRows = new();
        private bool _isHistoryMenuOpen = false;

        /// <summary>
        /// Initializes UI panel constraints, registers core signal connections, queries localized state tracking parameters 
        /// from the updated unified ConfigManager singleton object, and executes the default startup scene transition loop.
        /// </summary>
        public override void _Ready()
        {
            if (Logic.System.Config.ConfigManager.Instance != null)
            {
                _isCurrentlyDark = Logic.System.Config.ConfigManager.Instance.DarkMode;
            }

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
                AgiModeButton.Pressed += () => ChangeMode(AgiModeScene, "Modo AGI");

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

            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager != null)
            {
                chatManager.OnSessionListUpdated += LoadHistoryFiles;
            }

            if (SidebarFilesButton != null)
                SidebarFilesButton.Pressed += ToggleFilesPanel;
            
            if (SettingsTabBtn != null)
            {
                SettingsTabBtn.OffsetLeft = -20.0f;
                SettingsTabBtn.OffsetRight = 40.0f;
            }

            if (FilesTabBtn != null)
            {
                FilesTabBtn.Pressed += ToggleFilesPanel;
                FilesTabBtn.MouseEntered += OnFilesTabHovered;
                FilesTabBtn.MouseExited += OnFilesTabUnhovered;
                
                FilesTabBtn.OffsetLeft = -20.0f;
                FilesTabBtn.OffsetRight = 40.0f;
            }

            ChangeMode(ChatbotScene, "Modo Chat Bot");
            LoadHistoryFiles();
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

            _currentView = sceneToLoad.Instantiate();

            // Si la escena raíz NO es un Control 2D (es decir, es 3D), se envuelve en SubViewport
            if (_currentView is not Control)
            {
                SubViewportContainer viewportContainer = new SubViewportContainer();
                viewportContainer.Name = "AgiViewportContainer";
                viewportContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                viewportContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                viewportContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                viewportContainer.Stretch = true;

                SubViewport viewport = new SubViewport();
                viewport.Name = "AgiSubViewport";
                viewport.TransparentBg = true;
                viewport.RenderTargetClearMode = SubViewport.ClearMode.Always;
                viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
                viewport.AudioListenerEnable3D = true;
                viewport.AudioListenerEnable2D = true;

                viewport.AddChild(_currentView);
                viewportContainer.AddChild(viewport);
                ContentContainer.AddChild(viewportContainer);

                // Sync the viewport size once the container has been laid out
                viewportContainer.Ready += () =>
                {
                    viewport.Size = (Vector2I)viewportContainer.Size;
                };
                viewportContainer.Resized += () =>
                {
                    viewport.Size = (Vector2I)viewportContainer.Size;
                };
            }
            else
            {
                ContentContainer.AddChild(_currentView);

                if (_currentView is Control controlView)
                {
                    controlView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                    controlView.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    controlView.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                }
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
            if (_isHistoryMenuOpen) return; // Prevent background updates while menu is active

            string historyPath = "user://history/";
            if (!DirAccess.DirExistsAbsolute(historyPath))
            {
                DirAccess.MakeDirAbsolute(historyPath);
                return;
            }

            // Find all active history files currently on disk
            var filesOnDisk = new global::System.Collections.Generic.HashSet<string>();
            using var dir = DirAccess.Open(historyPath);
            if (dir != null)
            {
                dir.ListDirBegin();
                string fileName = dir.GetNext();
                while (fileName != "")
                {
                    if (!dir.CurrentIsDir() && fileName.EndsWith(".json"))
                    {
                        filesOnDisk.Add(fileName);
                    }
                    fileName = dir.GetNext();
                }
            }

            // Remove any items from rows cache and tracking set that are no longer on disk
            var filesToRemove = new global::System.Collections.Generic.List<string>();
            foreach (var displayed in _displayedHistoryFiles)
            {
                if (!filesOnDisk.Contains(displayed))
                {
                    filesToRemove.Add(displayed);
                }
            }

            foreach (var toRemove in filesToRemove)
            {
                _displayedHistoryFiles.Remove(toRemove);
                if (_historyRows.TryGetValue(toRemove, out var oldRow))
                {
                    oldRow.QueueFree();
                    _historyRows.Remove(toRemove);
                }
            }

            // Sort files by pin status first, then by last write time (descending)
            var sortedFiles = new global::System.Collections.Generic.List<string>(filesOnDisk);
            var configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            var pinnedList = configManager?.PinnedChats ?? new global::System.Collections.Generic.List<string>();

            sortedFiles.Sort((a, b) =>
            {
                string nameA = a.Replace(".json", "");
                string nameB = b.Replace(".json", "");

                bool aPinned = pinnedList.Contains(nameA);
                bool bPinned = pinnedList.Contains(nameB);

                if (aPinned && !bPinned) return -1;
                if (!aPinned && bPinned) return 1;

                string pathA = global::System.IO.Path.Combine(ProjectSettings.GlobalizePath(historyPath), a);
                string pathB = global::System.IO.Path.Combine(ProjectSettings.GlobalizePath(historyPath), b);

                long timeA = global::System.IO.File.Exists(pathA) ? global::System.IO.File.GetLastWriteTimeUtc(pathA).Ticks : 0;
                long timeB = global::System.IO.File.Exists(pathB) ? global::System.IO.File.GetLastWriteTimeUtc(pathB).Ticks : 0;

                return timeB.CompareTo(timeA); // Descending order
            });

            // Build or update rows
            foreach (var fileName in sortedFiles)
            {
                string chatName = fileName.Replace(".json", "");
                bool isPinned = pinnedList.Contains(chatName);

                if (!_displayedHistoryFiles.Contains(fileName))
                {
                    _displayedHistoryFiles.Add(fileName);

                    // Create root MarginContainer for the row to keep it flat and let the theme style it
                    MarginContainer rowPanel = new MarginContainer();
                    rowPanel.Name = chatName;
                    rowPanel.MouseFilter = Control.MouseFilterEnum.Stop; // Crucial for input capturing!
                    rowPanel.AddThemeConstantOverride("margin_left", 8);
                    rowPanel.AddThemeConstantOverride("margin_right", 8);
                    rowPanel.AddThemeConstantOverride("margin_top", 4);
                    rowPanel.AddThemeConstantOverride("margin_bottom", 4);

                    // Layout HBox
                    HBoxContainer rowLayout = new HBoxContainer();
                    rowLayout.MouseFilter = Control.MouseFilterEnum.Pass;
                    rowPanel.AddChild(rowLayout);

                    // 1. Pinned indicator if applicable
                    if (isPinned)
                    {
                        Label pinIndicator = new Label();
                        pinIndicator.Text = "📌";
                        pinIndicator.MouseFilter = Control.MouseFilterEnum.Pass;
                        rowLayout.AddChild(pinIndicator);
                    }

                    // 2. Chat name button (Flat button - styles, normal, hover, pressed are 100% styled by the global theme!)
                    Button historyBtn = new Button();
                    historyBtn.Name = "TextButton";
                    historyBtn.Text = chatName;
                    historyBtn.Alignment = HorizontalAlignment.Left;
                    historyBtn.ClipText = true;
                    historyBtn.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                    historyBtn.CustomMinimumSize = new Vector2(10, 0);
                    historyBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                    historyBtn.Flat = true;
                    historyBtn.MouseFilter = Control.MouseFilterEnum.Stop; // Crucial for receiving input clicks!

                    string sessionName = chatName;
                    historyBtn.Pressed += () =>
                    {
                        var manager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
                        if (manager != null)
                        {
                            manager.LoadSessionByName(sessionName);
                        }

                        // Refresh active row highlights
                        UpdateActiveRowHighlights();

                        if (_currentView != null && _currentView.HasMethod("LoadActiveMessagesIntoUI"))
                        {
                            _currentView.Call("LoadActiveMessagesIntoUI");
                        }
                    };

                    // Wire Right-Click for both button and container for premium desktop behavior!
                    rowPanel.GuiInput += (InputEvent @event) =>
                    {
                        if (@event is InputEventMouseButton mouseEvent)
                        {
                            if (mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Right)
                            {
                                ShowContextMenu(sessionName, historyPath);
                            }
                        }
                    };

                    historyBtn.GuiInput += (InputEvent @event) =>
                    {
                        if (@event is InputEventMouseButton mouseEvent)
                        {
                            if (mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Right)
                            {
                                ShowContextMenu(sessionName, historyPath);
                            }
                        }
                    };

                    rowLayout.AddChild(historyBtn);

                    _historyRows[fileName] = rowPanel;

                    // Exquisite entrance slide-in/fade-in animation
                    rowPanel.Modulate = new Color(1, 1, 1, 0);
                    rowPanel.Position = new Vector2(-20, rowPanel.Position.Y);

                    var tween = GetTree().CreateTween();
                    tween.Parallel().TweenProperty(rowPanel, "modulate", new Color(1, 1, 1, 1), 0.4f)
                         .SetTrans(Tween.TransitionType.Cubic)
                         .SetEase(Tween.EaseType.Out);
                    tween.Parallel().TweenProperty(rowPanel, "position:x", 0.0f, 0.4f)
                         .SetTrans(Tween.TransitionType.Back)
                         .SetEase(Tween.EaseType.Out);
                }
            }

            // Remove visual child association from container
            foreach (Node child in HistoryListContainer.GetChildren())
            {
                HistoryListContainer.RemoveChild(child);
            }

            // Append all rows back in sorted order
            foreach (var fileName in sortedFiles)
            {
                if (_historyRows.TryGetValue(fileName, out var rowNode))
                {
                    HistoryListContainer.AddChild(rowNode);
                }
            }

            // Update highlighting
            UpdateActiveRowHighlights();
        }

        private PopupMenu CreatePremiumPopupMenu()
        {
            PopupMenu menu = new PopupMenu();
            
            var panelStyle = new StyleBoxFlat();
            bool esOscuro = Logic.UI.ThemeManager.Instance?.EsModoOscuro ?? true;
            panelStyle.BgColor = esOscuro ? new Color(0.1f, 0.1f, 0.1f) : new Color(1.0f, 1.0f, 1.0f);
            
            var config = Logic.System.Config.ConfigManager.Instance;
            if (config != null && config.TransModeApplyToPopups)
            {
                Color c = panelStyle.BgColor;
                c.A = config.TransModePopupsOpacity;
                panelStyle.BgColor = c;
                menu.Transparent = true;
                menu.TransparentBg = true;
            }

            panelStyle.BorderWidthLeft = 1;
            panelStyle.BorderWidthTop = 1;
            panelStyle.BorderWidthRight = 1;
            panelStyle.BorderWidthBottom = 1;
            panelStyle.BorderColor = esOscuro ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.85f, 0.85f, 0.85f);
            panelStyle.CornerRadiusTopLeft = 8;
            panelStyle.CornerRadiusTopRight = 8;
            panelStyle.CornerRadiusBottomLeft = 8;
            panelStyle.CornerRadiusBottomRight = 8;
            panelStyle.SetContentMarginAll(6);
            panelStyle.ShadowColor = new Color(0, 0, 0, 0.08f);
            panelStyle.ShadowSize = 6;
            
            menu.AddThemeStyleboxOverride("panel", panelStyle);

            // Item hover background stylebox
            var hoverStyle = new StyleBoxFlat();
            hoverStyle.BgColor = new Color(0.92f, 0.92f, 0.96f); // Soft grey hover
            hoverStyle.CornerRadiusTopLeft = 4;
            hoverStyle.CornerRadiusTopRight = 4;
            hoverStyle.CornerRadiusBottomLeft = 4;
            hoverStyle.CornerRadiusBottomRight = 4;
            hoverStyle.ContentMarginLeft = 12;
            hoverStyle.ContentMarginRight = 12;
            hoverStyle.ContentMarginTop = 6;
            hoverStyle.ContentMarginBottom = 6;
            
            menu.AddThemeStyleboxOverride("hover", hoverStyle);

            // Fonts and Colors matching the exact screenshot (dark charcoal text)
            menu.AddThemeColorOverride("font_color", new Color(0.15f, 0.15f, 0.2f));
            menu.AddThemeColorOverride("font_hover_color", new Color(0.05f, 0.05f, 0.1f));
            menu.AddThemeColorOverride("font_separator_color", new Color(0.8f, 0.8f, 0.8f));
            
            // Spacing
            menu.AddThemeConstantOverride("v_separation", 6);
            menu.AddThemeConstantOverride("h_separation", 12);
            
            return menu;
        }

        private void ShowContextMenu(string sessionName, string historyPath)
        {
            var configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            var pinnedList = configManager?.PinnedChats ?? new global::System.Collections.Generic.List<string>();
            bool isPinned = pinnedList.Contains(sessionName);

            _isHistoryMenuOpen = true; // Shield list updates
            PopupMenu menu = CreatePremiumPopupMenu();
            menu.AddItem("Renombrar", 0);
            menu.AddItem(isPinned ? "Desfijar" : "Fijar", 1);
            menu.AddItem("Eliminar", 2);

            menu.IdPressed += (long id) =>
            {
                if (id == 0)
                {
                    PromptRenameChat(sessionName, historyPath);
                }
                else if (id == 1)
                {
                    TogglePinChat(sessionName);
                }
                else if (id == 2)
                {
                    PromptDeleteChat(sessionName, historyPath);
                }
            };

            menu.PopupHide += () =>
            {
                // Short delay to let clicks finish cleanly
                GetTree().CreateTimer(0.1f).Timeout += () =>
                {
                    _isHistoryMenuOpen = false;
                    LoadHistoryFiles();
                };
            };

            menu.PopupWindow = true;
            AddChild(menu);
            menu.Position = (Vector2I)GetViewport().GetMousePosition();
            menu.Popup();
        }

        private void UpdateActiveRowHighlights()
        {
            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            string activeChatName = chatManager?.CurrentSession?.SessionName ?? "";

            foreach (var kvp in _historyRows)
            {
                string chatName = kvp.Key.Replace(".json", "");
                MarginContainer rowPanel = kvp.Value;
                
                Button textBtn = rowPanel.FindChild("TextButton", true, false) as Button;
                if (textBtn != null)
                {
                    if (chatName == activeChatName)
                    {
                        // Accent active blue for selected text only
                        textBtn.AddThemeColorOverride("font_color", new Color(0.274f, 0.623f, 0.924f));
                        textBtn.AddThemeColorOverride("font_hover_color", new Color(0.274f, 0.623f, 0.924f));
                        textBtn.AddThemeColorOverride("font_focus_color", new Color(0.274f, 0.623f, 0.924f));
                    }
                    else
                    {
                        // Let the Theme handle unselected normal & hover colors entirely
                        textBtn.RemoveThemeColorOverride("font_color");
                        textBtn.RemoveThemeColorOverride("font_hover_color");
                        textBtn.RemoveThemeColorOverride("font_focus_color");
                    }
                }
            }
        }

        private void PromptRenameChat(string chatName, string historyPath)
        {
            var dialog = new ConfirmationDialog();
            dialog.Title = "Renombrar Conversación";
            
            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_top", 10);
            margin.AddThemeConstantOverride("margin_bottom", 10);
            margin.AddThemeConstantOverride("margin_left", 10);
            margin.AddThemeConstantOverride("margin_right", 10);

            var edit = new LineEdit();
            edit.Text = chatName;
            edit.PlaceholderText = "Nuevo nombre del chat...";
            edit.CustomMinimumSize = new Vector2(300, 0);
            margin.AddChild(edit);
            
            dialog.AddChild(margin);
            
            dialog.Confirmed += () =>
            {
                string newName = edit.Text.Trim();
                if (!string.IsNullOrEmpty(newName) && newName != chatName)
                {
                    string oldPath = global::System.IO.Path.Combine(ProjectSettings.GlobalizePath(historyPath), $"{chatName}.json");
                    string newPath = global::System.IO.Path.Combine(ProjectSettings.GlobalizePath(historyPath), $"{newName}.json");
                    if (global::System.IO.File.Exists(oldPath))
                    {
                        try
                        {
                            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
                            if (chatManager != null && chatManager.CurrentSession != null && chatManager.CurrentSession.SessionName == chatName)
                            {
                                chatManager.CurrentSession.SessionName = newName;
                            }
                            global::System.IO.File.Move(oldPath, newPath);
                            GD.Print($"[BRAIN] Renamed session {chatName} to {newName}");
                            
                            if (_historyRows.TryGetValue($"{chatName}.json", out var oldRow))
                            {
                                oldRow.QueueFree();
                                _historyRows.Remove($"{chatName}.json");
                            }
                            _displayedHistoryFiles.Remove($"{chatName}.json");
                            
                            LoadHistoryFiles();
                        }
                        catch (global::System.Exception ex)
                        {
                            GD.PrintErr($"Failed to rename chat: {ex.Message}");
                        }
                    }
                }
                dialog.QueueFree();
            };
            
            dialog.Canceled += () => dialog.QueueFree();
            GetTree().Root.AddChild(dialog);
            dialog.PopupCentered();
            edit.GrabFocus();
        }

        private void TogglePinChat(string chatName)
        {
            var configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            if (configManager != null)
            {
                if (configManager.PinnedChats.Contains(chatName))
                {
                    configManager.PinnedChats.Remove(chatName);
                }
                else
                {
                    configManager.PinnedChats.Add(chatName);
                }
                configManager.SaveConfiguration();
                LoadHistoryFiles();
            }
        }

        private void PromptDeleteChat(string chatName, string historyPath)
        {
            var dialog = new ConfirmationDialog();
            dialog.Title = "Eliminar Conversación";
            dialog.DialogText = $"¿Estás seguro de que deseas eliminar permanentemente la conversación '{chatName}'?";
            
            dialog.Confirmed += () =>
            {
                string filePath = global::System.IO.Path.Combine(ProjectSettings.GlobalizePath(historyPath), $"{chatName}.json");
                if (global::System.IO.File.Exists(filePath))
                {
                    try
                    {
                        global::System.IO.File.Delete(filePath);
                        GD.Print($"[BRAIN] Deleted session {chatName}");
                        
                        var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
                        if (chatManager != null && chatManager.CurrentSession != null && chatManager.CurrentSession.SessionName == chatName)
                        {
                            chatManager.InitializeNewSession("Chat");
                            if (_currentView != null && _currentView.HasMethod("LoadActiveMessagesIntoUI"))
                            {
                                _currentView.Call("LoadActiveMessagesIntoUI");
                            }
                        }
                        
                        if (_historyRows.TryGetValue($"{chatName}.json", out var oldRow))
                        {
                            oldRow.QueueFree();
                            _historyRows.Remove($"{chatName}.json");
                        }
                        _displayedHistoryFiles.Remove($"{chatName}.json");
                        
                        LoadHistoryFiles();
                    }
                    catch (global::System.Exception ex)
                    {
                        GD.PrintErr($"Failed to delete chat: {ex.Message}");
                    }
                }
                dialog.QueueFree();
            };
            
            dialog.Canceled += () => dialog.QueueFree();
            GetTree().Root.AddChild(dialog);
            dialog.PopupCentered();
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

        private void OnFilesTabHovered()
        {
            if (_isFilesOpen) return;
            _filesTabTween?.Kill();
            _filesTabTween = GetTree().CreateTween();
            if (FilesTabBtn != null)
            {
                _filesTabTween.Parallel().TweenProperty(FilesTabBtn, "offset_left", -60.0f, 0.2f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
                _filesTabTween.Parallel().TweenProperty(FilesTabBtn, "offset_right", 0.0f, 0.2f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            }
        }

        private void OnFilesTabUnhovered()
        {
            if (_isFilesOpen) return;
            _filesTabTween?.Kill();
            _filesTabTween = GetTree().CreateTween();
            if (FilesTabBtn != null)
            {
                _filesTabTween.Parallel().TweenProperty(FilesTabBtn, "offset_left", -20.0f, 0.3f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                _filesTabTween.Parallel().TweenProperty(FilesTabBtn, "offset_right", 40.0f, 0.3f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
            }
        }

        private void ToggleFilesPanel()
        {
            _isFilesOpen = !_isFilesOpen;
            _filesPanelTween?.Kill();
            _filesPanelTween = GetTree().CreateTween();

            if (_isFilesOpen)
            {
                _filesTabTween?.Kill();
                if (FilesTabBtn != null)
                {
                    FilesTabBtn.OffsetLeft = -60.0f;
                    FilesTabBtn.OffsetRight = 0.0f;
                }
                _filesPanelTween.Parallel().TweenProperty(FilesPanel, "offset_left", -SettingsWidth, 0.6f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                _filesPanelTween.Parallel().TweenProperty(FilesPanel, "offset_right", 0.0f, 0.6f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
            }
            else
            {
                _filesPanelTween.Parallel().TweenProperty(FilesPanel, "offset_left", 0.0f, 0.5f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                _filesPanelTween.Parallel().TweenProperty(FilesPanel, "offset_right", SettingsWidth, 0.5f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                _filesTabTween?.Kill();
                _filesTabTween = GetTree().CreateTween();
                if (FilesTabBtn != null)
                {
                    _filesTabTween.Parallel().TweenProperty(FilesTabBtn, "offset_left", -20.0f, 0.5f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                    _filesTabTween.Parallel().TweenProperty(FilesTabBtn, "offset_right", 40.0f, 0.5f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                }
            }
        }

        public void OpenFilesPanelIfNotOpen()
        {
            if (!_isFilesOpen)
            {
                ToggleFilesPanel();
            }
        }

        public void SetThemeMode(bool isDark)
        {
            _isCurrentlyDark = isDark;
            if (_currentView != null && _currentView.HasMethod("UpdateTheme"))
            {
                _currentView.Call("UpdateTheme", isDark);
            }
            
            var filesRoot = GetNodeOrNull("FilesOverlay/FilesPanel/FilesMargin/FilesRoot");
            if (filesRoot != null && filesRoot.HasMethod("UpdateTheme"))
            {
                filesRoot.Call("UpdateTheme", isDark);
            }
        }
    }
}