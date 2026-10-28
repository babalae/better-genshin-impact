using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>
/// 运行库安装包的解压与校验。所有文件先校验、写入暂存目录，调用方负责可回滚地提交安装。
/// 下载、续传与哈希校验由 IDownloadService 负责（见设计文档第 7 节）
/// </summary>
public static class RuntimePackageDownloader
{
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
