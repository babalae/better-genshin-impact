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

        // 实现 INotifier 接口的 Name 属性
        public string Name => "WebSocketNotifier";

        public async Task ConnectAsync()
        {
            await _webSocket.ConnectAsync(new Uri(_endpoint), _cts.Token);
        }

        public async Task SendAsync(BaseNotificationData notificationData)
        {
            var json = JsonSerializer.Serialize(notificationData, _jsonSerializerOptions);
            var buffer = System.Text.Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts.Token);
        }

        public async Task CloseAsync()
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", _cts.Token);
            }
        }

        public void Dispose()
        {
            _webSocket.Dispose();
            _cts.Cancel();
        }

        public async Task SendNotificationAsync(BaseNotificationData notificationData)
        {
            if (_webSocket.State != WebSocketState.Open)
            {
                await ConnectAsync();
            }
            await SendAsync(notificationData);
        }
    }
}
