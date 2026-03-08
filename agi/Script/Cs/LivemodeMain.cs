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

    private bool _isSidebarOpen = true;
    private Tween _sidebarTween;

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

    public void _OnMenuToggleButtonPressed()
    {
        var sidebar = GetNode<Control>("MainContainer/SidebarContainer");
        if (sidebar != null)
        {
            if (_sidebarTween != null && _sidebarTween.IsValid())
            {
                _sidebarTween.Kill();
            }

            _sidebarTween = CreateTween();
            _isSidebarOpen = !_isSidebarOpen;

            if (_isSidebarOpen)
            {
                sidebar.Visible = true;
                _sidebarTween.SetParallel(true);
                _sidebarTween.SetTrans(Tween.TransitionType.Cubic);
                _sidebarTween.SetEase(Tween.EaseType.Out);
                _sidebarTween.TweenProperty(sidebar, "custom_minimum_size:x", 250.0f, 0.4f);
                _sidebarTween.TweenProperty(sidebar, "modulate", new Color(1, 1, 1, 1), 0.4f);
            }
            else
            {
                _sidebarTween.SetParallel(true);
                _sidebarTween.SetTrans(Tween.TransitionType.Cubic);
                _sidebarTween.SetEase(Tween.EaseType.Out);
                _sidebarTween.TweenProperty(sidebar, "custom_minimum_size:x", 0.0f, 0.4f);
                _sidebarTween.TweenProperty(sidebar, "modulate", new Color(1, 1, 1, 0), 0.3f);
                _sidebarTween.SetParallel(false);
                _sidebarTween.TweenCallback(Callable.From(() => sidebar.Visible = false));
            }
        }
    }
}