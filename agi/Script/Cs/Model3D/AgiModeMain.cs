using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Standalone controller for the Kipfel 3D Avatar mode.
/// Exact functional replica of LivemodeMain: same audio pipeline (VAD, STT, TTS),
/// same state machine, same log format. Independent from LivemodeMain — both modes
/// are mutually exclusive and never loaded simultaneously.
///
/// Root node of the Kipfel 3D scene (Node3D).
/// AudioStreamPlayer nodes are created automatically in _Ready() — no Inspector setup required.
/// </summary>
public partial class AgiModeMain : Node3D
{
    [Export] public AudioStreamPlayer AIVoicePlayer;
    [Export] public AudioStreamPlayer MicroRecorderPlayer;
    [Export] public Kipfel3D Personaje;           // Referencia al nodo del personaje para lip sync
    [Export] public string RecordBusName = "Record";
    [Export] public string VoiceBusName  = "Voice"; // Mismo bus que usa LivemodeMain

    // ── State machine (mirrors LivemodeMain.LiveState) ────────────────────────
    public enum KipfelState { Idle, Listening, ProcessingSTT, ThinkingLLM, SpeakingTTS }
    private KipfelState _currentState = KipfelState.Idle;
    private bool _isLlamaThinking = false;

    // ── Audio hardware control — same thresholds as LiveMode ──────────────────
    private AudioEffectRecord _recorder;
    private float _silenceTimer       = 0.0f;
    private const float SilenceThreshold   = 0.04f;
    private const float MaxSilenceDuration = 3f;
    private float _recordingStartTime = 0.0f;
    private float _debugTimer         = 0.0f;
    private float _ttsFinishTimer     = 0.0f;
    private Queue<AudioStreamWav> _audioQueue = new Queue<AudioStreamWav>();

    public override void _Ready()
    {
        GD.Print("[FLAG] KipfelMode: Initializing hardware infrastructure...");

        // ── Start microphone capture ──────────────────────────────────────────
        if (MicroRecorderPlayer != null)
        {
            if (!MicroRecorderPlayer.Playing)
                MicroRecorderPlayer.Play();
            GD.Print("[FLAG] MIC: Hardware listener successfully mapped to Kipfel node.");
        }
        else
        {
            GD.PrintErr("[FLAG] KipfelMode: MicroRecorderPlayer is null — assign it in the Inspector.");
        }

        // ── Bind audio recorder effect ────────────────────────────────────────
        int recordBusIndex = AudioServer.GetBusIndex(RecordBusName);
        if (recordBusIndex != -1)
        {
            _recorder = (AudioEffectRecord)AudioServer.GetBusEffect(recordBusIndex, 0);
        }
        else
        {
            GD.PrintErr($"[FLAG] KipfelMode: AudioBus '{RecordBusName}' not found — check the Project Audio Bus layout.");
        }

        // ── Ensure audio directory exists ─────────────────────────────────────
        string audioDir = ProjectSettings.GlobalizePath("user://audio");
        if (!global::System.IO.Directory.Exists(audioDir))
            global::System.IO.Directory.CreateDirectory(audioDir);

        // ── Subscribe to network events ───────────────────────────────────────
        var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
        if (networkManager != null)
        {
            networkManager.TTSAudioChunkReceived += OnTTSAudioChunkReceived;
            networkManager.STTCompleted          += OnSTTCompleted;
            GD.Print("[FLAG] KipfelMode: NetworkManager delegates bound.");
        }
        else
        {
            GD.PrintErr("[FLAG] KipfelMode: NetworkManager not found at /root/NetworkManager.");
        }

        // ── Subscribe to ChatManager events ───────────────────────────────────
        var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
        if (chatManager != null)
        {
            chatManager.IsLiveModeActive         = true;
            chatManager.OnBotStartedThinking     += OnBotStartedThinking;
            chatManager.OnBotFinishedSpeaking    += OnBotFinishedSpeaking;
            GD.Print("[FLAG] KipfelMode: ChatManager delegates bound. IsLiveModeActive = true.");
        }
        else
        {
            GD.PrintErr("[FLAG] KipfelMode: ChatManager not found at /root/ChatManager.");
        }

        GD.Print("[FLAG] KipfelMode: All subsystems initialized. Waiting for voice input.");
    }

    /// <summary>
    /// Core execution loop — mirrors LivemodeMain._Process exactly.
    /// </summary>
    public override void _Process(double delta)
    {
        _debugTimer += (float)delta;

        // Consume TTS audio queue — gapless sequential playback
        if (AIVoicePlayer != null && !AIVoicePlayer.Playing && _audioQueue.Count > 0)
        {
            AIVoicePlayer.Stream = _audioQueue.Dequeue();
            AIVoicePlayer.Play();
        }

        // Sample raw amplitude from the Record bus
        int   testBusIndex = AudioServer.GetBusIndex(RecordBusName);
        float testDb       = testBusIndex != -1 ? AudioServer.GetBusPeakVolumeLeftDb(testBusIndex, 0) : -80f;
        float testLinear   = Mathf.DbToLinear(testDb);

        // Sample AI voice bus for lip sync — leer SOLO cuando AIVoicePlayer está sonando
        // Exact replica of LivemodeMain: TargetVoiceLevel = DbToLinear(bus) * 5.0f
        float voiceLinear = 0.0f;
        if (AIVoicePlayer != null && AIVoicePlayer.Playing)
        {
            int voiceBusIndex = AudioServer.GetBusIndex(VoiceBusName);
            if (voiceBusIndex != -1)
            {
                float voiceDb = AudioServer.GetBusPeakVolumeLeftDb(voiceBusIndex, 0);
                voiceLinear = Mathf.DbToLinear(voiceDb) * 5.0f;
            }
        }

        // Drive mouth blendshape directly on Kipfel3D
        Personaje?.EmpujarNivelVoz(voiceLinear, delta);

        // Periodic diagnostic — identical format to LiveMode [MIC DEBUG]
        if (_debugTimer > 0.5f)
        {
            bool isRecording = _recorder != null && _recorder.IsRecordingActive();
            GD.Print($"[MIC DEBUG] dB: {testDb:F1} | Linear: {testLinear:F4} | State: {_currentState} | Recording: {isRecording} | VoiceLinear: {voiceLinear:F4} | Personaje: {(Personaje != null ? Personaje.Name : "NULL")}");
            _debugTimer = 0.0f;
        }

        // VAD logic — identical to LivemodeMain
        if (_currentState == KipfelState.Idle || _currentState == KipfelState.Listening)
        {
            if (testLinear > SilenceThreshold)
            {
                if (_currentState == KipfelState.Idle) StartRecording();
                _silenceTimer = 0.0f;
            }
            else if (_currentState == KipfelState.Listening)
            {
                _silenceTimer += (float)delta;
                if (_silenceTimer >= MaxSilenceDuration)
                    StopAndSendRecording();
            }
        }

        // TTS grace-period debounce — identical to LivemodeMain
        if (_currentState == KipfelState.SpeakingTTS)
        {
            bool isStillGenerating   = _isLlamaThinking;
            bool isCurrentlySounding = (AIVoicePlayer != null && AIVoicePlayer.Playing) || _audioQueue.Count > 0;

            if (isStillGenerating || isCurrentlySounding)
            {
                _ttsFinishTimer = 0.0f;
            }
            else
            {
                _ttsFinishTimer += (float)delta;
                if (_ttsFinishTimer >= 3.0f)
                {
                    UpdateStatus(KipfelState.Idle);
                    _ttsFinishTimer = 0.0f;
                }
            }
        }
    }

    // ── Recording ─────────────────────────────────────────────────────────────

    private void StartRecording()
    {
        if (_recorder == null)
        {
            GD.PrintErr("[FLAG] KipfelMode VAD: Cannot start — AudioEffectRecord is null. Check bus.");
            return;
        }
        _recordingStartTime = (float)Time.GetTicksMsec() / 1000.0f;
        _silenceTimer = 0.0f;
        _recorder.SetRecordingActive(true);
        UpdateStatus(KipfelState.Listening);
        GD.Print("[FLAG] VAD: Voice frequency detected. Initializing buffer capture.");
    }

    private void StopAndSendRecording()
    {
        _recorder.SetRecordingActive(false);

        float  duration = ((float)Time.GetTicksMsec() / 1000.0f) - _recordingStartTime;
        string path     = ProjectSettings.GlobalizePath("user://audio/kipfel_input.wav");

        // Discard false-positive short audio spikes — same threshold as LiveMode
        if (duration < 1.0f)
        {
            GD.Print("[FLAG] VAD: Audio discarded (Duration threshold unmet).");
            if (global::System.IO.File.Exists(path)) global::System.IO.File.Delete(path);
            UpdateStatus(KipfelState.Idle);
            return;
        }

        AudioStreamWav recording = _recorder.GetRecording();
        if (recording != null && recording.Data.Length > 0)
        {
            Error err = recording.SaveToWav(path);
            if (err == Error.Ok)
            {
                GD.Print("[FLAG] AUDIO: Buffer written to disk successfully. Piping to Network Manager...");
                UpdateStatus(KipfelState.ProcessingSTT);
                GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager")?.RequestSTT(path);
            }
            else
            {
                GD.PrintErr($"[FLAG] AUDIO: Failed to save WAV — {err}");
                UpdateStatus(KipfelState.Idle);
            }
        }
        else
        {
            GD.PrintErr("[FLAG] AUDIO: Recording buffer is empty or null.");
            UpdateStatus(KipfelState.Idle);
        }
    }

    // ── Network callbacks ─────────────────────────────────────────────────────

    private void OnSTTCompleted(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            UpdateStatus(KipfelState.Idle);
            return;
        }
        GD.Print($"[FLAG] STT: Transcription complete → \"{text}\"");
        var activeTools = new global::System.Collections.Generic.List<string> { "Time", "MCP" };
        GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager")?.SendToAI(text, 1, activeTools);
    }

    private void OnTTSAudioChunkReceived(byte[] wavBytes)
    {
        if (wavBytes == null || wavBytes.Length < 44) return;

        UpdateStatus(KipfelState.SpeakingTTS);

        try
        {
            int   sampleRate    = BitConverter.ToInt32(wavBytes, 24);
            short numChannels   = BitConverter.ToInt16(wavBytes, 22);
            short bitsPerSample = BitConverter.ToInt16(wavBytes, 34);

            int dataOffset = 44;
            for (int i = 12; i < wavBytes.Length - 8; i++)
            {
                if (wavBytes[i] == 'd' && wavBytes[i+1] == 'a' &&
                    wavBytes[i+2] == 't' && wavBytes[i+3] == 'a')
                {
                    dataOffset = i + 8;
                    break;
                }
            }

            if (dataOffset >= wavBytes.Length)
            {
                GD.PrintErr("[TTS] WAV 'data' chunk not found. Skipping audio playback.");
                return;
            }

            byte[] pcmData = new byte[wavBytes.Length - dataOffset];
            Array.Copy(wavBytes, dataOffset, pcmData, 0, pcmData.Length);

            var stream = new AudioStreamWav
            {
                Format  = bitsPerSample == 16
                    ? AudioStreamWav.FormatEnum.Format16Bits
                    : AudioStreamWav.FormatEnum.Format8Bits,
                MixRate = sampleRate,
                Stereo  = numChannels > 1,
                Data    = pcmData
            };

            _audioQueue.Enqueue(stream);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TTS ERROR] Failed to process WAV bytes: {ex.Message}");
        }
    }

    // ── ChatManager callbacks ─────────────────────────────────────────────────

    private void OnBotStartedThinking()
    {
        _isLlamaThinking = true;
        UpdateStatus(KipfelState.ThinkingLLM);

        string[] frasesEspera = { "Mmm, dame un segundo...", "Estoy pensando...", "A ver, déjame revisarlo." };
        string fraseElegida = frasesEspera[new Random().Next(frasesEspera.Length)];
        GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager")?.RequestTTSWebSocket(fraseElegida);
    }

    private void OnBotFinishedSpeaking(string fullResponse)
    {
        _isLlamaThinking = false;
    }

    // ── State machine ─────────────────────────────────────────────────────────

    private void UpdateStatus(KipfelState nextStatus)
    {
        if (_currentState == nextStatus) return;
        _currentState = nextStatus;
        GD.Print($"[KIPFEL_STATUS] {nextStatus}");
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public override void _ExitTree()
    {
        GD.Print("[FLAG] SYSTEM: Nullifying hardware delegates and closing open logic channels.");

        if (MicroRecorderPlayer != null) MicroRecorderPlayer.Stop();
        if (_recorder != null) _recorder.SetRecordingActive(false);

        var chatManager = GetNodeOrNull<Logic.Lite.ChatManager>("/root/ChatManager");
        if (chatManager != null)
        {
            chatManager.IsLiveModeActive          = false;
            chatManager.OnBotStartedThinking      -= OnBotStartedThinking;
            chatManager.OnBotFinishedSpeaking     -= OnBotFinishedSpeaking;
        }

        var networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
        if (networkManager != null)
        {
            networkManager.TTSAudioChunkReceived  -= OnTTSAudioChunkReceived;
            networkManager.STTCompleted           -= OnSTTCompleted;
        }
    }
}
