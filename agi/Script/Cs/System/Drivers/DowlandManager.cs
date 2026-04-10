using Godot;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Logic.Network
{
    public partial class DownloadManager : Node
    {
        [Signal]
        public delegate void DownloadProgressEventHandler(string fileName, float percentage);

        [Signal]
        public delegate void DownloadCompletedEventHandler(string fileName, bool success);

        private bool CheckAria2Availability()
        {
            Godot.Collections.Array output = new Godot.Collections.Array();
            int exitCode = OS.Execute("which", new string[] { "aria2c" }, output, true);
            return exitCode == 0;
        }

        public async Task<bool> DownloadFileAsync(string url, string destinationFolder, string fileName)
        {
            // Paso 1: Inicialización
            url = url.Trim();
            bool hasAria2 = CheckAria2Availability();
            string globalDestination = ProjectSettings.GlobalizePath(destinationFolder);

            if (!Directory.Exists(globalDestination))
            {
                Directory.CreateDirectory(globalDestination);
            }

            string filePath = Path.Combine(globalDestination, fileName);

            // Paso 2 y 3: TODO EL PROCESO PESADO (Descarga y Extracción) EN EL SUBPROCESO
            bool finalSuccess = await Task.Run(async () =>
            {
                bool downloadSuccess = false;
                try
                {
                    if (hasAria2)
                    {
                        GD.Print($"DownloadManager: Utilizando aria2c para {fileName}");
                        
                        using Process process = new Process();
                        process.StartInfo.FileName = "aria2c";
                        
                        process.StartInfo.ArgumentList.Add("-x");
                        process.StartInfo.ArgumentList.Add("16");
                        process.StartInfo.ArgumentList.Add("-s");
                        process.StartInfo.ArgumentList.Add("16");
                        process.StartInfo.ArgumentList.Add("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                        process.StartInfo.ArgumentList.Add("--header=Accept: */*");
                        process.StartInfo.ArgumentList.Add("--summary-interval=1");
                        process.StartInfo.ArgumentList.Add("--continue=true");
                        process.StartInfo.ArgumentList.Add("-d");
                        process.StartInfo.ArgumentList.Add(globalDestination);
                        process.StartInfo.ArgumentList.Add("-o");
                        process.StartInfo.ArgumentList.Add(fileName);
                        process.StartInfo.ArgumentList.Add(url);

                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;

                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                Match match = Regex.Match(e.Data, @"\((\d+)%\)");
                                if (match.Success && float.TryParse(match.Groups[1].Value, out float percentage))
                                {
                                    if (Godot.GodotObject.IsInstanceValid(this) && !this.IsQueuedForDeletion())
                                    {
                                        try 
                                        {
                                            CallDeferred(Godot.GodotObject.MethodName.EmitSignal, SignalName.DownloadProgress, fileName, percentage);
                                        }
                                        catch (ObjectDisposedException) { }
                                    }
                                }
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();

                        downloadSuccess = (process.ExitCode == 0);
                    }
                    else
                    {
                        GD.Print($"DownloadManager: Utilizando HttpClient fallback para {fileName}");
                        
                        using global::System.Net.Http.HttpClient client = new global::System.Net.Http.HttpClient();
                        
                        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                        client.DefaultRequestHeaders.Add("Accept", "*/*");

                        using global::System.Net.Http.HttpResponseMessage response = await client.GetAsync(url, global::System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        long? totalBytes = response.Content.Headers.ContentLength;
                        
                        using Stream contentStream = await response.Content.ReadAsStreamAsync();
                        using FileStream fileStream = new FileStream(filePath, FileMode.Create, global::System.IO.FileAccess.Write, FileShare.None, 8192, true);

                        byte[] buffer = new byte[8192];
                        long totalBytesRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;

                            if (totalBytes.HasValue)
                            {
                                float percentage = (float)totalBytesRead / totalBytes.Value * 100f;
                                CallDeferred(MethodName.EmitSignal, SignalName.DownloadProgress, fileName, percentage);
                            }
                        }

                        downloadSuccess = true;
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"DownloadManager: Error en transferencia de {fileName}. Excepción: {ex.Message}");
                    downloadSuccess = false;
                }

                // Paso 3: EXTRACCIÓN (AHORA EN EL SUBPROCESO PARA NO CONGELAR LA UI)
                if (downloadSuccess)
                {
                    try
                    {
                        string expectedExtractedName = fileName.Replace(".tar.gz", "").Replace(".zip", "").Replace(".tar.bz2", "");
                        string expectedExtractedPath = Path.Combine(globalDestination, expectedExtractedName);

                        if (fileName.EndsWith(".tar.gz") || fileName.EndsWith(".tar.bz2") || fileName.EndsWith(".zip"))
                        {
                            // Avisamos a la interfaz que estamos extrayendo
                            CallDeferred(Godot.GodotObject.MethodName.EmitSignal, SignalName.DownloadProgress, fileName + " (Extrayendo...)", 99f);

                            using Process extractProcess = new Process();
                            extractProcess.StartInfo.UseShellExecute = false;
                            extractProcess.StartInfo.CreateNoWindow = true;

                            if (fileName.EndsWith(".tar.gz"))
                            {
                                extractProcess.StartInfo.FileName = "tar";
                                extractProcess.StartInfo.Arguments = $"-xzf \"{filePath}\" -C \"{globalDestination}\"";
                            }
                            else if (fileName.EndsWith(".tar.bz2"))
                            {
                                extractProcess.StartInfo.FileName = "tar";
                                extractProcess.StartInfo.Arguments = $"-xjf \"{filePath}\" -C \"{globalDestination}\"";
                            }
                            else if (fileName.EndsWith(".zip"))
                            {
                                extractProcess.StartInfo.FileName = "unzip";
                                extractProcess.StartInfo.Arguments = $"-o \"{filePath}\" -d \"{globalDestination}\"";
                            }

                            extractProcess.Start();
                            extractProcess.WaitForExit();

                            if (extractProcess.ExitCode != 0) throw new Exception("El proceso de extracción falló en el sistema.");

                            if (!File.Exists(expectedExtractedPath) && !Directory.Exists(expectedExtractedPath))
                            {
                                GD.PrintErr($"DownloadManager: Validación fallida. No se detectó la estructura extraída.");
                                downloadSuccess = false;
                            }
                            else
                            {
                                GD.Print("DownloadManager: Validación exitosa post-extracción.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"DownloadManager: Error crítico en extracción de {fileName}. Excepción: {ex.Message}");
                        downloadSuccess = false;
                    }
                }
                
                return downloadSuccess;
            });

            // Paso 4: Finalización y Notificación de vuelta al hilo principal
            if (Godot.GodotObject.IsInstanceValid(this) && !this.IsQueuedForDeletion())
            {
                try
                {
                    CallDeferred(MethodName.EmitSignal, SignalName.DownloadCompleted, fileName, finalSuccess);
                }
                catch (ObjectDisposedException) { }
            }
            return finalSuccess;
        }
    }
}