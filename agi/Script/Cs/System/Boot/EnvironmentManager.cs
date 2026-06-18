using Godot;
using System.IO;
using Logic.System.Platform;

/// <summary>
/// Clase encargada de la gestión del entorno de ejecución, detección de plataforma y persistencia de directorios.
/// </summary>
public partial class EnvironmentManager : Node
{
    public IPlatformBridge Bridge { get; private set; }

    // Banderas de control para capacidades del sistema y lógica de negocio
    public bool IsUIOnlyMode => !Bridge.CanRunLocalEngines;
    public bool CanRunLocalModels => Bridge.CanRunLocalEngines;
    public bool CanRunLocalTTS => Bridge.CanRunLocalEngines;

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
        Bridge = PlatformFactory.ResolveBridge();
        Bridge.InitializeEnvironment();
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

        // Test write permissions in BinPath to handle OS permission limitations
        try
        {
            string testFile = Path.Combine(BinPath, ".write_test");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            GD.Print($"EnvironmentManager: Write permissions verified successfully on {BinPath}");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"EnvironmentManager: CRITICAL - No write permissions on user://bin/ directory! Exception: {ex.Message}");
        }

        // Automatically copy local python scripts from res:// to user://bin/ for development
        string[] pythonScripts = { "search_server.py", "mcp_server.py", "tts_server.py", "file_extractor.py", "image_server.py", "video_server.py" };
        foreach (string script in pythonScripts)
        {
            CopyScriptFromRes(script);
        }

    }

    private void CopyScriptFromRes(string scriptName)
    {
        string resPath = $"res://Script/Cs/System/Drivers/{scriptName}";
        string destPath = Path.Combine(BinPath, scriptName);
        if (Godot.FileAccess.FileExists(resPath))
        {
            using (var srcFile = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read))
            {
                if (srcFile != null)
                {
                    byte[] buffer = srcFile.GetBuffer((long)srcFile.GetLength());
                    using (var destFile = Godot.FileAccess.Open(destPath, Godot.FileAccess.ModeFlags.Write))
                    {
                        if (destFile != null)
                        {
                            destFile.StoreBuffer(buffer);
                            GD.Print($"EnvironmentManager: Successfully copied/updated {scriptName} in user://bin/");
                        }
                    }
                }
            }
        }
    }
}