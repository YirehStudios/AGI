using Godot;

[Tool]
[GlobalClass]
public partial class HeaderFormat : Resource
{
    private float _fontSize = 2.285f;
    [Export] public float FontSize 
    { 
        get => _fontSize; 
        set { _fontSize = value; EmitChanged(); } 
    }

    private bool _isBold = false;
    [Export] public bool IsBold 
    { 
        get => _isBold; 
        set { _isBold = value; EmitChanged(); } 
    }

    private bool _isItalic = false;
    [Export] public bool IsItalic 
    { 
        get => _isItalic; 
        set { _isItalic = value; EmitChanged(); } 
    }

    private bool _isUnderlined = false;
    [Export] public bool IsUnderlined 
    { 
        get => _isUnderlined; 
        set { _isUnderlined = value; EmitChanged(); } 
    }

    private bool _overrideFontColor = false;
    [Export] public bool OverrideFontColor 
    { 
        get => _overrideFontColor; 
        set { _overrideFontColor = value; EmitChanged(); } 
    }

    private Color _fontColor = Colors.White;
    [Export] public Color FontColor 
    { 
        get => _fontColor; 
        set { _fontColor = value; EmitChanged(); } 
    }

    private bool _drawHorizontalRule = false;
    [Export] public bool DrawHorizontalRule 
    { 
        get => _drawHorizontalRule; 
        set { _drawHorizontalRule = value; EmitChanged(); } 
    }

    public HeaderFormat() {}
}
