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
        public string ActiveModelCategory { get; set; } = "LLM";

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
        public string ActiveImageEngine { get; set; } = "sd_cpp";
        public string ActiveImageModel { get; set; } = "";
        public string ActiveVideoEngine { get; set; } = "sd_cpp";
        public string ActiveVideoModel { get; set; } = "";

        public int PersistedSelectedAiMode { get; set; } = 1; // Default to Focus (1)
        public int PersistedToolTimeActive { get; set; } = 1; // Default to Active (1)
        public int PersistedToolWebSearchActive { get; set; } = 1; // Default to Active (1)
        public int PersistedToolMcpActive { get; set; } = 1; // Default to Active (1)
        public int PersistedToolImageActive { get; set; } = 1; // Default to Active (1)
        public int PersistedToolVideoActive { get; set; } = 1; // Default to Active (1)
        public string PersistedWorkspacePath { get; set; } = ""; // User's custom workspace
        public List<string> PinnedChats { get; set; } = new List<string>();

        public bool TransModeEnabled { get; set; } = false;
        public float TransModeBlur { get; set; } = 3.5f;
        public float TransModeOpacity { get; set; } = 0.5f;
        public bool TransModeApplyToPopups { get; set; } = false;
        public float TransModePopupsBlur { get; set; } = 3.5f;
        public float TransModePopupsOpacity { get; set; } = 0.5f;
        public bool TransModeApplyToSubWindows { get; set; } = false;
        public float TransModeSubWindowsBlur { get; set; } = 3.5f;
        public float TransModeSubWindowsOpacity { get; set; } = 0.5f;

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
            public EngineUrls SdCpp { get; set; }

            [global::System.Text.Json.Serialization.JsonPropertyName("tts_server")]
            public TtsServerConfig TtsServer { get; set; }

            [global::System.Text.Json.Serialization.JsonPropertyName("search_server")]
            public TtsServerConfig search_server { get; set; }

            [global::System.Text.Json.Serialization.JsonPropertyName("mcp_server")]
            public TtsServerConfig McpServer { get; set; }

            [global::System.Text.Json.Serialization.JsonPropertyName("file_extractor")]
            public TtsServerConfig FileExtractor { get; set; }
        }

        /// <summary>
        /// Explicit target mapping for dynamic model components (e.g. UNET, CLIP, VAE).
        /// </summary>
        public class DownloadTarget
        {
            public string Url { get; set; }
            public string ComfySubfolder { get; set; } // "unet", "vae", "clip", "checkpoints", etc.
        }

        /// <summary>
        /// Define la estructura de los presets de modelos cargados desde el JSON externo.
        /// </summary>
        public class ModelPreset
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public List<string> DownloadLinks { get; set; }
            public List<DownloadTarget> AdvancedDownloads { get; set; }
            public long ExpectedSize { get; set; }
            public string Category { get; set; } = "LLM"; // LLM, STT, TTS, Image, Video
            public string PromptStrategy { get; set; } = "description";
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
            public string Category { get; set; } = "LLM"; // Defaults to LLM if not specified
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

        public class EnginePerformanceProfile
        {
            public int CpuThreads { get; set; } = 4;
            public int GpuLayers { get; set; } = 99;
        }

        public class ComputePerformanceProfile
        {
            public EnginePerformanceProfile Llm { get; set; } = new EnginePerformanceProfile();
            public EnginePerformanceProfile Image { get; set; } = new EnginePerformanceProfile();
            public EnginePerformanceProfile Video { get; set; } = new EnginePerformanceProfile();
            public EnginePerformanceProfile Whisper { get; set; } = new EnginePerformanceProfile();
            public EnginePerformanceProfile PyScripts { get; set; } = new EnginePerformanceProfile();
            
            // Legacy global options or fallback
            public int CpuThreads { get; set; } = 4;
            public int GpuLayers { get; set; } = 99;
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
            public string ActiveImageEngine { get; set; }
            public string ActiveImageModel { get; set; }
            public string ActiveVideoEngine { get; set; }
            public string ActiveVideoModel { get; set; }
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
            public int? PersistedToolImageActive { get; set; }
            public int? PersistedToolVideoActive { get; set; }
            public string PersistedWorkspacePath { get; set; }
            public List<string> PinnedChats { get; set; }
            public bool? TransModeEnabled { get; set; }
            public float? TransModeBlur { get; set; }
            public float? TransModeOpacity { get; set; }
            public bool? TransModeApplyToPopups { get; set; }
            public float? TransModePopupsBlur { get; set; }
            public float? TransModePopupsOpacity { get; set; }
            public bool? TransModeApplyToSubWindows { get; set; }
            public float? TransModeSubWindowsBlur { get; set; }
            public float? TransModeSubWindowsOpacity { get; set; }
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
                
                using (JsonDocument doc = JsonDocument.Parse(jsonString))
                {
                    JsonElement root = doc.RootElement;
                    EngineConfig finalConfig = new EngineConfig();

                    // Parse root elements (Sherpa, Python, Servers)
                    JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    
                    if (root.TryGetProperty("Sherpa", out JsonElement sherpaEl))
                        finalConfig.Sherpa = JsonSerializer.Deserialize<EngineUrls>(sherpaEl.GetRawText(), options);
                        
                    if (root.TryGetProperty("Python", out JsonElement pythonEl))
                        finalConfig.Python = JsonSerializer.Deserialize<EngineUrls>(pythonEl.GetRawText(), options);
                        
                    if (root.TryGetProperty("tts_server", out JsonElement ttsEl))
                        finalConfig.TtsServer = JsonSerializer.Deserialize<TtsServerConfig>(ttsEl.GetRawText(), options);
                        
                    if (root.TryGetProperty("search_server", out JsonElement searchEl))
                        finalConfig.search_server = JsonSerializer.Deserialize<TtsServerConfig>(searchEl.GetRawText(), options);
                        
                    if (root.TryGetProperty("mcp_server", out JsonElement mcpEl))
                        finalConfig.McpServer = JsonSerializer.Deserialize<TtsServerConfig>(mcpEl.GetRawText(), options);
                        
                    if (root.TryGetProperty("file_extractor", out JsonElement fileExtEl))
                        finalConfig.FileExtractor = JsonSerializer.Deserialize<TtsServerConfig>(fileExtEl.GetRawText(), options);

                    // Parse dual-track engines
                    if (root.TryGetProperty("engines", out JsonElement enginesEl))
                    {
                        string targetTrack = UseCudaTurbo ? "cuda_turbo" : "vulkan_baseline";
                        if (!enginesEl.TryGetProperty(targetTrack, out JsonElement trackEl))
                        {
                            targetTrack = "vulkan_baseline";
                            enginesEl.TryGetProperty(targetTrack, out trackEl);
                        }

                        GD.Print($"ConfigManager: Operating on {targetTrack} download manifest track.");
                        
                        finalConfig.Llama = new EngineUrls();
                        finalConfig.Whisper = new EngineUrls();
                        
                        if (trackEl.TryGetProperty("llama_LinuxUrl", out JsonElement llamaLin)) finalConfig.Llama.LinuxUrl = llamaLin.GetString();
                        if (trackEl.TryGetProperty("llama_WindowsUrl", out JsonElement llamaWin)) finalConfig.Llama.WindowsUrl = llamaWin.GetString();
                        
                        if (trackEl.TryGetProperty("whisper_LinuxUrl", out JsonElement whisperLin)) finalConfig.Whisper.LinuxUrl = whisperLin.GetString();
                        if (trackEl.TryGetProperty("whisper_windowsUrl", out JsonElement whisperWin)) finalConfig.Whisper.WindowsUrl = whisperWin.GetString();
                        
                        finalConfig.SdCpp = new EngineUrls();
                        if (trackEl.TryGetProperty("sd_cpp_LinuxUrl", out JsonElement sdcppLin)) finalConfig.SdCpp.LinuxUrl = sdcppLin.GetString();
                        if (trackEl.TryGetProperty("sd_cpp_WindowsUrl", out JsonElement sdcppWin)) finalConfig.SdCpp.WindowsUrl = sdcppWin.GetString();
                    }
                    else
                    {
                        // Fallback for legacy flat JSON
                        GD.Print("ConfigManager: Operating on legacy single-track download manifest.");
                        finalConfig = JsonSerializer.Deserialize<EngineConfig>(jsonString, options);
                    }

                    return finalConfig;
                }
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
                    ActiveImageEngine = ActiveImageEngine,
                    ActiveImageModel = ActiveImageModel,
                    ActiveVideoEngine = ActiveVideoEngine,
                    ActiveVideoModel = ActiveVideoModel,
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
                    PersistedToolImageActive = this.PersistedToolImageActive,
                    PersistedToolVideoActive = this.PersistedToolVideoActive,
                    PersistedWorkspacePath = this.PersistedWorkspacePath,
                    PinnedChats = this.PinnedChats,
                    TransModeEnabled = this.TransModeEnabled,
                    TransModeBlur = this.TransModeBlur,
                    TransModeOpacity = this.TransModeOpacity,
                    TransModeApplyToPopups = this.TransModeApplyToPopups,
                    TransModePopupsBlur = this.TransModePopupsBlur,
                    TransModePopupsOpacity = this.TransModePopupsOpacity,
                    TransModeApplyToSubWindows = this.TransModeApplyToSubWindows,
                    TransModeSubWindowsBlur = this.TransModeSubWindowsBlur,
                    TransModeSubWindowsOpacity = this.TransModeSubWindowsOpacity,
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
                    if (!string.IsNullOrEmpty(state.ActiveImageEngine)) ActiveImageEngine = state.ActiveImageEngine;
                    if (!string.IsNullOrEmpty(state.ActiveImageModel)) ActiveImageModel = state.ActiveImageModel;
                    if (!string.IsNullOrEmpty(state.ActiveVideoEngine)) ActiveVideoEngine = state.ActiveVideoEngine;
                    if (!string.IsNullOrEmpty(state.ActiveVideoModel)) ActiveVideoModel = state.ActiveVideoModel;

                    CloudApiUrl = state.CloudApiUrl ?? "https://api.openai.com/v1";
                    CloudApiKey = state.CloudApiKey ?? string.Empty;
                    CloudModelName = state.CloudModelName ?? string.Empty;
                    if (state.DarkMode.HasValue) DarkMode = state.DarkMode.Value;
                    if (state.NetworkState.HasValue) CurrentNetworkState = state.NetworkState.Value;
                    if (state.PerformanceTier.HasValue) CurrentPerformanceTier = state.PerformanceTier.Value;
                    if (state.ActiveProfilePath != null) ActiveProfilePath = state.ActiveProfilePath;
                    if (state.PerformanceProfile != null) PerformanceProfile = state.PerformanceProfile;
                    if (state.ToolPermissions != null) 
                    {
                        ToolPermissions = state.ToolPermissions;
                        ToolPermissions.Remove("global_access");
                    }
                    if (state.PersistedSelectedAiMode.HasValue) PersistedSelectedAiMode = state.PersistedSelectedAiMode.Value;
                    if (state.PersistedToolTimeActive.HasValue) PersistedToolTimeActive = state.PersistedToolTimeActive.Value;
                    if (state.PersistedToolWebSearchActive.HasValue) PersistedToolWebSearchActive = state.PersistedToolWebSearchActive.Value;
                    if (state.PersistedToolMcpActive.HasValue) PersistedToolMcpActive = state.PersistedToolMcpActive.Value;
                    if (state.PersistedToolImageActive.HasValue) PersistedToolImageActive = state.PersistedToolImageActive.Value;
                    if (state.PersistedToolVideoActive.HasValue) PersistedToolVideoActive = state.PersistedToolVideoActive.Value;
                    if (state.PersistedWorkspacePath != null) PersistedWorkspacePath = state.PersistedWorkspacePath;
                    if (state.PinnedChats != null) PinnedChats = state.PinnedChats;

                    if (state.TransModeEnabled.HasValue) TransModeEnabled = state.TransModeEnabled.Value;
                    if (state.TransModeBlur.HasValue) TransModeBlur = state.TransModeBlur.Value;
                    if (state.TransModeOpacity.HasValue) TransModeOpacity = state.TransModeOpacity.Value;
                    if (state.TransModeApplyToPopups.HasValue) TransModeApplyToPopups = state.TransModeApplyToPopups.Value;
                    if (state.TransModePopupsBlur.HasValue) TransModePopupsBlur = state.TransModePopupsBlur.Value;
                    if (state.TransModePopupsOpacity.HasValue) TransModePopupsOpacity = state.TransModePopupsOpacity.Value;
                    if (state.TransModeApplyToSubWindows.HasValue) TransModeApplyToSubWindows = state.TransModeApplyToSubWindows.Value;
                    if (state.TransModeSubWindowsBlur.HasValue) TransModeSubWindowsBlur = state.TransModeSubWindowsBlur.Value;
                    if (state.TransModeSubWindowsOpacity.HasValue) TransModeSubWindowsOpacity = state.TransModeSubWindowsOpacity.Value;

                    if (state.SelectedGpuIndex.HasValue) SelectedGpuIndex = state.SelectedGpuIndex.Value;
                    if (state.UseCudaTurbo.HasValue) UseCudaTurbo = state.UseCudaTurbo.Value;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Failed to load configuration. Exception: {ex.Message}");
            }
        }

        public class PresetsCatalog
        {
            public List<ModelPreset> LLM { get; set; } = new List<ModelPreset>();
            public List<ModelPreset> STT { get; set; } = new List<ModelPreset>();
            public List<ModelPreset> TTS { get; set; } = new List<ModelPreset>();
            public List<ModelPreset> Image { get; set; } = new List<ModelPreset>();
            public List<ModelPreset> Video { get; set; } = new List<ModelPreset>();
        }

        public async Task<PresetsCatalog> GetOrDownloadPresetsAsync()
        {
            string resPresetsPath = ProjectSettings.GlobalizePath("res://Script/Cs/System/Config/presets.json");
            string targetPath = resPresetsPath;

            if (!File.Exists(resPresetsPath))
            {
                string userPresetsPath = ProjectSettings.GlobalizePath("user://presets.json");
                if (!File.Exists(userPresetsPath))
                {
                    await DownloadPresetsFromGitHub(userPresetsPath);
                }
                targetPath = userPresetsPath;
            }

            try
            {
                if (!File.Exists(targetPath)) return new PresetsCatalog();
                string jsonString = File.ReadAllText(targetPath);
                JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<PresetsCatalog>(jsonString, options) ?? new PresetsCatalog();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ConfigManager: Error leyendo presets catalog: {ex.Message}");
                return new PresetsCatalog();
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