using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Infrastructure.Network.Download;

/// <summary>
/// 下载服务的传输部分：复用已下载的文件、Range 续传、超时与重试、大小和哈希校验
/// </summary>
public sealed partial class DownloadService
{
    /// <summary>
    /// 等待响应头的最长时间
    /// </summary>
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 读取过程中多久没有收到数据视为停滞
    /// </summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 进度报告间隔（毫秒）
    /// </summary>
    private const long ProgressIntervalMilliseconds = 150;

    /// <summary>
    /// 速度按最近这段时间计算（毫秒）
    /// </summary>
    private const long SpeedWindowMilliseconds = 3000;

    /// <summary>
    /// 读写缓冲区大小
    /// </summary>
    private const int BufferSize = 81920;

    /// <summary>
    /// 下载到 filePath 并校验，返回 filePath。已有完整且校验通过的文件时直接复用
    /// </summary>
    private async Task<string> DownloadToFileAsync(DownloadTask task, string filePath, CancellationToken ct)
    {
        var request = task.Request;
        var partPath = filePath + ".part";

        // 1. 只有能校验（已知大小或哈希）时才复用已完成的文件，否则无法确认内容没有变化
        var canVerify = request.ExpectedSize is not null || request.ExpectedHash is not null;
        if (File.Exists(filePath))
        {
            if (canVerify && await VerifyAsync(task, filePath, ct).ConfigureAwait(false))
            {
                return filePath;
            }

            File.Delete(filePath);
        }

        // 2. 已知大小时检查 .part：超出说明不是同一个文件，正好等于说明上次只差校验
        var partLength = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (request.ExpectedSize is { } expectedSize && partLength >= expectedSize)
        {
            if (partLength == expectedSize && await VerifyAsync(task, partPath, ct).ConfigureAwait(false))
            {
                File.Move(partPath, filePath, true);
                return filePath;
            }

            File.Delete(partPath);
            partLength = 0;
        }

        // 3. 已知大小时先检查磁盘空间，不足时不开始下载
        if (request.ExpectedSize is { } size)
        {
            EnsureFreeSpace(filePath, size - partLength);
        }

        // 4. 依次尝试主地址和备用地址，每个地址内对可重试错误重试；超出大小上限等错误直接结束
        // 已取得下载位置，连接期间也显示为下载中，而不是排队
        Report(task, new DownloadProgress(DownloadState.Downloading, partLength, request.ExpectedSize, 0));
        Uri[] urls = [request.Url, .. request.Mirrors];
        Exception? lastError = null;
        var completed = false;
        foreach (var url in urls)
        {
            (completed, lastError) = await TryUrlAsync(task, url, partPath, ct).ConfigureAwait(false);
            if (completed)
            {
                break;
            }
        }

        if (!completed)
        {
            throw new HttpRequestException(lastError?.Message ?? "下载失败", lastError);
        }

        // 5. 校验大小和哈希；失败的 .part 不能续传，直接删除
        if (!await VerifyAsync(task, partPath, ct).ConfigureAwait(false))
        {
            File.Delete(partPath);
            throw new InvalidDataException("文件校验失败，请重新下载");
        }

        File.Move(partPath, filePath, true);
        return filePath;
    }

    /// <summary>
    /// 从一个地址下载：网络错误和 5xx 按 <see cref="RetryDelays"/> 重试，每次从已下载位置续传；4xx 不重试
    /// </summary>
    /// <returns>是否下载完成，以及失败时最后一次的错误</returns>
    private async Task<(bool Completed, Exception? Error)> TryUrlAsync(DownloadTask task, Uri url, string partPath,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await TransferAsync(task, url, partPath, ct).ConfigureAwait(false);
                return (true, null);
            }
            catch (RejectedDownloadException e)
            {
                _logger.LogWarning("下载地址被拒绝：{Name}（{Host}），{Reason}", task.Request.DisplayName, url.Host, e.Message);
                return (false, e);
            }
            catch (TransientDownloadException e)
            {
                if (attempt >= RetryDelays.Count)
                {
                    return (false, e);
                }

                _logger.LogInformation("下载中断，{Delay} 秒后重试（第 {Attempt} 次）：{Name}（{Host}），{Reason}",
                    RetryDelays[attempt].TotalSeconds, attempt + 1, task.Request.DisplayName, url.Host, e.Message);
                // 等待重试期间没有速度，避免界面继续显示中断前的速度和剩余时间
                Report(task, task.Progress with { BytesPerSecond = 0 });
                await Task.Delay(RetryDelays[attempt], ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 发送一次请求并把响应写入 .part。存在 .part 时用 Range 续传：206 时校验起点，200 时从头写
    /// </summary>
    private async Task TransferAsync(DownloadTask task, Uri url, string partPath, CancellationToken ct)
    {
        var request = task.Request;
        var offset = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        if (offset > 0)
        {
            message.Headers.Range = new RangeHeaderValue(offset, null);
        }

        // 同一个计时器先限制等待响应头，再在每次读取前重置为停滞超时
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HeaderTimeout);
        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TransientDownloadException($"服务器 {HeaderTimeout.TotalSeconds:0} 秒内没有响应");
        }
        catch (HttpRequestException e)
        {
            throw new TransientDownloadException($"无法连接服务器：{e.Message}", e);
        }

        using (response)
        {
            offset = CheckResponse(response, offset, partPath);
            var headers = response.Content.Headers;
            var total = headers.ContentRange?.Length ?? (headers.ContentLength + offset) ?? request.ExpectedSize;
            if (total > request.MaxBytes)
            {
                throw new InvalidDataException($"文件大小超过 {ByteSizeFormatter.Format(request.MaxBytes)} 的限制");
            }

            if (total is { } knownTotal)
            {
                EnsureFreeSpace(partPath, knownTotal - offset);
            }

            Report(task, new DownloadProgress(DownloadState.Downloading, offset, total, 0));
            await CopyToPartAsync(task, response, partPath, offset, total, timeout, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 检查响应状态，返回实际的写入起点：206 时为原起点，200 时为 0
    /// </summary>
    private static long CheckResponse(HttpResponseMessage response, long offset, string partPath)
    {
        // .part 已经不小于服务器上的文件，说明不是同一个文件，删除后从头下载
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && offset > 0)
        {
            File.Delete(partPath);
            throw new TransientDownloadException("续传位置无效，将从头下载");
        }

        if (!response.IsSuccessStatusCode)
        {
            var code = (int)response.StatusCode;
            var reason = $"服务器返回 {code} {response.ReasonPhrase}".TrimEnd();
            if (code >= 500 || code is 408 or 429)
            {
                throw new TransientDownloadException(reason);
            }

            throw new RejectedDownloadException(reason);
        }

        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            return 0;
        }

        if (response.Content.Headers.ContentRange?.From != offset)
        {
            File.Delete(partPath);
            throw new TransientDownloadException("服务器返回了错误的续传位置，将从头下载");
        }

        return offset;
    }

    /// <summary>
    /// 把响应内容追加（offset 大于 0）或覆盖写入 .part，按间隔报告进度
    /// </summary>
    private async Task CopyToPartAsync(DownloadTask task, HttpResponseMessage response, string partPath, long offset,
        long? total, CancellationTokenSource timeout, CancellationToken ct)
    {
        var maxBytes = task.Request.MaxBytes;
        var received = offset;
        var speed = new SpeedSampler(offset);
        var lastReport = Environment.TickCount64;
        var buffer = new byte[BufferSize];
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(partPath, offset > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, BufferSize, true);
        while (true)
        {
            int read;
            timeout.CancelAfter(StallTimeout);
            try
            {
                read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TransientDownloadException($"{StallTimeout.TotalSeconds:0} 秒没有收到数据，连接可能已中断");
            }
            catch (Exception e) when (e is IOException or HttpRequestException)
            {
                throw new TransientDownloadException("网络连接中断", e);
            }

            if (read == 0)
            {
                break;
            }

            received += read;
            if (received > maxBytes)
            {
                throw new InvalidDataException($"文件大小超过 {ByteSizeFormatter.Format(maxBytes)} 的限制");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            var now = Environment.TickCount64;
            if (now - lastReport >= ProgressIntervalMilliseconds)
            {
                lastReport = now;
                Report(task, new DownloadProgress(DownloadState.Downloading, received, total, speed.Add(now, received)));
            }
        }

        // 服务器正常关闭连接但内容不完整时，按中断处理，下次从这里续传
        if (total is { } expected && received < expected)
        {
            throw new TransientDownloadException("连接提前断开");
        }
    }

    /// <summary>
    /// 校验文件的大小和哈希；没有期望值的项不校验
    /// </summary>
    private async Task<bool> VerifyAsync(DownloadTask task, string path, CancellationToken ct)
    {
        var request = task.Request;
        var length = new FileInfo(path).Length;
        Report(task, new DownloadProgress(DownloadState.Verifying, length, request.ExpectedSize ?? length, 0));
        if (request.ExpectedSize is { } size && length != size)
        {
            return false;
        }

        if (request.ExpectedHash is not { } hash)
        {
            return true;
        }

        var (algorithm, expected) = DecodeHash(hash);
        using var incremental = IncrementalHash.CreateHash(algorithm);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true);
        var buffer = new byte[BufferSize];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            incremental.AppendData(buffer, 0, read);
        }

        return CryptographicOperations.FixedTimeEquals(incremental.GetHashAndReset(), expected);
    }

    /// <summary>
    /// 检查文件所在磁盘的剩余空间
    /// </summary>
    /// <exception cref="IOException">剩余空间不足</exception>
    private static void EnsureFreeSpace(string path, long requiredBytes)
    {
        if (requiredBytes <= 0 || Path.GetPathRoot(Path.GetFullPath(path)) is not { Length: > 0 } root)
        {
            return;
        }

        long available;
        try
        {
            available = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // 网络路径等无法读取剩余空间时不检查，写入失败时再报错
            return;
        }

        if (available < requiredBytes)
        {
            throw new IOException($"磁盘空间不足，还需要 {ByteSizeFormatter.Format(requiredBytes)}，" +
                                  $"当前剩余 {ByteSizeFormatter.Format(available)}");
        }
    }

    /// <summary>
    /// 按最近 3 秒的数据计算下载速度
    /// </summary>
    private sealed class SpeedSampler
    {
        /// <summary>
        /// 采样点：时间（毫秒）与累计字节数
        /// </summary>
        private readonly Queue<(long Time, long Bytes)> _samples = new();

        /// <param name="initialBytes">开始时已有的字节数（续传的起点）</param>
        public SpeedSampler(long initialBytes) => _samples.Enqueue((Environment.TickCount64, initialBytes));

        /// <summary>
        /// 加入一个采样点并返回每秒字节数
        /// </summary>
        public double Add(long time, long bytes)
        {
            _samples.Enqueue((time, bytes));
            while (_samples.Count > 2 && time - _samples.Peek().Time > SpeedWindowMilliseconds)
            {
                _samples.Dequeue();
            }

            var (firstTime, firstBytes) = _samples.Peek();
            return time > firstTime ? (bytes - firstBytes) * 1000d / (time - firstTime) : 0;
        }
    }

    /// <summary>
    /// 可重试的错误：网络中断、超时、5xx。下次从已下载位置续传
    /// </summary>
    private sealed class TransientDownloadException(string message, Exception? inner = null)
        : Exception(message, inner);

    /// <summary>
    /// 地址被服务器拒绝（4xx），不重试，换下一个地址
    /// </summary>
    private sealed class RejectedDownloadException(string message) : Exception(message);
}
