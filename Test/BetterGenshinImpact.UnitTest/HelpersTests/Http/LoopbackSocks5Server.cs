using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BetterGenshinImpact.UnitTest.HelpersTests.Http;

internal sealed class LoopbackSocks5Server : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentBag<Task> _connections = new();
    private readonly string _body;
    private readonly Task _acceptLoop;

    public LoopbackSocks5Server(string body)
    {
        _body = body;
        _listener.Start();
        Address = new Uri($"socks5://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
        _acceptLoop = AcceptAsync();
    }

    public Uri Address { get; }
    public ConcurrentQueue<string> Destinations { get; } = new();

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _connections.Add(HandleAsync(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                var greeting = new byte[2];
                await stream.ReadExactlyAsync(greeting, _stop.Token);
                Assert.Equal(5, greeting[0]);
                var methods = new byte[greeting[1]];
                await stream.ReadExactlyAsync(methods, _stop.Token);
                await stream.WriteAsync(new byte[] { 5, 0 }, _stop.Token);
                var connect = new byte[4];
                await stream.ReadExactlyAsync(connect, _stop.Token);
                Assert.Equal(1, connect[1]);
                Assert.Equal(3, connect[3]);
                var length = new byte[1];
                await stream.ReadExactlyAsync(length, _stop.Token);
                var host = new byte[length[0]];
                await stream.ReadExactlyAsync(host, _stop.Token);
                var port = new byte[2];
                await stream.ReadExactlyAsync(port, _stop.Token);
                Destinations.Enqueue(Encoding.ASCII.GetString(host));
                await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 80 }, _stop.Token);
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                while (!_stop.IsCancellationRequested)
                {
                    if (await reader.ReadLineAsync(_stop.Token) == null) return;
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                    var body = Encoding.UTF8.GetBytes(_body);
                    var prefix = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: keep-alive\r\n\r\n");
                    await stream.WriteAsync(prefix, _stop.Token);
                    await stream.WriteAsync(body, _stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _acceptLoop;
        await Task.WhenAll(_connections);
        _stop.Dispose();
    }
}
