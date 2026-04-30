using Godot;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Logic.System.Drivers
{
    /// <summary>
    /// Instala dependencias lanzando un script maestro generado dinámicamente.
    /// Detecta hardware y la distribución de Linux para automatizar la configuración.[cite: 4]
    /// </summary>
    public partial class DependencyInstaller : Node
    {
        private dynamic _environmentManager;

        /// <summary>
        /// Inicializa la dependencia recuperando el nodo Autoload del árbol principal.[cite: 4]
        /// </summary>
        public override void _Ready()
        {
            _environmentManager = GetNode("/root/EnvironmentManager");
        }

        /// <summary>
        /// Ejecuta comprobaciones del sistema de forma asíncrona.
        /// Genera y provisiona un script bash limitado a dependencias fundamentales del sistema operativo.[cite: 4]
        /// </summary>
        public async Task<(bool IsReady, string RequiredCommand, string AuditLog)> AuditSystemDependenciesAsync()
        {
            return await Task.Run(() =>
            {
                // Restricción de plataforma: Omite la auditoría en entornos Windows, Android o UI Only para evitar la ejecución de comandos Bash incompatibles.
                if (_environmentManager.IsWindows || _environmentManager.IsAndroid || _environmentManager.IsUIOnlyMode) { return (true, string.Empty, string.Empty); }

                bool hasAria2 = CheckCommandExists("aria2c");
                bool hasVulkan = CheckCommandExists("vulkaninfo");
                bool hasEspeak = CheckCommandExists("espeak-ng");

                bool needsAria2 = !hasAria2;
                bool needsVulkan = !hasVulkan;
                bool needsEspeak = !hasEspeak;

                if (!needsAria2 && !needsVulkan && !needsEspeak)
                {
                    return (true, string.Empty, "> Todos los subsistemas operativos C++ y nativos están en línea y funcionales.");
                }

                string missingLog = "> Análisis completado. Se detectaron dependencias base faltantes:\n";
                if (needsAria2) missingLog += "- Gestor de descargas acelerado (aria2c)\n";
                if (needsVulkan) missingLog += "- Aceleración gráfica (Vulkan Tools)\n";
                if (needsEspeak) missingLog += "- Diccionarios fonéticos para síntesis (espeak-ng)\n";
                missingLog += "\n> Generando script ligero de resolución automática...";

                string scriptPath = ProjectSettings.GlobalizePath("user://instalar_dependencias.sh");
                string scriptContent = "#!/bin/bash\nset -e\n\n";
                scriptContent += "echo '============================================'\n";
                scriptContent += "echo '  Instalador Ligero de AGI (Fedora/Linux)'\n";
                scriptContent += "echo '============================================'\n\n";

                bool hasApt = CheckCommandExists("apt-get");
                bool hasDnf = CheckCommandExists("dnf");
                bool hasPacman = CheckCommandExists("pacman");

                if (needsAria2 || needsVulkan || needsEspeak)
                {
                    scriptContent += "echo '-> Instalando dependencias de red, aceleración Vulkan y diccionarios...'\n";
                    if (hasApt) scriptContent += $"sudo apt-get update && sudo apt-get install -y {(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "mesa-vulkan-drivers vulkan-tools " : "")}{(needsEspeak ? "espeak-ng " : "")}\n";
                    else if (hasDnf) scriptContent += $"sudo dnf install -y {(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "mesa-vulkan-drivers vulkan-tools " : "")}{(needsEspeak ? "espeak-ng " : "")}\n";
                    else if (hasPacman) scriptContent += $"sudo pacman -S --noconfirm {(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "vulkan-radeon vulkan-intel vulkan-tools " : "")}{(needsEspeak ? "espeak-ng " : "")}\n";
                    scriptContent += "\n";
                }

                scriptContent += "echo '============================================'\n";
                scriptContent += "echo '¡Todo listo! Cierra esta terminal y reinicia tu app.'\n";

                global::System.IO.File.WriteAllText(scriptPath, scriptContent);
                OS.Execute("chmod", new string[] { "+x", scriptPath }, new Godot.Collections.Array(), true);

                string finalCommand = $"bash \"{scriptPath}\"";
                
                return (false, finalCommand, missingLog);
            });
        }

        private bool CheckCommandExists(string command)
        {
            var output = new Godot.Collections.Array();
            int exitCode = OS.Execute("which", new string[] { command }, output, true);
            return exitCode == 0;
        }
    }
}