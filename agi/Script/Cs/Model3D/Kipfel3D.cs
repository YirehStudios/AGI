using Godot;
using System;

public partial class Kipfel3D : CharacterBody3D
{
    [ExportGroup("Configuración de Movimiento")]
    [Export] public float Speed = 10.0f;
    [Export] public float JumpVelocity = 4.5f;
    [Export] public float Gravity = 9.8f;
    [Export] public float VerticalSpeed = 5.0f;

    [ExportGroup("Nodos de Cámara")]
    [Export] public Node3D CamaraNodo;

    [ExportGroup("Cabeza y Mirada")]
    [Export] public Node3D TargetMirada;
    [Export] public float RuidoFrecuencia = 0.5f;
    [Export] public float RuidoAmplitud = 0.05f;

    [ExportGroup("Pelo (SpringBone Nativo)")]
    [Export] public SkeletonModifier3D SpringBoneNodo;
    [Export] public Vector3 FuerzaManualPelo = new Vector3(0, 0, 0);
    [Export] public bool UsarVientoAleatorio = true;
    [Export] public float VientoIntensidad = 0.2f;
    [Export] public float VientoCoherencia = 0.3f;

    [ExportGroup("Animación de Boca (Lip Sync)")]
    [Export] public MeshInstance3D MallaRostro;
    [Export] public string NombreBlendShapeBoca = "mouth_o2";
    [Export] public string VoiceBusName = "AIVoice";
    [Export] public float MultiplicadorBoca = 2.5f;
    [Export] public float SuavizadoBoca = 15.0f;

    [ExportGroup("Animaciones VTuber (Idle)")]
    [Export] public Node3D TargetCintura;                    // Mapear a Target_Hip en el esqueleto
    [Export] public float IntensidadBalanceo   = 0.02f;      // Amplitud del balanceo flotante (MOVIMIENTO SUTIL en X/Z)
    [Export] public float VelocidadBalanceo    = 0.25f;      // Frecuencia del balanceo
    [Export] public float IntensidadRespiracion = 0.015f;    // Amplitud del ciclo de respiración en Y
    [Export] public float VelocidadRespiracion = 0.2f;       // Frecuencia respiratoria
    [Export] public float IntensidadCabeceo    = 0.10f;      // Desplazamiento del TargetMirada en Y al hablar
    [Export] public float VelocidadCabeceo     = 3.0f;       // Frecuencia del cabeceo al hablar
    [Export] public float IntensidadPaneoCabeza = 0.15f;     // Desplazamiento del TargetMirada en X (mirar sutilmente a los lados)

    // ── Blendshapes de expresión aleatoria ─────────────────────────────────────
    [ExportGroup("Micro-Expresiones (opcional)")]
    [Export] public string[] NombresExpresionesAleatorias = new string[]
    {
        "eye_joy", "eye_nagomi", "eye_happy", "mouth_smile", "mouth_smile2", "mouth_∧", "eyebrow_joy"
    };
    [Export] public float IntervaloExpresionMin = 8.0f;      // Segundos mínimos entre expresiones
    [Export] public float IntervaloExpresionMax = 20.0f;     // Segundos máximos entre expresiones
    [Export] public float DuracionExpresion     = 2.5f;      // Cuánto dura cada expresión
    [Export] public float SuavizadoExpresion    = 4.0f;

    // ── Estado de habla (empujado desde AgiModeMain) ───────────────────────────
    public float NivelVozActual = 0.0f; // Expuesto públicamente para que AgiModeMain lo actualice

    // ── Privados ────────────────────────────────────────────────────────────────
    private FastNoiseLite _noise = new FastNoiseLite();
    private float _tiempoGlobal = 0.0f;
    private Vector3 _posicionOriginalTargetMirada;
    private Vector3 _posicionOriginalTargetCintura;
    private float _valorBocaActual = 0.0f;
    private int _bocaIndex = -2;
    private int _eyeCloseIndex = -1; // Para evitar doble parpadeo


    // Micro-expresión
    private float _timerSiguienteExpresion = 0.0f;
    private float _timerExpresionActual    = 0.0f;
    private int   _expresionActualIndex    = -1;
    private float _valorExpresionActual    = 0.0f;
    private int[] _indicesExpresion; // Cacheados en _Ready

    // Cabeceo al hablar
    private float _rotCabezaActualX = 0.0f;
    private Vector3 _rotOriginalCabeza;

    public override void _Ready()
    {
        _noise.Seed = (int)GD.Randi();
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        _noise.Frequency = 0.01f;

        if (TargetMirada != null)
            _posicionOriginalTargetMirada = TargetMirada.Position;

        if (TargetCintura != null)
            _posicionOriginalTargetCintura = TargetCintura.Position;

        // Cachear índices de blendshapes de expresión
        _indicesExpresion = new int[NombresExpresionesAleatorias.Length];
        for (int i = 0; i < NombresExpresionesAleatorias.Length; i++)
        {
            if (MallaRostro != null && !string.IsNullOrEmpty(NombresExpresionesAleatorias[i]))
                _indicesExpresion[i] = MallaRostro.FindBlendShapeByName(NombresExpresionesAleatorias[i]);
            else
                _indicesExpresion[i] = -1;
        }

        if (MallaRostro != null)
        {
            _eyeCloseIndex = MallaRostro.FindBlendShapeByName("eye_close");
        }

        // Timer inicial aleatorio para no sincronizar todas las expresiones desde t=0
        _timerSiguienteExpresion = (float)GD.RandRange(IntervaloExpresionMin, IntervaloExpresionMax);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tiempoGlobal += (float)delta;
        ManejarMovimiento(delta);
        ControlarAlturaCamara(delta);

        // ── Efectos visuales existentes ──────────────────────────────────────
        AplicarRuidoMirada();
        AplicarFuerzaPelo();

        // ── Animaciones VTuber nuevas ─────────────────────────────────────────
        AplicarRespiracionYBalanceo(delta);
        AplicarCabeceoHabla(delta);
        AplicarMicroExpresion(delta);

        // Forzar parpadeo a 0 si hay una expresión de ojos activa (esto sobreescribe al AnimationPlayer)
        if (_expresionActualIndex >= 0 && _indicesExpresion[_expresionActualIndex] >= 0)
        {
            string nombreExpresion = NombresExpresionesAleatorias[_expresionActualIndex];
            if (nombreExpresion.StartsWith("eye_") && _eyeCloseIndex != -1)
            {
                MallaRostro.SetBlendShapeValue(_eyeCloseIndex, 0.0f);
            }
        }

        // La boca es empujada externamente por AgiModeMain.EmpujarNivelVoz()
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RESPIRACIÓN + BALANCEO FLOTANTE (Movimientos de cadera usando Target_Hip)
    // Combina un ciclo senoidal lento (respiración) con ruido suave (flotación).
    // ─────────────────────────────────────────────────────────────────────────
    private void AplicarRespiracionYBalanceo(double delta)
    {
        if (TargetCintura == null) return;

        // Respiración: ciclo sinusoidal puro en Y (la cadera baja al expirar, sube al inspirar)
        float resp = Mathf.Sin(_tiempoGlobal * VelocidadRespiracion * Mathf.Tau) * IntensidadRespiracion;

        // Balanceo: ruido de baja frecuencia en X y Z (traslación de la cadera)
        float tB = _tiempoGlobal * VelocidadBalanceo * 10.0f;
        float balX = _noise.GetNoise2D(tB + 500, 0)   * IntensidadBalanceo;
        float balZ = _noise.GetNoise2D(0, tB + 500)   * IntensidadBalanceo;

        // Aplicar la nueva posición al Target_Hip
        Vector3 nuevaPos = _posicionOriginalTargetCintura + new Vector3(balX, resp, balZ);
        TargetCintura.Position = TargetCintura.Position.Lerp(nuevaPos, (float)delta * 6.0f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CABECEO AL HABLAR Y RUIDO DE MIRADA (Movimientos de cabeza usando Target_Head)
    // Mueve el Target_Head (LookAt) para que el personaje asienta y mire alrededor.
    // ─────────────────────────────────────────────────────────────────────────
    private void AplicarCabeceoHabla(double delta)
    {
        // En lugar de rotar un hueso, movemos el target de mirada en Y
    }

    private void AplicarRuidoMirada()
    {
        if (TargetMirada == null) return;
        
        // Ruido de mirada base (X, Y, Z)
        float t = _tiempoGlobal * RuidoFrecuencia * 50.0f;
        float offX = _noise.GetNoise2D(t, 0) * RuidoAmplitud;
        float offY = _noise.GetNoise2D(0, t) * RuidoAmplitud;
        float offZ = _noise.GetNoise2D(t, t) * RuidoAmplitud;

        // Movimiento de cabeza muy sutil (mira hacia los lados despacio)
        float tPan = _tiempoGlobal * 0.15f * 10.0f;
        float panX = _noise.GetNoise2D(tPan + 200, 0) * IntensidadPaneoCabeza;

        // Si está hablando, agregar oscilación en Y (asiente) al Target de Mirada
        float targetNodY = 0.0f;
        if (NivelVozActual > 0.05f)
        {
            // Seno negativo para que asienta hacia abajo
            targetNodY = -Mathf.Abs(Mathf.Sin(_tiempoGlobal * VelocidadCabeceo)) * IntensidadCabeceo * NivelVozActual;
        }

        TargetMirada.Position = _posicionOriginalTargetMirada + new Vector3(offX + panX, offY + targetNodY, offZ);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MICRO-EXPRESIONES ALEATORIAS
    // Cada N segundos activa aleatoriamente una expresión de la lista.
    // ─────────────────────────────────────────────────────────────────────────
    private void AplicarMicroExpresion(double delta)
    {
        if (MallaRostro == null || _indicesExpresion == null || _indicesExpresion.Length == 0) return;

        // ── Contador para activar la próxima expresión ────────────────────────
        if (_expresionActualIndex == -1)
        {
            _timerSiguienteExpresion -= (float)delta;
            if (_timerSiguienteExpresion <= 0.0f)
            {
                // Elegir expresión aleatoria con índice válido
                int intentos = 0;
                do
                {
                    _expresionActualIndex = (int)GD.RandRange(0, _indicesExpresion.Length - 0.001f);
                    intentos++;
                } while (_indicesExpresion[_expresionActualIndex] < 0 && intentos < 10);

                if (_indicesExpresion[_expresionActualIndex] < 0)
                {
                    // No se encontró ninguna expresión válida, resetear timer
                    _expresionActualIndex = -1;
                    _timerSiguienteExpresion = (float)GD.RandRange(IntervaloExpresionMin, IntervaloExpresionMax);
                    return;
                }

                _timerExpresionActual = DuracionExpresion;
            }
        }
        else
        {
            // ── Animar la expresión activa (entrada, sostenida, salida) ─────────
            _timerExpresionActual -= (float)delta;

            float progreso = 1.0f - (_timerExpresionActual / DuracionExpresion);
            float target;

            if (progreso < 0.2f)
            {
                // Fase de entrada (0 → 1)
                target = Mathf.SmoothStep(0.0f, 1.0f, progreso / 0.2f);
            }
            else if (progreso < 0.75f)
            {
                // Fase sostenida
                target = 1.0f;
            }
            else
            {
                // Fase de salida (1 → 0)
                target = Mathf.SmoothStep(0.0f, 1.0f, 1.0f - ((progreso - 0.75f) / 0.25f));
            }

            _valorExpresionActual = Mathf.Lerp(_valorExpresionActual, target, (float)delta * SuavizadoExpresion);
            int bsIdx = _indicesExpresion[_expresionActualIndex];
            if (bsIdx >= 0)
                MallaRostro.SetBlendShapeValue(bsIdx, _valorExpresionActual);

            // Expresión terminada — resetear
            if (_timerExpresionActual <= 0.0f)
            {
                if (bsIdx >= 0) MallaRostro.SetBlendShapeValue(bsIdx, 0.0f);
                _expresionActualIndex    = -1;
                _valorExpresionActual    = 0.0f;
                _timerSiguienteExpresion = (float)GD.RandRange(IntervaloExpresionMin, IntervaloExpresionMax);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LIP SYNC — empujado externamente por AgiModeMain cada frame
    // ─────────────────────────────────────────────────────────────────────────
    public void EmpujarNivelVoz(float nivelLineal, double delta)
    {
        NivelVozActual = nivelLineal; // Exponer para AplicarCabeceoHabla

        if (MallaRostro == null) return;

        if (_bocaIndex == -2)
        {
            if (string.IsNullOrEmpty(NombreBlendShapeBoca))
            {
                _bocaIndex = -1;
                GD.PrintErr("[LIP-SYNC] NombreBlendShapeBoca está vacío — asígnalo en el Inspector (ej: mouth_o2).");
                return;
            }

            _bocaIndex = MallaRostro.FindBlendShapeByName(NombreBlendShapeBoca);
            if (_bocaIndex == -1)
                GD.PrintErr($"[LIP-SYNC] Blendshape '{NombreBlendShapeBoca}' no encontrado en '{MallaRostro.Name}'.");
            else
                GD.Print($"[LIP-SYNC] Blendshape '{NombreBlendShapeBoca}' encontrado en índice {_bocaIndex}. Lip sync activo.");
        }

        if (_bocaIndex < 0) return;

        float targetBoca = Mathf.Clamp(nivelLineal * MultiplicadorBoca, 0.0f, 1.0f);
        _valorBocaActual = Mathf.Lerp(_valorBocaActual, targetBoca, (float)delta * SuavizadoBoca);
        MallaRostro.SetBlendShapeValue(_bocaIndex, _valorBocaActual);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MIRADA / MOVIMIENTO / CÁMARA / PELO (sin cambios)
    // ─────────────────────────────────────────────────────────────────────────
    private void ManejarMovimiento(double delta)
    {
        Vector3 velocity = Velocity;
        if (!IsOnFloor()) velocity.Y -= Gravity * (float)delta;
        if (Input.IsActionJustPressed("ui_accept") && IsOnFloor()) velocity.Y = JumpVelocity;

        Vector2 inputDir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();

        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * Speed;
            velocity.Z = direction.Z * Speed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
            velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    private void ControlarAlturaCamara(double delta)
    {
        if (CamaraNodo == null) return;
        if (Input.IsKeyPressed(Key.Q)) CamaraNodo.Position += new Vector3(0, VerticalSpeed * (float)delta, 0);
        if (Input.IsKeyPressed(Key.E)) CamaraNodo.Position -= new Vector3(0, VerticalSpeed * (float)delta, 0);
    }

    private void AplicarFuerzaPelo()
    {
        if (SpringBoneNodo == null) return;
        Vector3 fuerzaFinal = FuerzaManualPelo;

        if (UsarVientoAleatorio)
        {
            float tViento = _tiempoGlobal * VientoCoherencia * 10.0f;
            float ruidoX = _noise.GetNoise2D(tViento, 100) * VientoIntensidad;
            float ruidoY = _noise.GetNoise2D(200, tViento) * (VientoIntensidad * 0.5f);
            float ruidoZ = _noise.GetNoise2D(tViento, tViento) * VientoIntensidad;
            fuerzaFinal += new Vector3(ruidoX, ruidoY, ruidoZ);
        }

        SpringBoneNodo.Set("external_force", fuerzaFinal);
    }
}
