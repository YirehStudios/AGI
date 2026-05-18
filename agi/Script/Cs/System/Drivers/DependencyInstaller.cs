using Godot;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Logic.System.Drivers
{
    /// <summary>
    /// Instala dependencias lanzando un script maestro generado dinámicamente.
    /// Detecta hardware y la distribución de Linux para automatizar la configuración.
    /// </summary>
    public partial class DependencyInstaller : Node
    {
        private dynamic _environmentManager;

        /// <summary>
        /// Inicializa el componente recuperando la instancia del gestor de entorno desde el árbol de nodos de Godot.
        /// </summary>
        public override void _Ready()
        {
            _environmentManager = GetNode("/root/EnvironmentManager");
        }

        /// <summary>
        /// Performs an asynchronous audit of operating system dependencies.
        /// Evaluates the presence of network tools, graphics acceleration, and the uv package manager.
        /// Implements a guard clause to skip validation on platforms incompatible with Linux shell subsystems.
        /// </summary>
        /// <returns>
        /// A tuple containing readiness status (IsReady), the resolution command (RequiredCommand), and the audit log (AuditLog).
        /// </returns>
        public async Task<(bool IsReady, string RequiredCommand, string AuditLog)> AuditSystemDependenciesAsync()
        {
            return await Task.Run(() =>
            {
                // Validation of current execution environment to prevent package management on incompatible OS or modes.
                if (_environmentManager.IsWindows || _environmentManager.IsAndroid || _environmentManager.IsUIOnlyMode)
                {
                    return (true, string.Empty, string.Empty);
                }

                // Execution of binary audit for essential tools. 
                // Legacy Python verification has been replaced by the 'uv' package manager audit.
                bool hasAria2 = CheckCommandExists("aria2c");
                bool hasVulkan = CheckCommandExists("vulkaninfo");
                bool hasEspeak = CheckCommandExists("espeak-ng");
                bool hasUv = CheckCommandExists("uv");

                // Evaluation of necessity states for missing components on the host system.
                bool needsAria2 = !hasAria2;
                bool needsVulkan = !hasVulkan;
                bool needsEspeak = !hasEspeak;
                bool needsUv = !hasUv;

                // Early exit if all native infrastructure and package manager requirements are satisfied.
                if (!needsAria2 && !needsVulkan && !needsEspeak && !needsUv)
                {
                    return (true, string.Empty, "> Todos los subsistemas operativos C++ y nativos están en línea y funcionales.");
                }

                // Compilation of audit metadata for technical feedback regarding missing dependencies.
                string missingLog = "> Análisis completado. Se detectaron dependencias base faltantes:\n";
                if (needsAria2) missingLog += "- Gestor de descargas acelerado (aria2c)\n";
                if (needsVulkan) missingLog += "- Aceleración gráfica (Vulkan Tools)\n";
                if (needsEspeak) missingLog += "- Diccionarios fonéticos para síntesis (espeak-ng)\n";
                if (needsUv) missingLog += "- Ultra-fast Python package manager (uv)\n";
                missingLog += "\n> Generando script ligero de resolución automática...";

                // Definition of parameters for automation script persistence in user storage.
                string scriptPath = ProjectSettings.GlobalizePath("user://instalar_dependencias.sh");
                string scriptContent = "#!/bin/bash\nset -e\n\n";
                scriptContent += "echo '============================================'\n";
                scriptContent += "echo '  Instalador Ligero de AGI (Fedora/Linux)'\n";
                scriptContent += "echo '============================================'\n\n";

                // Universal installation for the uv package manager using the Astral bootstrap script.
                if (needsUv)
                {
                    scriptContent += "echo '-> Installing uv package manager...'\n";
                    scriptContent += "curl -LsSf https://astral.sh/uv/install.sh | sh\n";
                    scriptContent += "source $HOME/.cargo/env\n\n";
                }

                // Native package manager detection for OS-specific dependency resolution.
                bool hasApt = CheckCommandExists("apt-get");
                bool hasDnf = CheckCommandExists("dnf");
                bool hasPacman = CheckCommandExists("pacman");

                // Dynamic construction of installation commands based on identified manager and necessity flags.
                // Legacy Python and pip packages have been removed from the installation strings.
                {
                    scriptContent += "echo '-> Instalando dependencias de red, aceleración Vulkan y entornos de ejecución...'\n";

                    if (hasApt)
                    {
                        scriptContent += $"sudo apt-get update && sudo apt-get install -y {(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "mesa-vulkan-drivers vulkan-tools " : "")}{(needsEspeak ? "espeak-ng " : "")}\n";
                    }
                    else if (hasDnf)
                    {
                        scriptContent += $"sudo dnf install -y {(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "mesa-vulkan-drivers vulkan-tools " : "")}{(needsEspeak ? "espeak-ng " : "")}\n";
                    }
                    else if (hasPacman)
                    {
                        scriptContent += $"sudo pacman -S --noconfirm {(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "vulkan-radeon vulkan-intel vulkan-tools " : "")}{(needsEspeak ? "espeak-ng " : "")}\n";
                    }
                    scriptContent += "\n";
                }

                scriptContent += "echo '============================================'\n";
                scriptContent += "echo '¡Todo listo! Cierra esta terminal y reinicia tu app.'\n";

                // Script serialization and execution privilege assignment via chmod.
                global::System.IO.File.WriteAllText(scriptPath, scriptContent);
                OS.Execute("chmod", new string[] { "+x", scriptPath }, new Godot.Collections.Array(), true);

                string finalCommand = $"bash \"{scriptPath}\"";

                return (false, finalCommand, missingLog);
            });
        }

        /// <summary>
        /// Verifica la disponibilidad del módulo venv dentro del intérprete de Python 3 ejecutando un comando de importación.[cite: 3]
        /// </summary>
        /// <returns>Retorna true si el proceso de importación finaliza con un código de salida exitoso (0).[cite: 3]</returns>
        private static bool CheckPythonVenv()
        {
            // Instancia un arreglo para la salida y ejecuta el intérprete de Python con la instrucción de importación del módulo específico.[cite: 3]
            var output = new Godot.Collections.Array();
            int exitCode = OS.Execute("python3", new string[] { "-c", "import venv" }, output, true);
            return exitCode == 0;
        }

        /// <summary>
        /// Valida la existencia de un comando ejecutable en el PATH del sistema operativo.
        /// </summary>
        /// <param name="command">Nombre del comando a verificar.</param>
        /// <returns>True si el comando está disponible, de lo contrario False.</returns>
        private static bool CheckCommandExists(string command)
        {
            var output = new Godot.Collections.Array();
            int exitCode = OS.Execute("which", new string[] { command }, output, true);
            return exitCode == 0;
        }
    }
}