using Godot;
using System;


public partial class LivemodeMain : Panel
{
   [Export] public ColorRect WaveVisualizer;
   [Export] public AnimationPlayer WaveAnimationPlayer;
  
   private ShaderMaterial _wavesMaterial;
  
   public float TargetVoiceLevel = 0.0f;
   private float _currentVoiceLevel = 0.0f;
   public bool IsSimulating = true;


   public override void _Ready()
   {
       if (WaveVisualizer != null)
       {
           _wavesMaterial = WaveVisualizer.Material as ShaderMaterial;
       }


       if (WaveAnimationPlayer == null)
       {
           GD.PrintErr("LivemodeMain: WaveAnimationPlayer is not assigned in the Inspector.");
       }


       // --- CONEXIÓN DEL BOTÓN "X" ---
       Button closeBtn = GetNodeOrNull<Button>("MainContainer/LiveAreaContainer/CenterArea/BottomControls/CenterButtonLayout/FloatingActionButton");
       if (closeBtn != null)
       {
           closeBtn.Pressed += OnCloseButtonPressed;
       }
   }


   public override void _Process(double delta)
   {
       if (IsSimulating)
       {
           TargetVoiceLevel = (Mathf.Sin(Time.GetTicksMsec() / 250.0f) * 0.5f) + 0.5f;
       }


       if (_wavesMaterial != null)
       {
           _currentVoiceLevel = Mathf.Lerp(_currentVoiceLevel, TargetVoiceLevel, (float)delta * 12.0f);
           _wavesMaterial.SetShaderParameter("voice_level", _currentVoiceLevel);
       }


       if (WaveAnimationPlayer != null)
       {
           if (TargetVoiceLevel > 0.1f && WaveAnimationPlayer.CurrentAnimation != "speak")
           {
               WaveAnimationPlayer.Play("speak");
           }
           else if (TargetVoiceLevel <= 0.1f && WaveAnimationPlayer.CurrentAnimation != "idle")
           {
               WaveAnimationPlayer.Play("idle");
           }
       }
   }


   // --- FUNCIÓN DEL BOTÓN "X" ---
   private void OnCloseButtonPressed()
   {
       // 1. Buscamos a la aplicación principal
       Logic.UI.MainApp mainApp = GetNodeOrNull<Logic.UI.MainApp>("/root/MainApp");
       if (mainApp != null)
       {
           // 2. Buscamos el botón "Modo Chat Bot" del Header superior
           Button chatHeaderBtn = mainApp.GetNodeOrNull<Button>("MainLayout/RightColumn/HeaderPanel/HeaderMargin/HeaderLayout/ChatBotModeButton");
          
           if (chatHeaderBtn != null)
           {
               // Lo marcamos visualmente como presionado
               chatHeaderBtn.ButtonPressed = true;
               // ¡Simulamos el clic! Esto dispara el cambio de escena y cambia el texto a "Chat"
               chatHeaderBtn.EmitSignal(Button.SignalName.Pressed);
           }
           else
           {
               // Fallback de emergencia por si no encuentra el botón
               mainApp.LoadMode(mainApp.ChatbotScene);
           }
       }


       // NOTA: Si más adelante añades lógica para apagar el micrófono o parar a Sherpa,
       // puedes agregar esa línea de código justo aquí antes de salir.
   }
}
