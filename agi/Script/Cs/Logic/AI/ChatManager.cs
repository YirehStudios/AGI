using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Logic.Lite
{
    public partial class ChatManager : Node
    {
        [Signal]
        public delegate void MessageReadyEventHandler(string jsonPayload);

        private List<Dictionary<string, string>> _chatHistory = new List<Dictionary<string, string>>();
        
        // System prompt forcing the persona
        private const string SystemPrompt = "You are an assistant for the STEAM Challenge. You are NOT an AI. You help users with technical tasks.";

        public void Initialize()
        {
            // Reset history on init
            _chatHistory.Clear();
            _chatHistory.Add(new Dictionary<string, string> { { "role", "system" }, { "content", SystemPrompt } });
        }

        /// <summary>
        /// Constructs the JSON payload for the ComfyUI workflow using System.Text.Json.
        /// </summary>
        public void GeneratePrompt(string userMessage)
        {
            _chatHistory.Add(new Dictionary<string, string> { { "role", "user" }, { "content", userMessage } });

            try
            {
                // This structure mimics a ComfyUI workflow where Node 6 is a Text Input
                // The actual structure depends on the specific workflow.json loaded in the engine
                // Serializing history first to embed it as a string
                string serializedHistory = JsonSerializer.Serialize(_chatHistory);

                var workflow = new Dictionary<string, object>
                {
                    { "prompt_id", Guid.NewGuid().ToString() },
                    { "client_id", "GodotClient" },
                    { "inputs", new Dictionary<string, object> 
                        { 
                            { "text", serializedHistory }, // Passing history as context
                            { "seed", new Random().Next() }
                        } 
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(workflow);
                EmitSignal(SignalName.MessageReady, jsonPayload);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"ChatManager: Serialization failed. {ex.Message}");
            }
        }

        /// <summary>
        /// Emergency reset to clear context and hallucinations.
        /// </summary>
        public void PanicReset()
        {
            GD.Print("ChatManager: PANIC RESET TRIGGERED.");
            _chatHistory.Clear();
            _chatHistory.Add(new Dictionary<string, string> { { "role", "system" }, { "content", SystemPrompt } });
        }
    }
}