using Godot;
using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Logic.System.Config
{
    /// <summary>
    /// Manages the application's configuration state, handles persistence, and validates model integrity.
    /// Operates strictly as a data layer Singleton without UI dependencies.
    /// </summary>
    public partial class ConfigManager : Node
    {
        public enum AppMode 
        { 
            None, 
            RemoteUI, 
            LocalHost 
        }

        public AppMode CurrentMode { get; set; } = AppMode.None;
        public string RemoteHostUrl { get; set; } = string.Empty;
        public string ActiveModelPath { get; set; } = string.Empty;
        public string ActiveModelName { get; set; } = string.Empty;

        private string _settingsDirectory;
        private string _configFilePath;
        private string _presetsFilePath;

        /// <summary>
        /// Defines the structure for model presets loaded from the external JSON configuration.
        /// </summary>
        public class ModelPreset
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public List<string> DownloadLinks { get; set; }
            public long ExpectedSize { get; set; }
        }

        public string ActiveModelUrl { get; set; } = string.Empty;

        /// <summary>
        /// Internal structure used exclusively for JSON serialization of the configuration state.
        /// Integra el seguimiento de la URL de descarga del modelo activo.
        /// </summary>
        private class ConfigState
        {
            public AppMode Mode { get; set; }
            public string RemoteHostUrl { get; set; }
            public string ActiveModelPath { get; set; }
            public string ActiveModelName { get; set; }
            public string ActiveModelUrl { get; set; }
        }

        public override void _Ready()
        {
            _settingsDirectory = ProjectSettings.GlobalizePath("user://settings");
            _configFilePath = Path.Combine(_settingsDirectory, "config.json");
            _presetsFilePath = ProjectSettings.GlobalizePath("user://presets.json");

            LoadConfiguration();
        }

        /// <summary>
        /// Serializes the current application state to the local configuration file.
        /// Captura y persiste la URL del modelo activo seleccionado en tiempo de ejecución.
        /// </summary>
        public void SaveConfiguration()
        {
            try
            {
                if (!Directory.Exists(_settingsDirectory))
                {
                    Directory.CreateDirectory(_settingsDirectory);
                }

                ConfigState state = new ConfigState
                {
                    Mode = CurrentMode,
                    RemoteHostUrl = RemoteHostUrl,
                    ActiveModelPath = ActiveModelPath,
                    ActiveModelName = ActiveModelName,
                    ActiveModelUrl = ActiveModelUrl
                };

                JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(state, options);
                
                File.WriteAllText(_configFilePath, jsonString);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Failed to save configuration. Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads and deserializes the configuration state from the local file system.
        /// Restaura en memoria la estructura de configuración incluyendo la URL de descarga del modelo.
        /// </summary>
        public void LoadConfiguration()
        {
            if (!File.Exists(_configFilePath))
            {
                return;
            }

            try
            {
                string jsonString = File.ReadAllText(_configFilePath);
                ConfigState state = JsonSerializer.Deserialize<ConfigState>(jsonString);

                if (state != null)
                {
                    CurrentMode = state.Mode;
                    RemoteHostUrl = state.RemoteHostUrl;
                    ActiveModelPath = state.ActiveModelPath;
                    ActiveModelName = state.ActiveModelName;
                    ActiveModelUrl = state.ActiveModelUrl;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Failed to load configuration. Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtiene la lista de modelos preconfigurados priorizando la última versión del repositorio remoto.
        /// Implementa una estrategia de tolerancia a fallos empleando la versión en caché local en caso 
        /// de indisponibilidad de la red.
        /// </summary>
        /// <returns>Una tarea asíncrona que contiene la lista de objetos ModelPreset actualizada o respaldada.</returns>
        public async Task<List<ModelPreset>> GetOrDownloadPresetsAsync()
        {
            string userPresetsPath = ProjectSettings.GlobalizePath("user://presets.json");

            bool downloadSuccess = await DownloadPresetsFromGitHub(userPresetsPath);

            if (!downloadSuccess)
            {
                GD.PrintErr("ConfigManager: La actualización remota falló. Evaluando contingencia en caché local.");
                
                if (!File.Exists(userPresetsPath))
                {
                    GD.PrintErr("ConfigManager: No existe caché local de presets. Operación abortada.");
                    return new List<ModelPreset>();
                }
            }

            try
            {
                string jsonString = File.ReadAllText(userPresetsPath);
                JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<List<ModelPreset>>(jsonString, options) ?? new List<ModelPreset>();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Error durante la lectura o deserialización de presets. Excepción: {ex.Message}");
                return new List<ModelPreset>();
            }
        }

        /// <summary>
        /// Instancia un cliente HTTP calificado explícitamente desde System.Net.Http para evitar colisiones 
        /// con la red nativa de Godot. Realiza una petición GET hacia la URL cruda del repositorio,
        /// recupera la cadena de texto de la respuesta y la persiste en la ruta de destino especificada.
        /// </summary>
        /// <param name="destinationPath">La ruta absoluta del sistema de archivos donde se almacenará el JSON.</param>
        /// <returns>Una tarea asíncrona que retorna verdadero si el proceso de descarga y escritura concluye con éxito.</returns>
        private async Task<bool> DownloadPresetsFromGitHub(string destinationPath)
        {
            string targetUrl = "https://raw.githubusercontent.com/YirehStudios/AGI/main/agi/Script/Cs/System/Config/presets.json";

            try
            {
                // Se utiliza la calificación explícita del espacio de nombres para aislar la implementación de .NET
                using global::System.Net.Http.HttpClient client = new global::System.Net.Http.HttpClient();
                string jsonContent = await client.GetStringAsync(targetUrl);
                
                File.WriteAllText(destinationPath, jsonContent);
                return true;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Interrupción o error en la solicitud de red para descargar presets. Excepción: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Validates the existence and expected byte size of the currently assigned model file.
        /// </summary>
        /// <param name="expectedSize">The expected file size in bytes to verify integrity.</param>
        /// <returns>A tuple containing a boolean success flag and a descriptive error message if applicable.</returns>
        public (bool IsValid, string ErrorMessage) ValidateModelIntegrity(long expectedSize)
        {
            if (string.IsNullOrEmpty(ActiveModelPath))
            {
                return (false, "Model path is not configured.");
            }

            if (!File.Exists(ActiveModelPath))
            {
                return (false, $"The model file was not found at the specified path: {ActiveModelPath}");
            }

            try
            {
                FileInfo fileInfo = new FileInfo(ActiveModelPath);
                if (fileInfo.Length != expectedSize)
                {
                    return (false, $"Model size mismatch. Expected {expectedSize} bytes, but found {fileInfo.Length} bytes. The file may be corrupted or incomplete.");
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"An error occurred while validating the model: {ex.Message}");
            }
        }
    }
}