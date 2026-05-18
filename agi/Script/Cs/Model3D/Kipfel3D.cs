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
    [Export] public Vector3 FuerzaManualPelo = new Vector3(0, 0, 0); // Control exacto X, Y, Z
    [Export] public bool UsarVientoAleatorio = true;
    [Export] public float VientoIntensidad = 0.2f; // Qué tan fuerte es el azar
    [Export] public float VientoCoherencia = 0.3f;  // Menos valor = cambios más lentos y suaves

    private FastNoiseLite _noise = new FastNoiseLite();
    private float _tiempoGlobal = 0.0f;
    private Vector3 _posicionOriginalTarget;

    public override void _Ready()
    {
        _noise.Seed = (int)GD.Randi();
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
        _noise.Frequency = 0.01f; // Esto hace que el ruido sea muy suave por defecto

        if (TargetMirada != null)
            _posicionOriginalTarget = TargetMirada.Position;
    }

    public override void _PhysicsProcess(double delta)
    {
        _tiempoGlobal += (float)delta;
        ManejarMovimiento(delta);
        ControlarAlturaCamara(delta);

        // Efectos Visuales
        AplicarRuidoMirada();
        AplicarFuerzaPelo();
    }

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

    private void AplicarRuidoMirada()
    {
        if (TargetMirada == null) return;
        float t = _tiempoGlobal * RuidoFrecuencia * 50.0f;
        float offX = _noise.GetNoise2D(t, 0) * RuidoAmplitud;
        float offY = _noise.GetNoise2D(0, t) * RuidoAmplitud;
        float offZ = _noise.GetNoise2D(t, t) * RuidoAmplitud;
        TargetMirada.Position = _posicionOriginalTarget + new Vector3(offX, offY, offZ);
    }

    private void AplicarFuerzaPelo()
    {
        if (SpringBoneNodo == null) return;

        Vector3 fuerzaFinal = FuerzaManualPelo;

        if (UsarVientoAleatorio)
        {
            // Usamos un tiempo más lento para que el aire sea cíclico y suave
            float tViento = _tiempoGlobal * VientoCoherencia * 10.0f;

            float ruidoX = _noise.GetNoise2D(tViento, 100) * VientoIntensidad;
            float ruidoY = _noise.GetNoise2D(200, tViento) * (VientoIntensidad * 0.5f); // Y suele ser menor
            float ruidoZ = _noise.GetNoise2D(tViento, tViento) * VientoIntensidad;

            fuerzaFinal += new Vector3(ruidoX, ruidoY, ruidoZ);
        }

        // Acceso a la propiedad nativa de SkeletonModifier3D
        // En Godot nativo, la propiedad se llama "external_force"
        SpringBoneNodo.Set("external_force", fuerzaFinal);
    }
}
