using Godot;
using System;

public partial class LivemodeMain : Panel
{
    [Export] public ColorRect WaveVisualizer;
    [Export] public AnimationPlayer WaveAnimationPlayer;
    [Export] public AudioStreamPlayer AIVoicePlayer;
    [Export] public AudioStreamPlayer MicroRecorderPlayer;
  
    private ShaderMaterial _wavesMaterial;
  
    public float TargetVoiceLevel = 0.0f;
    private float _currentVoiceLevel = 0.0f;

    private AudioEffectRecord _recorder;
    private float _silenceTimer = 0.0f;
    private const float SilenceThreshold = 0.05f;
    private bool _isRecording = false;

    /// <summary>
    /// Inicializa las referencias de la interfaz y configura los buses de audio para la captura del micrófono.
    /// Establece la conexión del botón de cierre y suscribe el delegado para la recepción de audio del motor TTS.
    /// </summary>
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

        int recordBusIndex = AudioServer.GetBusIndex("Record");
        _recorder = (AudioEffectRecord)AudioServer.GetBusEffect(recordBusIndex, 0);

        Button closeBtn = GetNodeOrNull<Button>("MainContainer/LiveAreaContainer/CenterArea/BottomControls/CenterButtonLayout/FloatingActionButton");
        if (closeBtn != null)
        {
            closeBtn.Pressed += OnCloseButtonPressed;
        }

        Logic.Backend.BackendLauncher backend = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
        if (backend != null)
        {
            backend.Connect("TTSCompleted", new Callable(this, MethodName.OnAIResponseReady));
        }

        // Aseguramos que la carpeta exista antes de que cualquier grabación intente guardarse
        string audioDir = ProjectSettings.GlobalizePath("user://audio");
        if (!global::System.IO.Directory.Exists(audioDir))
        {
            global::System.IO.Directory.CreateDirectory(audioDir);
            GD.Print("LiveMode: Carpeta de audio creada.");
        }
    }

    /// <summary>
    /// Receptor del delegado emitido por el BackendLauncher.
    /// Inyecta los datos binarios del archivo de audio directamente en la memoria para reconstruir 
    /// el stream en tiempo real y emitirlo a través del reproductor asignado.
    /// </summary>
    /// <param name="audioPath">Ruta absoluta del archivo WAV generado por el motor de síntesis.</param>
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

    /// <summary>
    /// Procesa de forma continua la amplitud de las señales de audio (micrófono local o respuesta del modelo) 
    /// para actualizar la interfaz gráfica visual. Gestiona simultáneamente la lógica VAD para la captura.
    /// </summary>
    public override void _Process(double delta)
    {
        
        if (AIVoicePlayer != null && AIVoicePlayer.Playing)
        {
            
            int voiceBusIndex = AudioServer.GetBusIndex(AIVoicePlayer.Bus);
            float aiDb = AudioServer.GetBusPeakVolumeLeftDb(voiceBusIndex, 0);
            TargetVoiceLevel = Mathf.DbToLinear(aiDb) * 5.0f;
        }
        else
        {
           int recordBusIndex = AudioServer.GetBusIndex("Record");
            if (recordBusIndex == -1) {
                GD.PrintErr("LiveMode: ¡ERROR! No existe el bus 'Record'");
                return;
            }

            float currentDb = AudioServer.GetBusPeakVolumeLeftDb(recordBusIndex, 0);
            float linearVolume = Mathf.DbToLinear(currentDb);

            // ESTO DEBE APARECER EN TU CONSOLA CADA FRAME
            // Si ves "0.0000", Godot no tiene acceso al hardware del micro.
            GD.Print($"[AUDIO DEBUG] Nivel: {linearVolume:F4} | Grabando: {_isRecording}");
            
            TargetVoiceLevel = linearVolume * 5.0f; // Mueve las ondas según tu voz 

            if (linearVolume > SilenceThreshold) // Si detecta ruido (habla)... 
            {
                if (!_isRecording)
                {
                    StartRecording(); // Empieza a grabar el archivo .wav 
                }
                _silenceTimer = 0.0f; // Resetea el tiempo de silencio 
            }
            else if (_isRecording) // Si hay silencio... 
            {
                _silenceTimer += (float)delta;
                if (_silenceTimer >= 3.0f) // Y pasan 3 segundos... 
                {
                    StopAndSendRecording(); // Guarda el .wav y lo manda a Whisper 
                }
            }
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

    /// <summary>
    /// Activa el bus de grabación de Godot y actualiza el estado interno para registrar la entrada de audio.
    /// </summary>
    private void StartRecording()
    {
        _isRecording = true;
        _recorder.SetRecordingActive(true);
        GD.Print("LiveMode: Grabando voz...");
    }

    /// <summary>
    /// Detiene la grabación actual, extrae el flujo de audio en formato WAV, lo persiste en el sistema de archivos local 
    /// y delega el archivo resultante al motor de transcripción (STT).
    /// </summary>
    private void StopAndSendRecording()
    {
        _isRecording = false;
        _recorder.SetRecordingActive(false);
        AudioStreamWav recording = _recorder.GetRecording();
        
        if (recording != null)
        {
            string path = ProjectSettings.GlobalizePath("user://audio/user_input.wav");
            recording.SaveToWav(path);
            
            Logic.Backend.BackendLauncher backend = GetNodeOrNull<Logic.Backend.BackendLauncher>("/root/BackendLauncher");
            if (backend != null) 
            {
                backend.ProcessSpeechToText(path);
            }
        }
        _silenceTimer = 0.0f;
    }

    /// <summary>
    /// Intercepta la acción de cierre para restaurar el estado de la aplicación principal y transicionar
    /// de vuelta a la escena de chat estándar.
    /// </summary>
    private void OnCloseButtonPressed()
    {
        Logic.UI.MainApp mainApp = GetNodeOrNull<Logic.UI.MainApp>("/root/MainApp");
        if (mainApp != null)
        {
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