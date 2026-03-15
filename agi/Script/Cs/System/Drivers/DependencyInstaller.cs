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
        public async Task<(bool HasDocker, string RequiredCommand)> AuditSystemDependenciesAsync()
        {
            // 1. Detectamos si hay una GPU NVIDIA de forma segura en el hilo principal
            string adapterName = Godot.RenderingServer.GetVideoAdapterName().ToLower();
            bool hasNvidiaGpu = adapterName.Contains("nvidia");

            return await Task.Run(() =>
            {
                // 2. Auditamos qué le falta a la computadora
                bool hasDocker = CheckCommandExists("docker");
                bool hasAria2 = CheckCommandExists("aria2c");
                bool hasNvidiaCtk = CheckCommandExists("nvidia-ctk");

                bool needsDocker = !hasDocker;
                bool needsAria2 = !hasAria2;
                bool needsNvidiaCtk = hasNvidiaGpu && !hasNvidiaCtk; // Solo lo pide si hay NVIDIA

                // Si la PC ya está lista, le damos luz verde de inmediato
                if (!needsDocker && !needsAria2 && !needsNvidiaCtk)
                {
                    return (true, string.Empty);
                }

                // 3. GENERADOR DE SCRIPT AUTOMÁTICO (Infraestructura como Código)
                string scriptPath = ProjectSettings.GlobalizePath("user://instalar_dependencias.sh");
                string scriptContent = "#!/bin/bash\nset -e\n\n";
                scriptContent += "echo '============================================'\n";
                scriptContent += "echo '  Instalador Automático de AGI (Godot)'\n";
                scriptContent += "echo '============================================'\n\n";

                bool hasApt = CheckCommandExists("apt-get");
                bool hasDnf = CheckCommandExists("dnf");
                bool hasPacman = CheckCommandExists("pacman");

                if (needsDocker || needsAria2)
                {
                    scriptContent += "echo '-> Instalando Docker y Aria2c...'\n";
                    if (hasApt) scriptContent += $"sudo apt-get update && sudo apt-get install -y {(needsDocker ? "docker.io " : "")}{(needsAria2 ? "aria2" : "")}\n";
                    else if (hasDnf) scriptContent += $"sudo dnf install -y {(needsDocker ? "docker " : "")}{(needsAria2 ? "aria2" : "")}\n";
                    else if (hasPacman) scriptContent += $"sudo pacman -S --noconfirm {(needsDocker ? "docker " : "")}{(needsAria2 ? "aria2" : "")}\n";
                    else scriptContent += "curl -fsSL https://get.docker.com | sudo sh\n";
                    scriptContent += "\n";
                }

                if (needsNvidiaCtk)
                {
                    scriptContent += "echo '-> Configurando NVIDIA Container Toolkit (Puente GPU)...'\n";
                    if (hasApt)
                    {
                        scriptContent += "curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg\n";
                        scriptContent += "curl -s -L https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' | sudo tee /etc/apt/sources.list.d/nvidia-container-toolkit.list > /dev/null\n";
                        scriptContent += "sudo apt-get update && sudo apt-get install -y nvidia-container-toolkit\n";
                    }
                    else if (hasDnf)
                    {
                        scriptContent += "curl -s -L https://nvidia.github.io/libnvidia-container/stable/rpm/nvidia-container-toolkit.repo | sudo tee /etc/yum.repos.d/nvidia-container-toolkit.repo > /dev/null\n";
                        scriptContent += "sudo dnf install -y nvidia-container-toolkit\n";
                    }
                    scriptContent += "sudo nvidia-ctk runtime configure --runtime=docker\n";
                    scriptContent += "\n";
                }

                if (needsDocker || needsNvidiaCtk)
                {
                    scriptContent += "echo '-> Reiniciando servicios y aplicando permisos...'\n";
                    scriptContent += "sudo systemctl enable --now docker\n";
                    scriptContent += "sudo systemctl restart docker\n";
                    scriptContent += "sudo usermod -aG docker $USER\n";
                    scriptContent += "\n";
                }

                scriptContent += "echo '============================================'\n";
                scriptContent += "echo '¡Todo listo! Cierra esta terminal y reinicia tu app.'\n";

                // Escribimos el script en el disco duro
                global::System.IO.File.WriteAllText(scriptPath, scriptContent);
                
                // Le damos permisos de ejecución en Linux
                OS.Execute("chmod", new string[] { "+x", scriptPath }, new Godot.Collections.Array(), true);

                // 4. Devolvemos UNA SOLA línea limpia para la interfaz gráfica del usuario
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