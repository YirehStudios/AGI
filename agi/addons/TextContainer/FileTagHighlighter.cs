using Godot;
using System.Text.RegularExpressions;

public partial class FileTagHighlighter : SyntaxHighlighter
{
    public override Godot.Collections.Dictionary _GetLineSyntaxHighlighting(int line)
    {
        var dict = new Godot.Collections.Dictionary();
        var textEdit = GetTextEdit();
        if (textEdit == null) return dict;
        
        string text = textEdit.GetLine(line);
        var fileRegex = new Regex(@"\[file\](.*?)\[\/file\]");
        
        foreach (Match match in fileRegex.Matches(text))
        {
            // Transparent color for the whole tag
            var transparent = new Godot.Collections.Dictionary();
            transparent["color"] = new Color(0, 0, 0, 0);
            dict[match.Index] = transparent;
            
            // Reset to normal color after the tag
            var normal = new Godot.Collections.Dictionary();
            normal["color"] = textEdit.GetThemeColor("font_color");
            dict[match.Index + match.Length] = normal;
        }
        
        return dict;
    }
}
