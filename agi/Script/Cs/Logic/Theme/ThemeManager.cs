using Godot;

namespace Logic.UI
{
    public partial class ThemeManager : Node
    {
        public static ThemeManager Instance { get; private set; }
        public bool EsModoOscuro { get; private set; } // Memoria global

        private Theme _temaClaro;
        private Theme _temaOscuro;

        // Dentro de ThemeManager.cs, actualiza el _Ready:
        public override void _Ready()
        {
            Instance = this;

            // Conectamos el archivo que acabas de hackear (Modo Claro)
            _temaClaro = ResourceLoader.Load<Theme>("res://Resources/UI_Themes/tema_claro.tres");

            // Conectamos el archivo original oscuro de Passivestar (Modo Oscuro)
            _temaOscuro = ResourceLoader.Load<Theme>("res://Resources/UI_Themes/minimal_theme.tres");
        }
        public Theme ObtenerTemaGlobal(bool esOscuro)
        {
            EsModoOscuro = esOscuro; // Guardamos el modo actual
            return esOscuro ? _temaOscuro : _temaClaro;
        }

        // EL EXTRACTOR A PRUEBA DE BALAS: Saca el diseño directo del archivo .theme
        public StyleBox ExtraerEstiloDirecto(string nombreVariacion)
        {
            Theme temaActivo = EsModoOscuro ? _temaOscuro : _temaClaro;
            if (temaActivo != null && temaActivo.HasStylebox("panel", nombreVariacion))
            {
                return temaActivo.GetStylebox("panel", nombreVariacion);
            }
            return new StyleBoxFlat(); // Fallback por si escribes mal el nombre
        }
    }
}