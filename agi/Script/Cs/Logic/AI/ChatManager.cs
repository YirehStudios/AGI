using Godot;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization; 

namespace Logic.Lite
{
    /// <summary>
    /// Data structure representing a single chat message entity for persistent JSON storage.
    /// Supports independent ID tracking for users and assistants alongside separated reasoning streams.
    /// </summary>
    public class ChatMessage
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? IdUser { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? IdAssistant { get; set; }
        
        public int Id { get; set; }
        public string Role { get; set; }
        public string Timestamp { get; set; }
        public string Think { get; set; } = string.Empty;
        public string Content { get; set; }
    }

    /// <summary>
    /// Data structure for tracking sentiment and the complete emotional spectrum across the session.
    /// </summary>
    public class ChatEmotions
    {
        public float Angry { get; set; } = 0.0f;
        public float Happy { get; set; } = 0.8f;
        public float Tired { get; set; } = 0.2f;
        public float Bored { get; set; } = 0.0f;
        public float Sleep { get; set; } = 0.9f;
        public float Sad { get; set; } = 1.0f;
        public float Flirty { get; set; } = 0.1f;
        public float Curiosity { get; set; } = 0.5f;
        public float Confusion { get; set; } = 0.0f;
        public float Confidence { get; set; } = 1.0f;
        public float Surprise { get; set; } = 0.0f;
        public float Sarcasm { get; set; } = 0.0f;
        public float Stress { get; set; } = 0.0f;
        public float Interest { get; set; } = 0.8f;
        public float Affection { get; set; } = 0.6f;
        public float Patience { get; set; } = 1.0f;
        public float Pride { get; set; } = 0.4f;
        public float Shame { get; set; } = 0.0f;
    }

    /// <summary>
    /// Data structure encapsulating the entirety of a conversation session including abstract summaries.
    /// Utilizes strict global namespace resolution to prevent shadowing from local 'Logic.System' hierarchies.
    /// </summary>
    public class ChatSession
    {
        public string SessionName { get; set; } = "Current_Session";
        public string Summary { get; set; } = "Nueva conversación iniciada.";
        public ChatEmotions CurrentEmotion { get; set; } = new ChatEmotions();
        public global::System.Collections.Generic.List<ChatMessage> Messages { get; set; } = new global::System.Collections.Generic.List<ChatMessage>();
    }

    /// <summary>
    /// The central LLM manager. Handles JSON history persistence, Mistral prompt formatting,
    /// and ensures the execution environment maintains a clean context window (preventing prompt poisoning).
    /// </summary>
    public partial class ChatManager : Node
    {
        [Signal] public delegate void OnBotStartedThinkingEventHandler();
        [Signal] public delegate void OnBotThoughtTokenReceivedEventHandler(string token);
        [Signal] public delegate void OnBotThoughtFinishedEventHandler();
        [Signal] public delegate void OnBotMessageTokenReceivedEventHandler(string token); 
        [Signal] public delegate void OnBotFinishedSpeakingEventHandler(string fullResponse);
        /// <summary>
        /// Defines the foundational behavior, empathy constraints, and tool-set availability for the AGI agent.
        /// Implements a multi-tool Agentic pattern that allows the model to switch between web exploration 
        /// and local system interaction via structured JSON intercepts.
        /// </summary>
        private static string SystemPromptInit = "Eres AGI, una asistente técnica altamente capacitada. Tienes acceso a tu propio entorno de ejecución mediante herramientas. " +
        "Herramientas disponibles:\n" +
        "1. Búsqueda Web: Úsala para obtener información actual o externa. Formato: {\"tool\": \"web_search\", \"query\": \"términos\"}\n" +
        "2. Consola Local: Úsala para ejecutar comandos en la PC del usuario (listar archivos, leer código, revisar sistema). Formato: {\"tool\": \"os_command\", \"command\": \"comando bash o cmd\"}\n" +
        "REGLA ESTRICTA: Si necesitas usar una herramienta, detén tu respuesta y genera ÚNICAMENTE el JSON exacto de una herramienta a la vez. No escribas <think> ni texto adicional. " +
        "El sistema ejecutará la acción y te devolverá los resultados. JAMÁS incluyas el código JSON en tu respuesta final visible para el usuario. " +
        "Si NO usas herramientas, piensa paso a paso usando la etiqueta <think> al inicio de tu respuesta para separar tu razonamiento. " +
        "Si la conversación cambia, incluye [SESSION_NAME: Nombre] y [SUMMARY: Resumen] al final de tu <think>.";
        private bool _isInsideThinkBlock = false;

        private string SystemPrompt = $"{SystemPromptInit}";
        private string _availableTools = "Sync tool MCP...";
        private ChatSession _currentSession;
        private string _historyDirectory;
        private string _currentFilePath;
        
        private string _currentAssistantBuffer = string.Empty;
        private string _uiBuffer = string.Empty;
        private string _ttsBuffer = string.Empty;
        private Logic.Network.NetworkManager _networkManager;
        private Logic.Backend.NativeTTSManager _ttsManager;

        /// <summary>
        /// Initializes the chat manager by resolving directory paths, connecting to the network manager, 
        /// and bootstrapping the initial session and MCP tool schemas.
        /// </summary>
        public override void _Ready()
        {
            _historyDirectory = ProjectSettings.GlobalizePath("user://history");
            if (!global::System.IO.Directory.Exists(_historyDirectory))
            {
                global::System.IO.Directory.CreateDirectory(_historyDirectory);
            }

            _networkManager = GetNodeOrNull<Logic.Network.NetworkManager>("/root/NetworkManager");
            if (_networkManager != null)
            {
                _networkManager.TokenReceived += HandleTokenReceived;
            }

            _ttsManager = GetNodeOrNull<Logic.Backend.NativeTTSManager>("/root/NativeTTSManager");

            InitializeNewSession("Chat_Default");
            
            // Triggers the asynchronous synchronization sequence to fetch available tool schemas from the MCP server.
            _ = SyncMCPTools();
        }

        /// <summary>
        /// Instantiates a new context window and writes the initial structured payload to the OS filesystem.
        /// </summary>
        /// <param name="sessionName">The target filename identifier for the JSON log.</param>
        public void InitializeNewSession(string sessionName)
        {
            _currentSession = new ChatSession { SessionName = sessionName };
            _currentFilePath = global::System.IO.Path.Combine(_historyDirectory, $"{sessionName}.json");
            SaveSession();
        }

        /// <summary>
        /// Serializes the current active session object and overrides the persistent JSON log.
        /// </summary>
        private void SaveSession()
        {
            try
            {
                var options = new global::System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                string jsonString = global::System.Text.Json.JsonSerializer.Serialize(_currentSession, options);
                global::System.IO.File.WriteAllText(_currentFilePath, jsonString);
            }
            catch (global::System.Exception ex)
            {
                GD.PrintErr($"[BRAIN] Failed to write JSON history to disk: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Orchestrates the core AI interaction pipeline using an Agentic Loop pattern.
        /// It manages session persistence, telemetry logging, and intercepts JSON tool calls 
        /// to perform autonomous web searches before delivering the final response.
        /// Dynamically routes inference between local Llama and Cloud API based on the active configuration.
        /// </summary>
        /// <param name="userInput">The raw text input received from the user interface.</param>
        public async global::System.Threading.Tasks.Task SendToAI(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput) || _networkManager == null) return;

            // Step 1: Record the user turn in the persistent session history.
            int newId = _currentSession.Messages.Count + 1;
            
            _currentSession.Messages.Add(new ChatMessage 
            { 
                IdUser = _currentSession.Messages.FindAll(m => m.Role == "user").Count + 1,
                Id = newId, 
                Role = "user", 
                Content = userInput, 
                Timestamp = global::System.DateTime.Now.ToString("O") 
            });
            SaveSession();

            // Step 2: Construct the initial LLM prompt based on the current conversation state.
            string prompt = BuildPrompt();

            // Notify UI layer that inference has commenced.
            EmitSignal(SignalName.OnBotStartedThinking);
            
            // Reset stateful buffers for the new transaction.
            _currentAssistantBuffer = string.Empty;
            _uiBuffer = string.Empty;
            _ttsBuffer = string.Empty;
            _isInsideThinkBlock = false;

            // Telemetry: Log the exact prompt being dispatched to the inference server.
            GD.Print("\n================ [AGI PROMPT DUMP] ================\n");
            GD.Print(prompt);
            GD.Print("\n===================================================\n");

            // Step 3: Initiate primary inference stream with dynamic backend routing.
            var config = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            if (config != null && config.CurrentMode == Logic.System.Config.ConfigManager.AppMode.CloudAPI)
            {
                await _networkManager.StreamCloudCompletion(prompt);
            }
            else
            {
                await _networkManager.StreamChatCompletion(prompt);
            }

            // --- AGENT LOOP START ---
            // Evaluates if the AI produced a tool-calling instruction (JSON) instead of natural language.
            string rawResponseAgent = _currentAssistantBuffer.Trim();
            int jsonStart = rawResponseAgent.IndexOf('{');
            int jsonEnd = rawResponseAgent.LastIndexOf('}');

            // Validates the structural integrity of the potential JSON payload.
            if (jsonStart != -1 && jsonEnd != -1 && jsonEnd > jsonStart && rawResponseAgent.Contains("\"tool\""))
            {
                try
                {
                    // Surgical extraction: Isolates the JSON block from any markdown or LLM stop tokens.
                    string jsonBlock = rawResponseAgent.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    using var doc = global::System.Text.Json.JsonDocument.Parse(jsonBlock);
                    
                    if (doc.RootElement.TryGetProperty("tool", out var toolElement))
                    {
                        string toolName = toolElement.GetString();
                        
                        // Log the interception and notify the UI of agent activity.
                        GD.Print($"\n[AGENT] Unified MCP Tool Call Intercepted: {toolName}");
                        CallDeferred(MethodName.EmitSignal, SignalName.OnBotThoughtTokenReceived, $"\n[AGENTE: Ejecutando herramienta MCP '{toolName}']...\n");

                        // Generic Dispatch: Replaces the old if/else hardcoded handlers with a unified call to the MCP gateway.
                        await _networkManager.RequestMCPExecution(toolName, jsonBlock);
                        
                        // Wait for the universal result signal from the networking layer.
                        var mcpSignal = await ToSignal(_networkManager, Logic.Network.NetworkManager.SignalName.SearchCompleted);
                        string contextResult = $"[MCP TOOL RESULT: {toolName}]\n{mcpSignal[0].AsString()}";

                        // Step 3.1: Temporarily inject the tool result into memory to ground the final response.
                        _currentSession.Messages.Add(new ChatMessage { Id = newId + 1, Role = "system", Content = contextResult });

                        // Step 3.2: Reset stateful buffers to prepare for the final natural language output.
                        _currentAssistantBuffer = string.Empty;
                        _uiBuffer = string.Empty;
                        _ttsBuffer = string.Empty;
                        _isInsideThinkBlock = false;

                        // Step 3.3: Re-generate the prompt with the newly acquired context.
                        string newPrompt = BuildPrompt();
                        
                        // Step 3.4: Re-stream inference based on the active mode (Cloud vs Local).
                        var configCheck = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
                        if (configCheck != null && configCheck.CurrentMode == Logic.System.Config.ConfigManager.AppMode.CloudAPI)
                        {
                            await _networkManager.StreamCloudCompletion(newPrompt);
                        }
                        else
                        {
                            await _networkManager.StreamChatCompletion(newPrompt);
                        }
                        
                        // Step 3.5: Clean up the temporary system injection to maintain history purity.
                        _currentSession.Messages.RemoveAt(_currentSession.Messages.Count - 1);
                    }
                }
                catch (global::System.Exception ex)
                {
                    GD.PrintErr($"[AGENT ERROR] Failed to parse or execute generic MCP tool call: {ex.Message}");
                }
            }
            // --- AGENT LOOP END ---

            // Step 4: Finalize audio playback for any remaining TTS fragments.
            if (!string.IsNullOrWhiteSpace(_ttsBuffer))
            {
                string safeText = CleanResponseForTTS(_ttsBuffer);
                if (!string.IsNullOrWhiteSpace(safeText)) _ttsManager?.Speak(safeText);
                _ttsBuffer = string.Empty;
            }

            // Step 5: Parse reasoning (think) and content for final storage.
            string rawResponse = _currentAssistantBuffer;
            string thoughtProcess = "";
            string finalContent = rawResponse;
            int thinkStart = rawResponse.IndexOf("<think>");
            int thinkEnd = rawResponse.IndexOf("</think>");

            if (thinkStart != -1 && thinkEnd != -1)
            {
                thoughtProcess = rawResponse.Substring(thinkStart + 7, thinkEnd - (thinkStart + 7)).Trim();
                finalContent = rawResponse.Substring(thinkEnd + 8).Trim();
            }
            else if (thinkStart != -1 && thinkEnd == -1) 
            {
                thoughtProcess = rawResponse.Substring(thinkStart + 7).Trim();
                finalContent = "";
            }

            // Step 6: Extract dynamic metadata (Name/Summary) from the reasoning block.
            if (thoughtProcess.Contains("[SESSION_NAME:"))
            {
                var matchName = global::System.Text.RegularExpressions.Regex.Match(thoughtProcess, @"\[SESSION_NAME:\s*(.+?)\]");
                if (matchName.Success) 
                {
                    string oldPath = _currentFilePath;
                    _currentSession.SessionName = matchName.Groups[1].Value.Trim();
                    _currentFilePath = global::System.IO.Path.Combine(_historyDirectory, $"{_currentSession.SessionName}.json");
                    if (global::System.IO.File.Exists(oldPath) && oldPath != _currentFilePath) global::System.IO.File.Delete(oldPath);
                }
            }

            if (thoughtProcess.Contains("[SUMMARY:"))
            {
                var matchSummary = global::System.Text.RegularExpressions.Regex.Match(thoughtProcess, @"\[SUMMARY:\s*(.+?)\]");
                if (matchSummary.Success) _currentSession.Summary = matchSummary.Groups[1].Value.Trim();
            }

            // Step 7: Finalize the transaction by saving the assistant turn.
            _currentSession.Messages.Add(new ChatMessage 
            { 
                IdAssistant = _currentSession.Messages.FindAll(m => m.Role == "assistant").Count + 1,
                Id = newId + 1, 
                Role = "assistant", 
                Think = thoughtProcess,
                Content = finalContent, 
                Timestamp = global::System.DateTime.Now.ToString("O") 
            });
            SaveSession();

            // Final notification to the UI and TTS systems.
            string safeTtsText = CleanResponseForTTS(finalContent);
            if (string.IsNullOrWhiteSpace(safeTtsText)) safeTtsText = "Pensé demasiado y perdí el hilo. ¿Puedes repetirlo?";
            
            EmitSignal(SignalName.OnBotFinishedSpeaking, safeTtsText);
        }

        /// <summary>
        /// Operates as real-time evaluation middleware traversing the token streaming pipeline.
        /// Silences UI and TTS output when a tool call JSON structure is detected.
        /// </summary>
        /// <param name="token">The atomic text fragment received from the inference stream.</param>
        private void HandleTokenReceived(string token)
        {
            _currentAssistantBuffer += token;
            
            // Agent Interception: If the stream starts with a JSON bracket, it's a tool call. Silence the UI and TTS.
            if (_currentAssistantBuffer.TrimStart().StartsWith("{")) return;

            if (!_isInsideThinkBlock && _currentAssistantBuffer.Contains("<think>") && !_currentAssistantBuffer.Contains("</think>"))
            {
                _isInsideThinkBlock = true;
                return; 
            }
            else if (_isInsideThinkBlock && _currentAssistantBuffer.Contains("</think>"))
            {
                _isInsideThinkBlock = false;
                _uiBuffer = ""; 
                
                CallDeferred(MethodName.EmitSignal, SignalName.OnBotThoughtFinished);
                return;
            }

            if (_isInsideThinkBlock)
            {
                string safeThoughtToken = token.Replace("<think>", "").Replace("\n", " ");
                if (!string.IsNullOrWhiteSpace(safeThoughtToken))
                {
                    CallDeferred(MethodName.EmitSignal, SignalName.OnBotThoughtTokenReceived, safeThoughtToken);
                }
            }
            else
            {
                if (_currentAssistantBuffer.Contains("</think>"))
                {
                    int index = _currentAssistantBuffer.IndexOf("</think>") + 8;
                    string visibleText = _currentAssistantBuffer.Substring(index).TrimStart();

                    if (visibleText.Length > _uiBuffer.Length)
                    {
                        string newChars = visibleText.Substring(_uiBuffer.Length);
                        _uiBuffer = visibleText;
                        _ttsBuffer += newChars;
                        CallDeferred(MethodName.EmitSignal, SignalName.OnBotMessageTokenReceived, newChars);
                    }
                }
                else if (!_currentAssistantBuffer.Contains("<think>"))
                {
                    if (_currentAssistantBuffer.Length > _uiBuffer.Length)
                    {
                        string newChars = _currentAssistantBuffer.Substring(_uiBuffer.Length);
                        _uiBuffer = _currentAssistantBuffer;
                        _ttsBuffer += newChars;
                        CallDeferred(MethodName.EmitSignal, SignalName.OnBotMessageTokenReceived, newChars);
                    }
                }

                if (_ttsBuffer.Contains(". ") || _ttsBuffer.Contains(", ") || 
                    _ttsBuffer.Contains("? ") || _ttsBuffer.Contains("! ") || 
                    _ttsBuffer.Contains("\n"))
                {
                    string cleanChunk = CleanResponseForTTS(_ttsBuffer);
                    if (!string.IsNullOrWhiteSpace(cleanChunk))
                    {
                        _ttsManager?.Speak(cleanChunk);
                    }
                    _ttsBuffer = string.Empty;
                }
            }
        }

        /// <summary>
        /// Constructs the standardized ChatML prompt, dynamically injecting current tool schemas 
        /// into the system context to guide agentic behavior.
        /// </summary>
        private string BuildPrompt()
        {
            StringBuilder builder = new StringBuilder();
            string timeString = global::System.DateTime.Now.ToString("f");
            string currentTimeContext = $"Fecha y hora actual del sistema: {timeString}.";
            
            // Injects the synchronized MCP tool list into the foundational prompt instructions.
            string dynamicSystemPrompt = $"{SystemPromptInit}\n\n[ MCP DYNAMIC TOOLS SCHEMA ]\n{_availableTools}";
            
            builder.Append($"<|im_start|>system\n{dynamicSystemPrompt}\n{currentTimeContext}\nMemoria actual: {_currentSession.Summary}<|im_end|>\n");

            int startIndex = global::System.Math.Max(0, _currentSession.Messages.Count - 10);
            for (int i = startIndex; i < _currentSession.Messages.Count; i++)
            {
                var msg = _currentSession.Messages[i];
                string fullContent = msg.Content;

                if (!string.IsNullOrWhiteSpace(msg.Think))
                {
                    fullContent = $"<think>\n{msg.Think}\n</think>\n{msg.Content}";
                }

                builder.Append($"<|im_start|>{msg.Role}\n{fullContent.Trim()}<|im_end|>\n");
            }

            builder.Append("<|im_start|>assistant\n");
            return builder.ToString();
        }

        /// <summary>
        /// Retrieves the manifest of available tools from the local MCP server.
        /// This allows the AGI to dynamically discover new capabilities (Search, OS, Files) 
        /// and inject their schemas into the system context.
        /// </summary>
        public async global::System.Threading.Tasks.Task SyncMCPTools()
        {
            try
            {
                using var client = new global::System.Net.Http.HttpClient();
                // Targets the standardized MCP gateway port.
                var response = await client.GetAsync("http://127.0.0.1:8002/list_tools");
                
                if (response.IsSuccessStatusCode)
                {
                    _availableTools = await response.Content.ReadAsStringAsync();
                    GD.Print("[BRAIN] MCP Tools synchronized successfully.");
                }
            }
            catch (global::System.Exception ex)
            {
                GD.PrintErr($"[BRAIN] Failed to sync MCP tools: {ex.Message}");
                _availableTools = "Error: No se pudo conectar con el servidor MCP.";
            }
        }

        /// <summary>
        /// Processes string normalization utilizing Regular Expressions to obliterate tags and bash-breaking symbols.
        /// </summary>
        private string CleanResponseForTTS(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            
            string cleaned = global::System.Text.RegularExpressions.Regex.Replace(
                input, 
                @"<think>.*?(</think>|$)", 
                "", 
                global::System.Text.RegularExpressions.RegexOptions.Singleline
            );
            
            cleaned = global::System.Text.RegularExpressions.Regex.Replace(cleaned, @"[*_~`#\[\]]", "");

            cleaned = global::System.Text.RegularExpressions.Regex.Replace(cleaned, @"\(.*?\)", "");
            
            cleaned = cleaned.Replace("\"", "").Replace("'", "").Replace("\n", " ").Replace("\r", " ").Trim();
            
            cleaned = global::System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");
            
            return cleaned;
        }
    }
}