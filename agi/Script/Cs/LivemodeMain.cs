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

        // Conectar botón de menú hamburguesa (Ruta actualizada)
        Button menuBtn = GetNodeOrNull<Button>("MainContainer/LiveAreaContainer/HeaderPanel/HeaderMargin/HeaderLayout/MenuToggleButton");
        if (menuBtn != null) menuBtn.Pressed += OnMenuTogglePressed;

        // Conectar botón para regresar al Chatbot (Ruta actualizada)
        Button chatBtn = GetNodeOrNull<Button>("MainContainer/LiveAreaContainer/HeaderPanel/HeaderMargin/HeaderLayout/ChatBotModeButton");
        if (chatBtn != null) chatBtn.Pressed += OnChatBotModePressed;
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

    private void OnMenuTogglePressed()
    {
        Logic.UI.MainApp mainApp = GetNodeOrNull<Logic.UI.MainApp>("/root/MainApp");
        if (mainApp != null) mainApp.ToggleSidebar();
    }

    private void OnChatBotModePressed()
    {
        Logic.UI.MainApp mainApp = GetNodeOrNull<Logic.UI.MainApp>("/root/MainApp");
        if (mainApp != null) mainApp.LoadMode(mainApp.ChatbotScene);
    }
}