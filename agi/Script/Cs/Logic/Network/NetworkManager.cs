using Godot;
using System;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace Logic.Network
{
    /// <summary>
    /// Se encarga de la comunicación con el LLM nativo utilizando el estándar de la API de OpenAI.
    /// Emite los tokens individuales recibidos por Server-Sent Events (SSE).
    /// </summary>
    public partial class NetworkManager : Node
    {
        [Signal]
        public delegate void HandshakeCompletedEventHandler(bool success);
        
        [Signal]
        public delegate void TokenReceivedEventHandler(string token);

        private const string BaseUrl = "http://127.0.0.1:8080";
        private readonly global::System.Net.Http.HttpClient _httpClient = new global::System.Net.Http.HttpClient();

        public async void PerformHandshake()
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{BaseUrl}/v1/models");
                
                if (response.IsSuccessStatusCode)
                {
                    GD.Print("NetworkManager: Handshake Successful. Native server verified.");
                    EmitSignal(SignalName.HandshakeCompleted, true);
                }
                else
                {
                     GD.PrintErr("NetworkManager: ERR_NET_API - API Unreachable or Invalid State.");
                     EmitSignal(SignalName.HandshakeCompleted, false);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"NetworkManager: ERR_NET_001 - Connection Refused. {ex.Message}");
                EmitSignal(SignalName.HandshakeCompleted, false);
            }
        }

        /// <summary>
        /// Realiza la petición POST utilizando el esquema de OpenAI (chat/completions) y decodifica el flujo continuo.
        /// </summary>
        public async Task StreamChatCompletion(string prompt)
        {
            try
            {
                // 1. Usamos 'prompt' crudo en lugar de 'messages' para evitar el doble formateo
                var requestBody = new
                {
                    prompt = prompt,
                    stream = true,
                    n_predict = 512 // Límite de seguridad para que no hable infinitamente
                };

                string jsonPayload = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // 2. Cambiamos la URL a /v1/completions (La ruta para texto crudo y puro)
                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/completions")
                {
                    Content = content
                };

                using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using Stream responseStream = await response.Content.ReadAsStreamAsync();
                using StreamReader reader = new StreamReader(responseStream);

                while (!reader.EndOfStream)
                {
                    string line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

                    string data = line.Substring(6);
                    if (data == "[DONE]") break;

                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(data);
                        JsonElement root = doc.RootElement;
                        if (root.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                        {
                            // 3. En completions crudos, el token viene directo en la propiedad 'text'
                            if (choices[0].TryGetProperty("text", out JsonElement contentElement))
                            {
                                string token = contentElement.GetString();
                                if (!string.IsNullOrEmpty(token))
                                {
                                    CallDeferred(MethodName.EmitSignal, SignalName.TokenReceived, token);
                                }
                            }
                        }
                    }
                    catch (JsonException) { }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"NetworkManager: Stream processing failure. {ex.Message}");
            }
        }
    }
}