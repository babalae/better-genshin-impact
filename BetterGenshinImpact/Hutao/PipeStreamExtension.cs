using System;
using System.Buffers;
using System.IO.Hashing;
using System.IO.Pipes;
using System.Text.Json;

namespace BetterGenshinImpact.Hutao;

internal static class PipeStreamExtension
{
    public static TData? ReadJsonContent<TData>(this PipeStream stream, ref readonly PipePacketHeader header)
    {
        using (IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(header.ContentLength))
        {
            Span<byte> content = memoryOwner.Memory.Span[..header.ContentLength];
            stream.ReadExactly(content);

            if (XxHash64.HashToUInt64(content) != header.Checksum)
            {
                throw new InvalidOperationException("PipePacket Content Hash incorrect");
            }
            return JsonSerializer.Deserialize<TData>(content);
        }
    }

    public static void ReadPacket<TData>(this PipeStream stream, out PipePacketHeader header, out TData? data)
        where TData : class
    {
        data = default;

        stream.ReadPacket(out header);
        if (header.ContentType is PipePacketContentType.Json)
        {
            data = stream.ReadJsonContent<TData>(in header);
        }
    }

    public static unsafe void ReadPacket(this PipeStream stream, out PipePacketHeader header)
    {
        fixed (PipePacketHeader* pHeader = &header)
        {
            stream.ReadExactly(new(pHeader, sizeof(PipePacketHeader)));
        }
    }

    public static void WritePacketWithJsonContent<TData>(this PipeStream stream, byte version, PipePacketType type, PipePacketCommand command, TData data)
    {
        PipePacketHeader header = default;
        header.Version = version;
        header.Type = type;
        header.Command = command;
        header.ContentType = PipePacketContentType.Json;

        stream.WritePacket(ref header, JsonSerializer.SerializeToUtf8Bytes(data));
    }

    public static void WritePacket(this PipeStream stream, ref PipePacketHeader header, byte[] content)
    {
        header.ContentLength = content.Length;
        header.Checksum = XxHash64.HashToUInt64(content);

        stream.WritePacket(in header);
        stream.Write(content);
    }

    public static void WritePacket(this PipeStream stream, byte version, PipePacketType type, PipePacketCommand command)
    {
        PipePacketHeader header = default;
        header.Version = version;
        header.Type = type;
        header.Command = command;

        stream.WritePacket(in header);
    }

    public static unsafe void WritePacket(this PipeStream stream, ref readonly PipePacketHeader header)
    {
        fixed (PipePacketHeader* pHeader = &header)
        {
            stream.Write(new(pHeader, sizeof(PipePacketHeader)));
        }
    }
}