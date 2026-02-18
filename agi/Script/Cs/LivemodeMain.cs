using Godot;
using System;

public partial class LivemodeMain : Panel
{
    private ColorRect _wavesRect;
    private ShaderMaterial _wavesMaterial;
    private AnimationPlayer _animator;
    public float TargetVoiceLevel = 0.0f;
    private float _currentVoiceLevel = 0.0f;
    public bool IsSimulating = true;

    public override void _Ready()
    {
        _wavesRect = GetNode<ColorRect>("MainLayout/LiveAreaVBox/CenterArea/Waves");
        _animator = GetNode<AnimationPlayer>("WaveAnimator");
        if (_wavesRect != null)
        {
            _wavesMaterial = _wavesRect.Material as ShaderMaterial;
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

        if (_animator != null)
        {
            if (TargetVoiceLevel > 0.1f && _animator.CurrentAnimation != "speak")
            {
                _animator.Play("speak");
            }
            else if (TargetVoiceLevel <= 0.1f && _animator.CurrentAnimation != "idle")
            {
                _animator.Play("idle");
            }
        }
    }
}