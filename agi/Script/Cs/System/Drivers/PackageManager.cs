using Godot;
using System.IO;
using System.Threading.Tasks;
using Logic.Utils;

namespace Logic.System.Drivers
{
    /// <summary>
    /// Gestiona el ciclo de vida de los paquetes de motores, incluyendo la descarga, 
    /// organización en directorios específicos y verificación de permisos de ejecución.
    /// </summary>
    public partial class PackageManager : Node
    {
        private dynamic _environmentManager;
        private dynamic _downloadManager;

        public override void _Ready()
        {
            _environmentManager = GetNode("/root/EnvironmentManager");
            _downloadManager = GetNode("/root/DownloadManager");
        }

        /// <summary>
        /// Valida la presencia del ejecutable del motor en la subcarpeta correspondiente a su arquitectura y prefijo.
        /// </summary>
        public bool IsEngineReady(string enginePrefix)
        {
            // Se calcula la ruta de destino basándose en el sistema operativo y el motor para una búsqueda precisa.
            string osFolder = _environmentManager.IsWindows ? "windows" : "linux";
            string engineTargetDir = Path.Combine(_environmentManager.BinPath, osFolder, enginePrefix);
            
            string path = FileResolver.FindExecutable(engineTargetDir, _environmentManager.IsWindows, enginePrefix);
            return !string.IsNullOrEmpty(path);
        }

        /// <summary>
        /// Ejecuta el aprovisionamiento del motor mediante descarga asíncrona y configuración de entorno.
        /// </summary>
        public async Task<bool> DownloadAndPrepareEngineAsync(string url, string fileName, string folderName, string exactExecutableName)
        {
            // Valida si el entorno actual requiere la preparación de binarios nativos.
            if (_environmentManager.IsUIOnlyMode || _environmentManager.IsAndroid)
            {
                return true;
            }

            // Define la segmentación de directorios según el sistema operativo identificado.
            string osFolder = _environmentManager.IsWindows ? "windows" : "linux";
            
            // Calcula la ruta absoluta de destino para el despliegue del motor.
            string engineTargetDir = Path.Combine(_environmentManager.BinPath, osFolder, folderName);
            
            // Establece la ruta interna de Godot para la persistencia del archivo comprimido.
            string godotDestination = $"user://bin/{osFolder}/{folderName}";

            // Inicia la transferencia de datos hacia la carpeta aislada del motor específico.
            bool downloadSuccess = await _downloadManager.DownloadFileAsync(url, godotDestination, fileName);
            if (!downloadSuccess)
            {
                return false;
            }

            // Localiza la ubicación exacta del binario ejecutable dentro del directorio extraído.
            string executablePath = FileResolver.FindExecutable(engineTargetDir, _environmentManager.IsWindows, exactExecutableName);

            GD.Print($"[PackageManager] Evaluating path for {exactExecutableName}: {executablePath}");
            if (string.IsNullOrEmpty(executablePath))
            {
                GD.PrintErr($"[PackageManager] ERROR: Executable for '{exactExecutableName}' not found within {engineTargetDir}. Aborting.");
                return false;
            }

            // Gestiona descriptores de seguridad y librerías vinculadas en sistemas POSIX.
            if (_environmentManager.IsLinux)
            {
                // Concede privilegios de ejecución al binario identificado.
                OS.Execute("chmod", new string[] { "+x", executablePath });

                // Procesa dependencias de librerías compartidas si el motor es Sherpa.
                if (folderName.Contains("sherpa"))
                {
                    string libPath = Path.Combine(engineTargetDir, "lib");
                    if (Directory.Exists(libPath))
                    {
                        string[] libFiles = Directory.GetFiles(libPath, "*.so*");
                        GD.Print($"[PackageManager] Applying read permissions to libraries in {libPath}");
                        foreach (string libFile in libFiles)
                        {
                            // Asegura que las librerías dinámicas sean accesibles para la carga en runtime.
                            OS.Execute("chmod", new string[] { "a+r", libFile });
                        }
                        GD.Print($"[PackageManager] Library permissions applied successfully.");
                    }
                }
            }

            GD.Print($"[PackageManager] -> Engine '{exactExecutableName}' prepared successfully.");
            return true;
        }
    }
}