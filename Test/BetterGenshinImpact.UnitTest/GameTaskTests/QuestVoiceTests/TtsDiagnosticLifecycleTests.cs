using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.QuestVoice;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging.Abstractions;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>生产测试入口的 HTTP 所有权：退出可中断、普通取消仍排空。</summary>
public sealed class TtsDiagnosticLifecycleTests
{
    /// <summary>构造被动宿主，不启动 App、截图、游戏或音频设备。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitAbortsTestHttpButOrdinaryStopDrains(bool applicationExit)
    {
        var root = Path.Combine(Path.GetTempPath(), "TtsDiagnostic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var synthesizing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            for (var index = 0; index < 3; index++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var line = await reader.ReadLineAsync();
                var length = 0;
                var chunked = false;
                while (await reader.ReadLineAsync() is { Length: > 0 } header)
                {
                    if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(header[15..].Trim());
                    if (header.Equals("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase)) chunked = true;
                }
                var payload = new StringBuilder();
                if (length > 0)
                {
                    var body = new char[length];
                    await reader.ReadBlockAsync(body, 0, body.Length);
                    payload.Append(body);
                }
                if (chunked)
                    while (true)
                    {
                        var count = Convert.ToInt32(await reader.ReadLineAsync(), 16);
                        if (count == 0) { await reader.ReadLineAsync(); break; }
                        var body = new char[count];
                        await reader.ReadBlockAsync(body, 0, body.Length);
                        payload.Append(body);
                        await reader.ReadLineAsync();
                    }
                // JsonContent 默认把非 ASCII 文本转义，因此此探针的块长度等于字符数。
                if (index == 2) Assert.Contains("\"streaming_mode\":false", payload.ToString());
                if (index < 2)
                {
                    Assert.StartsWith("GET /set_", line);
                    var body = "{\"message\":\"success\"}";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"));
                }
                else
                {
                    Assert.StartsWith("POST /tts", line);
                    await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: audio/wav\r\nConnection: close\r\n\r\n"u8.ToArray());
                    await stream.FlushAsync();
                    synthesizing.SetResult();
                    await release.Task;
                    if (!applicationExit)
                        await stream.WriteAsync(WavAndReferenceTests.Wave(32000, [100, -100]));
                }
            }
        });
        QuestVoiceService? service = null;
        try
        {
            var file = Path.Combine(root, "asset");
            await File.WriteAllBytesAsync(file, [1]);
            var all = new AllConfig();
            var config = all.QuestVoiceConfig;
            config.ServiceUrl = $"http://127.0.0.1:{port}/";
            config.GptWeightsPath = config.SovitsWeightsPath = config.ReferenceAudioPath = file;
            config.PromptText = "参考原文";
            config.StreamingEnabled = true;
            var factory = new QuestVoiceSessionFactory(config, root);
            using var controller = new QuestVoiceSessionController(factory);
            service = new((GameRuntimeService)RuntimeHelpers.GetUninitializedObject(typeof(GameRuntimeService)),
                (TaskTriggerDispatcher)RuntimeHelpers.GetUninitializedObject(typeof(TaskTriggerDispatcher)),
                all, factory, controller, NullLogger<QuestVoiceService>.Instance);
            var synthesis = service.TestSpeechAsync("test");
            await synthesizing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stop = service.StopAsync(applicationExit);
            if (applicationExit)
            {
                await stop.WaitAsync(TimeSpan.FromSeconds(3));
                var error = await Assert.ThrowsAsync<VoiceException>(() => synthesis);
                Assert.True(error.BackendUnknown);
                Assert.True(service.Status.BackendUnknown);
                // 混合声线可能在默认普通 Provider 下遇到 GPT 未知，恢复入口仍必须可见。
                config.ProviderId = "huoshan";
                var page = new QuestVoicePageViewModel(all, service, factory);
                Assert.True(page.RequiresBackendDrain);
            }
            else
            {
                Assert.False(stop.IsCompleted);
                Assert.False(synthesis.IsCompleted);
                release.SetResult();
                await stop.WaitAsync(TimeSpan.FromSeconds(3));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => synthesis);
                Assert.False(service.Status.BackendUnknown);
            }
            Assert.False(service.Status.IsBusy);
        }
        finally
        {
            release.TrySetResult();
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
            service?.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }
}
