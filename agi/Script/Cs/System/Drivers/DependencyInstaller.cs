using Godot;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Logic.System.Drivers
{
    /// <summary>
    /// Instala dependencias lanzando un script maestro generado dinámicamente.
    /// Detecta hardware (NVIDIA) y la distribución de Linux para automatizar la configuración.
    /// </summary>
    public partial class DependencyInstaller : Node
    {
        // Archivo: DependencyInstaller.cs

    public async Task<(bool IsReady, string RequiredCommand)> AuditSystemDependenciesAsync()
    {
        // Executes system checks asynchronously to prevent blocking the main rendering thread.
        return await Task.Run(() =>
        {
            // Evaluates the presence of required system binaries for networking and Vulkan rendering acceleration.
            bool hasAria2 = CheckCommandExists("aria2c");
            bool hasVulkan = CheckCommandExists("vulkaninfo");

            bool needsAria2 = !hasAria2;
            bool needsVulkan = !hasVulkan;

            // Bypasses the installation script generation if all dependencies are already satisfied.
            if (!needsAria2 && !needsVulkan)
            {
                return (true, string.Empty);
            }

            // Initializes the dynamic bash script string containing the required package manager invocations.
            string scriptPath = ProjectSettings.GlobalizePath("user://instalar_dependencias.sh");
            string scriptContent = "#!/bin/bash\nset -e\n\n";
            scriptContent += "echo '============================================'\n";
            scriptContent += "echo '  Instalador Automático de AGI (Fedora/Linux)'\n";
            scriptContent += "echo '============================================'\n\n";

            bool hasApt = CheckCommandExists("apt-get");
            bool hasDnf = CheckCommandExists("dnf");
            bool hasPacman = CheckCommandExists("pacman");

            if (needsAria2 || needsVulkan)
            {
                scriptContent += "echo '-> Instalando dependencias de red y aceleración Vulkan...'\n";
                if (hasApt) scriptContent += $"sudo apt-get update && sudo apt-get install -y {(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "mesa-vulkan-drivers vulkan-tools " : "")}\n";
                else if (hasDnf) scriptContent += $"sudo dnf install -y {(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "mesa-vulkan-drivers vulkan-tools " : "")}\n";
                else if (hasPacman) scriptContent += $"sudo pacman -S --noconfirm {(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "vulkan-radeon vulkan-intel vulkan-tools " : "")}\n";
                scriptContent += "\n";
            }

            scriptContent += "echo '============================================'\n";
            scriptContent += "echo '¡Todo listo! Cierra esta terminal y reinicia tu app.'\n";

            // Persists the generated script to the user directory and assigns executable permissions.
            global::System.IO.File.WriteAllText(scriptPath, scriptContent);
            OS.Execute("chmod", new string[] { "+x", scriptPath }, new Godot.Collections.Array(), true);

            string finalCommand = $"bash \"{scriptPath}\"";
            
            return (false, finalCommand);
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