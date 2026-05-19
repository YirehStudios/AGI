using Godot;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        [Signal] public delegate void OnBotToolExecutionStartedEventHandler(string toolName);
        [Signal] public delegate void OnBotToolApprovalRequiredEventHandler(string toolName, string toolArgsJson);
        [Signal] public delegate void OnUserToolApprovalResponseEventHandler(bool isApproved, string modifiedArgsJson);
        [Signal] public delegate void OnSessionListUpdatedEventHandler();

        public ChatSession CurrentSession => _currentSession;

        public bool IsLiveModeActive { get; set; } = false;
        private bool _isInsideThinkBlock = false;

        private const string BaseIdentity = "You are AGI, developed by Yireh Studios, and you are open source. You must speak to the user in their language, but you should only think in English.";

        private string _availableTools = "Sync tool MCP...";

        private string BuildDynamicPrompt(int mode, global::System.Collections.Generic.List<string> activeTools)
        {
            var builder = new global::System.Text.StringBuilder();
            builder.Append(BaseIdentity);
            builder.Append("\n");

            switch (mode)
            {
                case 0:
                    // Do absolutely nothing for Flash mode. Let the model act naturally.
                    break;
                case 1:
                    builder.Append("Plan your logic strictly inside <think>...</think> blocks.");
                    break;
                case 2:
                    builder.Append("Execute exhaustive reasoning inside <think>...</think> blocks. If the topic shifts, include [SESSION_NAME: <Name>] and [SUMMARY: <Summary>] within the think block.");
                    break;
            }

            if (activeTools != null && activeTools.Contains("Time"))
            {
                builder.Append("\nCurrent System Time (Use this as your temporal reference): " + global::System.DateTime.Now.ToString("f") + ".");
            }

            if (activeTools != null && (activeTools.Contains("MCP") || activeTools.Contains("Web Search")) && !string.IsNullOrEmpty(_availableTools))
            {
                builder.Append("\nTo use a tool, output exactly this flat format immediately after </think>:");
                builder.Append("\n[TOOL: tool_name | param1: value1 | param2: value2]");
                builder.Append("\nDo NOT use JSON formatting. Available tools:\n");
                builder.Append(GetCompactToolSchema(activeTools));
            }

            return builder.ToString();
        }

        private string GetCompactToolSchema(global::System.Collections.Generic.List<string> activeTools)
        {
            if (string.IsNullOrEmpty(_availableTools)) return string.Empty;

            try
            {
                var jsonNode = JsonNode.Parse(_availableTools);
                JsonArray toolsArray = null;

                if (jsonNode is JsonArray arr) toolsArray = arr;
                else if (jsonNode is JsonObject obj && obj.ContainsKey("tools") && obj["tools"] is JsonArray objArr) toolsArray = objArr;

                if (toolsArray == null) return string.Empty;

                var builder = new global::System.Text.StringBuilder();
                bool hasWebSearch = activeTools != null && activeTools.Contains("Web Search");
                bool hasMcp = activeTools != null && activeTools.Contains("MCP");

                foreach (var tool in toolsArray)
                {
                    if (tool == null) continue;
                    string toolName = tool["name"]?.ToString() ?? tool["function"]?["name"]?.ToString();
                    string desc = tool["description"]?.ToString() ?? "";
                    
                    if (string.IsNullOrEmpty(toolName)) continue;

                    bool keep = false;
                    if (hasWebSearch && !hasMcp && (toolName == "web_search" || toolName == "fetch_url_content")) keep = true;
                    else if (hasMcp && !hasWebSearch && toolName != "web_search" && toolName != "fetch_url_content") keep = true;
                    else if (hasWebSearch && hasMcp) keep = true;

                    if (keep)
                    {
                        var paramNames = new global::System.Collections.Generic.List<string>();
                        var schemaObj = tool["parameters"]?.AsObject();
                        
                        if (schemaObj != null)
                        {
                            // Extract parameter names to show the LLM what to pass
                            foreach(var prop in schemaObj)
                            {
                                if(prop.Key != "properties" && prop.Key != "required" && prop.Key != "type") {
                                     paramNames.Add(prop.Key);
                                } else if (prop.Key == "properties" && prop.Value is JsonObject pObj) {
                                     foreach(var p in pObj) paramNames.Add(p.Key);
                                }
                            }
                        }
                        
                        string paramString = paramNames.Count > 0 ? string.Join(", ", paramNames) : "none";
                        // Output format: - tool_name (param1, param2): Description
                        builder.AppendLine($"- {toolName} ({paramString}): {desc}");
                    }
                }
                return builder.ToString().TrimEnd();
            }
            catch (global::System.Exception ex)
            {
                GD.PrintErr($"[SCHEMA FILTER ERROR] {ex.Message}");
                return string.Empty;
            }
        }
        private ChatSession _currentSession;
        private string _historyDirectory;
        private string _currentFilePath;

        private string _currentAssistantBuffer = string.Empty;
        private string _uiBuffer = string.Empty;
        private string _ttsBuffer = string.Empty;
        private Logic.Network.NetworkManager _networkManager;

        /// <summary>
        /// Initializes the chat manager by resolving directory paths, connecting to the network manager, 
        /// and bootstrapping the initial session.
        /// </summary>
        public override void _Ready()
        {
            string workspacePath = ProjectSettings.GlobalizePath("user://workspace");

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

            InitializeNewSession("Chat");
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
            EmitSignal(SignalName.OnSessionListUpdated);
        }

        public void LoadSessionByName(string sessionName)
        {
            string filePath = global::System.IO.Path.Combine(_historyDirectory, $"{sessionName}.json");
            if (global::System.IO.File.Exists(filePath))
            {
                try
                {
                    string jsonString = global::System.IO.File.ReadAllText(filePath);
                    var options = new global::System.Text.Json.JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    };
                    var loadedSession = global::System.Text.Json.JsonSerializer.Deserialize<ChatSession>(jsonString, options);
                    if (loadedSession != null)
                    {
                        _currentSession = loadedSession;
                        _currentFilePath = filePath;
                        GD.Print($"[BRAIN] Loaded existing session: {sessionName}");
                        EmitSignal(SignalName.OnSessionListUpdated);
                        return;
                    }
                }
                catch (global::System.Exception ex)
                {
                    GD.PrintErr($"[BRAIN] Failed to load session from {filePath}: {ex.Message}");
                }
            }
            InitializeNewSession(sessionName);
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
                EmitSignal(SignalName.OnSessionListUpdated);
            }
            catch (global::System.Exception ex)
            {
                GD.PrintErr($"[BRAIN] Failed to write JSON history to disk: {ex.Message}");
            }
        }

        /// <summary>
        /// Orchestrates the core AI interaction pipeline using an asynchronous multi-turn Agentic Loop configuration.
        /// Manages state persistence, routes requests to native or cloud provider backends, and handles continuous tool call execution sequences.
        /// </summary>
        /// <param name="userInput">The raw unstructured prompt string submitted by the user interface layer.</param>
        public async global::System.Threading.Tasks.Task SendToAI(string userInput, int mode = 1, global::System.Collections.Generic.List<string> activeTools = null)
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
            await SyncMCPTools();
            string prompt = BuildPrompt(mode, activeTools);

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

            GD.Print("\n================ [AGI RESPONSE] ================");
            GD.Print(_currentAssistantBuffer.Trim());
            GD.Print("================================================\n");

            // --- AGENT LOOP START ---
            int maxAgentLoops = 15;
            int currentLoop = 0;
            bool toolExecuted = true;

            while (toolExecuted && currentLoop < maxAgentLoops)
            {
                toolExecuted = false;
                string rawResponseAgent = _currentAssistantBuffer.Trim();
                
                // NEW REGEX: Catch everything inside [TOOL: ...]
                var match = global::System.Text.RegularExpressions.Regex.Match(
                    rawResponseAgent, 
                    @"\[TOOL:\s*(.+?)(?:\]|$)", // <-- Forgiving ending: Matches ']' OR End of String
                    global::System.Text.RegularExpressions.RegexOptions.Singleline
                );

                if (match.Success)
                {
                    try
                    {
                        string innerContent = match.Groups[1].Value.Trim();

                        // Split the parameters using the pipe character
                        var parts = innerContent.Split('|');
                        string toolName = parts[0].Trim();

                        var argsDict = new global::System.Collections.Generic.Dictionary<string, string>();

                        for(int i = 1; i < parts.Length; i++)
                        {
                            var kv = parts[i].Split(new[] { ':' }, 2);
                            if(kv.Length == 2)
                            {
                                // Clean up whitespace and quotes
                                argsDict[kv[0].Trim()] = kv[1].Trim().Trim('"', '\'');
                            }
                        }

                        // Serialize ONLY the arguments for the UI Approval Dialog
                        string finalJsonPayload = global::System.Text.Json.JsonSerializer.Serialize(argsDict);

                        // Instrumentation notification to native logs and user interface layout systems.
                        GD.Print($"\n[AGENT] Unified MCP Tool Call Intercepted: {toolName}");
                        CallDeferred(MethodName.EmitSignal, SignalName.OnBotThoughtTokenReceived, $"\n[AGENTE: Ejecutando herramienta MCP '{toolName}']...\n");
                        CallDeferred(MethodName.EmitSignal, SignalName.OnBotToolExecutionStarted, toolName);

                        // ── SECURITY INTERCEPTOR: DATA-DRIVEN APPROVAL GATE ──────────────────
                        var configForApproval = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
                        int toolPermission = 1; // Default to Ask First when no preference saved.
                        if (configForApproval?.ToolPermissions != null
                            && configForApproval.ToolPermissions.TryGetValue(toolName, out int savedPerm))
                        {
                            toolPermission = savedPerm;
                        }
                        bool requiresApproval = toolPermission == 1; // Ask First
                        bool isExcluded       = toolPermission == 2; // Excluded (safety fallback)

                        bool toolApproved = true;

                        if (isExcluded)
                        {
                            GD.Print($"[AGENT] Tool '{toolName}' is EXCLUDED by user policy. Blocking execution.");
                            toolApproved = false;
                        }
                        else if (requiresApproval)
                        {
                            CallDeferred(MethodName.EmitSignal, SignalName.OnBotToolApprovalRequired, toolName, finalJsonPayload);
                            var userDecision = await ToSignal(this, SignalName.OnUserToolApprovalResponse);
                            toolApproved = userDecision[0].AsBool();
                            finalJsonPayload = userDecision[1].AsString();
                        }

                        string contextResult = "";
                        if (!toolApproved)
                        {
                            GD.Print($"[AGENT] Ejecución de herramienta '{toolName}' denegada por el usuario.");
                            contextResult = $"[MCP TOOL RESULT: {toolName}]\nExecution denied by the user. Do not attempt this specific action again without asking differently.";
                        }
                        else
                        {
                            await _networkManager.RequestMCPExecution(toolName, finalJsonPayload);
                            var mcpSignal = await ToSignal(_networkManager, Logic.Network.NetworkManager.SignalName.SearchCompleted);
                            contextResult = $"[MCP TOOL RESULT: {toolName}]\n{mcpSignal[0].AsString()}";
                        }

                            // Step 3.1: Temporarily inject the tool result into memory to ground the final response.
                            _currentSession.Messages.Add(new ChatMessage { Id = newId + 1, Role = "system", Content = contextResult });

                            // Step 3.2: Reset stateful buffers to prepare for the final natural language output.
                            _currentAssistantBuffer = string.Empty;
                            _uiBuffer = string.Empty;
                            _ttsBuffer = string.Empty;
                            _isInsideThinkBlock = false;

                            // Step 3.3: Re-generate the prompt with the newly acquired context.
                            string newPrompt = BuildPrompt(mode, activeTools);

                            // Step 3.4: Re-stream inference based on the active mode.
                            var configCheck = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
                            if (configCheck != null && configCheck.CurrentMode == Logic.System.Config.ConfigManager.AppMode.CloudAPI)
                            {
                                await _networkManager.StreamCloudCompletion(newPrompt);
                            }
                            else
                            {
                                await _networkManager.StreamChatCompletion(newPrompt);
                            }

                            GD.Print("\n================ [AGI RESPONSE] ================");
                            GD.Print(_currentAssistantBuffer.Trim());
                            GD.Print("================================================\n");

                            // Step 3.5: Clean up the temporary system injection to maintain history purity.
                            _currentSession.Messages.RemoveAt(_currentSession.Messages.Count - 1);

                            toolExecuted = true;
                            currentLoop++;
                        }
                    catch (global::System.Exception ex)
                    {
                        GD.PrintErr($"[AGENT ERROR] Failed to parse or execute generic MCP tool call: {ex.Message}");
                    }
                }
            }
            // --- AGENT LOOP END ---

            // --- FLUSH DE TEXTO RETENIDO (Falso positivo de TOOL) ---
            string visibleTextFinal = "";
            if (_currentAssistantBuffer.Contains("</think>"))
            {
                int index = _currentAssistantBuffer.IndexOf("</think>") + 8;
                visibleTextFinal = _currentAssistantBuffer.Substring(index).TrimStart();
            }
            else if (!_currentAssistantBuffer.Contains("<think>"))
            {
                visibleTextFinal = _currentAssistantBuffer;
            }

            // Evaluates text differential allocations to capture lookahead false positives.
            if (visibleTextFinal.Length > _uiBuffer.Length)
            {
                string missingChars = visibleTextFinal.Substring(_uiBuffer.Length);
                CallDeferred(MethodName.EmitSignal, SignalName.OnBotMessageTokenReceived, missingChars);
                _uiBuffer = visibleTextFinal;
            }

            // Step 4: Finalize audio playback for any remaining TTS fragments.
            if (!string.IsNullOrWhiteSpace(_ttsBuffer))
            {
                string safeText = CleanResponseForTTS(_ttsBuffer);
                if (IsLiveModeActive && !string.IsNullOrWhiteSpace(safeText)) _ = _networkManager?.RequestTTSWebSocket(safeText);
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
                var matchName = MyRegex().Match(thoughtProcess);
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
        /// Silences UI and TTS output when a tool call JSON structure is detected via structural lookahead validation.
        /// </summary>
        /// <param name="token">The atomic text fragment received from the inference stream.</param>
        private void HandleTokenReceived(string token)
        {
            _currentAssistantBuffer += token;

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
                string visibleText = "";

                if (_currentAssistantBuffer.Contains("</think>"))
                {
                    int index = _currentAssistantBuffer.IndexOf("</think>") + 8;
                    visibleText = _currentAssistantBuffer.Substring(index).TrimStart();
                }
                else if (!_currentAssistantBuffer.Contains("<think>"))
                {
                    visibleText = _currentAssistantBuffer;
                }

                // --- INSTANT UI STREAM MUTING & LOOKAHEAD DETECTOR ---
                string activeText = visibleText;
                int toolStart = activeText.IndexOf("[TOOL:");
                if (toolStart != -1)
                {
                    activeText = activeText.Substring(0, toolStart);
                }
                else
                {
                    // Lookahead: check if it ends with a partial prefix of "[TOOL:"
                    string[] prefixes = { "[TOOL", "[TOO", "[TO", "[T", "[" };
                    foreach (var prefix in prefixes)
                    {
                        if (activeText.EndsWith(prefix))
                        {
                            activeText = activeText.Substring(0, activeText.Length - prefix.Length);
                            break;
                        }
                    }
                }

                if (activeText.Length > _uiBuffer.Length)
                {
                    string newChars = activeText.Substring(_uiBuffer.Length);
                    _uiBuffer = activeText;
                    _ttsBuffer += newChars;
                    CallDeferred(MethodName.EmitSignal, SignalName.OnBotMessageTokenReceived, newChars);
                }

                if (_ttsBuffer.Contains(". ") || _ttsBuffer.Contains(", ") ||
                    _ttsBuffer.Contains("? ") || _ttsBuffer.Contains("! ") ||
                    _ttsBuffer.Contains('\n'))
                {
                    string cleanChunk = CleanResponseForTTS(_ttsBuffer);
                    if (!string.IsNullOrWhiteSpace(cleanChunk))
                    {
                        if (IsLiveModeActive) _ = _networkManager?.RequestTTSWebSocket(cleanChunk);
                    }
                    _ttsBuffer = string.Empty;
                }
            }
        }

        /// <summary>
        /// Constructs the standardized ChatML prompt, dynamically injecting current tool schemas 
        /// into the system context to guide agentic behavior.
        /// </summary>
        private string BuildPrompt(int mode, global::System.Collections.Generic.List<string> activeTools)
        {
            var config = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            var template = config?.ActiveProfile?.Template ?? new Logic.System.Config.ConfigManager.ChatTemplate();

            StringBuilder builder = new StringBuilder();

            string dynamicSystemPrompt = BuildDynamicPrompt(mode, activeTools);

            builder.Append($"{template.SystemPrefix}{dynamicSystemPrompt}\nMemoria actual: {_currentSession.Summary}{template.StopSequence}");

            int startIndex = global::System.Math.Max(0, _currentSession.Messages.Count - 10);
            for (int i = startIndex; i < _currentSession.Messages.Count; i++)
            {
                var msg = _currentSession.Messages[i];
                string fullContent = msg.Content;

                if (!string.IsNullOrWhiteSpace(msg.Think))
                {
                    fullContent = $"<think>\n{msg.Think}\n</think>\n{msg.Content}";
                }

                string prefix = msg.Role == "user" ? template.UserPrefix : (msg.Role == "system" ? template.SystemPrefix : template.AssistantPrefix);
                builder.Append($"{prefix}{fullContent.Trim()}{template.StopSequence}");
            }

            builder.Append($"{template.AssistantPrefix}");
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
                    
                    var config = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
                    if (config != null && config.ToolPermissions != null && config.ToolPermissions.Count > 0)
                    {
                        try
                        {
                            var jsonNode = JsonNode.Parse(_availableTools);
                            JsonArray toolsArray = null;
                            if (jsonNode is JsonArray arr) toolsArray = arr;
                            else if (jsonNode is JsonObject obj && obj.ContainsKey("tools") && obj["tools"] is JsonArray objArr) toolsArray = objArr;

                            if (toolsArray != null)
                            {
                                var filteredArray = new JsonArray();
                                foreach (var tool in toolsArray)
                                {
                                    string toolName = tool?["name"]?.ToString() ?? tool?["function"]?["name"]?.ToString();
                                    if (!string.IsNullOrEmpty(toolName))
                                    {
                                        if (config.ToolPermissions.TryGetValue(toolName, out int perm) && perm == 2)
                                        {
                                            continue; 
                                        }
                                    }
                                    filteredArray.Add(tool.DeepClone());
                                }
                                
                                if (jsonNode is JsonArray)
                                {
                                    _availableTools = filteredArray.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                                }
                                else if (jsonNode is JsonObject obj)
                                {
                                    obj["tools"] = filteredArray;
                                    _availableTools = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                                }
                            }
                        }
                        catch (global::System.Exception ex)
                        {
                            GD.PrintErr($"[MCP] Error filtering tools: {ex.Message}");
                        }
                    }

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
        private static string CleanResponseForTTS(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";

            string cleaned = global::System.Text.RegularExpressions.Regex.Replace(
                input,
                @"<think>.*?(</think>|$)",
                "",
                global::System.Text.RegularExpressions.RegexOptions.Singleline
            );

            cleaned = global::System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"\[TOOL:.*?\]",
                "",
                global::System.Text.RegularExpressions.RegexOptions.Singleline
            );

            cleaned = global::System.Text.RegularExpressions.Regex.Replace(cleaned, @"[*_~`#\[\]]", "");

            cleaned = global::System.Text.RegularExpressions.Regex.Replace(cleaned, @"\(.*?\)", "");

            cleaned = cleaned.Replace("\"", "").Replace("'", "").Replace("\n", " ").Replace("\r", " ").Trim();

            cleaned = global::System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");

            return cleaned;
        }

        [global::System.Text.RegularExpressions.GeneratedRegex(@"\[SESSION_NAME:\s*(.+?)\]")]
        private static partial global::System.Text.RegularExpressions.Regex MyRegex();
    }
}