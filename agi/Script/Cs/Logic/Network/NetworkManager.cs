using Godot;
using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace Logic.Network
{
    public partial class NetworkManager : Node
    {
        [Signal]
        public delegate void HandshakeCompletedEventHandler(bool success);
        
        [Signal]
        public delegate void TokenReceivedEventHandler(string token);

        private const string BaseUrl = "http://127.0.0.1:8188";
        private const string WsUrl = "ws://127.0.0.1:8188/ws";
        
        private ClientWebSocket _ws;
        
        // Explicit namespace to avoid conflict with Godot.HttpClient
        private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

        /// <summary>
        /// Validates connection to the backend via HTTP GET.
        /// </summary>
        public async void PerformHandshake()
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync($"{BaseUrl}/object_info/KSampler");
                
                if (response.IsSuccessStatusCode)
                {
                    GD.Print("NetworkManager: Handshake Successful. Core nodes found.");
                    await ConnectWebSocket();
                    EmitSignal(SignalName.HandshakeCompleted, true);
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                     GD.PrintErr("NetworkManager: ERR_NET_404 - Custom Nodes Missing.");
                     EmitSignal(SignalName.HandshakeCompleted, false);
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"NetworkManager: ERR_NET_001 - Connection Refused. {ex.Message}");
                EmitSignal(SignalName.HandshakeCompleted, false);
            }
        }

        private async Task ConnectWebSocket()
        {
            _ws = new ClientWebSocket();
            try
            {
                await _ws.ConnectAsync(new Uri($"{WsUrl}?clientId={Guid.NewGuid()}"), CancellationToken.None);
                GD.Print("NetworkManager: WebSocket Connected.");
                _ = ListenToSocket();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"NetworkManager: WebSocket connection failed. {ex.Message}");
            }
        }

        private async Task ListenToSocket()
        {
            var buffer = new byte[1024 * 4];
            while (_ws.State == WebSocketState.Open)
            {
                try 
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        // In a real scenario, parse JSON here to extract tokens/text
                        // For Lite mode, assuming stream of text tokens
                        CallDeferred(MethodName.EmitSignal, SignalName.TokenReceived, message);
                    }
                }
                catch (Exception) { break; }
            }
        }
    }
}