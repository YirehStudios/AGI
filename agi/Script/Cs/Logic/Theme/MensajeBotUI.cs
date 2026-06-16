using Godot;

namespace Logic.UI.Components
{
    public partial class MensajeBotUI : HBoxContainer
    {
        [Export] private TextContainer _messageBody;
        [Export] private HBoxContainer _botActionsContainer;
        [Export] private Label _botActionsLabel;

        private string _textoCompleto = "";
        private Timer _dotsTimer;
        private int _dotCount = 0;
        private string _baseActionText = "Pensando";
        public global::System.Collections.Generic.List<Control> DynamicBlocks = new global::System.Collections.Generic.List<Control>();

        public override void _Ready()
        {
            _dotsTimer = new Timer();
            _dotsTimer.WaitTime = 0.4f;
            _dotsTimer.Timeout += ActualizarPuntos;
            AddChild(_dotsTimer);

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

            bool esOscuro = ThemeManager.Instance.EsModoOscuro;
            Theme temaActivo = ThemeManager.Instance.ObtenerTemaGlobal(esOscuro);

            // El bot no debe tener burbuja visible, solo márgenes
            var emptyStyle = new StyleBoxEmpty();
            emptyStyle.ContentMarginTop = 25.0f;
            emptyStyle.ContentMarginBottom = 25.0f;
            miBurbuja.AddThemeStyleboxOverride("panel", emptyStyle);

            if (_messageBody != null)
            {
                Color colorTexto = esOscuro ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.15f, 0.15f, 0.15f);
                _messageBody.AddThemeColorOverride("default_color", colorTexto);
                var emptyRichStyle = new StyleBoxEmpty();
                _messageBody.AddThemeStyleboxOverride("normal", emptyRichStyle);
                _messageBody.AddThemeStyleboxOverride("focus", emptyRichStyle);
                string text = _messageBody.MarkdownText;
                _messageBody.MarkdownText = text; // Force re-parse
            }

            if (DynamicBlocks != null)
            {
                Color colorTexto = esOscuro ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.15f, 0.15f, 0.15f);
                var emptyRichStyle = new StyleBoxEmpty();
                foreach (var block in DynamicBlocks)
                {
                    if (block is TextContainer tc && block != _messageBody)
                    {
                        tc.AddThemeColorOverride("default_color", colorTexto);
                        tc.AddThemeStyleboxOverride("normal", emptyRichStyle);
                        tc.AddThemeStyleboxOverride("focus", emptyRichStyle);
                        string text = tc.MarkdownText;
                        tc.MarkdownText = text; // Force re-parse
                    }
                }
            }

            Color colorPensando = esOscuro ? new Color("e0e0e0") : new Color("808080");
            if (_botActionsLabel != null) _botActionsLabel.AddThemeColorOverride("font_color", colorPensando);
        }

        /// <summary>
        /// Prepares the message component for the initial processing state, resetting text fields and starting the processing timer.
        /// </summary>
        public void IniciarEstadoPensando(string accion = "Pensando")
        {
            _textoCompleto = "";
            _baseActionText = accion;
            if (_messageBody != null)
            {
                _messageBody.MarkdownText = "";
            }
            if (_botActionsContainer != null) _botActionsContainer.Visible = true;
            _dotsTimer.Start();
        }

        /// <summary>
        /// Updates the execution state text, ensures tracking timers are active, and clears 
        /// residual token string allocations to prevent JSON payload leakage in the user interface.
        /// </summary>
        public void CambiarEstadoAccion(string nuevaAccion)
        {
            _baseActionText = nuevaAccion;
            if (_botActionsContainer != null) _botActionsContainer.Visible = true;
            if (_dotsTimer.IsStopped()) _dotsTimer.Start();

            _textoCompleto = "";
            if (_messageBody != null)
            {
                _messageBody.MarkdownText = "";
            }
        }

        private PackedScene _codeEditScene = ResourceLoader.Load<PackedScene>("res://Scenes/IAScene/CodeEdit.tscn");

        private ulong _lastUpdateTicks = 0;

        public void AgregarToken(string token)
        {
            _textoCompleto += token;
            ulong now = Time.GetTicksMsec();
            if (now - _lastUpdateTicks > 60)
            {
                ActualizarBloques();
                _lastUpdateTicks = now;
            }
        }

        public void FinalizarRespuesta()
        {
            ActualizarBloques();
            _dotsTimer.Stop();
            if (_botActionsContainer != null) _botActionsContainer.Visible = false;
        }

        public void ConfigurarMensaje(string texto)
        {
            _textoCompleto = texto;
            ActualizarBloques();
            FinalizarRespuesta();
        }

        private Control CreateMediaBlock()
        {
            PanelContainer mediaPanel = new PanelContainer();
            var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.3f), CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8 };
            mediaPanel.AddThemeStyleboxOverride("panel", style);
            mediaPanel.CustomMinimumSize = new Vector2(0, 250);
            mediaPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            return mediaPanel;
        }

        private void ActualizarBloques()
        {
            if (_messageBody == null) return;
            var layout = GetNodeOrNull<VBoxContainer>("MessageBubble/MessageLayout");
            if (layout == null) return;

            if (DynamicBlocks.Count == 0)
            {
                DynamicBlocks.Add(_messageBody);
            }

            string tempTexto = _textoCompleto.Replace("[media]", "```media\n").Replace("[/media]", "\n```");
            var parts = tempTexto.Split("```");
            int requiredChildren = parts.Length;

            while (DynamicBlocks.Count > requiredChildren)
            {
                 var block = DynamicBlocks[DynamicBlocks.Count - 1];
                 if (block != _messageBody)
                 {
                     layout.RemoveChild(block);
                     block.QueueFree();
                 }
                 DynamicBlocks.RemoveAt(DynamicBlocks.Count - 1);
            }

            for (int i = 0; i < requiredChildren; i++)
            {
                string part = parts[i];
                bool isText = (i % 2 == 0);
                string lang = "";
                string content = "";
                if (!isText)
                {
                     var lines = part.Split(new[] { '\n' }, 2);
                     lang = lines[0].Trim();
                     content = lines.Length > 1 ? lines[1] : "";
                }

                string requiredType = "text";
                if (!isText) requiredType = lang == "media" ? "media" : "code";

                if (i < DynamicBlocks.Count)
                {
                     Control block = DynamicBlocks[i];
                     bool typeMatches = false;
                     if (requiredType == "text" && block is TextContainer) typeMatches = true;
                     else if (requiredType == "code" && block.HasNode("ContentLayout/CodeMargin/CodeEditorNode")) typeMatches = true;
                     else if (requiredType == "media" && block is PanelContainer) typeMatches = true;

                     if (!typeMatches && block != _messageBody)
                     {
                          layout.RemoveChild(block);
                          block.QueueFree();
                          
                          Control replacementBlock = null;
                          if (requiredType == "text") replacementBlock = (TextContainer)_messageBody.Duplicate();
                          else if (requiredType == "code") replacementBlock = _codeEditScene.Instantiate<Control>();
                          else if (requiredType == "media") replacementBlock = CreateMediaBlock();
                          
                          layout.AddChild(replacementBlock);
                          layout.MoveChild(replacementBlock, i); // Ensure it maintains visual order
                          DynamicBlocks[i] = replacementBlock;
                     }
                }
                
                if (i >= DynamicBlocks.Count)
                {
                     Control newBlock = null;
                     if (requiredType == "text") newBlock = (TextContainer)_messageBody.Duplicate();
                     else if (requiredType == "code") newBlock = _codeEditScene.Instantiate<Control>();
                     else if (requiredType == "media") newBlock = CreateMediaBlock();
                     
                     layout.AddChild(newBlock);
                     DynamicBlocks.Insert(i, newBlock);
                }

                DynamicBlocks[i].Visible = true;

                if (requiredType == "text")
                {
                    var tc = (TextContainer)DynamicBlocks[i];
                    bool esOscuro = ThemeManager.Instance != null && ThemeManager.Instance.EsModoOscuro;
                    Color colorTexto = esOscuro ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.15f, 0.15f, 0.15f);
                    tc.AddThemeColorOverride("default_color", colorTexto);
                    var emptyRichStyle = new StyleBoxEmpty();
                    tc.AddThemeStyleboxOverride("normal", emptyRichStyle);
                    tc.AddThemeStyleboxOverride("focus", emptyRichStyle);
                    if (tc.MarkdownText != part) tc.MarkdownText = part;
                }
                else if (requiredType == "code")
                {
                    var codeBlock = DynamicBlocks[i];
                    var codeEdit = codeBlock.GetNodeOrNull<CodeEdit>("ContentLayout/CodeMargin/CodeEditorNode");
                    var langLabel = codeBlock.GetNodeOrNull<Label>("ContentLayout/HeaderBar/HeaderMargin/HeaderLayout/LanguageIndicator");
                    
                    if (langLabel != null && langLabel.Text != lang) langLabel.Text = string.IsNullOrEmpty(lang) ? "code" : lang;
                    
                    if (codeEdit != null)
                    {
                        if (codeEdit.Text != content)
                        {
                            if (codeEdit.Text.Length > 0 && content.StartsWith(codeEdit.Text))
                            {
                                string diff = content.Substring(codeEdit.Text.Length);
                                codeEdit.SetCaretLine(codeEdit.GetLineCount() - 1);
                                codeEdit.SetCaretColumn(codeEdit.GetLine(codeEdit.GetLineCount() - 1).Length);
                                codeEdit.InsertTextAtCaret(diff);
                            }
                            else
                            {
                                codeEdit.Text = content;
                            }
                        }
                        
                        if (codeEdit.SyntaxHighlighter == null)
                        {
                            var hl = new CodeHighlighter();
                            hl.NumberColor = new Color(0.3f, 1.0f, 0.4f);
                            hl.SymbolColor = new Color(1.0f, 1.0f, 1.0f);
                            hl.FunctionColor = new Color(0.2f, 0.8f, 1.0f);
                            hl.MemberVariableColor = new Color(0.9f, 0.9f, 0.9f);

                            var keywordsPink = new[] { "import", "from", "return", "class", "def", "var", "func", "extends", "public", "private", "protected", "override" };
                            var keywordsPurple = new[] { "if", "else", "elif", "for", "while", "in", "break", "continue", "switch", "case", "try", "catch" };
                            var keywordsBlue = new[] { "int", "float", "bool", "string", "void", "null", "true", "false", "this", "self" };

                            foreach (var k in keywordsPink) hl.AddKeywordColor(k, new Color(1.0f, 0.2f, 0.6f));
                            foreach (var k in keywordsPurple) hl.AddKeywordColor(k, new Color(0.8f, 0.3f, 1.0f));
                            foreach (var k in keywordsBlue) hl.AddKeywordColor(k, new Color(0.1f, 0.6f, 1.0f));
                            codeEdit.SyntaxHighlighter = hl;
                        }
                        if (codeEdit.GetVScrollBar() != null) codeEdit.GetVScrollBar().Modulate = new Color(1, 1, 1, 0);
                    }

                    var copyBtn = codeBlock.GetNodeOrNull<Button>("ContentLayout/HeaderBar/HeaderMargin/HeaderLayout/CopyButton");
                    if (copyBtn != null && !copyBtn.HasMeta("connected"))
                    {
                        copyBtn.SetMeta("connected", true);
                        copyBtn.Pressed += () => { DisplayServer.ClipboardSet(codeEdit?.Text ?? ""); };
                    }
                }
                else if (requiredType == "media")
                {
                    string filename = content.Trim();
                    if (string.IsNullOrEmpty(filename)) continue;
                    
                    string osFolder = OS.GetName().ToLower() == "windows" ? "windows" : "linux";
                    string comfyOut = ProjectSettings.GlobalizePath($"user://bin/{osFolder}/comfyui/output");
                    string fullPath = global::System.IO.Path.Combine(comfyOut, filename);
                    
                    var mediaPanel = (PanelContainer)DynamicBlocks[i];
                    if (!mediaPanel.HasMeta("media_loaded") || mediaPanel.GetMeta("media_loaded").AsString() != fullPath)
                    {
                        foreach (Node child in mediaPanel.GetChildren()) { mediaPanel.RemoveChild(child); child.QueueFree(); }
                        
                        if (global::System.IO.File.Exists(fullPath))
                        {
                            string ext = global::System.IO.Path.GetExtension(fullPath).ToLower();
                            if (ext == ".mp4" || ext == ".webm" || ext == ".mkv" || ext == ".avi")
                            {
                                Button openBtn = new Button { Text = "▶ Reproducir Video: " + filename };
                                openBtn.Pressed += () => OS.ShellOpen("file://" + fullPath);
                                openBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                                openBtn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
                                openBtn.CustomMinimumSize = new Vector2(250, 60);
                                mediaPanel.AddChild(openBtn);
                            }
                            else
                            {
                                Image img = new Image();
                                Error err = img.Load(fullPath);
                                if (err == Error.Ok)
                                {
                                    TextureRect tex = new TextureRect();
                                    tex.Texture = ImageTexture.CreateFromImage(img);
                                    tex.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                                    tex.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                                    mediaPanel.AddChild(tex);
                                }
                                else
                                {
                                    Label errLabel = new Label { Text = "Error loading image: " + filename, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                                    mediaPanel.AddChild(errLabel);
                                }
                            }
                        }
                        else
                        {
                            Label errLabel = new Label { Text = "Generando multimedia...", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                            mediaPanel.AddChild(errLabel);
                        }
                        mediaPanel.SetMeta("media_loaded", fullPath);
                    }
                }
            }
        }

        public string ObtenerTextoCompleto() => _textoCompleto;

        private void ActualizarPuntos()
        {
            _dotCount = (_dotCount + 1) % 4;
            if (_botActionsLabel != null) _botActionsLabel.Text = _baseActionText + new string('.', _dotCount);
        }
    }
}