using Godot;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Logic.System.Drivers
{
    /// <summary>
    /// Instala dependencias lanzando una ventana de terminal nativa para una experiencia transparente.
    /// Utiliza un archivo temporal de estado para sincronizarse con Godot.
    /// </summary>
    public partial class DependencyInstaller : Node
    {
        /// <summary>
		/// Audita silenciosamente el sistema operativo en busca de los binarios 'docker' y 'aria2c'.
		/// Identifica dinámicamente el gestor de paquetes de la distribución Linux y construye
		/// un comando de instalación concatenado. Anexa la configuración de permisos y grupos del usuario
		/// al final de la cadena de ejecución.
		/// </summary>
		/// <returns>Una tupla indicando la presencia de Docker y el comando bash resultante si es necesario.</returns>
		public async Task<(bool HasDocker, string RequiredCommand)> AuditSystemDependenciesAsync()
		{
			return await Task.Run(() =>
			{
				bool hasDocker = CheckCommandExists("docker");
				bool hasAria2 = CheckCommandExists("aria2c");

				// Omite la generación del comando si la dependencia central (Docker) existe, delegando fallback de descargas al DownloadManager
				if (hasDocker)
				{
					return (true, string.Empty);
				}

				string installCommand = string.Empty;

				bool hasApt = CheckCommandExists("apt-get");
				bool hasDnf = CheckCommandExists("dnf");
				bool hasPacman = CheckCommandExists("pacman");

				string dockerPackage = "docker.io";
				string ariaPackage = "aria2";

				// Compilación lógica del comando de instalación en función del entorno detectado
				if (hasApt)
				{
					string packages = hasAria2 ? dockerPackage : $"{dockerPackage} {ariaPackage}";
					installCommand = $"sudo apt-get update && sudo apt-get install -y {packages}";
				}
				else if (hasDnf)
				{
					string dockerDnf = "docker";
					string packages = hasAria2 ? dockerDnf : $"{dockerDnf} {ariaPackage}";
					installCommand = $"sudo dnf install -y {packages}";
				}
				else if (hasPacman)
				{
					string dockerPacman = "docker";
					string packages = hasAria2 ? dockerPacman : $"{dockerPacman} {ariaPackage}";
					installCommand = $"sudo pacman -S --noconfirm {packages}";
				}
				else
				{
					// Redirección de salida directa desde el script remoto al intérprete nativo como último recurso
					installCommand = "curl -fsSL https://get.docker.com | sudo sh";
				}

				// Concatenación de la lógica de daemonización del servicio y elevación de permisos del entorno de escritorio actual
				string fullCommand = $"{installCommand} && sudo systemctl enable --now docker && sudo usermod -aG docker $USER";

				return (false, fullCommand);
			});
		}

		/// <summary>
		/// Invoca un proceso del sistema para evaluar la resolución del binario especificado
		/// utilizando el comando estandarizado POSIX 'which'.
		/// </summary>
		/// <param name="command">El nombre del ejecutable a auditar.</param>
		/// <returns>Verdadero si el binario existe en la variable de entorno PATH.</returns>
		private bool CheckCommandExists(string command)
		{
			var output = new Godot.Collections.Array();
			int exitCode = Godot.OS.Execute("which", new string[] { command }, output, true);
			return exitCode == 0;
		}
    }
}