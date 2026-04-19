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
        /// <summary>
        /// Executes system checks asynchronously to prevent blocking the main rendering thread.
        /// Generates and provisions a bash script to resolve missing networking, rendering, and Python infrastructure dependencies.
        /// Actively validates the presence of the required Python modules to ensure the TTS WebSocket bridge can initialize.
        /// </summary>
        public async Task<(bool IsReady, string RequiredCommand)> AuditSystemDependenciesAsync()
        {
            return await Task.Run(() =>
            {
                bool hasAria2 = CheckCommandExists("aria2c");
                bool hasVulkan = CheckCommandExists("vulkaninfo");

                Godot.Collections.Array pyOutput = new Godot.Collections.Array();
                
                int pyExitCode = OS.Execute("python3", new string[] { "-c", "import kokoro_onnx" }, pyOutput, true);
                bool hasKokoroPython = (pyExitCode == 0);

                bool needsAria2 = !hasAria2;
                bool needsVulkan = !hasVulkan;
                bool needsPythonBridge = !hasKokoroPython;

                if (!needsAria2 && !needsVulkan && !needsPythonBridge)
                {
                    return (true, string.Empty);
                }

                string scriptPath = ProjectSettings.GlobalizePath("user://instalar_dependencias.sh");
                string scriptContent = "#!/bin/bash\nset -e\n\n";
                scriptContent += "echo '============================================'\n";
                scriptContent += "echo '  Instalador Automático de AGI (Fedora/Linux)'\n";
                scriptContent += "echo '============================================'\n\n";

                bool hasApt = CheckCommandExists("apt-get");
                bool hasDnf = CheckCommandExists("dnf");
                bool hasPacman = CheckCommandExists("pacman");

                if (needsAria2 || needsVulkan || needsPythonBridge)
                {
                    scriptContent += "echo '-> Instalando dependencias de red, aceleración Vulkan y Python...'\n";
                    if (hasApt) scriptContent += $"sudo apt-get update && sudo apt-get install -y {(needsPythonBridge ? "python3 python3-pip " : "")}{(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "mesa-vulkan-drivers vulkan-tools " : "")}\n";
                    else if (hasDnf) scriptContent += $"sudo dnf install -y {(needsPythonBridge ? "python3 python3-pip " : "")}{(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "mesa-vulkan-drivers vulkan-tools " : "")}\n";
                    else if (hasPacman) scriptContent += $"sudo pacman -S --noconfirm {(needsPythonBridge ? "python3 python-pip " : "")}{(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "vulkan-radeon vulkan-intel vulkan-tools " : "")}\n";
                    scriptContent += "\n";
                }

                scriptContent += "echo '-> Instalando librerías Python para el motor de voz Kokoro-ONNX...'\n";
                scriptContent += "pip3 install --user kokoro-onnx soundfile websockets numpy --break-system-packages || pip3 install --user kokoro-onnx soundfile websockets numpy\n\n";

                scriptContent += "echo '============================================'\n";
                scriptContent += "echo '¡Todo listo! Cierra esta terminal y reinicia tu app.'\n";

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