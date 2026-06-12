using Godot;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Linq;

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
        private static bool CheckAria2Availability()
        {
            Godot.Collections.Array output = new Godot.Collections.Array();
            int exitCode = OS.Execute("which", new string[] { "aria2c" }, output, true);
            return exitCode == 0;
        }

        /// <summary>
        /// Ejecuta la descarga y extracción en un hilo secundario para preservar la reactividad de la interfaz.
        /// </summary>
        public async Task<bool> DownloadFileAsync(string url, string destinationFolder, string fileName)
        {
            url = url.Trim();
            bool hasAria2 = CheckAria2Availability();
            string globalDestination = ProjectSettings.GlobalizePath(destinationFolder);

            if (!Directory.Exists(globalDestination))
            {
                Directory.CreateDirectory(globalDestination);
            }

            string filePath = Path.Combine(globalDestination, fileName);

            bool finalSuccess = await Task.Run(async () =>
            {
                bool downloadSuccess = false;
                try
                {
                    if (hasAria2)
                    {
                        using Process process = new Process();
                        process.StartInfo.FileName = "aria2c";
                        process.StartInfo.ArgumentList.Add("-x");
                        process.StartInfo.ArgumentList.Add("16");
                        process.StartInfo.ArgumentList.Add("-s");
                        process.StartInfo.ArgumentList.Add("16");
                        process.StartInfo.ArgumentList.Add("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                        process.StartInfo.ArgumentList.Add("--header=Accept: */*");
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
                                Match match = MyRegex().Match(e.Data);
                                if (match.Success && float.TryParse(match.Groups[1].Value, out float percentage))
                                {
                                    if (Godot.GodotObject.IsInstanceValid(this) && !this.IsQueuedForDeletion())
                                    {
                                        CallDeferred(Godot.GodotObject.MethodName.EmitSignal, SignalName.DownloadProgress, fileName, percentage);
                                    }
                                }
                                else if (e.Data.Contains("[ERROR]") || e.Data.Contains("Exception:"))
                                {
                                    GD.PrintErr($"[aria2c - {fileName}]: {e.Data}");
                                }
                            }
                        };

                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                GD.PrintErr($"[aria2c - {fileName}]: {e.Data}");
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
                        using global::System.Net.Http.HttpClient client = new global::System.Net.Http.HttpClient();
                        using global::System.Net.Http.HttpResponseMessage response = await client.GetAsync(url, global::System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        long? totalBytes = response.Content.Headers.ContentLength;
                        using Stream contentStream = await response.Content.ReadAsStreamAsync();
                        using FileStream fileStream = new FileStream(filePath, FileMode.Create, global::System.IO.FileAccess.Write, FileShare.None, 8192, true);

                        byte[] buffer = new byte[8192];
                        long totalBytesRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
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
                    GD.PrintErr($"DownloadManager: Fallo en transferencia: {ex.Message}");
                    downloadSuccess = false;
                }

                if (downloadSuccess)
                {
                    try
                    {
                        if (fileName.EndsWith(".tar.gz") || fileName.EndsWith(".tar.bz2") || fileName.EndsWith(".zip"))
                        {
                            CallDeferred(Godot.GodotObject.MethodName.EmitSignal, SignalName.DownloadProgress, fileName + " (Extrayendo...)", 99f);

                            if (fileName.EndsWith(".zip"))
                            {
                                ZipFile.ExtractToDirectory(filePath, globalDestination, true);
                            }
                            else
                            {
                                using Process extractProcess = new Process();
                                extractProcess.StartInfo.UseShellExecute = false;
                                extractProcess.StartInfo.CreateNoWindow = true;
                                extractProcess.StartInfo.FileName = "tar";
                                extractProcess.StartInfo.Arguments = fileName.EndsWith(".tar.gz")
                                    ? $"-xzf \"{filePath}\" -C \"{globalDestination}\""
                                    : $"-xjf \"{filePath}\" -C \"{globalDestination}\"";

                                extractProcess.Start();
                                extractProcess.WaitForExit();
                                if (extractProcess.ExitCode != 0) throw new Exception("Fallo en el comando tar.");
                            }

                            // Ejecuta la normalización de la estructura de directorios y la limpieza de archivos temporales.
                            FlattenDirectoryIfNecessary(globalDestination, filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"DownloadManager: Error en extracción: {ex.Message}");
                        downloadSuccess = false;
                    }
                }
                return downloadSuccess;
            });

            if (Godot.GodotObject.IsInstanceValid(this) && !this.IsQueuedForDeletion())
            {
                CallDeferred(MethodName.EmitSignal, SignalName.DownloadCompleted, fileName, finalSuccess);
            }
            return finalSuccess;
        }

        /// <summary>
        /// Examina la raíz destino y extrae el contenido al nivel superior si se detecta redundancia,
        /// ignorando el archivo comprimido original durante el conteo y procediendo a su eliminación final.
        /// </summary>
        private static void FlattenDirectoryIfNecessary(string targetDirectory, string archivePath)
        {
            try
            {
                if (!Directory.Exists(targetDirectory)) return;

                string[] subdirectories = Directory.GetDirectories(targetDirectory);
                string[] allFiles = Directory.GetFiles(targetDirectory);

                // Filtra el conteo de archivos para ignorar el archivo comprimido descargado.
                string normalizedArchivePath = Path.GetFullPath(archivePath);
                int significantFilesCount = 0;
                foreach (string file in allFiles)
                {
                    if (Path.GetFullPath(file) != normalizedArchivePath)
                    {
                        significantFilesCount++;
                    }
                }

                // Identifica si existe una única carpeta contenedora sin otros archivos significativos en la raíz.
                if (subdirectories.Length == 1 && significantFilesCount == 0)
                {
                    string loneDirectory = subdirectories[0];

                    foreach (string file in Directory.GetFiles(loneDirectory))
                    {
                        string destFile = Path.Combine(targetDirectory, Path.GetFileName(file));
                        if (File.Exists(destFile)) File.Delete(destFile);
                        Directory.Move(file, destFile);
                    }

                    foreach (string dir in Directory.GetDirectories(loneDirectory))
                    {
                        string destDir = Path.Combine(targetDirectory, Path.GetFileName(dir));
                        if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
                        Directory.Move(dir, destDir);
                    }

                    Directory.Delete(loneDirectory, true);
                    GD.Print($"DownloadManager: Estructura de directorios aplanada con éxito en {targetDirectory}.");
                }

                // Elimina el archivo original para liberar almacenamiento tras completar la operación.
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                    GD.Print($"DownloadManager: Limpieza completada. Archivo {Path.GetFileName(archivePath)} eliminado.");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"DownloadManager: Error durante el proceso de aplanamiento o limpieza: {ex.Message}");
            }
        }

        [GeneratedRegex(@"\((\d+)%\)")]
        private static partial Regex MyRegex();
    }
}