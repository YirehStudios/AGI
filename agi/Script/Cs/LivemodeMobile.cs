using Godot;
using System;

public partial class LivemodeMainMobile : Panel
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
            GD.PrintErr("LivemodeMainMobile: WaveAnimationPlayer is not assigned in the Inspector.");
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
        // Buscamos directamente la aplicación principal de celular
        Logic.UI.MainAppMobile mainApp = GetNodeOrNull<Logic.UI.MainAppMobile>("/root/MainAppMobile");
                                
        if (mainApp != null)
        {
            // Buscamos el botón "Modo Chat" del Header superior
            Button chatHeaderBtn = mainApp.GetNodeOrNull<Button>("MainLayout/RightColumn/HeaderPanel/HeaderMargin/HeaderLayout/ChatBotModeButton");
           
            if (chatHeaderBtn != null)
            {
                chatHeaderBtn.ButtonPressed = true;
                chatHeaderBtn.EmitSignal(Button.SignalName.Pressed);
            }
            else
            {
                mainApp.LoadMode(mainApp.ChatbotScene);
            }
        }
    }
}