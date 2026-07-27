using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BetterGenshinImpact.Service.Instance;

public static class InstanceOperations
{
    public const string Ping = "ping";
    public const string Response = "response";
    public const string ConnectionOpen = "connection.open";
    public const string ActivationDispatch = "activation.dispatch";
    public const string RelativeMouseSubscribe = "input.relativeMouse.subscribe";
    public const string RelativeMouseUnsubscribe = "input.relativeMouse.unsubscribe";
    public const string RelativeMouseState = "input.relativeMouse.state";
    public const string WebViewList = "webview.list";
    public const string WebViewSend = "webview.send";
    public const string WebViewMessage = "webview.message";
    public const string TaskStartOneDragon = "task.startOneDragon";
}

public sealed class InstanceIpcEnvelope
{
    public int Version { get; init; } = InstanceIpcProtocol.Version;

    public Guid RequestId { get; init; } = Guid.NewGuid();

    public string Operation { get; init; } = string.Empty;

    public bool? Success { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public JObject? Data { get; init; }

    internal static InstanceIpcEnvelope Request(
        string operation,
        object? data = null)
    {
        return new InstanceIpcEnvelope
        {
            Operation = operation,
            Data = data is null ? null : JObject.FromObject(data, InstanceIpcProtocol.Serializer)
        };
    }

    internal static InstanceIpcEnvelope Response(
        InstanceIpcEnvelope request,
        object? data = null)
    {
        return new InstanceIpcEnvelope
        {
            RequestId = request.RequestId,
            Operation = InstanceOperations.Response,
            Success = true,
            Data = data is null ? null : JObject.FromObject(data, InstanceIpcProtocol.Serializer)
        };
    }

    internal static InstanceIpcEnvelope Failure(
        InstanceIpcEnvelope request,
        string errorCode,
        string errorMessage)
    {
        return new InstanceIpcEnvelope
        {
            RequestId = request.RequestId,
            Operation = InstanceOperations.Response,
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}

internal enum InstanceIpcPayloadType : byte
{
    Utf8Json = 1,
    RelativeMouseBatch = 2,
    RelativeMouseResult = 3
}

internal readonly record struct InstanceIpcFrame(
    InstanceIpcPayloadType PayloadType,
    byte[] Payload);

internal readonly record struct RelativeMouseSample(
    int DeltaX,
    int DeltaY,
    DateTime Timestamp);

internal readonly record struct RelativeMouseResult(
    ulong LastSequence,
    bool Handled);

internal static class InstanceIpcProtocol
{
    internal const int Version = 2;
    internal const int MaxPayloadLength = 1024 * 1024;
    private const int FrameHeaderLength = sizeof(uint) + sizeof(byte);
    private const int RelativeMouseBatchHeaderLength = sizeof(ushort) + sizeof(ulong) + sizeof(long);
    private const int RelativeMouseSampleLength = sizeof(int) * 3;
    private const int RelativeMouseResultLength = sizeof(ulong) + sizeof(byte);

    private static JsonSerializerSettings SerializerSettings { get; } = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    };

    internal static JsonSerializer Serializer { get; } = JsonSerializer.Create(SerializerSettings);

    internal static async ValueTask WriteJsonAsync(
        Stream stream,
        InstanceIpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope, SerializerSettings));
        await WriteFrameAsync(
            stream,
            InstanceIpcPayloadType.Utf8Json,
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    internal static InstanceIpcEnvelope ReadJson(InstanceIpcFrame frame)
    {
        if (frame.PayloadType != InstanceIpcPayloadType.Utf8Json)
        {
            throw new InvalidDataException($"预期 JSON 帧，实际为 {frame.PayloadType}。");
        }

        return JsonConvert.DeserializeObject<InstanceIpcEnvelope>(
                   Encoding.UTF8.GetString(frame.Payload),
                   SerializerSettings)
               ?? throw new InvalidDataException("命名管道 JSON 消息为空。");
    }

    internal static async ValueTask WriteRelativeMouseBatchAsync(
        Stream stream,
        ulong firstSequence,
        IReadOnlyList<RelativeMouseSample> samples,
        CancellationToken cancellationToken)
    {
        if (samples.Count is <= 0 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(samples), "相对鼠标批次必须包含 1 到 64 个样本。");
        }

        var payload = CreateRelativeMousePayload(firstSequence, samples);

        await WriteFrameAsync(
            stream,
            InstanceIpcPayloadType.RelativeMouseBatch,
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    private static byte[] CreateRelativeMousePayload(
        ulong firstSequence,
        IReadOnlyList<RelativeMouseSample> samples)
    {
        var payload = new byte[
            RelativeMouseBatchHeaderLength + RelativeMouseSampleLength * samples.Count];
        var span = payload.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span, checked((ushort)samples.Count));
        BinaryPrimitives.WriteUInt64LittleEndian(span[sizeof(ushort)..], firstSequence);
        var baseTicks = samples[0].Timestamp.ToUniversalTime().Ticks;
        BinaryPrimitives.WriteInt64LittleEndian(span[(sizeof(ushort) + sizeof(ulong))..], baseTicks);

        var offset = RelativeMouseBatchHeaderLength;
        foreach (var sample in samples)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[offset..], sample.DeltaX);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + sizeof(int))..], sample.DeltaY);
            var offsetMicroseconds = checked((int)((sample.Timestamp.ToUniversalTime().Ticks - baseTicks) / 10));
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + sizeof(int) * 2)..], offsetMicroseconds);
            offset += RelativeMouseSampleLength;
        }

        return payload;
    }

    internal static (ulong FirstSequence, RelativeMouseSample[] Samples) ReadRelativeMouseBatch(
        InstanceIpcFrame frame)
    {
        if (frame.PayloadType != InstanceIpcPayloadType.RelativeMouseBatch)
        {
            throw new InvalidDataException($"预期相对鼠标帧，实际为 {frame.PayloadType}。");
        }

        var span = frame.Payload.AsSpan();
        if (span.Length < RelativeMouseBatchHeaderLength)
        {
            throw new InvalidDataException("相对鼠标帧头不完整。");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(span);
        if (count is 0 or > 64
            || span.Length != RelativeMouseBatchHeaderLength + RelativeMouseSampleLength * count)
        {
            throw new InvalidDataException("相对鼠标帧长度无效。");
        }

        var firstSequence = BinaryPrimitives.ReadUInt64LittleEndian(span[sizeof(ushort)..]);
        var baseTicks = BinaryPrimitives.ReadInt64LittleEndian(span[(sizeof(ushort) + sizeof(ulong))..]);
        var samples = new RelativeMouseSample[count];
        var offset = RelativeMouseBatchHeaderLength;
        for (var index = 0; index < count; index++)
        {
            var deltaX = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
            var deltaY = BinaryPrimitives.ReadInt32LittleEndian(span[(offset + sizeof(int))..]);
            var offsetMicroseconds =
                BinaryPrimitives.ReadInt32LittleEndian(span[(offset + sizeof(int) * 2)..]);
            long timestampTicks;
            try
            {
                timestampTicks = checked(baseTicks + offsetMicroseconds * 10L);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("相对鼠标样本时间戳超出有效范围。", exception);
            }

            if (timestampTicks < DateTime.MinValue.Ticks
                || timestampTicks > DateTime.MaxValue.Ticks)
            {
                throw new InvalidDataException("相对鼠标样本时间戳超出有效范围。");
            }

            samples[index] = new RelativeMouseSample(
                deltaX,
                deltaY,
                new DateTime(timestampTicks, DateTimeKind.Utc));
            offset += RelativeMouseSampleLength;
        }

        return (firstSequence, samples);
    }

    internal static async ValueTask WriteRelativeMouseResultAsync(
        Stream stream,
        RelativeMouseResult result,
        CancellationToken cancellationToken)
    {
        var payload = new byte[RelativeMouseResultLength];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, result.LastSequence);
        payload[sizeof(ulong)] = result.Handled ? (byte)1 : (byte)0;

        await WriteFrameAsync(
            stream,
            InstanceIpcPayloadType.RelativeMouseResult,
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    internal static RelativeMouseResult ReadRelativeMouseResult(InstanceIpcFrame frame)
    {
        if (frame.PayloadType != InstanceIpcPayloadType.RelativeMouseResult)
        {
            throw new InvalidDataException($"预期相对鼠标处理结果帧，实际为 {frame.PayloadType}。");
        }

        var span = frame.Payload.AsSpan();
        if (span.Length != RelativeMouseResultLength || span[sizeof(ulong)] > 1)
        {
            throw new InvalidDataException("相对鼠标处理结果帧无效。");
        }

        return new RelativeMouseResult(
            BinaryPrimitives.ReadUInt64LittleEndian(span),
            span[sizeof(ulong)] == 1);
    }

    internal static async ValueTask<InstanceIpcFrame?> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[FrameHeaderLength];
        var firstRead = await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (firstRead == 0)
        {
            return null;
        }

        await stream.ReadExactlyAsync(header.AsMemory(1), cancellationToken).ConfigureAwait(false);
        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (payloadLength > MaxPayloadLength)
        {
            throw new InvalidDataException($"命名管道消息超过 {MaxPayloadLength} 字节限制。");
        }

        var payloadType = (InstanceIpcPayloadType)header[sizeof(uint)];
        if (!Enum.IsDefined(payloadType))
        {
            throw new InvalidDataException($"未知命名管道载荷类型：{header[sizeof(uint)]}。");
        }

        var payload = new byte[checked((int)payloadLength)];
        if (payload.Length > 0)
        {
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        return new InstanceIpcFrame(payloadType, payload);
    }

    private static async ValueTask WriteFrameAsync(
        Stream stream,
        InstanceIpcPayloadType payloadType,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (payload.Length > MaxPayloadLength)
        {
            throw new InvalidDataException($"命名管道消息超过 {MaxPayloadLength} 字节限制。");
        }

        var header = new byte[FrameHeaderLength];
        BinaryPrimitives.WriteUInt32LittleEndian(header, checked((uint)payload.Length));
        header[sizeof(uint)] = (byte)payloadType;
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
