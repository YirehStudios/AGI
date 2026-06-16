using Godot;
using System;
using System.IO;

namespace Logic.UI
{
    public partial class Files : PanelContainer
    {
        [Export] public Button AddFileBtn;
        [Export] public VBoxContainer FilesListContainer;
        [Export] public PanelContainer FileChipTemplate;
        [Export] public ConfirmationDialog OverwriteDialog;
        [Export] public FileDialog FileDialog;

        private string _pendingFilePath = "";
        
        private int _currentSortOption = 0; // 0: Date, 1: Name, 2: Size
        private bool _sortAscending = false;

        public override void _Ready()
        {
            if (AddFileBtn != null)
                AddFileBtn.Pressed += OnAddFilePressed;

            if (FileDialog != null)
            {
                FileDialog.FileSelected += OnFileSelectedFromDialog;
                FileDialog.Filters = new string[] {
                    "*.txt, *.md, *.json, *.xml, *.cs, *.py, *.js, *.html, *.css, *.gd, *.cpp, *.h; Archivos de Texto y Código",
                    "*.pdf; Documentos PDF",
                    "*.xlsx, *.xls, *.csv; Hojas de Cálculo",
                    "*.doc, *.docx, *.odt; Documentos de Texto"
                };
            }



            var sortOptionBtn = GetNodeOrNull<OptionButton>("MainLayout/SortBar/SortOptionBtn");
            if (sortOptionBtn != null)
            {
                sortOptionBtn.Select(0); // Select first option by default
                sortOptionBtn.ItemSelected += (index) => {
                    _currentSortOption = (int)index;
                    LoadWorkspace();
                };
            }
            
            var sortOrderBtn = GetNodeOrNull<Button>("MainLayout/SortBar/SortOrderBtn");
            if (sortOrderBtn != null)
            {
                sortOrderBtn.Text = "↓"; // Default to descending
                sortOrderBtn.Pressed += () => {
                    _sortAscending = !_sortAscending;
                    sortOrderBtn.Text = _sortAscending ? "↑" : "↓";
                    LoadWorkspace();
                };
            }
                
            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            if (chatManager != null)
            {
                chatManager.OnSessionListUpdated += LoadWorkspace;
            }
            
            ApplyStartupTheme();
            CallDeferred(MethodName.LoadWorkspace);
        }

        private void ApplyStartupTheme()
        {
            var configManager = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            if (configManager != null)
            {
                UpdateTheme(configManager.DarkMode);
            }
        }

        public void LoadWorkspace()
        {
            if (FilesListContainer == null) return;
            
            // Clear existing chips
            foreach (Node child in FilesListContainer.GetChildren())
            {
                if (child != FileChipTemplate && child is PanelContainer)
                {
                    child.QueueFree();
                }
            }

            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            string chatId = chatManager?.CurrentSession?.SessionName ?? "default_chat";
            
            string historyDir = Path.Combine(
                global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData),
                "agi", "history", chatId
            );

            if (Directory.Exists(historyDir))
            {
                string[] files = Directory.GetFiles(historyDir);
                var sortedFiles = new global::System.Collections.Generic.List<string>(files);
                
                // Remove id.txt and internal extracted files so the user only sees the original
                sortedFiles.RemoveAll(f => {
                    string name = Path.GetFileName(f);
                    return name == "id.txt" || name.EndsWith(".extracted.txt") || name.EndsWith("_meta.json");
                });

                sortedFiles.Sort((a, b) => {
                    int result = 0;
                    var infoA = new FileInfo(a);
                    var infoB = new FileInfo(b);
                    
                    if (_currentSortOption == 0) // Date
                        result = infoA.LastWriteTime.CompareTo(infoB.LastWriteTime);
                    else if (_currentSortOption == 1) // Name
                        result = string.Compare(infoA.Name, infoB.Name, StringComparison.OrdinalIgnoreCase);
                    else if (_currentSortOption == 2) // Size
                        result = infoA.Length.CompareTo(infoB.Length);

                    return _sortAscending ? result : -result;
                });

                foreach (var file in sortedFiles)
                {
                    AddFileChipUI(file);
                }
                
                var dropZone = GetNodeOrNull<Control>("MainLayout/DropZonePanel");
                if (dropZone != null)
                {
                    dropZone.Visible = (sortedFiles.Count == 0);
                }
            }
        }

        private void OnAddFilePressed()
        {
            if (FileDialog != null)
                FileDialog.PopupCentered();
        }

        private async void OnFileSelectedFromDialog(string path)
        {
            bool success = await ProcessNewFile(path);
            if (success)
            {
                var mainApp = GetNodeOrNull<Node>("/root/MainApp");
                if (mainApp == null) mainApp = GetParent().GetParent().GetParent().GetParent(); // Fallback
                var chatbot = mainApp?.GetNodeOrNull("Chatbot/MainLayout/ChatbotMain");
                if (chatbot == null) chatbot = GetTree().Root.GetNodeOrNull("MainApp/Chatbot/MainLayout/ChatbotMain");

                var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
                string chatId = chatManager?.CurrentSession?.SessionName ?? "default_chat";
                string targetPath = Path.Combine(
                    global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData),
                    "agi", "history", chatId, Path.GetFileName(path)
                );
                
                chatbot?.Call("AttachFileToMessage", targetPath);
            }
        }

                public async global::System.Threading.Tasks.Task<bool> ProcessNewFile(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return false;

            string fileName = Path.GetFileName(sourcePath);
            var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
            string chatId = chatManager?.CurrentSession?.SessionName ?? "default_chat";
            
            string historyDir = Path.Combine(
                global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.LocalApplicationData),
                "agi", "history", chatId
            );

            if (!Directory.Exists(historyDir))
            {
                Directory.CreateDirectory(historyDir);
            }

            // Create id.txt as requested
            string idTxtPath = Path.Combine(historyDir, "id.txt");
            if (!File.Exists(idTxtPath))
            {
                File.WriteAllText(idTxtPath, chatId);
            }

            string targetPath = Path.Combine(historyDir, fileName);

            if (File.Exists(targetPath) && targetPath != sourcePath)
            {
                _pendingFilePath = sourcePath;
                if (OverwriteDialog != null)
                {
                    var tcs = new global::System.Threading.Tasks.TaskCompletionSource<bool>();
                    
                    Action onConfirm = null;
                    Action onCancel = null;
                    
                    onConfirm = () => {
                        OverwriteDialog.Confirmed -= onConfirm;
                        OverwriteDialog.Canceled -= onCancel;
                        tcs.SetResult(true);
                    };
                    onCancel = () => {
                        OverwriteDialog.Confirmed -= onConfirm;
                        OverwriteDialog.Canceled -= onCancel;
                        tcs.SetResult(false);
                    };
                    
                    OverwriteDialog.Confirmed += onConfirm;
                    OverwriteDialog.Canceled += onCancel;
                    
                    OverwriteDialog.PopupCentered();
                    
                    bool confirmed = await tcs.Task;
                    if (confirmed)
                    {
                        CopyFileToWorkspace(sourcePath, targetPath);
                        _ = RunExtractor(targetPath);
                        return true;
                    }
                    return false;
                }
                return false;
            }
            else
            {
                CopyFileToWorkspace(sourcePath, targetPath);
                _ = RunExtractor(targetPath);
                return true;
            }
        }

        public async global::System.Threading.Tasks.Task RunExtractor(string targetPath)
        {
            string ext = Path.GetExtension(targetPath).ToLower();
            string[] supportedExts = { ".pdf", ".xlsx", ".xls", ".csv", ".mp4", ".avi", ".mkv", ".mov", ".mp3", ".wav", ".m4a" };
            
            if (global::System.Array.IndexOf(supportedExts, ext) >= 0)
            {
                var envManager = GetNodeOrNull<global::EnvironmentManager>("/root/EnvironmentManager");
                if (envManager?.Bridge != null)
                {
                    string scriptPath = Path.Combine(envManager.BinPath, "file_extractor.py");
                    
                    if (!File.Exists(scriptPath))
                    {
                        // Fallback a la ubicación nativa del proyecto
                        string resPath = ProjectSettings.GlobalizePath("res://Script/Cs/System/Drivers/file_extractor.py");
                        if (File.Exists(resPath)) {
                            scriptPath = resPath;
                        }
                    }
                    
                    // Definir extensión de salida dependiendo si es multimedia o documento
                    string outPath = targetPath + ".extracted.txt";
                    if (ext == ".mp4" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".mp3" || ext == ".wav" || ext == ".m4a")
                    {
                        outPath = targetPath + "_meta.json";
                    }

                    string args = $"\"{targetPath}\" \"{outPath}\"";
                    var startInfo = envManager.Bridge.ConfigurePythonMicroservice(scriptPath, args, ProjectSettings.GlobalizePath("res://"));
                    
                    startInfo.CreateNoWindow = true;
                    startInfo.UseShellExecute = false;
                    
                    try
                    {
                        startInfo.RedirectStandardOutput = true;
                        startInfo.RedirectStandardError = true;

                        using (var process = new global::System.Diagnostics.Process { StartInfo = startInfo })
                        {
                            process.OutputDataReceived += (sender, args) => {
                                if (!string.IsNullOrEmpty(args.Data)) GD.Print($"[FileExtractor] {args.Data}");
                            };
                            process.ErrorDataReceived += (sender, args) => {
                                if (!string.IsNullOrEmpty(args.Data)) GD.PrintErr($"[FileExtractor ERROR] {args.Data}");
                            };

                            process.Start();
                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();
                            
                            await global::System.Threading.Tasks.Task.Run(() => process.WaitForExit(30000)); // 30 seg timeout máximo
                        }
                        
                        // Como ahora operamos bajo el telón, no renderizamos un chip nuevo visible para la versión .txt
                        // El sistema en el fondo lo sabrá acceder.
                        // if (File.Exists(outPath)) { CallDeferred(MethodName.AddFileChipUI, outPath); }
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"Failed to run Python file extractor: {ex.Message}");
                    }
                }
            }
        }


        private void CopyFileToWorkspace(string source, string target)
        {
            try
            {
                if (source != target)
                {
                    File.Copy(source, target, true);
                }
                
                // Remove existing UI chip if overwriting
                foreach (Node child in FilesListContainer.GetChildren())
                {
                    if (child is PanelContainer existingChip && existingChip != FileChipTemplate)
                    {
                        if (existingChip.HasMeta("absolute_path") && existingChip.GetMeta("absolute_path").AsString() == target)
                        {
                            existingChip.QueueFree();
                        }
                    }
                }
                
                AddFileChipUI(target);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to copy file to workspace: {ex.Message}");
            }
        }

        private void AddFileChipUI(string absolutePath)
        {
            if (FileChipTemplate == null || FilesListContainer == null) return;

            var chip = FileChipTemplate.Duplicate() as FileChip;
            if (chip == null) return;
            chip.Visible = true;
            chip.SetMeta("absolute_path", absolutePath);
            chip.AbsolutePath = absolutePath;

            var nameLabel = chip.GetNodeOrNull<Label>("ChipHBox/TextVBox/FileNameLabel");
            var nameEdit = chip.GetNodeOrNull<LineEdit>("ChipHBox/TextVBox/FileNameEdit");
            var infoLabel = chip.GetNodeOrNull<Label>("ChipHBox/TextVBox/FileInfoLabel");
            
            var iconRect = chip.GetNodeOrNull<TextureRect>("ChipHBox/FileIcon");
            if (iconRect != null)
            {
                var iconTex = GD.Load<Texture2D>("res://Resources/Images/Icons/Util/files2.svg");
                if (iconTex != null)
                {
                    iconRect.Texture = iconTex;
                    iconRect.Modulate = new Color(0.8f, 0.8f, 0.85f);
                }
            }
            
            if (nameLabel != null)
            {
                nameLabel.Text = Path.GetFileName(absolutePath);
            }
            
            if (infoLabel != null)
            {
                try
                {
                    var fileInfo = new FileInfo(absolutePath);
                    string dateStr = fileInfo.LastWriteTime.ToString("dd/MM/yyyy HH:mm");
                    string sizeStr = FormatSize(fileInfo.Length);
                    infoLabel.Text = $"{dateStr} - {sizeStr}";
                }
                catch { infoLabel.Text = "Unknown"; }
            }

            if (nameEdit != null)
            {
                nameEdit.Text = Path.GetFileName(absolutePath);

                nameEdit.TextSubmitted += (newName) => {
                    nameEdit.Visible = false;
                    if (nameLabel != null) nameLabel.Visible = true;
                    OnRenameFile(chip, absolutePath, newName);
                };
                nameEdit.FocusExited += () => {
                    nameEdit.Visible = false;
                    if (nameLabel != null) nameLabel.Visible = true;
                    OnRenameFile(chip, absolutePath, nameEdit.Text);
                };
            }

            chip.GuiInput += (@event) => {
                if (@event is InputEventMouseButton mouseEvent && mouseEvent.ButtonIndex == MouseButton.Left && mouseEvent.DoubleClick)
                {
                    if (nameEdit != null && nameLabel != null)
                    {
                        nameLabel.Visible = false;
                        nameEdit.Visible = true;
                        nameEdit.GrabFocus();
                    }
                }
            };

            var removeBtn = chip.GetNodeOrNull<TextureButton>("ChipHBox/RemoveFileBtn");
            if (removeBtn != null)
            {
                removeBtn.Modulate = new Color(0.8f, 0.3f, 0.3f, 0.9f);
                removeBtn.Pressed += () => {
                    if (File.Exists(absolutePath))
                    {
                        try { File.Delete(absolutePath); } catch {}
                    }
                    chip.QueueFree();
                };
            }

            FilesListContainer.AddChild(chip);
        }

        private void OnRenameFile(PanelContainer chip, string oldPath, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return;
            string newPath = Path.Combine(Path.GetDirectoryName(oldPath) ?? "", newName);
            if (oldPath == newPath) return;

            try
            {
                if (File.Exists(oldPath))
                {
                    File.Move(oldPath, newPath);
                    chip.SetMeta("absolute_path", newPath);
                    var nameLabel = chip.GetNodeOrNull<Label>("ChipHBox/TextVBox/FileNameLabel");
                    if (nameLabel != null) nameLabel.Text = newName;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to rename file: {ex.Message}");
                var nameEdit = chip.GetNodeOrNull<LineEdit>("ChipHBox/TextVBox/FileNameEdit");
                if (nameEdit != null) nameEdit.Text = Path.GetFileName(oldPath);
                var nameLabel = chip.GetNodeOrNull<Label>("ChipHBox/TextVBox/FileNameLabel");
                if (nameLabel != null) nameLabel.Text = Path.GetFileName(oldPath);
            }
        }
        
        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public void UpdateTheme(bool isDark)
        {
            Material = null;
            
            var title = GetNodeOrNull<Label>("MainLayout/TopBar/Title");
            if (title != null)
            {
                title.AddThemeColorOverride("font_color", isDark ? new Color(1, 1, 1) : new Color(0, 0, 0));
            }

            if (FilesListContainer != null)
            {
                foreach (Node child in FilesListContainer.GetChildren())
                {
                    if (child is PanelContainer chip)
                    {
                        var lbl = chip.GetNodeOrNull<Label>("ChipHBox/FileNameLabel");
                        if (lbl != null)
                        {
                            lbl.AddThemeColorOverride("font_color", isDark ? new Color(1, 1, 1) : new Color(0, 0, 0));
                        }
                    }
                }
            }
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
            {
                var focusOwner = GetViewport().GuiGetFocusOwner();
                if (focusOwner is LineEdit le && le.Name == "FileNameEdit")
                {
                    if (!le.GetGlobalRect().HasPoint(mouseEvent.GlobalPosition))
                    {
                        le.ReleaseFocus();
                    }
                }
            }
        }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            // For dragging a file out to the chat
            // Find which chip we clicked on
            foreach (Node child in FilesListContainer.GetChildren())
            {
                if (child is PanelContainer chip && child != FileChipTemplate && chip.Visible)
                {
                    if (chip.GetGlobalRect().HasPoint(GetGlobalMousePosition()))
                    {
                        var path = chip.GetMeta("absolute_path").AsString();
                        
                        var preview = new Label { Text = Path.GetFileName(path) };
                        SetDragPreview(preview);
                        
                        // We return the file path wrapped in an array or dictionary 
                        // so ChatbotMain can detect it as dropped files.
                        return new Godot.Collections.Dictionary { { "files", new string[] { path } } };
                    }
                }
            }
            return default;
        }
    }
}
