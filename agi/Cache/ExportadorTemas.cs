using Godot;
using System;

public partial class ExportadorTemas : Node
{
    public override void _Ready()
    {
        // 1. GENERAR EL TEMA DÍA (Estilo Software Claro)
        Theme temaDia = new Theme();
        // Usamos una fuente de sistema limpia (ej. Arial, Helvetica o Roboto)
        SystemFont fuenteApp = new SystemFont();
        fuenteApp.FontNames = new string[] { "Sans-Serif", "Arial" };
        temaDia.DefaultFont = fuenteApp;
        temaDia.DefaultFontSize = 14;

        // Configuramos los contenedores y paneles tipo App de escritorio blanca
        StyleBoxFlat fondoClaro = new StyleBoxFlat();
        fondoClaro.BgColor = new Color("#F9F9FB"); // Blanco grisáceo premium
        fondoClaro.CornerRadiusTopLeft = 6;       // Bordes redondeados sutiles de app
        fondoClaro.CornerRadiusTopRight = 6;
        fondoClaro.CornerRadiusBottomLeft = 6;
        fondoClaro.CornerRadiusBottomRight = 6;
        temaDia.SetStylebox("panel", "PanelContainer", fondoClaro);
        temaDia.SetColor("font_color", "Label", new Color("#1E1E24")); // Texto oscuro

        // Guardamos directamente tu archivo .tres de Día
        ResourceSaver.Save(temaDia, "res://tema_dia.tres");

        // 2. GENERAR EL TEMA NOCHE (Estilo Software Oscuro moderno)
        Theme temaNoche = new Theme();
        temaNoche.DefaultFont = fuenteApp;
        temaNoche.DefaultFontSize = 14;

        StyleBoxFlat fondoOscuro = new StyleBoxFlat();
        fondoOscuro.BgColor = new Color("#16161A"); // Gris oscuro/antracita elegante
        fondoOscuro.CornerRadiusTopLeft = 6;
        fondoOscuro.CornerRadiusTopRight = 6;
        fondoOscuro.CornerRadiusBottomLeft = 6;
        fondoOscuro.CornerRadiusBottomRight = 6;
        temaNoche.SetStylebox("panel", "PanelContainer", fondoOscuro);
        temaNoche.SetColor("font_color", "Label", new Color("#FFFFFE")); // Texto blanco

        // Guardamos tu archivo .tres de Noche
        ResourceSaver.Save(temaNoche, "res://tema_noche.tres");

        GD.Print("¡Archivos tema_dia.tres y tema_noche.tres creados con éxito!");
        GetTree().Quit(); // Cierra la app al terminar
    }
}
