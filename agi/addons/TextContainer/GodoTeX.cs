#if TOOLS
using Godot;
using System;

[Tool]
public partial class GodoTeX : EditorPlugin {
	public override void _EnterTree() {
		var texture_grey = GD.Load<Texture2D>("addons/textcontainer/iconGrey.svg");
		var texture_script = GD.Load<Script>("addons/textcontainer/LaTeXture.cs");
		AddCustomType("LaTeXture", "ImageTexture", texture_script, texture_grey);
		
		var texture_red = GD.Load<Texture2D>("addons/textcontainer/iconRed.svg");
		var spatial_script = GD.Load<Script>("addons/textcontainer/LaTeX3D.cs");
		AddCustomType("LaTeX3D", "Sprite3D", spatial_script, texture_red);
		
		var texture = GD.Load<Texture2D>("addons/textcontainer/icon.svg");
		var script = GD.Load<Script>("addons/textcontainer/LaTeX.cs");
		AddCustomType("LaTeX", "Sprite2D", script, texture);
		
		var texture_button = GD.Load<Texture2D>("addons/textcontainer/iconButton.svg");
		var button_script = GD.Load<Script>("addons/textcontainer/LaTeXButton.cs");
		AddCustomType("LaTeXButton", "TextureButton", button_script, texture_button);

		var textContainer_script = GD.Load<Script>("addons/textcontainer/TextContainer.cs");
		AddCustomType("TextContainer", "RichTextLabel", textContainer_script, texture);
	}

	public override void _ExitTree() {
		RemoveCustomType("LaTeX");
		RemoveCustomType("LaTeX3D");
		RemoveCustomType("LaTeXture");
		RemoveCustomType("LaTeXButton");
		RemoveCustomType("TextContainer");
	}
}
#endif

