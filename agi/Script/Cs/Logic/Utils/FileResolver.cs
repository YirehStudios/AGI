using System;
using System.IO;
using System.Linq;
using Godot;

namespace Logic.Utils
{
    /// <summary>
    /// Provides utility methods for file system operations, including searching for executables and model files.
    /// </summary>
    public static class FileResolver
    {
        /// <param name="directoryPath">The root directory to start the search.</param>
        /// <param name="isWindows">Boolean indicating if the host OS is Windows.</param>
        /// <param name="fallbackPrefix">The prefix expected in the executable filename.</param>
        /// <returns>The full path of the found executable, or an empty string if not found.</returns>
        /// <summary>
        /// Searches for an executable file within the specified directory recursively.
        /// Prioritizes exact filename matches before falling back to partial string containment.
        /// </summary>
        public static string FindExecutable(string directoryPath, bool isWindows, string fallbackPrefix = "")
        {
            try
            {
                // Inicializa el escaneo recursivo de todos los archivos presentes en el árbol de directorios indicado.
                GD.Print($"[FileResolver] Searching for executable '{fallbackPrefix}' in: {directoryPath}");
                if (!Directory.Exists(directoryPath)) return string.Empty;

                var directoryInfo = new DirectoryInfo(directoryPath);
                var files = directoryInfo.GetFiles("*", SearchOption.AllDirectories);

                FileInfo foundFile = null;

                if (isWindows)
                {
                    // Ejecuta una búsqueda de alta prioridad basada en la igualdad exacta del nombre con la extensión .exe.
                    string targetName = fallbackPrefix + ".exe";
                    foundFile = files.FirstOrDefault(f => f.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
                    
                    // Si no hay coincidencia exacta, aplica una búsqueda por contención de prefijo como método de respaldo.
                    if (foundFile == null)
                    {
                        foundFile = files
                            .Where(f => f.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => f.Name.Contains(fallbackPrefix, StringComparison.OrdinalIgnoreCase))
                            .FirstOrDefault();
                    }
                }
                else
                {
                    // Busca coincidencias exactas en sistemas Linux donde los binarios no suelen poseer extensión.
                    foundFile = files.FirstOrDefault(f => f.Name.Equals(fallbackPrefix, StringComparison.OrdinalIgnoreCase));
                    
                    // Realiza una búsqueda secundaria filtrando archivos sin extensión que contengan el identificador.
                    if (foundFile == null)
                    {
                        foundFile = files
                            .Where(f => string.IsNullOrEmpty(f.Extension) && f.Name.Contains(fallbackPrefix))
                            .FirstOrDefault();
                    }
                }

                string result = foundFile?.FullName ?? string.Empty;

                // Valida y reporta el hallazgo para la depuración del sistema de drivers.
                if (!string.IsNullOrEmpty(result)) GD.Print($"[FileResolver] -> Found: {result}");
                else GD.PrintErr($"[FileResolver] Executable '{fallbackPrefix}' not found.");

                return result;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[FileResolver] Error: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Finds the largest file with a matching extension in the specified directory.
        /// </summary>
        public static string FindModelFile(string directoryPath, params string[] allowedExtensions)
        {
            try
            {
                if (!Directory.Exists(directoryPath)) return string.Empty;

                var directoryInfo = new DirectoryInfo(directoryPath);
                
                return directoryInfo.GetFiles()
                    .Where(f => allowedExtensions.Any(ext => f.Extension.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(f => f.Length)
                    .FirstOrDefault()?.FullName ?? string.Empty;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[FileResolver] Error searching for model file in {directoryPath}: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Retrieves the first sub-directory that matches the provided prefix.
        /// </summary>
        public static string FindDirectoryByPrefix(string parentDirectory, string prefix)
        {
            try
            {
                if (!Directory.Exists(parentDirectory)) return string.Empty;

                var directoryInfo = new DirectoryInfo(parentDirectory);
                
                return directoryInfo.GetDirectories()
                    .FirstOrDefault(d => d.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?.FullName ?? string.Empty;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[FileResolver] Error searching for directory with prefix {prefix} in {parentDirectory}: {ex.Message}");
                return string.Empty;
            }
        }
    }
}