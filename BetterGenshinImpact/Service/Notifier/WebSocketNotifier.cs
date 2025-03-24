using System;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Notification.Model;
using BetterGenshinImpact.Service.Notifier.Interface;

namespace BetterGenshinImpact.Service.Notifier
{
    public class WebSocketNotifier : IDisposable, INotifier
    {
        private ClientWebSocket _webSocket;
        private readonly string _endpoint;
        private readonly JsonSerializerOptions _jsonSerializerOptions;
        private readonly CancellationTokenSource _cts;

        public WebSocketNotifier(string endpoint, JsonSerializerOptions jsonSerializerOptions, CancellationTokenSource cts)
        {
            _endpoint = endpoint;
            _jsonSerializerOptions = jsonSerializerOptions;
            _cts = cts;
            _webSocket = new ClientWebSocket();
        }

        public string Name => "WebSocketNotifier";

        private async Task EnsureConnectedAsync()
        {
            if (_webSocket.State == WebSocketState.Open)
                return;

            _webSocket.Dispose();
            _webSocket = new ClientWebSocket();
            
            try
            {
                Console.WriteLine("Connecting to WebSocket...");
                await _webSocket.ConnectAsync(new Uri(_endpoint), _cts.Token);
                Console.WriteLine("WebSocket connected.");
            }
            catch (SystemException ex)
            {
                Console.WriteLine($"WebSocket connection failed: {ex.Message}");
            }
        }

        public async Task SendAsync(BaseNotificationData notificationData)
        {
            try
            {
                await EnsureConnectedAsync();
                var json = JsonSerializer.Serialize(notificationData, _jsonSerializerOptions);
                var buffer = System.Text.Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts.Token);
                await CloseAsync(); // 添加关闭连接的代码
            }
            catch (WebSocketException ex)
            {
                Console.WriteLine($"WebSocket send failed: {ex.Message}");
                await EnsureConnectedAsync();  // Attempt to reconnect
            }
        }

        public async Task CloseAsync()
        {
            if (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.CloseReceived)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", _cts.Token);
            }
            _webSocket.Dispose();
            _webSocket = new ClientWebSocket();
        }

        public void Dispose()
        {
            _webSocket.Dispose();
            _cts.Cancel();
        }

        public async Task SendNotificationAsync(BaseNotificationData notificationData)
        {
            await SendAsync(notificationData);
        }
    }
}