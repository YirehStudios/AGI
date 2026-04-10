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
        public bool SetupCompleted { get; set; } = false;
        public bool IsLanConnection { get; set; } = false;
        public string CustomPort { get; set; } = "8080";

        // -1 significa "Auto-detectar la GPU más potente". 
        // 0, 1, 2... serán los índices si el usuario lo cambia manualmente en tu futura UI.
        public int SelectedGpuIndex { get; set; } = -1;

        /// <summary>
        /// Define los motores y modelos de procesamiento de lenguaje natural y síntesis de voz utilizados por defecto.
        /// </summary>
        public string ActiveSTTEngine { get; set; } = "whisper.cpp";
        public string ActiveSTTModel { get; set; } = "base.bin";
        public string ActiveTTSEngine { get; set; } = "sherpa-onnx";
        public string ActiveTTSModel { get; set; } = "vits-piper-es_ES-miro-high";

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

        private Logic.Network.DownloadManager _downloadManager;

        /// <summary>
        /// Internal structure used exclusively for JSON serialization of the configuration state.
        /// Integra el mapeo de los motores de síntesis y reconocimiento para la persistencia local.
        /// </summary>
        private class ConfigState
        {
            public AppMode Mode { get; set; }
            public string RemoteHostUrl { get; set; }
            public string ActiveModelPath { get; set; }
            public string ActiveModelName { get; set; }
            public string ActiveModelUrl { get; set; }
            public bool SetupCompleted { get; set; }
            public bool IsLanConnection { get; set; }
            public string CustomPort { get; set; }
            public string ActiveSTTEngine { get; set; }
            public string ActiveSTTModel { get; set; }
            public string ActiveTTSEngine { get; set; }
            public string ActiveTTSModel { get; set; }
        }

        /// <summary>
        /// Inicializa las dependencias del nodo al entrar al árbol de escena.
        /// Establece las rutas de configuración y recupera la instancia del DownloadManager en memoria.
        /// </summary>
        public override void _Ready()
        {
            _settingsDirectory = ProjectSettings.GlobalizePath("user://settings");
            _configFilePath = Path.Combine(_settingsDirectory, "preferences.json"); 
            _presetsFilePath = ProjectSettings.GlobalizePath("user://presets.json");

            _downloadManager = GetNodeOrNull<Logic.Network.DownloadManager>("/root/DownloadManager");

            LoadConfiguration();
        }

        /// <summary>
        /// Orquesta la descarga asíncrona de un modelo de IA predefinido utilizando el DownloadManager global.
        /// </summary>
        /// <param name="preset">Objeto que contiene los metadatos y enlaces de descarga del modelo.</param>
        public async Task DownloadModelAsync(ModelPreset preset)
        {
            if (_downloadManager == null || preset.DownloadLinks.Count == 0) return;

            string url = preset.DownloadLinks[0];
            string fileName = Path.GetFileName(new Uri(url).LocalPath);
            
            string folder = "user://agi/models";

            GD.Print($"ConfigManager: Iniciando descarga de {preset.Name}...");
            bool success = await _downloadManager.DownloadFileAsync(url, folder, fileName);

            if (success)
            {
                GD.Print($"ConfigManager: {preset.Name} instalado y extraído con éxito.");
            }
        }

        /// <summary>
        /// Serializes the current application state to the local configuration file.
        /// Captura y persiste las asignaciones de motor y modelo de audio activas.
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
                    ActiveModelUrl = ActiveModelUrl,
                    SetupCompleted = SetupCompleted,
                    IsLanConnection = IsLanConnection,
                    CustomPort = CustomPort,
                    ActiveSTTEngine = ActiveSTTEngine,
                    ActiveSTTModel = ActiveSTTModel,
                    ActiveTTSEngine = ActiveTTSEngine,
                    ActiveTTSModel = ActiveTTSModel
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
        /// Restaura en memoria la configuración, inyectando las preferencias de audio si están definidas.
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
                    SetupCompleted = state.SetupCompleted;
                    IsLanConnection = state.IsLanConnection;
                    CustomPort = state.CustomPort;
                    
                    if (!string.IsNullOrEmpty(state.ActiveSTTEngine)) ActiveSTTEngine = state.ActiveSTTEngine;
                    if (!string.IsNullOrEmpty(state.ActiveSTTModel)) ActiveSTTModel = state.ActiveSTTModel;
                    if (!string.IsNullOrEmpty(state.ActiveTTSEngine)) ActiveTTSEngine = state.ActiveTTSEngine;
                    if (!string.IsNullOrEmpty(state.ActiveTTSModel)) ActiveTTSModel = state.ActiveTTSModel;
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