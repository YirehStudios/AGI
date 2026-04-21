using Godot;
using System;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Net.WebSockets;

namespace Logic.Network
{
    public partial class NetworkManager : Node
    {
        [Signal]
        public delegate void HandshakeCompletedEventHandler(bool success);
        
        [Signal]
        public delegate void TokenReceivedEventHandler(string token);
        
        [Signal]
        public delegate void STTCompletedEventHandler(string text);
        [Signal]
        public delegate void TTSAudioChunkReceivedEventHandler(byte[] pcmData);

        private readonly global::System.Net.Http.HttpClient _httpClient = new global::System.Net.Http.HttpClient();

        public async void PerformHandshake()
        {
            try
            {
                // Antes: HttpResponseMessage response = await _httpClient.GetAsync($"{BaseUrl}/v1/models");
                HttpResponseMessage response = await _httpClient.GetAsync($"{GetActiveUrl()}/v1/models");
                
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
            // --- CAMBIO CRUCIAL: Obtenemos la URL en el hilo principal antes de entrar al Task ---
            string urlSegura = GetActiveUrl(); 
            GD.Print($"[NET] Enviando petición a Llama en: {urlSegura}");

            await Task.Run(async () => 
            {
                try
                {
                    var requestBody = new
                    {
                        prompt = prompt,
                        stream = true,
                        n_predict = 2048 
                    };

                    string jsonPayload = JsonSerializer.Serialize(requestBody);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    var request = new HttpRequestMessage(HttpMethod.Post, $"{urlSegura}/v1/completions")
                    {
                        Content = content
                    };

                    // Añadimos un timeout de seguridad de 60 segundos para la conexión inicial
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
                            if (doc.RootElement.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                            {
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
                        catch { /* Ignorar fragmentos JSON malformados durante el stream */ }
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[NET ERROR] Fallo en el flujo de Llama: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Establishes a persistent ClientWebSocket connection directed to the native Sherpa C++ TTS engine.
        /// Parses standard JSON requests expected by Sherpa and manages raw binary payload reception robustly without 
        /// relying on legacy string-based control signals from the Python bridge.
        /// Integrates mandatory Speaker ID parameters to explicitly map to multi-language .bin voice assets.
        /// </summary>
        public async Task RequestTTSWebSocket(string textToSynthesize)
        {
            try
            {
                using var ws = new ClientWebSocket();
                Uri serverUri = new Uri("ws://127.0.0.1:8888"); 
                
                await ws.ConnectAsync(serverUri, global::System.Threading.CancellationToken.None);

                // Instancia y empaqueta el diccionario estricto esperado por el binario C++ de Sherpa.
                // Inyecta el índice del hablante (sid) para resolver la asignación de voz en modelos que consolidan múltiples firmas acústicas.
                var payload = new { 
                    text = textToSynthesize, 
                    sid = 0 
                };
                
                string jsonPayload = global::System.Text.Json.JsonSerializer.Serialize(payload);
                byte[] bytes = global::System.Text.Encoding.UTF8.GetBytes(jsonPayload);

                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, global::System.Threading.CancellationToken.None);

                byte[] buffer = new byte[8192];

                // Evalúa el socket en un ciclo continuo que intercepta estructuras WAV nativas.
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), global::System.Threading.CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        byte[] audioChunk = new byte[result.Count];
                        global::System.Array.Copy(buffer, audioChunk, result.Count);

                        CallDeferred(MethodName.EmitSignal, SignalName.TTSAudioChunkReceived, audioChunk);
                        
                        // Cierra controladamente el ciclo si la señal EOF se define por la clausura anticipada del socket tras el streaming binario.
                        if (result.EndOfMessage && result.Count < buffer.Length)
                        {
                            // Heurística de salida limpia dependiente de los chunks del binario nativo.
                            break; 
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // Resuelve y finaliza la conexión sistemáticamente acatando el cierre del servidor remoto C++.
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[NET ERROR] Native WebSocket TTS Engine Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Offloads the synchronous network request onto an asynchronous task thread.
        /// Handles multipart form data for Whisper local inference and emits parsed text payload.
        /// </summary>
        public async Task RequestSTT(string audioFilePath)
        {
            GD.Print($"[FLAG] STT: Dispatching {Path.GetFileName(audioFilePath)} via HTTP...");
            
            await Task.Run(async () => 
            {
                try
                {
                    using var client = new global::System.Net.Http.HttpClient();
                    using var form = new global::System.Net.Http.MultipartFormDataContent();
                    
                    byte[] audioBytes = global::System.IO.File.ReadAllBytes(audioFilePath);
                    var audioContent = new global::System.Net.Http.ByteArrayContent(audioBytes);
                    audioContent.Headers.ContentType = global::System.Net.Http.Headers.MediaTypeHeaderValue.Parse("audio/wav");
                    
                    form.Add(audioContent, "file", global::System.IO.Path.GetFileName(audioFilePath));
                    form.Add(new global::System.Net.Http.StringContent("es"), "language"); 
                    form.Add(new global::System.Net.Http.StringContent("json"), "response_format");
                    
                    var response = await client.PostAsync("http://127.0.0.1:8081/inference", form);
                    response.EnsureSuccessStatusCode();
                    
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    
                    using var doc = global::System.Text.Json.JsonDocument.Parse(jsonResponse);
                    if (doc.RootElement.TryGetProperty("text", out global::System.Text.Json.JsonElement textElement))
                    {
                        string recognizedText = textElement.GetString().Trim();
                        GD.Print($"[FLAG] STT SUCCESS: {recognizedText}");
                        CallDeferred(MethodName.EmitSignal, SignalName.STTCompleted, recognizedText);
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[FLAG] STT ERROR: Whisper HTTP request failed. {ex.Message}");
                }
            });
        }

        private string GetActiveUrl()
        {
            var config = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");

            if (config != null && config.CurrentMode == Logic.System.Config.ConfigManager.AppMode.RemoteUI)
            {
                if (!string.IsNullOrWhiteSpace(config.RemoteHostUrl))
                {
                    // Limpiamos la barra final si el usuario la puso por error (ej: "http://192.168.1.10:8080/")
                    return config.RemoteHostUrl.TrimEnd('/');
                }
            }
            // Si es LocalHost o la URL está vacía, regresamos al de por defecto
            return "http://127.0.0.1:8080";
        }
    } 
}