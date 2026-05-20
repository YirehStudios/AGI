using Godot;
using System.IO;

/// <summary>
/// Clase encargada de la gestión del entorno de ejecución, detección de plataforma y persistencia de directorios.
/// </summary>
public partial class EnvironmentManager : Node
{
    // Propiedades de estado para la identificación de la plataforma de ejecución
    public bool IsWindows => OS.GetName() == "Windows";
    public bool IsLinux => OS.GetName() == "Linux" || OS.GetName() == "FreeBSD" || OS.GetName() == "X11";
    public bool IsAndroid => OS.GetName() == "Android";

    // Banderas de control para capacidades del sistema y lógica de negocio
    public bool IsUIOnlyMode => IsAndroid;
    public bool CanRunLocalModels => !IsAndroid;
    public bool CanRunLocalTTS => !IsAndroid;

    // Definición de rutas de acceso global para recursos del sistema
    public string BinPath => ProjectSettings.GlobalizePath("user://bin");
    public string ModelsPath => ProjectSettings.GlobalizePath("user://models");
    public string EnvPath => ProjectSettings.GlobalizePath("user://env");
    public string SettingsPath => ProjectSettings.GlobalizePath("user://settings");

    /// <summary>
    /// Ciclo de vida inicial del nodo; dispara la configuración del entorno y validación de archivos.
    /// </summary>
    public override void _Ready()
    {
        EnsureDirectoriesExist();
    }

    /// <summary>
    /// Garantiza la integridad de la estructura de carpetas necesaria para el funcionamiento del sistema.
    /// </summary>
    private void EnsureDirectoriesExist()
    {
        // Colección de rutas críticas que requieren verificación de existencia física
        string[] paths = { BinPath, ModelsPath, EnvPath, SettingsPath };

        foreach (string path in paths)
        {
            // Verificación y creación de directorios en caso de ausencia en el volumen de almacenamiento
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}