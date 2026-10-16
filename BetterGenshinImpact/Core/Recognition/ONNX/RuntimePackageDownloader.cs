using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>可恢复的下载与解压。所有文件先校验、写入暂存目录，调用方负责可回滚地提交安装。</summary>
public sealed class RuntimePackageDownloader(HttpClient client)
{
    public async Task<string> DownloadAsync(RuntimeDownloadPackage package, string cacheDirectory,
        IProgress<RuntimeDownloadProgress>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(cacheDirectory);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(package.Url.AbsoluteUri)));
        var path = Path.Combine(cacheDirectory, key + ".zip");
        if (File.Exists(path))
        {
            if (await VerifyHashAsync(path, package, token)) return path;
            File.Delete(path);
        }
        var partial = path + ".part";
        var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (offset > 0 && package.DownloadBytes > 0 && offset >= package.DownloadBytes)
        {
            if (offset == package.DownloadBytes && await VerifyHashAsync(partial, package, token))
            {
                File.Move(partial, path, true);
                return path;
            }
            File.Delete(partial);
            offset = 0;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, package.Url);
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        headersTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
        response.EnsureSuccessStatusCode();
        headersTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (response.Content.Headers.ContentRange?.From != offset) throw new InvalidDataException("下载服务器返回了错误的续传位置。");
        }
        else offset = 0;
        var total = response.Content.Headers.ContentRange?.Length ?? (response.Content.Headers.ContentLength + offset) ?? package.DownloadBytes;
        await using (var input = await response.Content.ReadAsStreamAsync(token))
        await using (var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            var buffer = new byte[81920];
            var received = offset;
            var lastReport = Environment.TickCount64;
            int length;
            while ((length = await input.ReadAsync(buffer, token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, length), token);
                received += length;
                if (Environment.TickCount64 - lastReport >= 150)
                {
                    progress?.Report(new($"正在下载 {package.Name}", received, total));
                    lastReport = Environment.TickCount64;
                }
            }
            progress?.Report(new($"正在校验 {package.Name}", received, total));
        }
        if (!await VerifyHashAsync(partial, package, token))
        {
            File.Delete(partial);
            throw new InvalidDataException($"{package.Name} 下载校验失败，请重新下载。");
        }
        File.Move(partial, path, true);
        return path;
    }

    public static async Task<bool> VerifyHashAsync(string path, RuntimeDownloadPackage package, CancellationToken token)
    {
        if (package.DownloadBytes > 0 && new FileInfo(path).Length != package.DownloadBytes) return false;
        // TensorRT 官方 ZIP 未发布摘要，提取时逐文件核对中央目录的 CRC32 和长度。
        if (package.Hash == null) return true;
        using var hash = IncrementalHash.CreateHash(new HashAlgorithmName(package.HashAlgorithm));
        await using var stream = File.OpenRead(path);
        var buffer = new byte[81920];
        int length;
        while ((length = await stream.ReadAsync(buffer, token)) > 0) hash.AppendData(buffer, 0, length);
        var actual = hash.GetHashAndReset();
        var expected = package.HashAlgorithm == "SHA512" ? Convert.FromBase64String(package.Hash) : Convert.FromHexString(package.Hash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static async Task ExtractAsync(string archivePath, string directory, Func<string, bool> include, CancellationToken token)
    {
        var crcs = ReadCrcs(archivePath);
        using var archive = ZipFile.OpenRead(archivePath);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var normalized = entry.FullName.Replace('\\', '/');
            if (!include(normalized)) continue;
            // 只提取已选的运行文件并平铺，拒绝路径穿越和重复的文件名。
            var name = Path.GetFileName(normalized);
            if (name.Length == 0 || normalized.Split('/').Contains("..") || !names.Add(name))
                throw new InvalidDataException($"压缩包包含不安全或重复的文件：{normalized}");
            if (!crcs.TryGetValue(entry.FullName, out var expected)) throw new InvalidDataException("压缩包校验目录缺失。");
            await using var source = entry.Open();
            await using var output = File.Create(Path.Combine(directory, name));
            var buffer = new byte[81920];
            uint crc = uint.MaxValue;
            long count = 0;
            int length;
            while ((length = await source.ReadAsync(buffer, token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, length), token);
                count += length;
                for (var i = 0; i < length; i++) crc = CrcTable[(crc ^ buffer[i]) & 255] ^ (crc >> 8);
            }
            if (count != entry.Length || ~crc != expected) throw new InvalidDataException($"文件校验失败：{name}");
        }
        if (names.Count == 0) throw new InvalidDataException("压缩包中没有对应的 Windows x64 运行文件。");
    }

    private static Dictionary<string, uint> ReadCrcs(string path)
    {
        using var stream = File.OpenRead(path);
        var tail = new byte[(int)Math.Min(stream.Length, 65557)];
        stream.Position = stream.Length - tail.Length;
        stream.ReadExactly(tail);
        var end = tail.Length - 22;
        while (end >= 0 && BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(end, 4)) != 0x06054b50) end--;
        if (end < 0) throw new InvalidDataException("ZIP 中央目录缺失。");
        long directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(end + 16, 4));
        long entries = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(end + 10, 2));
        if (directoryOffset == uint.MaxValue || entries == ushort.MaxValue)
        {
            var locator = end - 20;
            if (locator < 0 || BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(locator, 4)) != 0x07064b50)
                throw new InvalidDataException("ZIP64 校验目录缺失。");
            stream.Position = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(tail.AsSpan(locator + 8, 8)));
            var zip64 = new byte[56];
            stream.ReadExactly(zip64);
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) != 0x06064b50) throw new InvalidDataException("ZIP64 校验目录损坏。");
            entries = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(32, 8)));
            directoryOffset = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(48, 8)));
        }
        if (entries > 1_000_000 || directoryOffset >= stream.Length) throw new InvalidDataException("ZIP 校验目录大小无效。");
        stream.Position = directoryOffset;
        var result = new Dictionary<string, uint>(StringComparer.Ordinal);
        var header = new byte[46];
        for (var i = 0; i < entries; i++)
        {
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50) throw new InvalidDataException("ZIP 校验目录损坏。");
            var name = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28))];
            stream.ReadExactly(name);
            result.Add(Encoding.UTF8.GetString(name), BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16)));
            stream.Position += BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30)) + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));
        }
        return result;
    }

    private static readonly uint[] CrcTable = CreateCrcTable();
    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? (value >> 1) ^ 0xedb88320 : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
