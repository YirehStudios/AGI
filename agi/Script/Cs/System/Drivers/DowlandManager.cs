using Godot;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Logic.Network
{
    /// <summary>
    /// Manages external resource acquisition via terminal execution using aria2c or native fallback.
    /// </summary>
    public partial class DownloadManager : Node
    {
        [Signal]
        public delegate void DownloadProgressEventHandler(string fileName, float percentage);

        [Signal]
        public delegate void DownloadCompletedEventHandler(string fileName, bool success);

        /// <summary>
        /// Validates the presence of aria2c. If missing on Linux, attempts graphical installation via pkexec.
        /// </summary>
        private bool CheckAria2Availability()
        {
            Godot.Collections.Array output = new Godot.Collections.Array();
            int exitCode = OS.Execute("which", new string[] { "aria2c" }, output, true);

            if (exitCode == 0) return true;

            if (OS.GetName() == "Linux")
            {
                GD.Print("DownloadManager: aria2c not found. Attempting automatic installation via pkexec...");
                int installExitCode = OS.Execute("pkexec", new string[] { "apt-get", "install", "-y", "aria2" }, output, true);
                if (installExitCode == 0)
                {
                    GD.Print("DownloadManager: aria2c successfully installed.");
                    return true;
                }
                else
                {
                    GD.PrintErr("DownloadManager: Failed to install aria2c via pkexec. Falling back to native HttpClient.");
                }
            }
            return false;
        }

        /// <summary>
        /// Executes aria2c binary asynchronously to download a specified file.
        /// Falls back to HttpClient if aria2 is unavailable.
        /// Evaluates the exit code to determine the success state.
        /// </summary>
        /// <param name="url">The direct URL of the resource.</param>
        /// <param name="destinationFolder">The target local directory.</param>
        /// <param name="fileName">The specific output filename.</param>
        /// <returns>A boolean indicating if the download operation succeeded.</returns>
        public async Task<bool> DownloadFileAsync(string url, string destinationFolder, string fileName)
        {
            return await Task.Run(async () => 
            {
                try
                {
                    string globalDestination = ProjectSettings.GlobalizePath(destinationFolder);
                    
                    if (!Directory.Exists(globalDestination))
                    {
                        Directory.CreateDirectory(globalDestination);
                    }

                    string filePath = Path.Combine(globalDestination, fileName);

                    if (CheckAria2Availability())
                    {
                        GD.Print($"DownloadManager: Utilizing aria2c for {fileName}");
                        string[] args = new string[] 
                        { 
                            "-x", "16", 
                            "-s", "16", 
                            "--continue=true", 
                            "-d", globalDestination, 
                            "-o", fileName, 
                            url 
                        };

                        Godot.Collections.Array output = new Godot.Collections.Array();
                        int exitCode = OS.Execute("aria2c", args, output, true);

                        bool success = (exitCode == 0);
                        
                        CallDeferred(MethodName.EmitSignal, SignalName.DownloadCompleted, fileName, success);
                        
                        return success;
                    }
                    else
                    {
                        GD.Print($"DownloadManager: Utilizing HttpClient fallback for {fileName}");
                        
                        // CORRECCIÓN CS0104: Declaración explícita para evitar conflicto con Godot.HttpClient
                        using System.Net.Http.HttpClient client = new System.Net.Http.HttpClient();
                        using System.Net.Http.HttpResponseMessage response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        using Stream contentStream = await response.Content.ReadAsStreamAsync();
                        
                        // CORRECCIÓN CS0104: Declaración explícita para evitar conflicto con Godot.FileAccess
                        using FileStream fileStream = new FileStream(filePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.None, 8192, true);

                        await contentStream.CopyToAsync(fileStream);

                        CallDeferred(MethodName.EmitSignal, SignalName.DownloadCompleted, fileName, true);
                        
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"DownloadManager: Critical failure while downloading {fileName}. Exception: {ex.Message}");
                    CallDeferred(MethodName.EmitSignal, SignalName.DownloadCompleted, fileName, false);
                    return false;
                }
            });
        }
    }
}