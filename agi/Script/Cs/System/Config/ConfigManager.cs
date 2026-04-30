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
    /// Gestiona el estado de configuración de la aplicación, maneja la persistencia y valida la integridad de los modelos.
    /// Opera como una capa de datos Singleton sin dependencias de interfaz de usuario.
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

        public int SelectedGpuIndex { get; set; } = -1;

        public string ActiveSTTEngine { get; set; } = "whisper.cpp";
        public string ActiveSTTModel { get; set; } = "base.bin";
        public string ActiveTTSEngine { get; set; } = "sherpa-onnx";
        public string ActiveTTSModel { get; set; } = "vits-piper-es_ES-miro-high";

        /// <summary>
        /// Define la estructura para las URLs de descarga de motores en diferentes plataformas.
        /// </summary>
        public class EngineUrls
        {
            public string LinuxUrl { get; set; }
            public string WindowsUrl { get; set; }
        }

        /// <summary>
        /// Contenedor raíz para la configuración de los diversos motores de inferencia.
        /// </summary>
        public class EngineConfig
        {
            public EngineUrls Llama { get; set; }
            public EngineUrls Whisper { get; set; }
            public EngineUrls Sherpa { get; set; }
        }

        /// <summary>
        /// Define la estructura de los presets de modelos cargados desde el JSON externo.
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

        public override void _Ready()
        {
            _settingsDirectory = ProjectSettings.GlobalizePath("user://settings");
            _configFilePath = Path.Combine(_settingsDirectory, "preferences.json"); 
            _presetsFilePath = ProjectSettings.GlobalizePath("user://presets.json");

            _downloadManager = GetNodeOrNull<Logic.Network.DownloadManager>("/root/DownloadManager");

            LoadConfiguration();
        }

        /// <summary>
        /// Obtiene la configuración de motores desde el repositorio remoto con respaldo local.
        /// Implementa lógica de cache-busting para garantizar la frescura de los datos.
        /// </summary>
        /// <returns>Objeto EngineConfig con las URLs de descarga por plataforma.</returns>
        public async Task<EngineConfig> GetOrDownloadEnginesAsync()
        {
            string enginesPath = ProjectSettings.GlobalizePath("user://engines.json");
            
            // Intento de sincronización con el repositorio remoto.
            bool downloadSuccess = await DownloadEnginesFromGitHub(enginesPath);

            if (!downloadSuccess)
            {
                GD.PrintErr("ConfigManager: La actualización de motores falló. Verificando disponibilidad local.");
                if (!File.Exists(enginesPath))
                {
                    GD.PrintErr("ConfigManager: No se encontró definición de motores.");
                    return null;
                }
            }

            try
            {
                string jsonString = File.ReadAllText(enginesPath);
                JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<EngineConfig>(jsonString, options);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Error deserializando engines.json: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Realiza la petición HTTP para descargar el manifiesto de motores.
        /// Utiliza el calificador global para evitar colisiones entre el cliente de Godot y el de .NET.
        /// </summary>
        /// <param name="destinationPath">Ruta local de persistencia del archivo JSON.</param>
        /// <returns>True si la descarga y escritura fueron exitosas.</returns>
        private async Task<bool> DownloadEnginesFromGitHub(string destinationPath)
        {
            string cacheBuster = DateTime.Now.Ticks.ToString();
            string targetUrl = $"https://github.com/YirehStudios/AGI/raw/refs/heads/main/agi/Script/Cs/System/Config/engines.json?t={cacheBuster}";

            try
            {
                // Se utiliza el calificador global:: para resolver la ambigüedad con Godot.HttpClient
                // y prevenir que el compilador busque 'Net' dentro del espacio de nombres 'Logic.System'.
                using global::System.Net.Http.HttpClient client = new global::System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.CacheControl = new global::System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                
                string jsonContent = await client.GetStringAsync(targetUrl);
                File.WriteAllText(destinationPath, jsonContent);
                return true;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Error de red descargando motores: {ex.Message}");
                return false;
            }
        }

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

        public void LoadConfiguration()
        {
            if (!File.Exists(_configFilePath)) return;

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

        public async Task<List<ModelPreset>> GetOrDownloadPresetsAsync()
        {
            string userPresetsPath = ProjectSettings.GlobalizePath("user://presets.json");
            bool downloadSuccess = await DownloadPresetsFromGitHub(userPresetsPath);

            if (!downloadSuccess)
            {
                if (!File.Exists(userPresetsPath)) return new List<ModelPreset>();
            }

            try
            {
                string jsonString = File.ReadAllText(userPresetsPath);
                JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<List<ModelPreset>>(jsonString, options) ?? new List<ModelPreset>();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Error leyendo presets: {ex.Message}");
                return new List<ModelPreset>();
            }
        }

        /// <summary>
        /// Recupera el catálogo de modelos preestablecidos desde el repositorio remoto.
        /// Implementa resolución explícita de tipos para asegurar la integridad de la operación de red.
        /// </summary>
        /// <param name="destinationPath">Ruta de destino para la persistencia del archivo de presets.</param>
        /// <returns>Booleano indicando el éxito de la transferencia de datos.</returns>
        private async Task<bool> DownloadPresetsFromGitHub(string destinationPath)
        {
            string cacheBuster = DateTime.Now.Ticks.ToString();
            string targetUrl = $"https://raw.githubusercontent.com/YirehStudios/AGI/main/agi/Script/Cs/System/Config/presets.json?t={cacheBuster}";

            try
            {
                // La instanciación mediante el espacio de nombres global garantiza que se utilice el cliente HTTP de .NET Core.
                using global::System.Net.Http.HttpClient client = new global::System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.CacheControl = new global::System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                
                string jsonContent = await client.GetStringAsync(targetUrl);
                File.WriteAllText(destinationPath, jsonContent);
                return true;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Error en la red (Presets): {ex.Message}");
                return false;
            }
        }

        public (bool IsValid, string ErrorMessage) ValidateModelIntegrity(long expectedSize)
        {
            if (string.IsNullOrEmpty(ActiveModelPath)) return (false, "Model path is not configured.");
            if (!File.Exists(ActiveModelPath)) return (false, $"File not found at: {ActiveModelPath}");

            try
            {
                FileInfo fileInfo = new FileInfo(ActiveModelPath);
                if (fileInfo.Length != expectedSize)
                {
                    return (false, $"Size mismatch. Expected {expectedSize}, found {fileInfo.Length}.");
                }
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Validation error: {ex.Message}");
            }
        }
    }
}