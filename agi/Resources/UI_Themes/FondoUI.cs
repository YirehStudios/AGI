using Godot;
using System;

namespace Logic.UI // Agregamos esto para que MainApp lo encuentre fácil
{
    public partial class FondoUI : ColorRect
    {
        public override void _Ready()
        {
            // Usamos nameof para evitar errores de escritura
            CallDeferred(nameof(ActualizarColor));
        }

        public override void _Notification(int what)
        {
            // Nota: En C# de Godot 4, es NotificationThemeChanged (sin el 'int' manual si es posible)
            if (what == NotificationThemeChanged)
            {
                ActualizarColor();
            }
        }

        public void ActualizarColor()
        {
            // Verificamos que el ThemeManager (el cerebro) esté cargado
            if (ThemeManager.Instance != null)
            {
                // #131313 es tu oscuro técnico, #f5f5f7 es el claro premium que definimos
                this.Color = ThemeManager.Instance.EsModoOscuro ? new Color("#131313") : new Color("#f5f5f7");
            }
        }
    }
}