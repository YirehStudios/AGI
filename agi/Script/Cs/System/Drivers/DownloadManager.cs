using Godot;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Logic.Network
{
    /// <summary>
    /// Gestiona la descarga asíncrona de activos y su posterior descompresión.
    /// Implementa soporte para aceleración mediante aria2c y extracción nativa de archivos comprimidos.
    /// </summary>
    public partial class DownloadManager : Node
    {
        [Signal]
        public delegate void DownloadProgressEventHandler(string fileName, float percentage);

        [Signal]
        public delegate void DownloadCompletedEventHandler(string fileName, bool success);

        /// <summary>
        /// Verifica la presencia del binario aria2c en las variables de entorno del sistema.
        /// </summary>
        private bool CheckAria2Availability()
        {
            Godot.Collections.Array output = new Godot.Collections.Array();
            int exitCode = OS.Execute("which", new string[] { "aria2c" }, output, true);
            return exitCode == 0;
        }

        /// <summary>
        /// Ejecuta el flujo de trabajo de descarga y extracción en un hilo secundario para preservar la reactividad de la interfaz.
        /// </summary>
        public async Task<bool> DownloadFileAsync(string url, string destinationFolder, string fileName)
        {
            // Paso 1: Inicialización y normalización de rutas.
            url = url.Trim();
            bool hasAria2 = CheckAria2Availability();
            string globalDestination = ProjectSettings.GlobalizePath(destinationFolder);

            if (!Directory.Exists(globalDestination))
            {
                Directory.CreateDirectory(globalDestination);
            }

            string filePath = Path.Combine(globalDestination, fileName);

            // Paso 2 y 3: Ejecución de transferencia de datos y procesamiento de archivos en subproceso.
            bool finalSuccess = await Task.Run(async () =>
            {
                bool downloadSuccess = false;
                try
                {
                    if (hasAria2)
                    {
                        // Configuración del proceso aria2c para descarga segmentada y multi-hilo.
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

                        // Captura y parseo de la salida estándar para reportar el progreso de descarga.
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
                        // Implementación de respaldo mediante HttpClient en caso de ausencia de aria2c.
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
                    GD.PrintErr($"DownloadManager: Fallo en la transferencia de {fileName}. Detalle: {ex.Message}");
                    downloadSuccess = false;
                }

                // Paso 3: EXTRACCIÓN Y PROCESAMIENTO POST-DESCARGA.
                if (downloadSuccess)
                {
                    try
                    {
                        if (fileName.EndsWith(".tar.gz") || fileName.EndsWith(".tar.bz2") || fileName.EndsWith(".zip"))
                        {
                            CallDeferred(Godot.GodotObject.MethodName.EmitSignal, SignalName.DownloadProgress, fileName + " (Extrayendo...)", 99f);

                            // Gestión de extracción nativa para el formato ZIP mediante la librería System.IO.Compression.
                            if (fileName.EndsWith(".zip"))
                            {
                                ZipFile.ExtractToDirectory(filePath, globalDestination, true);
                                GD.Print($"DownloadManager: Extracción nativa de {fileName} completada.");
                            }
                            else
                            {
                                // Delegación a utilidades del sistema operativo para formatos TAR comprimidos.
                                using Process extractProcess = new Process();
                                extractProcess.StartInfo.UseShellExecute = false;
                                extractProcess.StartInfo.CreateNoWindow = true;
                                extractProcess.StartInfo.FileName = "tar";

                                if (fileName.EndsWith(".tar.gz"))
                                {
                                    extractProcess.StartInfo.Arguments = $"-xzf \"{filePath}\" -C \"{globalDestination}\"";
                                }
                                else if (fileName.EndsWith(".tar.bz2"))
                                {
                                    extractProcess.StartInfo.Arguments = $"-xjf \"{filePath}\" -C \"{globalDestination}\"";
                                }

                                extractProcess.Start();
                                extractProcess.WaitForExit();

                                if (extractProcess.ExitCode != 0) 
                                {
                                    throw new Exception("El comando tar reportó un código de salida no exitoso.");
                                }
                                
                                GD.Print($"DownloadManager: Extracción vía shell de {fileName} finalizada.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"DownloadManager: Error crítico durante la descompresión de {fileName}. Detalle: {ex.Message}");
                        downloadSuccess = false;
                    }
                }
                
                return downloadSuccess;
            });

            // Paso 4: Notificación de finalización al hilo de ejecución de Godot.
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