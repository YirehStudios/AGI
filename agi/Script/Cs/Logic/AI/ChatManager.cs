// Archivo: ChatManager.cs

using Godot;
using System.Collections.Generic;
using System.Text;

namespace Logic.Lite
{
    /// <summary>
    /// Data structure representing a single chat message entity for persistent JSON storage.
    /// </summary>
    public class ChatMessage
    {
        public int Id { get; set; }
        public string Role { get; set; }
        public string Content { get; set; }
        public string Timestamp { get; set; }
    }

    /// <summary>
    /// Data structure for tracking sentiment and emotional context across the session.
    /// </summary>
    public class ChatEmotions
    {
        public float Angry { get; set; } = 0f;
        public float Happy { get; set; } = 0f;
        public float Tired { get; set; } = 0f;
    }

    /// <summary>
    /// Data structure encapsulating the entirety of a conversation session.
    /// </summary>
    public class ChatSession
    {
        public string SessionName { get; set; } = "Current_Session";
        public string Summary { get; set; } = "";
        public ChatEmotions CurrentEmotion { get; set; } = new ChatEmotions();
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
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

        private const string SystemPrompt = "You are AGI, an empathetic, highly skilled technical assistant running locally. You must help users with technical tasks. Never reveal you are an AI. Act exclusively under the traits of your defined identity. Respond in the length and format necessary to solve the user's query, but be concise. Always think step by step and use the <tool_call> tag to separate your thought process from the final answer. The user will interact with you through a custom interface that can read your thoughts in real time as you type them, so use the <tool_call> tag wisely to optimize for response latency. If you need to ask the user a clarifying question, do so immediately without using the <tool_call> tag. Always end your response with a clear answer or action, never leaving the user hanging.";
        
        private ChatSession _currentSession;
        private string _historyDirectory;
        private string _currentFilePath;
        
        private string _currentAssistantBuffer = string.Empty;
        private string _uiBuffer = string.Empty;
        private string _ttsBuffer = string.Empty;
        private Logic.Network.NetworkManager _networkManager;

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
        /// and manages the tail-end WebSocket evaluation of the remaining text string.
        /// </summary>
        public async global::System.Threading.Tasks.Task SendToAI(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput) || _networkManager == null) return;

            int newId = _currentSession.Messages.Count + 1;
            _currentSession.Messages.Add(new ChatMessage 
            { 
                Id = newId, Role = "user", Content = userInput, Timestamp = global::System.DateTime.Now.ToString("O") 
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
                    // Migrated execution to WebSocket endpoint.
                    _ = _networkManager.RequestTTSWebSocket(safeText);
                }
                _ttsBuffer = string.Empty;
            }

            string safeLogText = CleanResponseForTTS(_currentAssistantBuffer);
            if (string.IsNullOrWhiteSpace(safeLogText)) safeLogText = "Entendido."; 

            _currentSession.Messages.Add(new ChatMessage 
            { 
                Id = newId + 1, Role = "assistant", Content = safeLogText, Timestamp = global::System.DateTime.Now.ToString("O") 
            });
            SaveSession();

            _currentSession.Messages.Add(new ChatMessage 
            { 
                Id = newId + 1, Role = "assistant", Content = _currentAssistantBuffer, Timestamp = global::System.DateTime.Now.ToString("O") 
            });
            SaveSession();

            string safeTtsText = CleanResponseForTTS(_currentAssistantBuffer);
            if (string.IsNullOrWhiteSpace(safeTtsText)) safeTtsText = "Pensé demasiado y perdí el hilo. ¿Puedes repetirlo?";

            EmitSignal(SignalName.OnBotFinishedSpeaking, safeTtsText);
        }

        /// <summary>
        /// Operates as real-time evaluation middleware traversing the token streaming pipeline.
        /// Enforces semantic chunking on visible text fragments and dynamically triggers WebSocket TTS streaming.
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
                _uiBuffer = _currentAssistantBuffer; 
                
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

                if (_ttsBuffer.Contains('.') || _ttsBuffer.Contains(',') || 
                    _ttsBuffer.Contains('?') || _ttsBuffer.Contains('!') || 
                    _ttsBuffer.Contains('\n'))
                {
                    string cleanChunk = CleanResponseForTTS(_ttsBuffer);
                    if (!string.IsNullOrWhiteSpace(cleanChunk))
                    {
                        // Migrated execution to WebSocket endpoint.
                        _ = _networkManager?.RequestTTSWebSocket(cleanChunk);
                    }
                    _ttsBuffer = string.Empty;
                }
            }
        }

        /// <summary>
        /// Constructs the standardized ChatML prompt injecting system identity and a sliding window context.
        /// </summary>
        private string BuildPrompt()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append($"<|im_start|>system\n{SystemPrompt}<|im_end|>\n");

            // Implements a strict sliding context window of the last 10 interactions to prevent RAM exhaustion.
            int startIndex = global::System.Math.Max(0, _currentSession.Messages.Count - 10);
            for (int i = startIndex; i < _currentSession.Messages.Count; i++)
            {
                var msg = _currentSession.Messages[i];
                string contentToSend = msg.Content;

                // Si es un mensaje viejo de la IA, le borramos el <think> para que no se envenene su memoria
                if (msg.Role == "assistant") 
                {
                    contentToSend = global::System.Text.RegularExpressions.Regex.Replace(
                        contentToSend, @"<think>.*?(</think>|$)", "", global::System.Text.RegularExpressions.RegexOptions.Singleline).Trim();
                }

                builder.Append($"<|im_start|>{msg.Role}\n{contentToSend}<|im_end|>\n");
            }

            // --- ESTAS DOS LÍNEAS FALTABAN ---
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