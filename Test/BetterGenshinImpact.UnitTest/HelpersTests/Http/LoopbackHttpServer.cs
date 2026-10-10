using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace BetterGenshinImpact.UnitTest.HelpersTests.Http;

internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<string, CancellationToken, Task<(int Status, string Body)>> _respond;
    private readonly ConcurrentBag<Task> _connections = new();
    private readonly Task _acceptLoop;
    private readonly X509Certificate2? _certificate;
    private readonly bool _secureProxy;
    public ConcurrentQueue<string> Requests { get; } = new();

    public LoopbackHttpServer(string body, X509Certificate2? certificate = null, bool secureProxy = false)
        : this((_, _) => Task.FromResult((200, body)), certificate, secureProxy)
    {
    }

    public LoopbackHttpServer(Func<string, CancellationToken, Task<(int Status, string Body)>> respond,
        X509Certificate2? certificate = null, bool secureProxy = false)
    {
        _respond = respond;
        _certificate = certificate;
        _secureProxy = secureProxy;
        _listener.Start();
        Address = new Uri($"{(secureProxy ? "https" : "http")}://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}");
        _acceptLoop = AcceptAsync();
    }

    public Uri Address { get; }

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
                using var network = client.GetStream();
                if (_certificate != null && !_secureProxy)
                {
                    using var connectReader = new StreamReader(network, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var connect = await connectReader.ReadLineAsync(_stop.Token);
                    Assert.StartsWith("CONNECT ", connect);
                    while (!string.IsNullOrEmpty(await connectReader.ReadLineAsync(_stop.Token))) { }
                    await network.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"), _stop.Token);
                }

                using var tls = _certificate == null ? null : new SslStream(network, leaveInnerStreamOpen: true);
                if (tls != null)
                {
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificate,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    }, _stop.Token);
                }

                Stream stream = tls ?? (Stream)network;
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                while (!_stop.IsCancellationRequested)
                {
                    var first = await reader.ReadLineAsync(_stop.Token);
                    if (first == null) return;
                    var headers = new StringBuilder(first);
                    string? line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stop.Token)))
                    {
                        headers.Append('\n').Append(line);
                    }

                    var request = headers.ToString();
                    Requests.Enqueue(request);
                    var response = await _respond(request, _stop.Token);
                    var bytes = Encoding.UTF8.GetBytes(response.Body);
                    var prefix = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 {response.Status} Test\r\nContent-Length: {bytes.Length}\r\nConnection: keep-alive\r\n\r\n");
                    await stream.WriteAsync(prefix, _stop.Token);
                    await stream.WriteAsync(bytes, _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (SocketException) { }
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
