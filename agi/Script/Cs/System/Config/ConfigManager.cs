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
        /// <summary>
        /// Defines the operational modes of the application, including local execution, 
        /// remote UI control, and universal cloud-based inference.
        /// </summary>
        public enum AppMode
        {
            None,
            RemoteUI,
            LocalHost,
            CloudAPI
        }

        /// <summary>
        /// Specifies the network isolation and routing topology configuration for local and remote endpoints.
        /// </summary>
        public enum NetworkState
        {
            StrictLocalhost,
            LanPublic,
            CloudAPI
        }

        /// <summary>
        /// Defines the compute resource boundaries, scaling behaviors, and latency mitigation profiles.
        /// </summary>
        public enum PerformanceTier
        {
            Low,
            Medium,
            High
        }

        /// <summary> Gets or sets the current operational network isolation state. </summary>
        public NetworkState CurrentNetworkState { get; set; } = NetworkState.StrictLocalhost;

        /// <summary> Gets or sets the target compute capability profile for infrastructure allocation. </summary>
        public PerformanceTier CurrentPerformanceTier { get; set; } = PerformanceTier.Medium;

        /// <summary>
        /// Computes the correct target interface binding address based on the designated network state topology.
        /// </summary>
        public string TargetBindAddress => CurrentNetworkState == NetworkState.LanPublic ? "0.0.0.0" : "127.0.0.1";

        /// <summary> Gets or sets the target endpoint for OpenAI-compatible cloud providers (Gemini, DeepSeek, etc.). </summary>
        public string CloudApiUrl { get; set; } = "https://api.openai.com/v1";

        /// <summary> Gets or sets the secret authentication key for external AI services. </summary>
        public string CloudApiKey { get; set; } = string.Empty;

        /// <summary> Gets or sets the specific model identifier used for cloud inference requests. </summary>
        public string CloudModelName { get; set; } = "gemini-1.5-pro";

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
        public bool UseCudaTurbo { get; set; } = false;

        public string ActiveSTTEngine { get; set; } = "whisper.cpp";
        public string ActiveSTTModel { get; set; } = "base.bin";
        public string ActiveTTSEngine { get; set; } = "sherpa-onnx";
        public string ActiveTTSModel { get; set; } = "vits-piper-es_ES-miro-high";

        public int PersistedSelectedAiMode { get; set; } = 1; // Default to Focus (1)
        public int PersistedToolTimeActive { get; set; } = 1; // Default to Active (1)
        public int PersistedToolWebSearchActive { get; set; } = 1; // Default to Active (1)
        public int PersistedToolMcpActive { get; set; } = 1; // Default to Active (1)
        public string PersistedWorkspacePath { get; set; } = ""; // User's custom workspace
        public List<string> PinnedChats { get; set; } = new List<string>();

        /// <summary>
        /// Provides a global static reference to the active configuration manager instance.
        /// </summary>
        public static ConfigManager Instance { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether the dark user interface theme is active.
        /// </summary>
        public bool DarkMode { get; set; } = true;

        /// <summary>
        /// Define la estructura para las URLs de descarga de motores en diferentes plataformas.
        /// </summary>
        public class EngineUrls
        {
            public string LinuxUrl { get; set; }
            public string WindowsUrl { get; set; }
        }

        /// <summary>
        /// Define la estructura para la URL del puente de inferencia TTS.
        /// </summary>
        public class TtsServerConfig
        {
            public string Url { get; set; }
        }

        public class EngineManifest
        {
            [global::System.Text.Json.Serialization.JsonPropertyName("vulkan_baseline")]
            public EngineConfig VulkanBaseline { get; set; }

            [global::System.Text.Json.Serialization.JsonPropertyName("cuda_turbo")]
            public EngineConfig CudaTurbo { get; set; }
        }

        /// <summary>
        /// Defines the structural mapping for the engines configuration manifest.
        /// Facilitates the deserialization of remote engine metadata into strongly-typed properties.
        /// </summary>
        public class EngineConfig
        {
            public EngineUrls Llama { get; set; }
            public EngineUrls Whisper { get; set; }
            public EngineUrls Sherpa { get; set; }
            public EngineUrls Python { get; set; }

            [global::System.Text.Json.Serialization.JsonPropertyName("tts_server")]
            public TtsServerConfig TtsServer { get; set; }

            [global::System.Text.Json.Serialization.JsonPropertyName("search_server")]
            public TtsServerConfig search_server { get; set; }

            [global::System.Text.Json.Serialization.JsonPropertyName("mcp_server")]
            public TtsServerConfig McpServer { get; set; }
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

        public class ChatTemplate
        {
            public string SystemPrefix { get; set; } = "<|im_start|>system\n";
            public string UserPrefix { get; set; } = "<|im_start|>user\n";
            public string AssistantPrefix { get; set; } = "<|im_start|>assistant\n";
            public string StopSequence { get; set; } = "<|im_end|>\n";
            public int ContextCeiling { get; set; } = 4096;
        }

        public class ModelProfile
        {
            public string Nombre { get; set; }
            public int Tipo { get; set; }
            public string EndpointUrl { get; set; }
            public string ModelId { get; set; }
            public string ApiKey { get; set; }
            public ChatTemplate Template { get; set; } = new ChatTemplate();

            /// <summary>Maximum tokens the model accepts per inference request (input context ceiling).</summary>
            public int MaxInputTokens { get; set; } = 4096;

            /// <summary>Maximum tokens the model may generate per response cycle (output generation ceiling).</summary>
            public int MaxOutputTokens { get; set; } = 2048;
        }

        public string ActiveModelUrl { get; set; } = string.Empty;
        public ModelProfile ActiveProfile { get; set; } = null;
        public string ActiveProfilePath { get; set; } = string.Empty;

        public class ComputePerformanceProfile
        {
            public int CpuThreads { get; set; } = 4;
            public int GpuLayers { get; set; } = 0;
            public int RamSaturationCeilingMB { get; set; } = 8192;
        }

        public ComputePerformanceProfile PerformanceProfile { get; set; } = new ComputePerformanceProfile();
        public Dictionary<string, int> ToolPermissions { get; set; } = new Dictionary<string, int>();

        private Logic.Network.DownloadManager _downloadManager;

        /// <summary>
        /// Internal data structure used for JSON serialization and persistence of the application state.
        /// Updated to securely capture and persist advanced network states and compute performance profiles.
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
            public string CloudApiUrl { get; set; }
            public string CloudApiKey { get; set; }
            public string CloudModelName { get; set; }
            public bool? DarkMode { get; set; }
            public NetworkState? NetworkState { get; set; }
            public PerformanceTier? PerformanceTier { get; set; }
            public string ActiveProfilePath { get; set; }
            public ComputePerformanceProfile PerformanceProfile { get; set; }
            public Dictionary<string, int> ToolPermissions { get; set; }
            public int? PersistedSelectedAiMode { get; set; }
            public int? PersistedToolTimeActive { get; set; }
            public int? PersistedToolWebSearchActive { get; set; }
            public int? PersistedToolMcpActive { get; set; }
            public string PersistedWorkspacePath { get; set; }
            public List<string> PinnedChats { get; set; }
            public int? SelectedGpuIndex { get; set; }
            public bool? UseCudaTurbo { get; set; }
        }

        /// <summary>
        /// Initializes the configuration manager, resolves persistent paths, and loads the user manifest.
        /// Incorporates a temporary test override block to force CloudAPI mode targeting the base Gemini domain.
        /// This ensures the networking layer correctly triggers the native Gemini streaming protocol.
        /// </summary>
        public override void _Ready()
        {
            Instance = this;
            _settingsDirectory = ProjectSettings.GlobalizePath("user://settings");
            _configFilePath = Path.Combine(_settingsDirectory, "preferences.json");
            _presetsFilePath = ProjectSettings.GlobalizePath("user://presets.json");

            _downloadManager = GetNodeOrNull<Logic.Network.DownloadManager>("/root/DownloadManager");

            // Restores the application state from the local preferences file.
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
                
                // Attempt to deserialize as the dual-track manifest architecture first.
                var manifest = JsonSerializer.Deserialize<EngineManifest>(jsonString, options);
                
                if (manifest?.VulkanBaseline != null)
                {
                    if (UseCudaTurbo && manifest.CudaTurbo != null)
                    {
                        GD.Print("ConfigManager: Operating on CUDA Turbo download manifest track.");
                        return manifest.CudaTurbo;
                    }
                    
                    GD.Print("ConfigManager: Operating on Vulkan Baseline download manifest track.");
                    return manifest.VulkanBaseline;
                }

                // Fallback for legacy flat JSON engines format
                GD.Print("ConfigManager: Operating on legacy single-track download manifest.");
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
        private static async Task<bool> DownloadEnginesFromGitHub(string destinationPath)
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

        /// <summary>
        /// Synchronizes class properties into the ConfigState DTO and persists the resulting JSON to the filesystem.
        /// Extended to safely capture topological network changes and structural performance tiers.
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
                    ActiveTTSModel = ActiveTTSModel,
                    CloudApiUrl = CloudApiUrl,
                    CloudApiKey = CloudApiKey,
                    CloudModelName = this.CloudModelName,
                    DarkMode = this.DarkMode,
                    NetworkState = this.CurrentNetworkState,
                    PerformanceTier = this.CurrentPerformanceTier,
                    ActiveProfilePath = this.ActiveProfilePath,
                    PerformanceProfile = this.PerformanceProfile,
                    ToolPermissions = this.ToolPermissions,
                    PersistedSelectedAiMode = this.PersistedSelectedAiMode,
                    PersistedToolTimeActive = this.PersistedToolTimeActive,
                    PersistedToolWebSearchActive = this.PersistedToolWebSearchActive,
                    PersistedToolMcpActive = this.PersistedToolMcpActive,
                    PersistedWorkspacePath = this.PersistedWorkspacePath,
                    PinnedChats = this.PinnedChats,
                    SelectedGpuIndex = this.SelectedGpuIndex,
                    UseCudaTurbo = this.UseCudaTurbo
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
        /// Reads the preference manifest from disk and restores the application state, 
        /// including local engine paths, cloud service parameters, network topology, and performance tiers.
        /// Includes fallback validation checks to handle structural schema changes safely.
        /// </summary>
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

                    CloudApiUrl = state.CloudApiUrl ?? "https://api.openai.com/v1";
                    CloudApiKey = state.CloudApiKey ?? string.Empty;
                    CloudModelName = state.CloudModelName ?? string.Empty;
                    if (state.DarkMode.HasValue) DarkMode = state.DarkMode.Value;
                    if (state.NetworkState.HasValue) CurrentNetworkState = state.NetworkState.Value;
                    if (state.PerformanceTier.HasValue) CurrentPerformanceTier = state.PerformanceTier.Value;
                    if (state.ActiveProfilePath != null) ActiveProfilePath = state.ActiveProfilePath;
                    if (state.PerformanceProfile != null) PerformanceProfile = state.PerformanceProfile;
                    if (state.ToolPermissions != null) ToolPermissions = state.ToolPermissions;
                    if (state.PersistedSelectedAiMode.HasValue) PersistedSelectedAiMode = state.PersistedSelectedAiMode.Value;
                    if (state.PersistedToolTimeActive.HasValue) PersistedToolTimeActive = state.PersistedToolTimeActive.Value;
                    if (state.PersistedToolWebSearchActive.HasValue) PersistedToolWebSearchActive = state.PersistedToolWebSearchActive.Value;
                    if (state.PersistedToolMcpActive.HasValue) PersistedToolMcpActive = state.PersistedToolMcpActive.Value;
                    if (state.PersistedWorkspacePath != null) PersistedWorkspacePath = state.PersistedWorkspacePath;
                    if (state.PinnedChats != null) PinnedChats = state.PinnedChats;
                    if (state.SelectedGpuIndex.HasValue) SelectedGpuIndex = state.SelectedGpuIndex.Value;
                    if (state.UseCudaTurbo.HasValue) UseCudaTurbo = state.UseCudaTurbo.Value;
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
        private static async Task<bool> DownloadPresetsFromGitHub(string destinationPath)
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