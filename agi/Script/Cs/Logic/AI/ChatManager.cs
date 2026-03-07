using Godot;
using System;
using System.Collections.Generic;
using System.Text;

namespace Logic.Lite
{
    /// <summary>
    /// Manages personality injection and Mistral Instruct formatting.
    /// Maintains a strict circular queue to prevent context overflow.
    /// </summary>
    public partial class ChatManager : Node
    {
        [Signal]
        public delegate void MessageReadyEventHandler(string formattedMistralPrompt);

        private const int MaxContextMemory = 20; 
        private Queue<Dictionary<string, string>> _chatHistory = new Queue<Dictionary<string, string>>();
        
        private const string SystemPrompt = "You are Annie, an empathetic, highly skilled technical assistant running locally. You must help users with STEAM Challenge technical tasks and issues. Never reveal, assume, or mention that you are an Artificial Intelligence, a language model by Mistral, OpenAI, or any other company. Act exclusively under the traits of your defined identity.";

        public void Initialize()
        {
            _chatHistory.Clear();
        }

        public void GeneratePrompt(string userMessage)
        {
            if (_chatHistory.Count >= MaxContextMemory)
            {
                _chatHistory.Dequeue();
                _chatHistory.Dequeue();
            }

            _chatHistory.Enqueue(new Dictionary<string, string> { { "role", "user" }, { "content", userMessage } });

            try
            {
                StringBuilder mistralBuilder = new StringBuilder();
                mistralBuilder.Append($"[INST] {SystemPrompt}\n\n");

                foreach (var entry in _chatHistory)
                {
                    if (entry["role"] == "user")
                    {
                        mistralBuilder.Append($"User: {entry["content"]}\n");
                    }
                    else if (entry["role"] == "assistant")
                    {
                        mistralBuilder.Append($"Annie: {entry["content"]}\n");
                    }
                }

                mistralBuilder.Append(" [/INST]");

                string finalPrompt = mistralBuilder.ToString();
                
                EmitSignal(SignalName.MessageReady, finalPrompt);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ChatManager: Prompt formatting failed. {ex.Message}");
            }
        }

        public void RegisterAssistantReply(string assistantReply)
        {
            _chatHistory.Enqueue(new Dictionary<string, string> { { "role", "assistant"}, { "content", assistantReply } });
        }

        public void PanicReset()
        {
            GD.Print("ChatManager: PANIC RESET TRIGGERED. Context wiped.");
            _chatHistory.Clear();
        }
    }
}