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
        [Signal]
        public delegate void SearchCompletedEventHandler(string markdownResults);

        private readonly global::System.Net.Http.HttpClient _httpClient = new global::System.Net.Http.HttpClient
        {
            Timeout = global::System.Threading.Timeout.InfiniteTimeSpan
        };

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
        /// Dispatches a POST request utilizing the OpenAI specification format, intercepts the 
        /// server-sent events stream, and streams tokens to the console and engine delegates in real-time.
        /// </summary>
        /// <param name="prompt">The absolute instruction and context template context payload.</param>
        public async Task StreamChatCompletion(string prompt)
        {
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
                                        GD.PrintRaw(token);
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
        /// Facilitates a high-performance streaming connection to cloud AI providers.
        /// Implements a Dual-Track protocol supporting native Google Gemini (streamGenerateContent) 
        /// and the standard OpenAI (chat/completions) Server-Sent Events (SSE) specification.
        /// Prints raw textual chunks onto the engine console frame as they arrive.
        /// </summary>
        /// <param name="prompt">The fully context-augmented prompt string for inference.</param>
        public async Task StreamCloudCompletion(string prompt)
        {
            var config = GetNodeOrNull<Logic.System.Config.ConfigManager>("/root/ConfigManager");
            if (config?.ActiveProfile == null || string.IsNullOrEmpty(config.ActiveProfile.ApiKey)) return;

            bool isGemini = config.ActiveProfile.EndpointUrl.Contains("googleapis.com");

            string requestUrl = isGemini
                ? $"{config.ActiveProfile.EndpointUrl.TrimEnd('/')}/models/{config.ActiveProfile.ModelId}:streamGenerateContent?alt=sse"
                : $"{config.ActiveProfile.EndpointUrl.TrimEnd('/')}/chat/completions";

            GD.Print($"[NET] Dispatching Cloud Request: {requestUrl}");

            await Task.Run(async () =>
            {
                try
                {
                    object requestBody = isGemini
                        ? (object)new { contents = new[] { new { parts = new[] { new { text = prompt } } } } }
                        : (object)new { model = config.ActiveProfile.ModelId, messages = new[] { new { role = "user", content = prompt } }, stream = true };

                    var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
                    {
                        Content = new StringContent(global::System.Text.Json.JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                    };

                    if (isGemini) request.Headers.Add("x-goog-api-key", config.ActiveProfile.ApiKey);
                    else request.Headers.Add("Authorization", $"Bearer {config.ActiveProfile.ApiKey}");

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                    if (!response.IsSuccessStatusCode)
                    {
                        string errorContext = await response.Content.ReadAsStringAsync();
                        GD.PrintErr($"[NET ERROR] Cloud AI API Failure: {response.StatusCode}. Details: {errorContext}");
                        if (response.StatusCode == (global::System.Net.HttpStatusCode)429)
                        {
                            CallDeferred(MethodName.EmitSignal, SignalName.TokenReceived, "\n[SYSTEM ERROR: Gemini API Quota Exceeded. Please wait a minute.]\n");
                        }
                        return;
                    }

                    using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
                    while (!reader.EndOfStream)
                    {
                        string line = await reader.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

                        string data = line.Substring(6).Trim();
                        using JsonDocument doc = JsonDocument.Parse(data);
                        string token = "";

                        if (isGemini)
                        {
                            if (doc.RootElement.TryGetProperty("candidates", out JsonElement candidates) && candidates.GetArrayLength() > 0)
                            {
                                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                                if (parts.GetArrayLength() > 0 && parts[0].TryGetProperty("text", out JsonElement textEl))
                                {
                                    token = textEl.GetString();
                                }
                            }
                        }
                        else
                        {
                            if (doc.RootElement.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                            {
                                var delta = choices[0].GetProperty("delta");
                                if (delta.TryGetProperty("content", out JsonElement contentElement))
                                {
                                    token = contentElement.GetString();
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(token))
                        {
                            GD.PrintRaw(token);
                            CallDeferred(MethodName.EmitSignal, SignalName.TokenReceived, token);
                        }
                    }
                }
                catch (Exception ex) { GD.PrintErr($"[NET CLOUD ERROR] {ex.Message}"); }
            });
        }

        /// <summary>
        /// Dispatches the exact validated JSON payload directly to the ununified Model Context Protocol (MCP) server gateway.
        /// Removes legacy dictionary structural rebuilding loops to strictly comply with backend payload validation schemas.
        /// </summary>
        /// <param name="toolName">The unique programmatic string identifier of the target capability.</param>
        /// <param name="jsonPayload">The complete, schema-conforming JSON specification block generated by the model.</param>
        public async Task RequestMCPExecution(string toolName, string jsonPayload)
        {
            try
            {
                // Deserialize the payload
                using JsonDocument parsedDoc = JsonDocument.Parse(jsonPayload);
                string finalMcpPayload;
                
                // If it's already wrapped, unwrap it recursively to be extremely bulletproof
                if (parsedDoc.RootElement.ValueKind == JsonValueKind.Object &&
                    (parsedDoc.RootElement.TryGetProperty("name", out _) || parsedDoc.RootElement.TryGetProperty("tool", out _)) &&
                    parsedDoc.RootElement.TryGetProperty("arguments", out JsonElement argsEl))
                {
                    JsonElement currentArgs = argsEl;
                    string currentName = toolName;
                    while (currentArgs.ValueKind == JsonValueKind.Object &&
                           (currentArgs.TryGetProperty("name", out JsonElement nameProp) || currentArgs.TryGetProperty("tool", out nameProp)) &&
                           currentArgs.TryGetProperty("arguments", out JsonElement innerArgs))
                    {
                        currentName = nameProp.GetString() ?? currentName;
                        currentArgs = innerArgs;
                    }
                    
                    var mcpRequest = new
                    {
                        tool = currentName,
                        arguments = currentArgs
                    };
                    var options = new JsonSerializerOptions { Encoder = global::System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                    finalMcpPayload = JsonSerializer.Serialize(mcpRequest, options);
                }
                else
                {
                    var mcpRequest = new
                    {
                        tool = toolName,
                        arguments = parsedDoc.RootElement
                    };
                    var options = new JsonSerializerOptions { Encoder = global::System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                    finalMcpPayload = JsonSerializer.Serialize(mcpRequest, options);
                }
                
                var content = new StringContent(finalMcpPayload, Encoding.UTF8, "application/json");

                // Outbox logging for continuous telemetry tracing of the dispatched request buffer.
                GD.Print($"\n[NET -> MCP] Enviando payload:\n{finalMcpPayload}");

                HttpResponseMessage response = await _httpClient.PostAsync("http://127.0.0.1:8002/call_tool", content);
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();

                // Inbound logging capturing execution results returned from the Python microservice pipeline.
                GD.Print($"\n[NET <- MCP] Respuesta recibida:\n{jsonResponse}");

                using JsonDocument resultDoc = JsonDocument.Parse(jsonResponse);
                if (resultDoc.RootElement.TryGetProperty("result", out JsonElement resElement))
                {
                    CallDeferred(MethodName.EmitSignal, SignalName.SearchCompleted, resElement.ToString());
                }
                else
                {
                    GD.PrintErr("[NET ERROR] MCP response missing 'result' key.");
                    CallDeferred(MethodName.EmitSignal, SignalName.SearchCompleted, $"Error: Unexpected JSON schema from MCP. Payload: {jsonResponse}");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[NET ERROR] MCP Execution Failed: {ex.Message}");
                CallDeferred(MethodName.EmitSignal, SignalName.SearchCompleted, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Dispatches a search request to the local Python microservice to retrieve web-augmented data.
        /// Sends a JSON payload containing the query and depth parameters to the FastAPI endpoint.
        /// Upon success, it extracts the Markdown-formatted results and notifies the UI layer.
        /// </summary>
        /// <param name="query">The search string to be processed by DuckDuckGo.</param>
        /// <param name="deepResearch">Flag to enable full-text extraction via Trafilatura.</param>
        public async Task RequestWebSearch(string query, bool deepResearch)
        {
            try
            {
                var payload = new
                {
                    query = query,
                    deep_research = deepResearch
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Diagnostic log tracking outbox query requests to the search microservice engine.
                GD.Print($"\n[NET -> SEARCH] Enviando consulta:\n{jsonPayload}");

                HttpResponseMessage response = await _httpClient.PostAsync("http://127.0.0.1:8000/search", content);
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();

                // Diagnostic log tracking inbound data frame buffer characteristics received from the search endpoint.
                GD.Print($"\n[NET <- SEARCH] Fragmentos recibidos (Longitud: {jsonResponse.Length} chars)");

                using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                if (doc.RootElement.TryGetProperty("results", out JsonElement resultsElement))
                {
                    string markdownResults = resultsElement.GetString();
                    CallDeferred(MethodName.EmitSignal, SignalName.SearchCompleted, markdownResults);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[NET ERROR] Search Microservice Request Failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Establishes an ephemeral WebSocket connection to the TTS microservice to request audio synthesis.
        /// Transmits the target text alongside a hardcoded voice profile identifier ('ef_dora') as strictly 
        /// defined by the architectural constraints to ensure pipeline stability.
        /// Accumulates the binary WAV chunks returned by the server to circumvent MTU fragmentation limits, 
        /// emitting the complete contiguous byte array once the transmission naturally concludes.
        /// </summary>
        /// <param name="textToSynthesize">The clean, plain-text string segment to be converted into speech.</param>
        public async Task RequestTTSWebSocket(string textToSynthesize)
        {
            if (string.IsNullOrWhiteSpace(textToSynthesize)) return;

            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri("ws://127.0.0.1:8888"), global::System.Threading.CancellationToken.None);

                string jsonPayload = $"{{\"text\":\"{EscapeJsonString(textToSynthesize)}\", \"voice\":\"ef_dora\"}}";
                byte[] sendBytes = global::System.Text.Encoding.UTF8.GetBytes(jsonPayload);

                GD.Print($"[TTS->WS] Dispatching: {jsonPayload}");

                await ws.SendAsync(
                    new ArraySegment<byte>(sendBytes),
                    WebSocketMessageType.Text,
                    true,
                    global::System.Threading.CancellationToken.None);

                using var wavAccumulator = new global::System.IO.MemoryStream();
                byte[] recvBuffer = new byte[16384];

                while (ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult chunk = await ws.ReceiveAsync(
                        new ArraySegment<byte>(recvBuffer),
                        global::System.Threading.CancellationToken.None);

                    if (chunk.MessageType == WebSocketMessageType.Binary)
                    {
                        wavAccumulator.Write(recvBuffer, 0, chunk.Count);

                        if (chunk.EndOfMessage)
                        {
                            byte[] completeWav = wavAccumulator.ToArray();
                            GD.Print($"[TTS<-WS] WAV received: {completeWav.Length} bytes");
                            CallDeferred(MethodName.EmitSignal, SignalName.TTSAudioChunkReceived, completeWav);
                            wavAccumulator.SetLength(0);
                        }
                    }
                    else if (chunk.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                    else if (chunk.MessageType == WebSocketMessageType.Text)
                    {
                        string msg = global::System.Text.Encoding.UTF8.GetString(recvBuffer, 0, chunk.Count);
                        GD.PrintErr($"[TTS<-WS] Unexpected text frame: {msg}");
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[TTS ERROR] WebSocket TTS engine exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Escapes characters that would break a manually-constructed JSON string literal.
        /// Used only for the minimal TTS payload; all other payloads use JsonSerializer.
        /// </summary>
        private static string EscapeJsonString(string input)
        {
            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
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

            if (config?.ActiveProfile != null)
            {
                return config.ActiveProfile.EndpointUrl.TrimEnd('/');
            }

            if (config != null && config.CurrentMode == Logic.System.Config.ConfigManager.AppMode.RemoteUI)
            {
                if (!string.IsNullOrWhiteSpace(config.RemoteHostUrl))
                {
                    return config.RemoteHostUrl.TrimEnd('/');
                }
            }
            return "http://127.0.0.1:8080";
        }
    }
}