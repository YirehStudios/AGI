using Godot;
using System;

public partial class LivemodeMain : Panel
{
    [Export] public ColorRect WaveVisualizer;
    [Export] public AnimationPlayer WaveAnimationPlayer;
    [Export] public AudioStreamPlayer AIVoicePlayer;
    [Export] public AudioStreamPlayer MicroRecorderPlayer;
    [Export] public string RecordBusName = "Record";
    [Export] public string VoiceBusName = "Voice";
  
    private ShaderMaterial _wavesMaterial;
  
    public float TargetVoiceLevel = 0.0f;
    private float _currentVoiceLevel = 0.0f;

    private bool _isRecording = false;
    // MÁQUINA DE ESTADOS ESTRICTA
    public enum LiveState { Idle, Listening, ProcessingSTT, ThinkingLLM, SpeakingTTS }
    private LiveState _currentState = LiveState.Idle;
    
    // Variables de control de audio
    private AudioEffectRecord _recorder;
    private float _silenceTimer = 0.0f;
    private const float SilenceThreshold = 0.02f;
    private const float MaxSilenceDuration = 1.5f; // Reducido de 3.0s a 1.5s para mayor fluidez
    private float _recordingStartTime = 0.0f;

    public override void _Ready()
    {
        GD.Print("[FLAG] LiveMode: Iniciando hardware...");
        
        // Retrieves the designated audio bus index and applies a programmatic mute to block physical output while preserving raw capture capability.
        int recordBusIndex = AudioServer.GetBusIndex(RecordBusName);
        if (recordBusIndex != -1)
        {
            AudioServer.SetBusMute(recordBusIndex, true); 
            _recorder = (AudioEffectRecord)AudioServer.GetBusEffect(recordBusIndex, 0);
        }
        
        // Overrides current microphone stream configuration to force continuous capture enablement independently of editor state.
        if (MicroRecorderPlayer != null)
        {
            MicroRecorderPlayer.Stream = new AudioStreamMicrophone();
            MicroRecorderPlayer.Bus = RecordBusName;
            MicroRecorderPlayer.Autoplay = true;
            MicroRecorderPlayer.Play(); 
            GD.Print("[FLAG] MIC: Bus silenciado para evitar eco, pero capturando datos crudos.");
        }
        
        // Casts and caches the shader material dependency for real-time visual property updates.
        if (WaveVisualizer != null)
        {
            _wavesMaterial = WaveVisualizer.Material as ShaderMaterial;
        }

        if (WaveAnimationPlayer == null)
        {
            GD.PrintErr("LivemodeMain: WaveAnimationPlayer is not assigned in the Inspector.");
        }

        // Establishes event subscription between the backend layer and the local UI for TTS audio playback handling.
        Logic.Backend.BackendLauncher backend = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
        if (backend != null)
        {
            backend.Connect("TTSCompleted", new Callable(this, MethodName.OnAIResponseReady));
        }

        // Validates and constructs the local user directory structure required for volatile audio processing operations.
        string audioDir = ProjectSettings.GlobalizePath("user://audio");
        if (!global::System.IO.Directory.Exists(audioDir))
        {
            global::System.IO.Directory.CreateDirectory(audioDir);
            GD.Print("LiveMode: Carpeta de audio creada.");
        }
    }

    private void OnAIResponseReady(string audioPath)
    {
        if (AIVoicePlayer == null || !global::System.IO.File.Exists(audioPath)) return;

        try
        {
            byte[] audioData = global::System.IO.File.ReadAllBytes(audioPath); 
            AudioStreamWav newStream = new AudioStreamWav 
            {
                Data = audioData,
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                MixRate = 22050 
            };

            AIVoicePlayer.Stream = newStream;
            AIVoicePlayer.Play();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"LivemodeMain: Fallo al cargar el buffer de audio. Excepción: {ex.Message}");
        }
    }

    public override void _Process(double delta)
    {
        // 1. Lógica visual: Siempre actualizamos el nivel de voz de la IA si está hablando
        if (AIVoicePlayer != null && AIVoicePlayer.Playing)
        {
            UpdateStatus(LiveState.SpeakingTTS);
            int voiceBusIndex = AudioServer.GetBusIndex(VoiceBusName);
            float aiDb = AudioServer.GetBusPeakVolumeLeftDb(voiceBusIndex, 0);
            TargetVoiceLevel = Mathf.DbToLinear(aiDb) * 5.0f;
        }
        else
        {
            // Si la IA ya no habla, pero nuestro estado seguía en SpeakingTTS, lo liberamos
            if (_currentState == LiveState.SpeakingTTS)
            {
                UpdateStatus(LiveState.Idle);
            }
        }

        // 2. VAD: Solo escuchamos al usuario si estamos libres
        if (_currentState == LiveState.Idle || _currentState == LiveState.Listening)
        {
            int recordBusIndex = AudioServer.GetBusIndex(RecordBusName);
            float currentDb = AudioServer.GetBusPeakVolumeLeftDb(recordBusIndex, 0);
            float linearVolume = Mathf.DbToLinear(currentDb);

            TargetVoiceLevel = linearVolume * 5.0f;

            if (linearVolume > SilenceThreshold)
            {
                if (_currentState == LiveState.Idle) StartRecording();
                _silenceTimer = 0.0f; // Reinicia el temporizador de silencio
            }
            else if (_currentState == LiveState.Listening)
            {
                _silenceTimer += (float)delta;
                if (_silenceTimer >= MaxSilenceDuration) 
                {
                    StopAndSendRecording();
                }
            }
        }

        // 3. Renderizado Visual (Igual que antes)
        if (_wavesMaterial != null)
        {
            _currentVoiceLevel = Mathf.Lerp(_currentVoiceLevel, TargetVoiceLevel, (float)delta * 12.0f);
            _wavesMaterial.SetShaderParameter("voice_level", _currentVoiceLevel);
        }

        if (WaveAnimationPlayer != null)
        {
            if (TargetVoiceLevel > 0.1f && WaveAnimationPlayer.CurrentAnimation != "speak")
                WaveAnimationPlayer.Play("speak");
            else if (TargetVoiceLevel <= 0.1f && WaveAnimationPlayer.CurrentAnimation != "idle")
                WaveAnimationPlayer.Play("idle");
        }
    }

    private void StartRecording()
    {
        _recordingStartTime = (float)Time.GetTicksMsec() / 1000.0f;
        _silenceTimer = 0.0f; 
        _recorder.SetRecordingActive(true);
        UpdateStatus(LiveState.Listening);
        GD.Print("[FLAG] VAD: Voz detectada. Iniciando captura.");
    }

    private void StopAndSendRecording()
    {
        _recorder.SetRecordingActive(false);
        
        float duration = ((float)Time.GetTicksMsec() / 1000.0f) - _recordingStartTime;
        string fileName = "user_input.wav"; 
        string path = ProjectSettings.GlobalizePath($"user://audio/{fileName}");

        if (duration < 1.0f) // Filtro ajustado a 1 segundo
        {
            GD.Print($"[FLAG] VAD: Audio descartado (muy corto).");
            if (global::System.IO.File.Exists(path)) global::System.IO.File.Delete(path);
            UpdateStatus(LiveState.Idle);
            return;
        }

        AudioStreamWav recording = _recorder.GetRecording();
        if (recording != null && recording.Data.Length > 0)
        {
            Error err = recording.SaveToWav(path);
            if (err == Error.Ok)
            {
                GD.Print($"[FLAG] AUDIO: Archivo guardado. Enviando a STT...");
                UpdateStatus(LiveState.ProcessingSTT); // BLOQUEAMOS EL MICRÓFONO AQUÍ
                GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher")?.ProcessSpeechToText(path);
            }
        }
    }

    private void UpdateStatus(LiveState nextStatus)
    {
        if (_currentState == nextStatus) return;
        _currentState = nextStatus;
        GD.Print($"[ANNIE_STATUS] {nextStatus.ToString()}"); 
    }
    public override void _ExitTree()
    {
        GD.Print("[FLAG] SISTEMA: Liberando hardware de audio.");
        if (MicroRecorderPlayer != null) MicroRecorderPlayer.Stop();
        if (_recorder != null) _recorder.SetRecordingActive(false);
    }
}