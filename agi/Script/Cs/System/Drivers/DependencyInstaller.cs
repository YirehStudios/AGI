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
        /// Realiza una auditoría asíncrona de las dependencias del sistema operativo.
        /// Evalúa la presencia de herramientas de red, aceleración gráfica y entornos de ejecución.
        /// Implementa una cláusula de guarda para omitir la validación en plataformas que no operan bajo subsistemas de shell compatibles con Linux.
        /// </summary>
        /// <returns>
        /// Una tupla que contiene el estado de preparación (IsReady), el comando de resolución (RequiredCommand) y el registro de auditoría (AuditLog).
        /// </returns>
        public async Task<(bool IsReady, string RequiredCommand, string AuditLog)> AuditSystemDependenciesAsync()
        {
            return await Task.Run(() =>
            {
                // Validación de entorno de ejecución: Se intercepta la operación en sistemas donde la gestión de paquetes vía shell es incompatible o innecesaria.
                // Esta lógica de barrera previene la evaluación errónea de binarios nativos de Linux en hosts Windows o Android.
                if (_environmentManager.IsWindows || _environmentManager.IsAndroid || _environmentManager.IsUIOnlyMode)
                { 
                    return (true, string.Empty, string.Empty);
                }

                // Ejecución de la auditoría de binarios mediante la resolución de rutas en las variables de entorno del sistema operativo.
                bool hasAria2 = CheckCommandExists("aria2c");
                bool hasVulkan = CheckCommandExists("vulkaninfo");
                bool hasEspeak = CheckCommandExists("espeak-ng");
                bool hasPython = CheckCommandExists("python3");

                // Evaluación de estados de necesidad para determinar la brecha de dependencias en el sistema host.
                bool needsAria2 = !hasAria2;
                bool needsVulkan = !hasVulkan;
                bool needsEspeak = !hasEspeak;
                bool needsPython = !hasPython;

                // Finalización prematura de la auditoría si el entorno satisface íntegramente los requisitos de ejecución nativa.
                if (!needsAria2 && !needsVulkan && !needsEspeak && !needsPython)
                {
                    return (true, string.Empty, "> Todos los subsistemas operativos C++ y nativos están en línea y funcionales.");
                }

                // Compilación de metadatos de auditoría para la retroalimentación del sistema sobre los componentes ausentes.
                string missingLog = "> Análisis completado. Se detectaron dependencias base faltantes:\n";
                if (needsAria2) missingLog += "- Gestor de descargas acelerado (aria2c)\n";
                if (needsVulkan) missingLog += "- Aceleración gráfica (Vulkan Tools)\n";
                if (needsEspeak) missingLog += "- Diccionarios fonéticos para síntesis (espeak-ng)\n";
                if (needsPython) missingLog += "- Entorno base de Python 3 y herramientas virtuales (venv/pip)\n";
                missingLog += "\n> Generando script ligero de resolución automática...";

                // Definición de parámetros para la generación del script de automatización en el almacenamiento persistente del usuario.
                string scriptPath = ProjectSettings.GlobalizePath("user://instalar_dependencias.sh");
                string scriptContent = "#!/bin/bash\nset -e\n\n";
                scriptContent += "echo '============================================'\n";
                scriptContent += "echo '  Instalador Ligero de AGI (Fedora/Linux)'\n";
                scriptContent += "echo '============================================'\n\n";

                // Clasificación del gestor de paquetes nativo mediante la detección de binarios de administración de sistemas.
                bool hasApt = CheckCommandExists("apt-get");
                bool hasDnf = CheckCommandExists("dnf");
                bool hasPacman = CheckCommandExists("pacman");

                // Construcción dinámica de la cadena de comandos de instalación basada en el gestor de paquetes identificado.
                {
                    scriptContent += "echo '-> Instalando dependencias de red, aceleración Vulkan y entornos de ejecución...'\n";
                    
                    if (hasApt) 
                    {
                        scriptContent += $"sudo apt-get update && sudo apt-get install -y {(needsPython ? "python3 python3-venv python3-pip " : "")}{(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "mesa-vulkan-drivers vulkan-tools " : "")}{(needsEspeak ? "espeak-ng " : "")}\n";
                    }
                    else if (hasDnf) 
                    {
                        scriptContent += $"sudo dnf install -y {(needsPython ? "python3 python3-pip " : "")}{(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "mesa-vulkan-drivers vulkan-tools " : "")}{(needsEspeak ? "espeak-ng " : "")}\n";
                    }
                    else if (hasPacman) 
                    {
                        scriptContent += $"sudo pacman -S --noconfirm {(needsPython ? "python python-pip " : "")}{(needsAria2 ? "aria2 " : "")}{(needsVulkan ? "vulkan-radeon vulkan-intel vulkan-tools " : "")}{(needsEspeak ? "espeak-ng " : "")}\n";
                    }
                    scriptContent += "\n";
                }

                scriptContent += "echo '============================================'\n";
                scriptContent += "echo '¡Todo listo! Cierra esta terminal y reinicia tu app.'\n";

                // Serialización del script en disco y asignación de privilegios de ejecución mediante llamadas al sistema operativo.
                global::System.IO.File.WriteAllText(scriptPath, scriptContent);
                OS.Execute("chmod", new string[] { "+x", scriptPath }, new Godot.Collections.Array(), true);

                string finalCommand = $"bash \"{scriptPath}\"";
                
                // Retorno del estado de auditoría indicando la necesidad de ejecución externa para la resolución de dependencias.
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