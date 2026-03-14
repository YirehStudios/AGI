using Godot;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Logic.Network
{
    /// <summary>
    /// Gestiona la adquisición de recursos externos y la extracción de paquetes comprimidos.
    /// Emplea un modelo de arquitectura híbrida, asegurando que las transferencias de red se ejecuten en 
    /// subprocesos dedicados para emitir eventos de progreso, mientras delega las verificaciones de sistema 
    /// y las extracciones al hilo principal mediante OS.Execute para prevenir interbloqueos en el motor de Godot.
    /// </summary>
    public partial class DownloadManager : Node
    {
        [Signal]
        public delegate void DownloadProgressEventHandler(string fileName, float percentage);

        [Signal]
        public delegate void DownloadCompletedEventHandler(string fileName, bool success);

        /// <summary>
        /// Evalúa en el hilo principal la disponibilidad del ejecutable aria2c invocando al sistema operativo.
        /// </summary>
        /// <returns>Verdadero si el binario existe y es accesible en la ruta del sistema.</returns>
        private bool CheckAria2Availability()
        {
            Godot.Collections.Array output = new Godot.Collections.Array();
            int exitCode = OS.Execute("which", new string[] { "aria2c" }, output, true);
            return exitCode == 0;
        }

        /// <summary>
        /// Coordina el ciclo de vida de una descarga externa de forma asíncrona.
        /// Separa el análisis del entorno (hilo principal), la transferencia de bytes (subproceso)
        /// y la descompresión de datos (hilo principal) para mantener fluidez gráfica (60 FPS).
        /// </summary>
        /// <param name="url">Ubicación de red absoluta del recurso objetivo.</param>
        /// <param name="destinationFolder">Directorio virtual interno destino (e.g. "user://models").</param>
        /// <param name="fileName">Nombre de archivo forzado para la estandarización local.</param>
        public async Task<bool> DownloadFileAsync(string url, string destinationFolder, string fileName)
        {
            // Paso 1: Inicialización en el Hilo Principal
            url = url.Trim();
            bool hasAria2 = CheckAria2Availability();
            string globalDestination = ProjectSettings.GlobalizePath(destinationFolder);

            if (!Directory.Exists(globalDestination))
            {
                Directory.CreateDirectory(globalDestination);
            }

            string filePath = Path.Combine(globalDestination, fileName);

            // Paso 2: Ejecución de Transferencia en Subproceso
            bool downloadSuccess = await Task.Run(async () =>
            {
                try
                {
                    if (hasAria2)
                    {
                        GD.Print($"DownloadManager: Utilizando aria2c para {fileName}");
                        
                        using Process process = new Process();
                        process.StartInfo.FileName = "aria2c";
                        
                        process.StartInfo.ArgumentList.Add("-x");
                        process.StartInfo.ArgumentList.Add("4");
                        process.StartInfo.ArgumentList.Add("-s");
                        process.StartInfo.ArgumentList.Add("4");
                        // Disfraz de Google Chrome para engañar al 403
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
                                    CallDeferred(MethodName.EmitSignal, SignalName.DownloadProgress, fileName, percentage);
                                }
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();

                        return (process.ExitCode == 0);
                    }
                    else
                    {
                        GD.Print($"DownloadManager: Utilizando HttpClient fallback para {fileName}");
                        
                        using global::System.Net.Http.HttpClient client = new global::System.Net.Http.HttpClient();
                        
                        // --- NUEVO: Disfraz y Mirror para evitar el 403 ---
                        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                        client.DefaultRequestHeaders.Add("Accept", "*/*");
                        // --------------------------------------------------

                        // NUEVO: Usamos safeUrl en lugar de url
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

                        return true;
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"DownloadManager: Error en transferencia de {fileName}. Excepción: {ex.Message}");
                    return false;
                }
            });

            // Paso 3: Extracción de Paquetes Segura en Hilo Principal
            if (downloadSuccess)
            {
                try
                {
                    string expectedExtractedName = fileName.Replace(".tar.gz", "").Replace(".zip", "").Replace(".tar.bz2", "");
                    string expectedExtractedPath = Path.Combine(globalDestination, expectedExtractedName);

                    if (fileName.EndsWith(".tar.gz"))
                    {
                        GD.Print($"DownloadManager: Extrayendo paquete tar.gz {fileName}...");
                        Godot.Collections.Array tarOutput = new Godot.Collections.Array();
                        int tarExitCode = OS.Execute("tar", new string[] { "-xzf", filePath, "-C", globalDestination }, tarOutput, true);
                        if (tarExitCode != 0) throw new Exception("La extracción vía tar falló.");
                    }
                    else if (fileName.EndsWith(".tar.bz2"))
                    {
                        GD.Print($"DownloadManager: Extrayendo paquete tar.bz2 {fileName}...");
                        Godot.Collections.Array tarOutput = new Godot.Collections.Array();
                        int tarExitCode = OS.Execute("tar", new string[] { "-xjf", filePath, "-C", globalDestination }, tarOutput, true);
                        if (tarExitCode != 0) throw new Exception("La extracción de bz2 vía tar falló.");
                    }
                    else if (fileName.EndsWith(".zip"))
                    {
                        GD.Print($"DownloadManager: Extrayendo archivo zip {fileName}...");
                        Godot.Collections.Array zipOutput = new Godot.Collections.Array();
                        int zipExitCode = OS.Execute("unzip", new string[] { "-o", filePath, "-d", globalDestination }, zipOutput, true);
                        if (zipExitCode != 0) throw new Exception("La extracción vía unzip falló.");
                    }

                    if (fileName.EndsWith(".tar.gz") || fileName.EndsWith(".zip") || fileName.EndsWith(".tar.bz2"))
                    {
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

            // Paso 4: Finalización y Notificación
            EmitSignal(SignalName.DownloadCompleted, fileName, downloadSuccess);
            return downloadSuccess;
        }
    }
}