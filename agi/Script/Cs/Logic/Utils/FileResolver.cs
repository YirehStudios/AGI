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
        /// <summary>
        /// Searches for an executable file within the specified directory recursively.
        /// </summary>
        /// <param name="directoryPath">The root directory to start the search.</param>
        /// <param name="isWindows">Boolean indicating if the host OS is Windows.</param>
        /// <param name="fallbackPrefix">The prefix expected in the executable filename.</param>
        /// <returns>The full path of the found executable, or an empty string if not found.</returns>
        public static string FindExecutable(string directoryPath, bool isWindows, string fallbackPrefix = "")
        {
            try
            {
                GD.Print($"[FileResolver] Searching for executable '{fallbackPrefix}' in: {directoryPath}");
                if (!Directory.Exists(directoryPath)) return string.Empty;

                var directoryInfo = new DirectoryInfo(directoryPath);
                // Perform a recursive search through all subdirectories
                var files = directoryInfo.GetFiles("*", SearchOption.AllDirectories);

                string result;
                if (isWindows)
                {
                    result = files
                        .Where(f => f.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => f.Name.Contains(fallbackPrefix, StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault()?.FullName ?? string.Empty;
                }
                else
                {
                    result = files
                        .Where(f => string.IsNullOrEmpty(f.Extension) && f.Name.Contains(fallbackPrefix))
                        .FirstOrDefault()?.FullName ?? string.Empty;
                }

                if (!string.IsNullOrEmpty(result))
                {
                    GD.Print($"[FileResolver] -> Found: {result}");
                }
                else
                {
                    GD.PrintErr($"[FileResolver] Executable with prefix '{fallbackPrefix}' not found in {directoryPath}");
                }

                return result;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[FileResolver] Error searching for executable in {directoryPath}: {ex.Message}");
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