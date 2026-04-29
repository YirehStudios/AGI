using Godot;
using System.IO;
using System.Threading.Tasks;
using Logic.Utils;

namespace Logic.System.Drivers
{
    /// <summary>
    /// Manages the lifecycle of engine packages, including downloading and verifying file system permissions.
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
        /// Validates if the required engine executable is present and ready.
        /// </summary>
        public bool IsEngineReady(string enginePrefix)
        {
            string path = FileResolver.FindExecutable(_environmentManager.BinPath, _environmentManager.IsWindows, enginePrefix);
            return !string.IsNullOrEmpty(path);
        }

        /// <summary>
        /// Downloads and prepares the engine, including recursive search and Linux permission configuration.
        /// </summary>
        public async Task<bool> DownloadAndPrepareEngineAsync(string url, string fileName, string enginePrefix)
        {
            if (_environmentManager.IsUIOnlyMode || _environmentManager.IsAndroid)
            {
                return true;
            }

            bool downloadSuccess = await _downloadManager.DownloadFileAsync(url, _environmentManager.BinPath, fileName);
            if (!downloadSuccess)
            {
                return false;
            }

            string subDir = FileResolver.FindDirectoryByPrefix(_environmentManager.BinPath, enginePrefix);
            string targetFolder = string.IsNullOrEmpty(subDir) ? _environmentManager.BinPath : subDir;

            string executablePath = FileResolver.FindExecutable(targetFolder, _environmentManager.IsWindows, enginePrefix);

            GD.Print($"[PackageManager] Evaluating path for {enginePrefix}: {executablePath}");
            if (string.IsNullOrEmpty(executablePath))
            {
                GD.PrintErr($"[PackageManager] ERROR: Executable for '{enginePrefix}' not found within {targetFolder}. Aborting.");
                return false;
            }

            if (_environmentManager.IsLinux)
            {
                // Assign execute permissions to the main binary
                OS.Execute("chmod", new string[] { "+x", executablePath });

                if (enginePrefix.Contains("sherpa"))
                {
                    string libPath = Path.Combine(targetFolder, "lib");
                    if (Directory.Exists(libPath))
                    {
                        string[] libFiles = Directory.GetFiles(libPath, "*.so*");
                        GD.Print($"[PackageManager] Applying read permissions to libraries in {libPath}");
                        foreach (string libFile in libFiles)
                        {
                            OS.Execute("chmod", new string[] { "a+r", libFile });
                        }
                        GD.Print($"[PackageManager] Library permissions applied successfully.");
                    }
                }
            }

            GD.Print($"[PackageManager] -> Engine '{enginePrefix}' prepared successfully.");
            return true;
        }
    }
}