using Godot;
using System.IO;

public partial class EnvironmentManager : Node
{
    // Propiedades de solo lectura para la detección del SO
    public bool IsWindows { get; private set; }
    public bool IsLinux { get; private set; }
    public bool IsAndroid { get; private set; }

    // Propiedades de Feature Flags
    public bool IsUIOnlyMode { get; private set; }
    public bool CanRunLocalModels { get; private set; }
    public bool CanRunLocalTTS { get; private set; }

    // Propiedades de rutas globales
    public string BinPath { get; private set; }
    public string ModelsPath { get; private set; }
    public string EnvPath { get; private set; }
    public string SettingsPath { get; private set; }

    public override void _Ready()
    {
        InitializeEnvironment();
        EnsureDirectoriesExist();
    }

    private void InitializeEnvironment()
    {
        string osName = OS.GetName();

        //Evaluación de sistema operativo
        IsWindows = osName == "Windows";
        IsLinux = osName == "LinuxBSD";
        IsAndroid = osName == "Android";

        // IsWindows = true; 
        // IsLinux = false;
        // IsAndroid = false;

        // Asignación de flags según reglas de negocio
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

        // Conversión de rutas de Godot a rutas del sistema operativo
        BinPath = ProjectSettings.GlobalizePath("user://bin");
        ModelsPath = ProjectSettings.GlobalizePath("user://models");
        EnvPath = ProjectSettings.GlobalizePath("user://env");
        SettingsPath = ProjectSettings.GlobalizePath("user://settings");
    }

    private void EnsureDirectoriesExist()
    {
        // Creación física de directorios en el sistema de archivos si no existen
        string[] paths = { BinPath, ModelsPath, EnvPath, SettingsPath };

        foreach (string path in paths)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}