using Godot;
using System;
using System.Text.RegularExpressions;

[Tool]
[GlobalClass]
public partial class TextContainer : RichTextLabel
{
    private MarkdownProcessor _markdownProcessor;
    private bool _dirty = false;
    private string _frontmatter = "";
    private TextEdit _inputEdit;

    // Signals matching markdownlabel.gd
    [Signal] public delegate void UnhandledLinkClickedEventHandler(Variant meta);
    [Signal] public delegate void TaskCheckboxClickedEventHandler(int id, int line, bool checkedState, string taskString);
    [Signal] public delegate void TextChangedEventHandler();

    private string _markdownText = "";
    [Export(PropertyHint.MultilineText)]
    public string MarkdownText
    {
        get => _markdownText;
        set 
        { 
            _markdownText = value; 
            if (_inputEdit != null && _inputEdit.Text != value)
            {
                _inputEdit.Text = value;
            }
            QueueUpdate(); 
        }
    }

    [Export] public bool AutomaticLinks { get; set; } = true;
    [Export] public bool AssumeHttpsLinks { get; set; } = true;

    private void ConnectHeader(HeaderFormat oldFormat, HeaderFormat newFormat)
    {
        var callable = new Callable(this, MethodName.QueueUpdate);
        if (oldFormat != null && oldFormat.IsConnected(Resource.SignalName.Changed, callable))
        {
            oldFormat.Disconnect(Resource.SignalName.Changed, callable);
        }
        if (newFormat != null && !newFormat.IsConnected(Resource.SignalName.Changed, callable))
        {
            newFormat.Connect(Resource.SignalName.Changed, callable);
        }
    }

    [ExportGroup("Header formats")]
    private HeaderFormat _h1 = new HeaderFormat() { FontSize = 2.285f };
    [Export] public HeaderFormat H1 { get => _h1; set { ConnectHeader(_h1, value); _h1 = value; QueueUpdate(); } }

    private HeaderFormat _h2 = new HeaderFormat() { FontSize = 1.714f };
    [Export] public HeaderFormat H2 { get => _h2; set { ConnectHeader(_h2, value); _h2 = value; QueueUpdate(); } }

    private HeaderFormat _h3 = new HeaderFormat() { FontSize = 1.428f };
    [Export] public HeaderFormat H3 { get => _h3; set { ConnectHeader(_h3, value); _h3 = value; QueueUpdate(); } }

    private HeaderFormat _h4 = new HeaderFormat() { FontSize = 1.142f };
    [Export] public HeaderFormat H4 { get => _h4; set { ConnectHeader(_h4, value); _h4 = value; QueueUpdate(); } }

    private HeaderFormat _h5 = new HeaderFormat() { FontSize = 1.0f };
    [Export] public HeaderFormat H5 { get => _h5; set { ConnectHeader(_h5, value); _h5 = value; QueueUpdate(); } }

    private HeaderFormat _h6 = new HeaderFormat() { FontSize = 0.857f };
    [Export] public HeaderFormat H6 { get => _h6; set { ConnectHeader(_h6, value); _h6 = value; QueueUpdate(); } }

    [ExportGroup("Task lists")]
    private bool _enableCheckboxClicks = true;
    [Export] public bool EnableCheckboxClicks { get => _enableCheckboxClicks; set { _enableCheckboxClicks = value; QueueUpdate(); } }

    private string _uncheckedItemCharacter = "☐";
    [Export] public string UncheckedItemCharacter { get => _uncheckedItemCharacter; set { _uncheckedItemCharacter = value; QueueUpdate(); } }

    private string _checkedItemCharacter = "☑";
    [Export] public string CheckedItemCharacter { get => _checkedItemCharacter; set { _checkedItemCharacter = value; QueueUpdate(); } }

    [ExportGroup("Horizontal rules", "Hr")]
    private int _hrHeight = 2;
    [Export(PropertyHint.Range, "0,99,1,suffix:px")]
    public int HrHeight { get => _hrHeight; set { _hrHeight = value; QueueUpdate(); } }

    private float _hrWidth = 90.0f;
    [Export(PropertyHint.Range, "0,100,1,suffix:%")]
    public float HrWidth { get => _hrWidth; set { _hrWidth = value; QueueUpdate(); } }

    private string _hrAlignment = "center";
    [Export(PropertyHint.Enum, "left,center,right")]
    public string HrAlignment { get => _hrAlignment; set { _hrAlignment = value; QueueUpdate(); } }

    private Color _hrColor = Colors.White;
    [Export] public Color HrColor { get => _hrColor; set { _hrColor = value; QueueUpdate(); } }

    [ExportGroup("Input Mode")]
    [Export] public bool IsInputMode { get; set; } = false;
    
    private string _placeholderText = "";
    private System.Collections.Generic.List<PanelContainer> _chipOverlays = new System.Collections.Generic.List<PanelContainer>();

    [Export(PropertyHint.MultilineText)] public string PlaceholderText 
    { 
        get => _placeholderText; 
        set 
        { 
            _placeholderText = value; 
            if (_inputEdit != null) _inputEdit.PlaceholderText = value;
        } 
    }

    public TextContainer()
    {
        _markdownProcessor = new MarkdownProcessor(this);
        BbcodeEnabled = true;
    }

    public override void _Ready()
    {
        base._Ready();
        if (Engine.IsEditorHint())
        {
            BbcodeEnabled = true;
        }

        if (_h1 != null) ConnectHeader(null, _h1);
        if (_h2 != null) ConnectHeader(null, _h2);
        if (_h3 != null) ConnectHeader(null, _h3);
        if (_h4 != null) ConnectHeader(null, _h4);
        if (_h5 != null) ConnectHeader(null, _h5);
        if (_h6 != null) ConnectHeader(null, _h6);

        if (AutomaticLinks)
        {
            MetaClicked += OnMetaClicked;
        }

        if (IsInputMode)
        {
            _inputEdit = new TextEdit();
            _inputEdit.SetAnchorsPreset(LayoutPreset.FullRect);
            _inputEdit.WrapMode = TextEdit.LineWrappingMode.Boundary;
            _inputEdit.CaretBlink = true;
            
            var emptyStyle = new StyleBoxEmpty();
            emptyStyle.ContentMarginLeft = 15;
            emptyStyle.ContentMarginRight = 15;
            emptyStyle.ContentMarginTop = 12;
            emptyStyle.ContentMarginBottom = 12;
            _inputEdit.AddThemeStyleboxOverride("normal", emptyStyle);
            _inputEdit.AddThemeStyleboxOverride("focus", emptyStyle);
            _inputEdit.PlaceholderText = _placeholderText;

            _inputEdit.SyntaxHighlighter = new FileTagHighlighter();

            _inputEdit.Text = _markdownText;
            _inputEdit.TextChanged += () => 
            {
                _markdownText = _inputEdit.Text;
                EmitSignal(SignalName.TextChanged);
                QueueUpdate();
            };
            
            // Forward GuiInput so ChatbotMain can intercept Enter key, and handle backspace over [file] tags
            _inputEdit.GuiInput += OnInputEditGuiInput;
            _inputEdit.FocusEntered += () => EmitSignal(SignalName.FocusEntered);
            _inputEdit.FocusExited += () => EmitSignal(SignalName.FocusExited);
            _inputEdit.CaretChanged += OnInputEditCaretChanged;

            var canDropCall = Callable.From<Vector2, Variant, bool>((pos, data) => {
                try { return _customCanDrop.Call(pos, data).AsBool(); } catch { }
                return _CanDropData(pos, data);
            });
            var dropCall = Callable.From<Vector2, Variant>((pos, data) => {
                try { _customDrop.Call(pos, data); return; } catch { }
                _DropData(pos, data);
            });
            _inputEdit.SetDragForwarding(new Callable(), canDropCall, dropCall);

            AddChild(_inputEdit);
            MouseFilter = MouseFilterEnum.Ignore;
            FocusMode = FocusModeEnum.None;
        }
        else
        {
            MouseFilter = MouseFilterEnum.Stop;
            SelectionEnabled = true;
            ContextMenuEnabled = true;
        }
    }

    private Callable _customCanDrop;
    private Callable _customDrop;

    public void SetInputDragForwarding(Callable drag, Callable canDrop, Callable drop)
    {
        _customCanDrop = canDrop;
        _customDrop = drop;
        if (_inputEdit != null)
        {
            var canDropCall = Callable.From<Vector2, Variant, bool>((pos, data) => {
                try { return _customCanDrop.Call(pos, data).AsBool(); } catch { }
                return _CanDropData(pos, data);
            });
            var dropCall = Callable.From<Vector2, Variant>((pos, data) => {
                try { _customDrop.Call(pos, data); return; } catch { }
                _DropData(pos, data);
            });
            _inputEdit.SetDragForwarding(drag, canDropCall, dropCall);
        }
    }

    private void OnInputEditCaretChanged()
    {
        if (_inputEdit == null) return;
        
        int line = _inputEdit.GetCaretLine();
        int caret = _inputEdit.GetCaretColumn();
        string lineText = _inputEdit.GetLine(line);
        
        var regex = new System.Text.RegularExpressions.Regex(@"\[file\](.*?)\[\/file\]");
        var matches = regex.Matches(lineText);
        
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            int start = match.Index;
            int end = start + match.Length;
            
            if (caret > start && caret < end)
            {
                if (caret - start > end - caret)
                    _inputEdit.SetCaretColumn(end);
                else
                    _inputEdit.SetCaretColumn(start);
                break;
            }
        }
    }

    private void OnInputEditGuiInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            if (keyEvent.Keycode == Key.Left || keyEvent.Keycode == Key.Right)
            {
                int line = _inputEdit.GetCaretLine();
                int caret = _inputEdit.GetCaretColumn();
                string lineText = _inputEdit.GetLine(line);
                
                var regex = new System.Text.RegularExpressions.Regex(@"\[file\](.*?)\[\/file\]");
                var matches = regex.Matches(lineText);
                
                if (keyEvent.Keycode == Key.Left)
                {
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        if (caret == match.Index + match.Length)
                        {
                            _inputEdit.SetCaretColumn(match.Index);
                            GetViewport().SetInputAsHandled();
                            return;
                        }
                    }
                }
                else if (keyEvent.Keycode == Key.Right)
                {
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        if (caret == match.Index)
                        {
                            _inputEdit.SetCaretColumn(match.Index + match.Length);
                            GetViewport().SetInputAsHandled();
                            return;
                        }
                    }
                }
            }
            
            if (keyEvent.Keycode == Key.Backspace)
            {
                int caret = _inputEdit.GetCaretColumn();
                string text = _inputEdit.Text;
                
                // If cursor is right after `[/file] ` or `[/file]`
                int offset = -1;
                if (caret >= 8 && text.Substring(0, caret).EndsWith("[/file] ")) offset = 1;
                else if (caret >= 7 && text.Substring(0, caret).EndsWith("[/file]")) offset = 0;
                
                if (offset != -1 && (caret >= 7 + offset))
                {
                    int endSearch = caret - offset;
                    if (text.Substring(0, endSearch).EndsWith("[/file]"))
                    {
                        int startIndex = text.LastIndexOf("[file]", endSearch - 7);
                        if (startIndex != -1)
                        {
                            _inputEdit.Text = text.Remove(startIndex, caret - startIndex);
                            _inputEdit.SetCaretColumn(startIndex);
                            _markdownText = _inputEdit.Text;
                            EmitSignal(SignalName.TextChanged);
                            QueueUpdate();
                            GetViewport().SetInputAsHandled();
                            EmitSignal(SignalName.GuiInput, @event); // Still emit for ChatbotMain if needed, though usually backspace isn't handled by it
                            return;
                        }
                    }
                }
            }
            else if (keyEvent.Keycode == Key.Delete)
            {
                int caret = _inputEdit.GetCaretColumn();
                string text = _inputEdit.Text;
                
                // If cursor is right before `[file]` or ` [file]`
                int offset = -1;
                if (text.Length >= caret + 7 && text.Substring(caret).StartsWith(" [file]")) offset = 1;
                else if (text.Length >= caret + 6 && text.Substring(caret).StartsWith("[file]")) offset = 0;

                if (offset != -1 && text.Length >= caret + 6 + offset)
                {
                    int startSearch = caret + offset;
                    if (text.Substring(startSearch).StartsWith("[file]"))
                    {
                        int endIndex = text.IndexOf("[/file]", startSearch + 6);
                        if (endIndex != -1)
                        {
                            endIndex += 7; // length of [/file]
                            // Also remove a trailing space if present
                            if (text.Length > endIndex && text[endIndex] == ' ') endIndex += 1;
                            
                            _inputEdit.Text = text.Remove(caret, endIndex - caret);
                            _inputEdit.SetCaretColumn(caret);
                            _markdownText = _inputEdit.Text;
                            EmitSignal(SignalName.TextChanged);
                            QueueUpdate();
                            GetViewport().SetInputAsHandled();
                            EmitSignal(SignalName.GuiInput, @event);
                            return;
                        }
                    }
                }
            }
        }
        
        EmitSignal(SignalName.GuiInput, @event);
    }

    public override void _Process(double delta)
    {
        if (_dirty)
        {
            UpdateContent();
        }
        
        // Ensure GrabFocus on parent redirects to child
        UpdateChipsOverlay();
        if (IsInputMode && _inputEdit != null && HasFocus() && !_inputEdit.HasFocus())
        {
            _inputEdit.GrabFocus();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationTranslationChanged)
        {
            QueueUpdate();
        }
    }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        string propName = property["name"].AsString();
        
        // Hide the default RichTextLabel "text" property to avoid inspector confusion
        if (propName == "text")
        {
            property["usage"] = (int)PropertyUsageFlags.None;
        }
    }

    public void InsertTextAtCaret(string text)
    {
        if (_inputEdit != null)
        {
            _inputEdit.InsertTextAtCaret(text);
            _markdownText = _inputEdit.Text;
            EmitSignal(SignalName.TextChanged);
            QueueUpdate();
        }
    }

    public int GetCaretColumn()
    {
        if (_inputEdit == null) return 0;
        int line = _inputEdit.GetCaretLine();
        int col = _inputEdit.GetCaretColumn();
        string[] lines = _inputEdit.Text.Split('\n');
        int absoluteIndex = 0;
        for (int i = 0; i < line && i < lines.Length; i++)
        {
            absoluteIndex += lines[i].Length + 1;
        }
        absoluteIndex += col;
        return absoluteIndex;
    }

    public void SetCaretColumn(int absoluteIndex)
    {
        if (_inputEdit == null) return;
        string[] lines = _inputEdit.Text.Split('\n');
        int currentCount = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            int lineLen = lines[i].Length + 1;
            if (currentCount + lineLen > absoluteIndex || i == lines.Length - 1)
            {
                _inputEdit.SetCaretLine(i);
                _inputEdit.SetCaretColumn(Math.Max(0, absoluteIndex - currentCount));
                break;
            }
            currentCount += lineLen;
        }
    }

    private void UpdateChipsOverlay()
    {
        if (!IsInputMode) 
        {
            foreach(var chip in _chipOverlays) chip.Visible = false;
            return;
        }

        string textToParse = _inputEdit != null ? _inputEdit.Text : MarkdownText;
        if (string.IsNullOrEmpty(textToParse)) 
        {
            foreach(var chip in _chipOverlays) chip.Visible = false;
            return;
        }

        var regex = new System.Text.RegularExpressions.Regex(@"\[file\](.*?)\[\/file\]");
        var matches = regex.Matches(textToParse);

        // Ensure we have enough chip overlays
        while (_chipOverlays.Count < matches.Count)
        {
            var chip = CreateChip();
            _chipOverlays.Add(chip);
        }

        // Hide unused ones
        for (int i = matches.Count; i < _chipOverlays.Count; i++)
        {
            _chipOverlays[i].Visible = false;
        }

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            string filepath = match.Groups[1].Value;
            string filename = System.IO.Path.GetFileName(filepath);
            string ext = System.IO.Path.GetExtension(filepath).ToLower();

            var chip = _chipOverlays[i];
            
            // Parent management
            if (chip.GetParent() != this)
            {
                chip.GetParent()?.RemoveChild(chip);
                AddChild(chip);
            }

            chip.Visible = true;
            
            var panelStyle = new StyleBoxFlat();
            bool isDark = true;
            if (Logic.UI.ThemeManager.Instance != null) isDark = Logic.UI.ThemeManager.Instance.EsModoOscuro;
            
            panelStyle.BgColor = isDark ? new Color(0.15f, 0.15f, 0.2f, 0.7f) : new Color(0.9f, 0.9f, 0.95f, 0.7f);
            panelStyle.CornerRadiusTopLeft = 8;
            panelStyle.CornerRadiusTopRight = 8;
            panelStyle.CornerRadiusBottomLeft = 8;
            panelStyle.CornerRadiusBottomRight = 8;
            panelStyle.BorderWidthBottom = 1;
            panelStyle.BorderWidthTop = 1;
            panelStyle.BorderWidthLeft = 1;
            panelStyle.BorderWidthRight = 1;
            panelStyle.BorderColor = new Color(1, 1, 1, 0.2f);
            panelStyle.ContentMarginLeft = 6;
            panelStyle.ContentMarginRight = 8;
            panelStyle.ContentMarginTop = 2;
            panelStyle.ContentMarginBottom = 2;
            chip.AddThemeStyleboxOverride("panel", panelStyle);

            var label = chip.GetNode<Label>("HBox/Label");
            label.Text = filename;
            label.AddThemeColorOverride("font_color", isDark ? Colors.White : Colors.Black);

            var icon = chip.GetNode<TextureRect>("HBox/Icon");
            string iconPath = "res://Resources/Images/Icons/Util/files2.svg";
            Color iconColor = Colors.White;
            
            if (ext == ".cs") { iconPath = "res://Resources/Images/Icons/Util/files2.svg"; iconColor = new Color("#23a31c"); }
            else if (ext == ".py") { iconPath = "res://Resources/Images/Icons/Util/files2.svg"; iconColor = new Color("#3572A5"); }
            else if (ext == ".gd" || ext == ".tscn") { iconPath = "res://Resources/Images/Icons/Util/files2.svg"; iconColor = new Color("#478cbf"); }
            else if (ext == ".json") { iconPath = "res://Resources/Images/Icons/Util/files2.svg"; iconColor = new Color("#e6cc2e"); }
            else if (ext == ".txt" || ext == ".md" || ext == ".csv" || ext == ".log") { iconPath = "res://Resources/Images/Icons/Util/files2.svg"; iconColor = new Color("#a9b2c3"); }
            else if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp") { iconPath = "res://Resources/Images/Icons/Util/files2.svg"; iconColor = new Color("#e34f8c"); }
            else if (ext == ".mp4" || ext == ".webm" || ext == ".mkv") { iconPath = "res://Resources/Images/Icons/Util/files2.svg"; iconColor = new Color("#b44fe3"); }
            
            if (ResourceLoader.Exists(iconPath))
            {
                icon.Texture = ResourceLoader.Load<Texture2D>(iconPath);
                icon.Modulate = iconColor;
            }

            // Position and Size
            if (IsInputMode && _inputEdit != null)
            {
                Rect2 rect = new Rect2();
                // Find line and column of the match.Index
                int line = 0, col = 0;
                string[] lines = textToParse.Split('\n');
                int count = 0;
                for (int l = 0; l < lines.Length; l++)
                {
                    if (count + lines[l].Length >= match.Index)
                    {
                        line = l;
                        col = match.Index - count;
                        break;
                    }
                    count += lines[l].Length + 1;
                }
                
                Rect2 startRect = _inputEdit.GetRectAtLineColumn(line, col);
                
                // End rect
                int endLine = 0, endCol = 0;
                count = 0;
                int endIndex = match.Index + match.Length - 1;
                for (int l = 0; l < lines.Length; l++)
                {
                    if (count + lines[l].Length >= endIndex)
                    {
                        endLine = l;
                        endCol = endIndex - count;
                        break;
                    }
                    count += lines[l].Length + 1;
                }
                Rect2 endRect = _inputEdit.GetRectAtLineColumn(endLine, endCol);
                
                rect.Position = startRect.Position;
                rect.Size = new Vector2(endRect.End.X - startRect.Position.X, Mathf.Max(startRect.Size.Y, 24));
                
                // Allow the chip to take its natural minimum size
                chip.CustomMinimumSize = new Vector2(0, 24);
                
                // Center vertically and horizontally within the space claimed by the invisible BBCode
                float targetY = rect.Position.Y + (rect.Size.Y - chip.GetCombinedMinimumSize().Y) / 2;
                
                // If the invisible text is wider than the chip, center it. 
                // If the chip is wider than the text, it will overflow, but usually text is wider.
                float targetX = rect.Position.X;
                float chipWidth = chip.GetCombinedMinimumSize().X;
                if (rect.Size.X > chipWidth && chipWidth > 0)
                {
                    targetX = rect.Position.X + (rect.Size.X - chipWidth) / 2;
                }
                else
                {
                    // Add a tiny 4px padding so the cursor doesn't touch the background edge
                    targetX += 4;
                }
                
                chip.Position = new Vector2(targetX, targetY);
            }
        }
    }

    private PanelContainer CreateChip()
    {
        var panel = new PanelContainer();
        
        var hbox = new HBoxContainer();
        hbox.Name = "HBox";
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        panel.AddChild(hbox);
        
        var icon = new TextureRect();
        icon.Name = "Icon";
        icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        icon.CustomMinimumSize = new Vector2(16, 16);
        icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        hbox.AddChild(icon);
        
        var label = new Label();
        label.Name = "Label";
        label.AddThemeFontSizeOverride("font_size", 14);
        hbox.AddChild(label);
        
        // Ensure it doesn't block mouse
        panel.MouseFilter = MouseFilterEnum.Ignore;
        hbox.MouseFilter = MouseFilterEnum.Ignore;
        icon.MouseFilter = MouseFilterEnum.Ignore;
        label.MouseFilter = MouseFilterEnum.Ignore;
        
        return panel;
    }

    public int GetLineWrapCount(int line) => _inputEdit != null ? _inputEdit.GetLineWrapCount(line) : 0;
    public void SetEditable(bool value) { if (_inputEdit != null) _inputEdit.Editable = value; }
    public bool IsEditable() => _inputEdit != null ? _inputEdit.Editable : false;

    public Error DisplayFile(string filePath, bool handleFrontmatter = true)
    {
        Error result = Error.Ok;
        string content = FileAccess.GetFileAsString(filePath);
        if (string.IsNullOrEmpty(content))
        {
            result = FileAccess.GetOpenError();
        }
        if (handleFrontmatter && result == Error.Ok)
        {
            var regex = new Regex(@"^(?:(?:---|\+\+\+)\r?\n([\s\S]*?)\r?\n(?:---|\+\+\+)\r?\n)?(?:\r?\n)?([\s\S]*)$");
            Match match = regex.Match(content);
            if (match.Success)
            {
                _frontmatter = match.Groups[1].Value.Trim();
                MarkdownText = match.Groups[2].Value;
                return Error.Ok;
            }
            else
            {
                result = Error.Bug;
            }
        }
        _frontmatter = "";
        MarkdownText = content;
        return result;
    }

    public string GetFrontmatter() => _frontmatter;

    public void QueueUpdate()
    {
        _dirty = true;
        QueueRedraw();
    }

    private void UpdateContent()
    {
        _dirty = false;
        Clear();
        LatexProcessor.ClearCache();

        if (IsInputMode) return;
        
        string textToConvert = MarkdownText ?? "";
        
        // Handling translation like Godot 4.3+ auto_translate if possible
        if (Get("auto_translate").AsBool())
        {
            textToConvert = TranslationServer.Translate(textToConvert);
        }

        string bbcodeText = _markdownProcessor.Process(textToConvert);
        
        string[] parts = bbcodeText.Split(new string[] { "[mathimg]" }, StringSplitOptions.None);
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                int endIdx = parts[i].IndexOf("[/mathimg]");
                if (endIdx != -1)
                {
                    string uuidStr = parts[i].Substring(0, endIdx);
                    if (int.TryParse(uuidStr, out int uuid) && LatexProcessor.TextureCache.ContainsKey(uuid))
                    {
                        var tex = LatexProcessor.TextureCache[uuid];
                        AddImage(tex, 0, 0, Colors.White, InlineAlignment.Center);
                    }
                    AppendText(parts[i].Substring(endIdx + 12));
                }
                else
                {
                    AppendText("[mathimg]" + parts[i]);
                }
            }
            else
            {
                AppendText(parts[i]);
            }
        }
    }

    private void OnMetaClicked(Variant meta)
    {
        if (meta.VariantType != Variant.Type.String)
        {
            EmitSignal(SignalName.UnhandledLinkClicked, meta);
            return;
        }
        
        string metaStr = meta.AsString();
        if (metaStr.StartsWith("{") && metaStr.Contains("markdownlabel-checkbox"))
        {
            var parsed = Json.ParseString(metaStr).AsGodotDictionary();
            if (parsed.ContainsKey("markdownlabel-checkbox") && parsed["markdownlabel-checkbox"].AsBool())
            {
                int id = parsed["id"].AsInt32();
                bool isChecked = parsed["checked"].AsBool();
                
                if (_markdownProcessor.CheckboxRecord.ContainsKey(id))
                {
                    OnCheckboxClicked(id, isChecked);
                }
            }
            return;
        }

        if (!AutomaticLinks)
        {
            EmitSignal(SignalName.UnhandledLinkClicked, meta);
            return;
        }

        if (metaStr.StartsWith("#")) // Add support for paragraph jumps here if needed
        {
            // Similar to GDScript scroll_to_paragraph logic
            return;
        }

        var urlPattern = new Regex(@"^(ftp|http|https):\/\/[^\s\""]+$");
        var mailPattern = new Regex(@"^mailto:[^\s]+@[^\s]+\.[^\s]+$");

        if (urlPattern.IsMatch(metaStr) || mailPattern.IsMatch(metaStr))
        {
            OS.ShellOpen(metaStr);
            return;
        }

        if (AssumeHttpsLinks)
        {
            OS.ShellOpen("https://" + metaStr);
        }
        else
        {
            EmitSignal(SignalName.UnhandledLinkClicked, meta);
        }
    }

    private void OnCheckboxClicked(int id, bool wasChecked)
    {
        int iline = _markdownProcessor.CheckboxRecord[id];
        string[] lines = _markdownText.Replace("\r", "").Split('\n');
        
        string oldString = wasChecked ? "[x]" : "[ ]";
        string newString = wasChecked ? "[ ]" : "[x]";
        
        int idx = lines[iline].IndexOf(oldString);
        if (idx == -1)
        {
            GD.PushError($"Couldn't find the clicked task list checkbox (id={id}, line={iline})");
            return;
        }

        lines[iline] = lines[iline].Remove(idx, oldString.Length).Insert(idx, newString);
        _markdownText = string.Join("\n", lines);
        QueueUpdate();

        string taskStr = lines[iline].Substring(idx + 4);
        EmitSignal(SignalName.TaskCheckboxClicked, id, iline, !wasChecked, taskStr);
    }

    public HeaderFormat GetHeaderFormat(int level)
    {
        switch (level)
        {
            case 1: return H1;
            case 2: return H2;
            case 3: return H3;
            case 4: return H4;
            case 5: return H5;
            case 6: return H6;
            default: return null;
        }
    }
}
