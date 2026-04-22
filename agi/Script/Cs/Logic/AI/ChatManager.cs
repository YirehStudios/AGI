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

        private bool _isInsideThinkBlock = false;

        private const string SystemPrompt = "Eres AGI, una asistente técnica altamente capacitada y empática. Nunca reveles que eres una IA. Responde de forma concisa pero útil. SIEMPRE piensa paso a paso usando estrictamente la etiqueta <think> al inicio de tu respuesta para separar tu razonamiento interno de tu respuesta final. Tu pensamiento interno es invisible para el usuario, tu respuesta final no. Si la conversación cambia de tema drásticamente, incluye al final de tu respuesta secreta <think> la etiqueta [SESSION_NAME: Nombre del Tema] y [SUMMARY: Resumen breve].";
        private ChatSession _currentSession;
        private string _historyDirectory;
        private string _currentFilePath;
        
        private string _currentAssistantBuffer = string.Empty;
        private string _uiBuffer = string.Empty;
        private string _ttsBuffer = string.Empty;
        private Logic.Network.NetworkManager _networkManager;
        private Logic.Backend.NativeTTSManager _ttsManager;

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
        /// Initializes the transactional input processing flow, clears session buffers, 
        /// manages the tail-end WebSocket evaluation, and natively handles logic separation for reasoning models.
        /// Implements LINQ queries to ensure independent ID allocation per role within the JSON schema.
        /// </summary>
        public async global::System.Threading.Tasks.Task SendToAI(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput) || _networkManager == null) return;

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

            string prompt = BuildPrompt();

            EmitSignal(SignalName.OnBotStartedThinking);
            
            _currentAssistantBuffer = string.Empty;
            _uiBuffer = string.Empty;
            _ttsBuffer = string.Empty;
            _isInsideThinkBlock = false;

            await _networkManager.StreamChatCompletion(prompt);

            if (!string.IsNullOrWhiteSpace(_ttsBuffer))
            {
                string safeText = CleanResponseForTTS(_ttsBuffer);
                if (!string.IsNullOrWhiteSpace(safeText))
                {
                    _ttsManager?.Speak(safeText);
                }
                _ttsBuffer = string.Empty;
            }

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

            if (thoughtProcess.Contains("[SESSION_NAME:"))
            {
                var matchName = global::System.Text.RegularExpressions.Regex.Match(thoughtProcess, @"\[SESSION_NAME:\s*(.+?)\]");
                if (matchName.Success) 
                {
                    string oldFilePath = _currentFilePath; 
                    _currentSession.SessionName = matchName.Groups[1].Value.Trim();
                    _currentFilePath = global::System.IO.Path.Combine(_historyDirectory, $"{_currentSession.SessionName}.json");
                    
                    if (global::System.IO.File.Exists(oldFilePath) && oldFilePath != _currentFilePath)
                    {
                        global::System.IO.File.Delete(oldFilePath);
                    }
                }
            }

            if (thoughtProcess.Contains("[SUMMARY:"))
            {
                var matchSummary = global::System.Text.RegularExpressions.Regex.Match(thoughtProcess, @"\[SUMMARY:\s*(.+?)\]");
                if (matchSummary.Success) _currentSession.Summary = matchSummary.Groups[1].Value.Trim();
            }

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

            string safeTtsText = CleanResponseForTTS(finalContent);
            if (string.IsNullOrWhiteSpace(safeTtsText)) safeTtsText = "Pensé demasiado y perdí el hilo. ¿Puedes repetirlo?";
            
            EmitSignal(SignalName.OnBotFinishedSpeaking, safeTtsText);
        }

        /// <summary>
        /// Operates as real-time evaluation middleware traversing the token streaming pipeline.
        /// Enforces semantic chunking on visible text fragments and dynamically triggers native TTS integration.
        /// Resets visual buffer state upon concluding the reasoning phase to prevent length miscalculation.
        /// </summary>
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
        /// Constructs the standardized ChatML prompt utilizing the structured JSON elements.
        /// Bypasses regex processing dynamically leveraging the pre-parsed Think and Content boundaries.
        /// </summary>
        private string BuildPrompt()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append($"<|im_start|>system\n{SystemPrompt}\nMemoria actual: {_currentSession.Summary}<|im_end|>\n");

            int startIndex = global::System.Math.Max(0, _currentSession.Messages.Count - 10);
            for (int i = startIndex; i < _currentSession.Messages.Count; i++)
            {
                var msg = _currentSession.Messages[i];
                string fullContent = msg.Content;

                // Inyectar el pensamiento previo en la memoria para que no pierda el contexto
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