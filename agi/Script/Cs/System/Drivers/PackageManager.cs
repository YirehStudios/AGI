using Godot;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using Logic.Utils;

namespace Logic.System.Drivers
{
    /// <summary>
    /// Gestiona el ciclo de vida de los paquetes de motores, incluyendo la descarga, 
    /// organización en directorios específicos y verificación de permisos de ejecución.
    /// Incorpora la lógica para la creación de entornos de ejecución aislados (Python).
    /// </summary>
    public partial class PackageManager : Node
    {
        private dynamic _environmentManager;
        private dynamic _downloadManager;

        /// <summary>
        /// Inicializa las referencias a los gestores de entorno y descarga mediante el sistema de Autoload de Godot.
        /// </summary>
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

        /// <summary>
        /// Sincroniza y configura un entorno de ejecución de Python aislado para el host local.[cite: 1]
        /// En sistemas Linux, utiliza el intérprete del sistema para instanciar un entorno virtual (venv).[cite: 1]
        /// En sistemas Windows, despliega una distribución portable (embeddable), habilita el soporte de sitios 
        /// y aprovisiona el gestor de paquetes pip para la resolución de dependencias externas.[cite: 1]
        /// Tras la configuración base, procede con la instalación de dependencias para el motor de inferencia TTS.[cite: 1]
        /// </summary>
        /// <param name="pythonUrl">Dirección de descarga del paquete binario portable, requerida únicamente para despliegues en Windows.</param>
        /// <returns>Tarea asíncrona que representa el éxito de la inicialización y aprovisionamiento del entorno.</returns>
        public async Task<bool> EnsurePythonEnvironmentAsync(string pythonUrl)
        {
            // Evalúa el contexto de ejecución para restringir operaciones de sistema de archivos en plataformas no soportadas.[cite: 1]
            if (_environmentManager.IsUIOnlyMode || _environmentManager.IsAndroid)
            {
                return true;
            }

            // Establece la ruta absoluta para el directorio del entorno de ejecución aislado.[cite: 1]
            string envPath = Path.Combine(_environmentManager.EnvPath, "python");
            
            // Asegura la existencia del contenedor de directorio antes de la inicialización de binarios.[cite: 1]
            if (!Directory.Exists(envPath))
            {
                Directory.CreateDirectory(envPath);
            }

            // Configuración de entorno para sistemas operativos basados en Linux.[cite: 1]
            if (_environmentManager.IsLinux)
            {
                string pythonBin = Path.Combine(envPath, "bin", "python3");
                
                // Inicializa el entorno virtual (venv) si el intérprete local no está presente.[cite: 1]
                if (!File.Exists(pythonBin))
                {
                    OS.Execute("python3", new string[] { "-m", "venv", envPath }, new Godot.Collections.Array(), true);

                    // Valida la creación efectiva del binario del intérprete tras la ejecución del comando venv.[cite: 1]
                    if (!File.Exists(pythonBin))
                    {
                        GD.PrintErr("Fallo crítico: No se pudo crear el entorno venv. ¿Está instalado python3-venv?");
                        return false;
                    }
                }

                // Localiza el binario de pip e instala las dependencias de red, procesamiento de audio y tensores Kokoro.[cite: 1]
                string pipBin = Path.Combine(envPath, "bin", "pip");

                // Verifica la integridad del entorno validando la existencia del gestor de paquetes pip antes de su invocación.[cite: 1]
                if (!File.Exists(pipBin))
                {
                    GD.PrintErr("Fallo crítico: No se encontró el binario de pip en el entorno virtual.");
                    return false;
                }

                // Ejecuta la instalación de dependencias necesarias para el flujo de trabajo de inferencia.[cite: 1]
                OS.Execute(pipBin, new string[] { "install", "websockets", "soundfile", "numpy", "kokoro-onnx", "onnxruntime-vulkan" }, new Godot.Collections.Array(), true);
                
                GD.Print("[PackageManager] Dependencias de Kokoro instaladas satisfactoriamente en el entorno Linux.");
                return true;
            }

            // Configuración de entorno para sistemas operativos Windows.[cite: 1]
            if (_environmentManager.IsWindows)
            {
                string pythonExe = Path.Combine(envPath, "python.exe");

                // Realiza el despliegue de la distribución embebida de Python si no se detecta el ejecutable principal.[cite: 1]
                if (!File.Exists(pythonExe))
                {
                    // Descarga y extrae el paquete binario de Python.[cite: 1]
                    bool downloadSuccess = await _downloadManager.DownloadFileAsync(pythonUrl, envPath, "python-embed.zip");
                    if (!downloadSuccess) return false;

                    // Obtiene el script de arranque para la instalación manual de pip en distribuciones embebidas.[cite: 1]
                    string pipScriptUrl = "https://bootstrap.pypa.io/get-pip.py";
                    bool pipDownloadSuccess = await _downloadManager.DownloadFileAsync(pipScriptUrl, envPath, "get-pip.py");
                    if (!pipDownloadSuccess) return false;

                    // Modifica el archivo de configuración de rutas para habilitar la carga de módulos externos (site-packages).[cite: 1]
                    string[] pthFiles = Directory.GetFiles(envPath, "python*._pth");
                    if (pthFiles.Length > 0)
                    {
                        string pthFilePath = pthFiles[0];
                        string pthContent = File.ReadAllText(pthFilePath);
                        pthContent = pthContent.Replace("#import site", "import site");
                        File.WriteAllText(pthFilePath, pthContent);
                    }

                    // Ejecuta el script de instalación de pip mediante el intérprete local.[cite: 1]
                    string getPipLocalPath = Path.Combine(envPath, "get-pip.py");
                    OS.Execute(pythonExe, new string[] { getPipLocalPath }, new Godot.Collections.Array(), true);
                }

                // Invoca el módulo pip para instalar dependencias optimizadas para DirectML en hardware Windows.[cite: 1]
                OS.Execute(pythonExe, new string[] { "-m", "pip", "install", "websockets", "soundfile", "numpy", "kokoro-onnx", "onnxruntime-directml" }, new Godot.Collections.Array(), true);
                
                GD.Print("[PackageManager] Dependencias de Kokoro instaladas satisfactoriamente en el entorno Windows.");
                return true;
            }

            return true;
        }
    }
}