using Godot;
using System;

namespace Logic.UI
{
    public partial class MainApp : Panel
    {
        [Export] public PackedScene ChatbotScene;
        [Export] public PackedScene LivemodeScene;
        
        // ¡Noticia! Ahora controlamos el envoltorio, no el contenedor directamente
        [Export] public Control SidebarWrapper; 
        [Export] public Control ContentContainer;
        
        private Node _currentView;
        private bool _isSidebarOpen = true;

        public override void _Ready()
        {
            LoadMode(ChatbotScene);
        }

        public void LoadMode(PackedScene sceneToLoad)
        {
            if (sceneToLoad == null) return;

            if (_currentView != null)
            {
                _currentView.QueueFree();
            }

            _currentView = sceneToLoad.Instantiate();
            ContentContainer.AddChild(_currentView);

            if (_currentView is Control controlView)
            {
                controlView.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                controlView.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                controlView.SizeFlagsVertical = SizeFlags.ExpandFill;
            }
        }

        // --- MAGIA DE ANIMACIÓN DEL MENÚ ---
        public void ToggleSidebar()
        {
            _isSidebarOpen = !_isSidebarOpen;
            Tween tween = GetTree().CreateTween();
            
            // Le decimos al Tween que ejecute el cambio de tamaño y de opacidad al MISMO tiempo
            tween.SetParallel(true);
            
            float targetWidth = _isSidebarOpen ? 250.0f : 0.0f;
            float targetAlpha = _isSidebarOpen ? 1.0f : 0.0f; 
            
            // Animamos el tamaño de 0 a 250 con un efecto "Quart" (empieza rápido y frena suave al final)
            tween.TweenProperty(SidebarWrapper, "custom_minimum_size:x", targetWidth, 0.4f)
                 .SetTrans(Tween.TransitionType.Quart)
                 .SetEase(Tween.EaseType.Out);
                 
            // Animamos la opacidad para que el menú se desvanezca al encogerse y no se vea cortado de golpe
            tween.TweenProperty(SidebarWrapper, "modulate:a", targetAlpha, 0.3f)
                 .SetTrans(Tween.TransitionType.Linear)
                 .SetEase(Tween.EaseType.InOut);
        }
    }
}