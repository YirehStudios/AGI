using Godot;
using System;

public partial class LivemodeMain : Panel
{
    // Core visual components via Export (Must match Node names in Editor exactly)
    [Export] public ColorRect WaveVisualizer;
    [Export] public AnimationPlayer WaveAnimationPlayer;
    
    // Internal reference for the shader
    private ShaderMaterial _wavesMaterial;
    
    // Simulation variables
    public float TargetVoiceLevel = 0.0f;
    private float _currentVoiceLevel = 0.0f;
    public bool IsSimulating = true;

    /// <summary>
    /// Initializes references to the visualizer components.
    /// Checks if Exported nodes are valid.
    /// </summary>
    public override void _Ready()
    {
        if (WaveVisualizer != null)
        {
            _wavesMaterial = WaveVisualizer.Material as ShaderMaterial;
        }
        else
        {
            GD.PrintErr("LivemodeMain: WaveVisualizer is not assigned in the Inspector.");
        }

        if (WaveAnimationPlayer == null)
        {
            GD.PrintErr("LivemodeMain: WaveAnimationPlayer is not assigned in the Inspector.");
        }
    }

    /// <summary>
    /// Per-frame processing to handle audio simulation and shader parameter updates.
    /// </summary>
    /// <param name="delta">Time elapsed since the last frame.</param>
    public override void _Process(double delta)
    {
        // Simulate audio input levels using a sine wave if simulation mode is active
        if (IsSimulating)
        {
            TargetVoiceLevel = (Mathf.Sin(Time.GetTicksMsec() / 250.0f) * 0.5f) + 0.5f;
        }

        // Smoothly interpolate the current voice level towards the target and update the shader
        if (_wavesMaterial != null)
        {
            _currentVoiceLevel = Mathf.Lerp(_currentVoiceLevel, TargetVoiceLevel, (float)delta * 12.0f);
            _wavesMaterial.SetShaderParameter("voice_level", _currentVoiceLevel);
        }

        // Handle animation state transitions based on audio levels
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
}