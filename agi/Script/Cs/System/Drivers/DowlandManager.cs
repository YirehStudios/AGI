using Godot;
using System;
using System.IO;
using System.Threading.Tasks;


namespace Logic.Network
{
    /// <summary>
    /// Gestiona la adquisición de recursos externos y la extracción de paquetes comprimidos.
    /// Archivo actualizado para aislar la lógica de red y agregar soporte nativo de extracción para archivos .tar.bz2.
    /// </summary>
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

            if (exitCode == 0) return true;

            if (OS.GetName() == "Linux")
            {
                GD.Print("DownloadManager: aria2c no encontrado. Intentando instalación automática...");
                int installExitCode = OS.Execute("pkexec", new string[] { "apt-get", "install", "-y", "aria2" }, output, true);
                if (installExitCode == 0)
                {
                    GD.Print("DownloadManager: aria2c instalado correctamente.");
                    return true;
                }
                else
                {
                    GD.PrintErr("DownloadManager: Falló la instalación de aria2c. Usando HttpClient como respaldo.");
                }
            }
            return false;
        }

        /// <summary>
        /// Ejecuta la descarga. Utiliza aria2c con la directiva --continue=true para robustez ante fallos de red.
        /// Aplica extracción automática (tar/unzip/bz2) al finalizar si corresponde y verifica el resultado.
        /// </summary>
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
                    bool downloadSuccess = false;

                    // Robustez de Red (aria2c continuation support)
                    if (CheckAria2Availability())
                    {
                        GD.Print($"DownloadManager: Utilizando aria2c para {fileName}");
                        
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

                        downloadSuccess = (exitCode == 0);
                    }
                    else
                    {
                        GD.Print($"DownloadManager: Utilizando HttpClient fallback para {fileName}");
                        
                        using global::System.Net.Http.HttpClient client = new global::System.Net.Http.HttpClient();
                        using global::System.Net.Http.HttpResponseMessage response = await client.GetAsync(url, global::System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        using Stream contentStream = await response.Content.ReadAsStreamAsync();
                        using FileStream fileStream = new FileStream(filePath, FileMode.Create, global::System.IO.FileAccess.Write, FileShare.None, 8192, true);

                        await contentStream.CopyToAsync(fileStream);
                        downloadSuccess = true;
                    }

                    // Extracción de Paquetes
                    if (downloadSuccess)
                    {
                        // Estructura el nombre esperado sustrayendo las extensiones de compresión (incluyendo .tar.bz2)
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
                            // Usar -xjf para archivos bzip2 como es requerido por los paquetes de Sherpa-ONNX
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

                        // Verificación de post-procesamiento para todas las extensiones administradas
                        if (fileName.EndsWith(".tar.gz") || fileName.EndsWith(".zip") || fileName.EndsWith(".tar.bz2"))
                        {
                            if (!File.Exists(expectedExtractedPath) && !Directory.Exists(expectedExtractedPath))
                            {
                                GD.PrintErr($"DownloadManager: Validación fallida. No se detectó la estructura '{expectedExtractedName}' extraída.");
                                downloadSuccess = false; // Revierte el estado de éxito debido a la falla en la extracción real
                            }
                            else
                            {
                                GD.Print($"DownloadManager: Validación exitosa post-extracción del recurso {expectedExtractedName}.");
                            }
                        }
                    }

                    CallDeferred(MethodName.EmitSignal, SignalName.DownloadCompleted, fileName, downloadSuccess);
                    return downloadSuccess;
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"DownloadManager: Error crítico en descarga/extracción de {fileName}. Excepción: {ex.Message}");
                    CallDeferred(MethodName.EmitSignal, SignalName.DownloadCompleted, fileName, false);
                    return false;
                }
            });
        }
    }
}