using Godot;
using System.IO;

/// <summary>
/// Clase encargada de la gestión del entorno de ejecución, detección de plataforma y persistencia de directorios.[cite: 1]
/// </summary>
public partial class EnvironmentManager : Node
{
    // Propiedades de estado para la identificación de la plataforma de ejecución[cite: 1]
    public bool IsWindows { get; private set; }
    public bool IsLinux { get; private set; }
    public bool IsAndroid { get; private set; }

    // Banderas de control para capacidades del sistema y lógica de negocio[cite: 1]
    public bool IsUIOnlyMode { get; private set; }
    public bool CanRunLocalModels { get; private set; }
    public bool CanRunLocalTTS { get; private set; }

    // Definición de rutas de acceso global para recursos del sistema[cite: 1]
    public string BinPath { get; private set; }
    public string ModelsPath { get; private set; }
    public string EnvPath { get; private set; }
    public string SettingsPath { get; private set; }

    /// <summary>
    /// Ciclo de vida inicial del nodo; dispara la configuración del entorno y validación de archivos.[cite: 1]
    /// </summary>
    public override void _Ready()
    {
        InitializeEnvironment();
        EnsureDirectoriesExist();
    }

    /// <summary>
    /// Realiza la detección del kernel del sistema operativo y establece las políticas de ejecución del software.[cite: 1]
    /// </summary>
    private void InitializeEnvironment()
    {
        // Recuperación del identificador de plataforma mediante el motor Godot[cite: 1]
        string osName = OS.GetName();

        // Evaluación de tipos de sistema operativo para determinar compatibilidad y dependencias[cite: 1]
        IsWindows = osName == "Windows";
        IsLinux = osName == "Linux" || osName == "FreeBSD" || osName == "X11";
        IsAndroid = osName == "Android";

        // Definición de restricciones operativas basadas en la arquitectura del dispositivo móvil[cite: 1]
        if (IsAndroid)
        {
            IsUIOnlyMode = true;
            CanRunLocalModels = false;
            CanRunLocalTTS = false;
        }
        else
        {
            IsUIOnlyMode = false;
            CanRunLocalModels = true;
            CanRunLocalTTS = true;
        }

        // Globalización de rutas de usuario para acceso directo desde el sistema de archivos del SO[cite: 1]
        BinPath = ProjectSettings.GlobalizePath("user://bin");
        ModelsPath = ProjectSettings.GlobalizePath("user://models");
        EnvPath = ProjectSettings.GlobalizePath("user://env");
        SettingsPath = ProjectSettings.GlobalizePath("user://settings");
    }

    /// <summary>
    /// Garantiza la integridad de la estructura de carpetas necesaria para el funcionamiento del sistema.[cite: 1]
    /// </summary>
    private void EnsureDirectoriesExist()
    {
        // Colección de rutas críticas que requieren verificación de existencia física[cite: 1]
        string[] paths = { BinPath, ModelsPath, EnvPath, SettingsPath };

        foreach (string path in paths)
        {
            // Verificación y creación de directorios en caso de ausencia en el volumen de almacenamiento[cite: 1]
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}