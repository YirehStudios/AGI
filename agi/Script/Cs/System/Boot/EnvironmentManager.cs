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

        DeployBundledComfyNodes();
    }

    /// <summary>
    /// Deploys bundled ComfyUI custom nodes to the runtime binary folder offline,
    /// using a chunked reading mechanism to avoid RAM spikes.
    /// </summary>
    private void DeployBundledComfyNodes()
    {
        string osFolder = Godot.OS.GetName().ToLower();
        string customNodesDestPath = Path.Combine(BinPath, osFolder, "comfyui", "custom_nodes");
        Directory.CreateDirectory(customNodesDestPath);

        string ggufNodeTarget = Path.Combine(customNodesDestPath, "ComfyUI-GGUF");
        
        if (!Directory.Exists(ggufNodeTarget))
        {
            GD.Print("EnvironmentManager: Instalando nodo ComfyUI-GGUF nativo desde empaquetado seguro offline...");
            
            string resZipPath = "res://Script/Python/ComfyUI-GGUF.zip";
            string tempZipPath = Path.Combine(BinPath, "temp_node.zip");
            
            if (Godot.FileAccess.FileExists(resZipPath))
            {
                using (var src = Godot.FileAccess.Open(resZipPath, Godot.FileAccess.ModeFlags.Read))
                using (var dest = Godot.FileAccess.Open(tempZipPath, Godot.FileAccess.ModeFlags.Write))
                {
                    // Memory-optimized 4KB chunk copying
                    int bufferSize = 4096;
                    long fileLength = (long)src.GetLength();
                    long bytesRead = 0;
                    
                    while (bytesRead < fileLength)
                    {
                        int currentChunkSize = (int)System.Math.Min(bufferSize, fileLength - bytesRead);
                        byte[] buffer = src.GetBuffer(currentChunkSize);
                        dest.StoreBuffer(buffer);
                        bytesRead += currentChunkSize;
                    }
                }
                
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, customNodesDestPath);
                System.IO.File.Delete(tempZipPath);
                
                // Handle potential "-main" suffix from GitHub zips
                string extractedMainFolder = Path.Combine(customNodesDestPath, "ComfyUI-GGUF-main");
                if (Directory.Exists(extractedMainFolder))
                {
                    Directory.Move(extractedMainFolder, ggufNodeTarget);
                }
                
                GD.Print("EnvironmentManager: ComfyUI-GGUF desplegado correctamente en custom_nodes.");
            }
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