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
        /// Dynamically resolves the absolute path to the 'uv' executable on Linux systems.
        /// This prevents 'command not found' exceptions caused by Godot's inherited environment 
        /// not reflecting recent PATH updates in the user's shell profile[cite: 3].
        /// </summary>
        /// <returns>
        /// The absolute path to the 'uv' binary if found in common installation directories; 
        /// otherwise, returns "uv" to attempt resolution from the global system PATH[cite: 3].
        /// </returns>
        private string GetUvPath()
        {
            // Retrieve the current user's home directory path from environment variables.
            string home = global::System.Environment.GetEnvironmentVariable("HOME");
            
            // Define the most frequent installation targets for the uv package manager.
            string localUv = $"{home}/.local/bin/uv";
            string cargoUv = $"{home}/.cargo/bin/uv";

            // Perform synchronous validation of the binary's existence in the file system.
            if (global::System.IO.File.Exists(localUv)) return localUv;
            if (global::System.IO.File.Exists(cargoUv)) return cargoUv;

            // Fallback to the standard command name if specific absolute paths do not exist.
            return "uv"; 
        }

        /// <summary>
        /// Synchronizes and configures an isolated Python execution environment for the local host[cite: 8].
        /// On Linux systems, it utilizes the 'uv' package manager to provision Python 3.13, create a virtual environment, and install dependencies[cite: 8].
        /// On Windows systems, it deploys a portable distribution, enables site-packages, and provisions pip for module resolution[cite: 8].
        /// </summary>
        /// <param name="pythonUrl">The download URL for the portable Python binary, required for Windows deployments[cite: 8].</param>
        /// <returns>A task representing the success of the environment initialization and provisioning[cite: 8].</returns>
        public async Task<bool> EnsurePythonEnvironmentAsync(string pythonUrl)
        {
            // Restricts execution in environments where local binary provisioning is not supported or required[cite: 8].
            if (_environmentManager.IsUIOnlyMode || _environmentManager.IsAndroid)
            {
                return true;
            }

            // Defines the absolute target directory for the isolated Python environment[cite: 8].
            string envPath = Path.Combine(_environmentManager.EnvPath, "python");
            
            // Verifies the existence of the parent directory before proceeding with environment setup[cite: 8].
            if (!Directory.Exists(envPath))
            {
                Directory.CreateDirectory(envPath);
            }

            // Provisioning logic for Linux-based systems utilizing the 'uv' package manager[cite: 8].
            if (_environmentManager.IsLinux)
            {
                // Resolves the absolute path of the uv binary to ensure execution stability across different distributions[cite: 8].
                string uvCommand = GetUvPath();
                
                // Constructs the expected path to the internal Python interpreter to verify if the virtual environment is already established[cite: 8].
                string pythonBin = Path.Combine(envPath, "bin", "python");
                
                var output = new Godot.Collections.Array();
                int exitCode;

                // Conditional block to prevent redundant environment initialization and potential directory access conflicts[cite: 8].
                if (!File.Exists(pythonBin))
                {
                    // Invokes the package manager to download and install the specific Python 3.13 runtime[cite: 8].
                    exitCode = OS.Execute(uvCommand, new string[] { "python", "install", "3.13" }, output, true);
                    if (exitCode != 0)
                    {
                        GD.PrintErr($"[PackageManager] uv Error: {string.Join("\n", output)}");
                        return false;
                    }

                    // Initializes an isolated virtual environment bound to the provisioned Python 3.13 runtime at the designated path[cite: 8].
                    output.Clear();
                    exitCode = OS.Execute(uvCommand, new string[] { "venv", "--python", "3.13", envPath }, output, true);
                    if (exitCode != 0)
                    {
                        GD.PrintErr($"[PackageManager] uv Error: {string.Join("\n", output)}");
                        return false;
                    }
                }

                // Executes dependency resolution via uv's pip interface. This uses a universal onnxruntime build to ensure 
                // maximum compatibility across various Linux distributions and hardware configurations[cite: 8].
                output.Clear();
                string[] dependencies = { "pip", "install", "--python", envPath, "websockets", "soundfile", "numpy", "kokoro-onnx", "onnxruntime" };
                exitCode = OS.Execute(uvCommand, dependencies, output, true);
                if (exitCode != 0)
                {
                    GD.PrintErr($"[PackageManager] uv Error: {string.Join("\n", output)}");
                    return false;
                }
                
                GD.Print("[PackageManager] Linux environment and Kokoro dependencies provisioned successfully via uv[cite: 8].");
                return true;
            }

            // Provisioning logic for Windows-based systems using portable (embeddable) distributions[cite: 8].
            if (_environmentManager.IsWindows)
            {
                string pythonExe = Path.Combine(envPath, "python.exe");

                // Deploys the portable Python distribution if the main executable is missing[cite: 8].
                if (!File.Exists(pythonExe))
                {
                    // Asynchronously retrieves the compressed package and the pip bootstrap script[cite: 8].
                    bool downloadSuccess = await _downloadManager.DownloadFileAsync(pythonUrl, envPath, "python-embed.zip");
                    if (!downloadSuccess) return false;

                    string pipScriptUrl = "https://bootstrap.pypa.io/get-pip.py";
                    bool pipDownloadSuccess = await _downloadManager.DownloadFileAsync(pipScriptUrl, envPath, "get-pip.py");
                    if (!pipDownloadSuccess) return false;

                    // Modifies the path configuration file to enable 'site' module support for external libraries[cite: 8].
                    string[] pthFiles = Directory.GetFiles(envPath, "python*._pth");
                    if (pthFiles.Length > 0)
                    {
                        string pthFilePath = pthFiles[0];
                        string pthContent = File.ReadAllText(pthFilePath);
                        pthContent = pthContent.Replace("#import site", "import site");
                        File.WriteAllText(pthFilePath, pthContent);
                    }

                    // Installs the pip package manager using the downloaded initialization script[cite: 8].
                    string getPipLocalPath = Path.Combine(envPath, "get-pip.py");
                    OS.Execute(pythonExe, new string[] { getPipLocalPath }, new Godot.Collections.Array(), true);
                }

                // Provision dependencies via pip, utilizing the DirectML provider for hardware acceleration on Windows[cite: 8].
                var output = new Godot.Collections.Array();
                int pipExit = OS.Execute(pythonExe, new string[] { "-m", "pip", "install", "websockets", "soundfile", "numpy", "kokoro-onnx", "onnxruntime-directml" }, output, true);

                // Verifies successful completion of the package installation process[cite: 8].
                if (pipExit != 0)
                {
                    GD.PrintErr($"[PackageManager] PIP Error Windows: {string.Join("\n", output)}[cite: 8]");
                    return false;
                }
                
                GD.Print("[PackageManager] Kokoro dependencies successfully installed in the Windows environment[cite: 8].");
                return true;
            }

            return true;
        }
    }
}