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
        /// Provisions a dedicated Python environment for the Search and MCP Microservices.
        /// Performs full deployment including script synchronization, portable distribution setup, 
        /// and dependency installation (FastAPI, MCP, httpx, Search tools).
        /// Updated with resilient download logic and physical file presence verification.
        /// </summary>
        /// <param name="pythonUrl">The remote URL for the portable Python distribution (Windows).</param>
        /// <param name="searchServerUrl">The remote URL for the search_server.py script from manifest.</param>
        /// <param name="mcpServerUrl">The remote URL for the mcp_server.py script from manifest.</param>
        /// <returns>True if the environment and all microservice dependencies are ready; otherwise, false.</returns>
        public async Task<bool> EnsureMicroservicesEnvironmentAsync(string pythonUrl, string searchServerUrl, string mcpServerUrl)
        {
            // Validates operational support for the current hardware platform.
            if (_environmentManager.IsUIOnlyMode || _environmentManager.IsAndroid) return true;

            string envPath = Path.Combine(_environmentManager.EnvPath, "python_search");
            if (!Directory.Exists(envPath)) Directory.CreateDirectory(envPath);

            // Synchronizes the microservice logic from the repository to the local system.
            // Uses resilient logic to tolerate empty URLs from the manifest.
            bool searchDownload = true;
            if (!string.IsNullOrEmpty(searchServerUrl))
                searchDownload = await _downloadManager.DownloadFileAsync(searchServerUrl, _environmentManager.BinPath, "search_server.py");

            bool mcpDownload = true;
            if (!string.IsNullOrEmpty(mcpServerUrl))
                mcpDownload = await _downloadManager.DownloadFileAsync(mcpServerUrl, _environmentManager.BinPath, "mcp_server.py");
            
            // Strictly verifies physical file presence to prevent silent uv/pip execution failures.
            string mcpLocalPath = Path.Combine(_environmentManager.BinPath, "mcp_server.py");
            if (!File.Exists(mcpLocalPath))
            {
                GD.PrintErr($"[PackageManager] Fatal: mcp_server.py is missing from disk. URL was: '{mcpServerUrl}'. Check your manifest.");
                return false;
            }

            if (!searchDownload || !mcpDownload)
            {
                GD.PrintErr("[PackageManager] Error: Failed to synchronize microservice scripts.");
                return false;
            }

            // Linux Path: Leverages 'uv' for high-performance virtual environment provisioning.
            if (_environmentManager.IsLinux)
            {
                string uvCommand = GetUvPath();
                string pythonBin = Path.Combine(envPath, "bin", "python");
                var output = new global::Godot.Collections.Array();

                if (!File.Exists(pythonBin))
                {
                    GD.Print($"[PackageManager] Search Env: Installing Python 3.13 via uv...");
                    if (OS.Execute(uvCommand, new string[] { "python", "install", "3.13" }, output, true) != 0) return false;
                    output.Clear();
                    if (OS.Execute(uvCommand, new string[] { "venv", "--python", "3.13", envPath }, output, true) != 0) return false;
                }

                output.Clear();
                GD.Print($"[PackageManager] Search Env: Installing pip dependencies via uv...");
                
                // Includes mcp and httpx as core dependencies for the tool gateway.
                string[] dependencies = { "pip", "install", "--python", envPath, "fastapi", "uvicorn", "ddgs", "trafilatura", "mcp", "httpx" };
                int exitCode = OS.Execute(uvCommand, dependencies, output, true);
                
                if (exitCode != 0)
                {
                    GD.PrintErr($"[PackageManager] Linux uv Search error: {string.Join("\n", output)}.");
                    return false;
                }

                GD.Print($"[PackageManager] -> Search/MCP Environment provisioned successfully on Linux.");
                return true;
            }

            // Windows Path: Implements a portable environment for the microservice stack.
            if (_environmentManager.IsWindows)
            {
                string pythonExe = Path.Combine(envPath, "python.exe");

                if (!File.Exists(pythonExe))
                {
                    GD.Print($"[PackageManager] Search Env: Downloading portable Python for Windows...");
                    if (!await _downloadManager.DownloadFileAsync(pythonUrl, envPath, "python-embed.zip")) return false;
                    await _downloadManager.DownloadFileAsync("https://bootstrap.pypa.io/get-pip.py", envPath, "get-pip.py");

                    // Patches the embedded Python path configuration.
                    string[] pthFiles = Directory.GetFiles(envPath, "python*._pth");
                    if (pthFiles.Length > 0)
                    {
                        string content = File.ReadAllText(pthFiles[0]).Replace("#import site", "import site");
                        File.WriteAllText(pthFiles[0], content);
                    }

                    OS.Execute(pythonExe, new string[] { Path.Combine(envPath, "get-pip.py") }, new global::Godot.Collections.Array(), true);
                }

                var output = new global::Godot.Collections.Array();
                GD.Print($"[PackageManager] Search Env: Installing pip dependencies on Windows...");
                
                // Added mcp and httpx to the Windows pip installation command.
                string[] winDeps = { "-m", "pip", "install", "fastapi", "uvicorn", "ddgs", "trafilatura", "mcp", "httpx" };
                int pipExit = OS.Execute(pythonExe, winDeps, output, true);
                
                if (pipExit != 0)
                {
                    GD.PrintErr($"[PackageManager] Windows pip Search error: {string.Join("\n", output)}.");
                    return false;
                }

                GD.Print($"[PackageManager] -> Search/MCP Environment provisioned successfully on Windows.");
                return true;
            }

            return true;
        }

        /// <summary>
        /// Provisions the standard Python environment for the TTS (Sherpa/Kokoro) bridge.
        /// Targets the 'user://env/python' directory and installs required signal processing libraries.
        /// </summary>
        /// <param name="pythonUrl">The remote URL for the portable Python distribution.</param>
        /// <returns>True if the environment and audio dependencies are successfully prepared.</returns>
        public async Task<bool> EnsurePythonEnvironmentAsync(string pythonUrl)
        {
            if (_environmentManager.IsUIOnlyMode || _environmentManager.IsAndroid) return true;

            string envPath = Path.Combine(_environmentManager.EnvPath, "python");
            if (!Directory.Exists(envPath)) Directory.CreateDirectory(envPath);

            // Linux logic: Uses 'uv' for environment orchestration.
            if (_environmentManager.IsLinux)
            {
                string uvCommand = GetUvPath();
                string pythonBin = Path.Combine(envPath, "bin", "python");
                var output = new Godot.Collections.Array();

                if (!File.Exists(pythonBin))
                {
                    if (OS.Execute(uvCommand, new string[] { "python", "install", "3.10" }, output, true) != 0) return false;
                    output.Clear();
                    if (OS.Execute(uvCommand, new string[] { "venv", "--python", "3.10", envPath }, output, true) != 0) return false;
                }

                output.Clear();
                string[] dependencies = { "pip", "install", "--python", envPath, "websockets", "soundfile", "numpy", "kokoro-onnx" };
                return OS.Execute(uvCommand, dependencies, output, true) == 0;
            }

            // Windows logic: Deploys portable Python for the TTS stack.
            if (_environmentManager.IsWindows)
            {
                string pythonExe = Path.Combine(envPath, "python.exe");

                if (!File.Exists(pythonExe))
                {
                    if (!await _downloadManager.DownloadFileAsync(pythonUrl, envPath, "python-embed.zip")) return false;
                    await _downloadManager.DownloadFileAsync("https://bootstrap.pypa.io/get-pip.py", envPath, "get-pip.py");

                    string[] pthFiles = Directory.GetFiles(envPath, "python*._pth");
                    if (pthFiles.Length > 0)
                    {
                        string content = File.ReadAllText(pthFiles[0]).Replace("#import site", "import site");
                        File.WriteAllText(pthFiles[0], content);
                    }

                    OS.Execute(pythonExe, new string[] { Path.Combine(envPath, "get-pip.py") }, new Godot.Collections.Array(), true);
                }

                var output = new Godot.Collections.Array();
                int pipExit = OS.Execute(pythonExe, new string[] { "-m", "pip", "install", "websockets", "soundfile", "numpy", "kokoro-onnx" }, output, true);
                return pipExit == 0;
            }

            return true;
        }
    }
}