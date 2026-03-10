using Godot;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Logic.System.Drivers
{
    /// <summary>
    /// Handles the automated installation of system-level dependencies securely.
    /// Redirects process streams to Godot signals for real-time UI logging.
    /// </summary>
    public partial class DependencyInstaller : Node
    {
        [Signal]
        public delegate void TerminalOutputReceivedEventHandler(string logLine);

        [Signal]
        public delegate void InstallationCompletedEventHandler(bool success);

        /// <summary>
        /// Executes the installation script using elevated privileges and captures the output streams asynchronously.
        /// </summary>
        public async Task BeginInstallationAsync()
        {
            bool installationSuccess = false;
            
            // Obtiene el identificador del usuario del sistema operativo activo mediante la resolución global de .NET
            string currentUser = global::System.Environment.UserName;

            await Task.Run(() =>
            {
                // Define la ruta absoluta en el directorio temporal del sistema resolviendo explícitamente System.IO
                string tempScriptPath = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "agi_install.sh");

                try
                {
                    // Interpolación de cadenas para la inserción segura del usuario evaluado en el script bash
                    string installScript = $@"
                    echo 'Starting dependency checks...'
                    ARIA2_FALLBACK=0

                    echo 'Checking for aria2...'
                    if ! command -v aria2c >/dev/null 2>&1; then
                        echo 'aria2 not found. Attempting installation...'
                        if command -v apt-get >/dev/null 2>&1; then apt-get update && apt-get install -y aria2
                        elif command -v dnf >/dev/null 2>&1; then dnf install -y aria2
                        elif command -v pacman >/dev/null 2>&1; then pacman -S --noconfirm aria2
                        else 
                            echo 'Could not determine package manager for aria2.'
                            ARIA2_FALLBACK=1
                        fi
                    else
                        echo 'aria2 is already installed.'
                    fi

                    echo 'Checking for Docker...'
                    if ! command -v docker >/dev/null 2>&1; then
                        echo 'Docker not found. Attempting installation...'
                        if command -v apt-get >/dev/null 2>&1; then apt-get update && apt-get install -y docker.io
                        elif command -v dnf >/dev/null 2>&1; then dnf install -y docker
                        elif command -v pacman >/dev/null 2>&1; then pacman -S --noconfirm docker
                        else curl -fsSL https://get.docker.com | sh
                        fi
                        
                        systemctl enable --now docker
                        usermod -aG docker {currentUser}
                        echo 'Docker installed successfully. A system reboot may be required for group permissions.'
                    else
                        echo 'Docker is already installed.'
                    fi

                    if [ $ARIA2_FALLBACK -eq 1 ]; then
                        echo 'Warning: aria2 installation failed. Downloads will require fallback mechanisms.'
                    fi
                    
                    echo 'Dependency verification complete.'
                    ";

                    // Escribe los bytes completos del script interpolado al disco forzando la ruta global
                    global::System.IO.File.WriteAllText(tempScriptPath, installScript);
                    
                    // Invoca al sistema operativo para asignar permisos de ejecución mediante el binario chmod
                    Godot.OS.Execute("chmod", new string[] { "+x", tempScriptPath }, new Godot.Collections.Array());

                    // Configura los parámetros de elevación de privilegios apuntando al archivo temporal
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = "pkexec",
                        Arguments = tempScriptPath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using Process process = new Process { StartInfo = startInfo };

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            CallDeferred(MethodName.EmitTerminalLog, e.Data);
                        }
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            CallDeferred(MethodName.EmitTerminalLog, $"[ERROR] {e.Data}");
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    installationSuccess = process.ExitCode == 0;
                }
                catch (Exception ex)
                {
                    CallDeferred(MethodName.EmitTerminalLog, $"[FATAL ERROR] {ex.Message}");
                    installationSuccess = false;
                }
                finally
                {
                    // Garantiza la limpieza del sistema de archivos comprobando y eliminando mediante la directiva global
                    if (global::System.IO.File.Exists(tempScriptPath))
                    {
                        try
                        {
                            global::System.IO.File.Delete(tempScriptPath);
                        }
                        catch (Exception cleanupEx)
                        {
                            CallDeferred(MethodName.EmitTerminalLog, $"[CLEANUP ERROR] Failed to remove temp script: {cleanupEx.Message}");
                        }
                    }
                }
            });

            CallDeferred(MethodName.EmitCompletionSignal, installationSuccess);
        }

        /// <summary>
        /// Marshals the log string emission back to the main Godot thread.
        /// </summary>
        /// <param name="message">The text line to emit.</param>
        private void EmitTerminalLog(string message)
        {
            EmitSignal(SignalName.TerminalOutputReceived, message);
        }

        /// <summary>
        /// Marshals the completion signal back to the main Godot thread.
        /// </summary>
        /// <param name="success">The result of the installation process.</param>
        private void EmitCompletionSignal(bool success)
        {
            EmitSignal(SignalName.InstallationCompleted, success);
        }
    }
}