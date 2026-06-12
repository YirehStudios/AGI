using Godot;

namespace Logic.UI
{
    public partial class FileChip : PanelContainer
    {
        public string AbsolutePath { get; set; }

        public override Variant _GetDragData(Vector2 atPosition)
        {
            if (string.IsNullOrEmpty(AbsolutePath)) return default;
            
            var preview = new Label { 
                Text = global::System.IO.Path.GetFileName(AbsolutePath)
            };
            preview.AddThemeColorOverride("font_color", new Color(1, 1, 1));
            
            var previewPanel = new PanelContainer();
            var style = new StyleBoxFlat {
                BgColor = new Color(0.2f, 0.2f, 0.25f, 0.8f),
                CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
                CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
                ContentMarginLeft = 10, ContentMarginRight = 10,
                ContentMarginTop = 4, ContentMarginBottom = 4
            };
            previewPanel.AddThemeStyleboxOverride("panel", style);
            previewPanel.AddChild(preview);
            
            SetDragPreview(previewPanel);
            
            var dict = new Godot.Collections.Dictionary();
            var arr = new Godot.Collections.Array<string> { AbsolutePath };
            dict["files"] = Variant.CreateFrom(arr);
            return Variant.CreateFrom(dict);
        }
    }
}
