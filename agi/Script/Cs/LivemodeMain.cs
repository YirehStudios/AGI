using Godot;
using System;
using System.Collections.Generic;

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

    // Strict internal state machine architecture.
    public enum LiveState { Idle, Listening, ProcessingSTT, ThinkingLLM, SpeakingTTS }
    private LiveState _currentState = LiveState.Idle;
    
    // Concurrency flag locking microphone state during LLM reasoning latency.
    private bool _isLlamaThinking = false;
    
    // Audio hardware control and threshold parameters.
    private AudioEffectRecord _recorder;
    private float _silenceTimer = 0.0f;
    private const float SilenceThreshold = 0.04f;
    private const float MaxSilenceDuration = 3f; 
    private float _recordingStartTime = 0.0f;
    private float _debugTimer = 0.0f;
    private AudioStreamGeneratorPlayback _ttsPlayback;

    public override void _Ready()
    {
        GD.Print("[FLAG] LiveMode: Initializing hardware infrastructure...");
        
        int recordBusIndex = AudioServer.GetBusIndex(RecordBusName);
        if (recordBusIndex != -1)
        {
            _recorder = (AudioEffectRecord)AudioServer.GetBusEffect(recordBusIndex, 0);
        }
        
        if (MicroRecorderPlayer != null)
        {
            if (!MicroRecorderPlayer.Playing) 
            {
                MicroRecorderPlayer.Play(); 
            }
            GD.Print("[FLAG] MIC: Hardware listener successfully mapped to Godot node.");
        }

        // Activates the audio generator loop to keep the bus open to receive pushed vectors securely.
        if (AIVoicePlayer != null && AIVoicePlayer.Stream is AudioStreamGenerator)
        {
            AIVoicePlayer.Play(); 
            _ttsPlayback = (AudioStreamGeneratorPlayback)AIVoicePlayer.GetStreamPlayback();
            GD.Print("[FLAG] TTS: AudioStreamGenerator continuously listening to PCM chunks.");
        }
        
        if (WaveVisualizer != null)
        {
            _wavesMaterial = WaveVisualizer.Material as ShaderMaterial;
        }

        Logic.Network.NetworkManager networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
        if (networkManager != null)
        {
            // Binds real-time binary byte processing event instead of resolving physical path operations.
            networkManager.TTSAudioChunkReceived += OnTTSAudioChunkReceived;
            networkManager.STTCompleted += OnSTTCompleted; 
        }

        var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
        if (chatManager != null)
        {
            chatManager.OnBotStartedThinking += OnBotStartedThinking;
            chatManager.OnBotFinishedSpeaking += OnBotFinishedSpeaking;
        }

        string audioDir = ProjectSettings.GlobalizePath("user://audio");
        if (!global::System.IO.Directory.Exists(audioDir))
        {
            global::System.IO.Directory.CreateDirectory(audioDir);
        }
    }


    public override void _Process(double delta)
    {
        _debugTimer += (float)delta;

        int testBusIndex = AudioServer.GetBusIndex(RecordBusName);
        float testDb = AudioServer.GetBusPeakVolumeLeftDb(testBusIndex, 0);
        float testLinear = Mathf.DbToLinear(testDb);

        if (_debugTimer > 0.5f) 
        {
            GD.Print($"[MIC DEBUG] dB: {testDb:F1} | Linear: {testLinear:F4} | State: {_currentState} | Recording: {(_recorder != null && _recorder.IsRecordingActive())}");
            _debugTimer = 0.0f;
        }

        if (AIVoicePlayer != null && AIVoicePlayer.Playing)
        {
            int voiceBusIndex = AudioServer.GetBusIndex(VoiceBusName);
            float aiDb = AudioServer.GetBusPeakVolumeLeftDb(voiceBusIndex, 0);
            TargetVoiceLevel = Mathf.DbToLinear(aiDb) * 5.0f;
        }

        if (_currentState == LiveState.Idle || _currentState == LiveState.Listening)
        {
            int recordBusIndex = AudioServer.GetBusIndex(RecordBusName);
            float currentDb = AudioServer.GetBusPeakVolumeLeftDb(recordBusIndex, 0);
            float linearVolume = Mathf.DbToLinear(currentDb);

            TargetVoiceLevel = linearVolume * 5.0f;

            if (linearVolume > SilenceThreshold)
            {
                if (_currentState == LiveState.Idle) StartRecording();
                _silenceTimer = 0.0f; 
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

        // Resolves state machine lock utilizing real-time bus amplitude mapping 
        // since the generator playback continuous execution bypasses native Finish flags.
        if (_currentState == LiveState.SpeakingTTS && !_isLlamaThinking && TargetVoiceLevel <= 0.05f)
        {
            UpdateStatus(LiveState.Idle);
        }

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

    /// <summary>
    /// Forwards STT parsed output strings securely towards the internal Brain (ChatManager) singleton logic.
    /// </summary>
    private void OnSTTCompleted(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) 
        {
            UpdateStatus(LiveState.Idle);
            return;
        }
        
        GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager")?.SendToAI(text);
    }

    /// <summary>
    /// Processes incoming 16-bit PCM byte arrays originating directly from the websocket stream.
    /// Casts data into floating-point bounds and propagates execution into Godot's audio bus.
    /// </summary>
    private void OnTTSAudioChunkReceived(byte[] pcmData)
    {
        if (_ttsPlayback == null) return;
        
        UpdateStatus(LiveState.SpeakingTTS);

        int startIndex = 0;
        if (pcmData.Length > 44 && pcmData[0] == 'R' && pcmData[1] == 'I' && pcmData[2] == 'F' && pcmData[3] == 'F')
        {
            startIndex = 44; 
        }

        for (int i = startIndex; i < pcmData.Length - 1; i += 2)
        {
            short sample = global::System.BitConverter.ToInt16(pcmData, i);
            float floatSample = sample / 32768f; 
            
            _ttsPlayback.PushFrame(new Vector2(floatSample, floatSample));
        }
    }

    /// <summary>
    /// Secures microphone and starts system VAD active recording cycle.
    /// </summary>
    private void StartRecording()
    {
        _recordingStartTime = (float)Time.GetTicksMsec() / 1000.0f;
        _silenceTimer = 0.0f; 
        _recorder.SetRecordingActive(true);
        UpdateStatus(LiveState.Listening);
        GD.Print("[FLAG] VAD: Voice frequency detected. Initializing buffer capture.");
    }

    /// <summary>
    /// Processes captured byte streams, filters out micro-sounds, and dispatches legitimate inputs to the Network layer.
    /// </summary>
    private void StopAndSendRecording()
    {
        _recorder.SetRecordingActive(false);
        
        float duration = ((float)Time.GetTicksMsec() / 1000.0f) - _recordingStartTime;
        string fileName = "user_input.wav"; 
        string path = ProjectSettings.GlobalizePath($"user://audio/{fileName}");

        // Aborts dispatch mechanism for false-positive short audio spikes.
        if (duration < 1.0f) 
        {
            GD.Print($"[FLAG] VAD: Audio discarded (Duration threshold unmet).");
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
                GD.Print($"[FLAG] AUDIO: Buffer written to disk successfully. Piping to Network Manager...");
                UpdateStatus(LiveState.ProcessingSTT); // STRICT LOCK TO AVOID MICROPHONE FEEDBACK
                GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager")?.RequestSTT(path);
            }
        }
    }

    /// <summary>
    /// Modifies the active state machine status safely and invokes a standard telemetry footprint update.
    /// </summary>
    private void UpdateStatus(LiveState nextStatus)
    {
        if (_currentState == nextStatus) return;
        _currentState = nextStatus;
        GD.Print($"[ANNIE_STATUS] {nextStatus.ToString()}"); 
    }

    /// <summary>
    /// Triggers visual status blocks, instantiates the concurrency flag, and routes filler audio logic to websocket runtime.
    /// </summary>
    private void OnBotStartedThinking()
    {
        _isLlamaThinking = true; 
        UpdateStatus(LiveState.ThinkingLLM);
        
        string[] frasesEspera = { "Mmm, dame un segundo...", "Estoy pensando...", "A ver, déjame revisarlo." };
        string fraseElegida = frasesEspera[new Random().Next(frasesEspera.Length)];
        
        GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager")?.RequestTTSWebSocket(fraseElegida);
    }

    /// <summary>
    /// Safely unlocks the concurrency logic allowing the state machine to finalize once the queue is emptied.
    /// Actual chunk dispatch is orchestrated by the ChatManager middleware.
    /// </summary>
    private void OnBotFinishedSpeaking(string fullResponse)
    {
        _isLlamaThinking = false; 
    }

    public override void _ExitTree()
    {
        GD.Print("[FLAG] SYSTEM: Nullifying hardware delegates and closing open logic channels.");
        if (MicroRecorderPlayer != null) MicroRecorderPlayer.Stop();
        if (_recorder != null) _recorder.SetRecordingActive(false);

        var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
        if (chatManager != null)
        {
            chatManager.OnBotStartedThinking -= OnBotStartedThinking;
            chatManager.OnBotFinishedSpeaking -= OnBotFinishedSpeaking;
        }

        var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
        if (networkManager != null)
        {
            networkManager.TTSAudioChunkReceived -= OnTTSAudioChunkReceived;
            networkManager.STTCompleted -= OnSTTCompleted; 
        }
    }
}