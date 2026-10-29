using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Tts;
using NAudio.Wave;

namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Audio;

/// <summary>HTTP与播放分别持有引用的有限增量PCM源。</summary>
public sealed class StreamingPcmAudio : IDisposable
{
    /// <summary>读者仅访问已Flush提交区域；不依赖设备速度阻塞HTTP。</summary>
    private readonly FileStream _writer;
    /// <summary>由固定缓存根和随机ID构造的本次PCM路径。</summary>
    private readonly string _path;
    /// <summary>不可变PCM格式。</summary>
    private readonly WaveFormat _format;
    /// <summary>本次磁盘字节预算，达到上限会报错后排空HTTP。</summary>
    private readonly long _maximumBytes;
    /// <summary>传输初始引用和Ready/Playing引用分别配对释放。</summary>
    private int _references = 1;
    /// <summary>发布给读取者的已提交PCM长度。</summary>
    private long _committed;
    /// <summary>正常完成、非静音证据和独占读取者标记。</summary>
    private int _completed, _hasSignal, _readerTaken;
    /// <summary>传输/格式故障必须传递给尚在播放的读取者。</summary>
    private Exception? _failure;
    /// <summary>按实际缺料统计，不逐回调写日志。</summary>
    private int _underruns;
    /// <summary>实际插零总量。</summary>
    private long _silenceBytes;
    /// <summary>同一音频源的诊断身份，不是HTTP首声测量。</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");

    /// <summary>创建本轮独立PCM归档；由传输所有者持有初始引用。</summary>
    public StreamingPcmAudio(string directory, int sampleRate, int channels, long maximumBytes = 32 * 1024 * 1024)
    {
        if (sampleRate is < 8000 or > 192000 || channels is < 1 or > 2 || maximumBytes <= 0 || maximumBytes > 32 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), "PCM格式或32MiB有限归档预算无效。");
        _format = new(sampleRate, 16, channels); _maximumBytes = maximumBytes;
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, Id + ".stream.pcm");
        _writer = new(_path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete,
            16384, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }
    /// <summary>首段非静音PCM是否已经达到可播放水位。</summary>
    public bool IsReady => Volatile.Read(ref _failure) is null && Volatile.Read(ref _hasSignal) != 0
        && (PcmBytes >= _format.AverageBytesPerSecond / 5 || Volatile.Read(ref _completed) != 0);
    /// <summary>已提交至有限spool的PCM字节数。</summary>
    public long PcmBytes => Interlocked.Read(ref _committed);
    /// <summary>播放缺料次数。</summary>
    public int UnderrunEpisodes => Volatile.Read(ref _underruns);
    /// <summary>因缺料插入的静音字节数。</summary>
    public long UnderrunSilenceBytes => Interlocked.Read(ref _silenceBytes);
    /// <summary>增量追加已经对齐的PCM，不能等待播放消费。</summary>
    public async ValueTask AppendAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        if (Volatile.Read(ref _completed) != 0 || Volatile.Read(ref _failure) is not null)
            throw new InvalidOperationException("PCM源已经结束。");
        if (bytes.Length % _format.BlockAlign != 0 || PcmBytes + bytes.Length > _maximumBytes)
            throw new VoiceException(VoiceErrorCode.InvalidAudio, "流式PCM帧不完整或超过32MiB音频预算。");
        var signal = ContainsSignal(bytes.Span);
        await _writer.WriteAsync(bytes, token).ConfigureAwait(false);
        await _writer.FlushAsync(token).ConfigureAwait(false);
        Interlocked.Add(ref _committed, bytes.Length);
        if (signal) Volatile.Write(ref _hasSignal, 1);
    }
    /// <summary>只查看PCM采样，不把静音头部当作可接替语音。</summary>
    private static bool ContainsSignal(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i + 1 < bytes.Length; i += 2)
            if (BinaryPrimitives.ReadInt16LittleEndian(bytes[i..]) != 0) return true;
        return false;
    }
    /// <summary>只有正常EOF且完整校验后才发布完成。</summary>
    public void Complete() => Volatile.Write(ref _completed, 1);
    /// <summary>使消费者明确失败，不能伪装为较短的正常音频。</summary>
    public void Fail(Exception error) => Volatile.Write(ref _failure, error);
    /// <summary>只在EOF后获取有界完整WAV供既有校验和归档。</summary>
    public byte[] ToWav()
    {
        var length = checked((int)PcmBytes);
        if (length == 0 || Volatile.Read(ref _hasSignal) == 0)
            throw new VoiceException(VoiceErrorCode.InvalidAudio, "流式响应没有有效非静音PCM。");
        var bytes = new byte[checked(length + 44)];
        "RIFF"u8.CopyTo(bytes); "WAVEfmt "u8.CopyTo(bytes.AsSpan(8)); "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), length + 36);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), (short)_format.Channels);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), _format.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), _format.AverageBytesPerSecond);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), (short)_format.BlockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), length);
        using var reader = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        reader.ReadExactly(bytes.AsSpan(44));
        return bytes;
    }
    /// <summary>就绪/播放槽获取自己的引用，需配对Dispose。</summary>
    public void Retain()
    {
        while (true)
        {
            var count = Volatile.Read(ref _references);
            ObjectDisposedException.ThrowIf(count <= 0, this);
            if (Interlocked.CompareExchange(ref _references, count + 1, count) == count) return;
        }
    }
    /// <summary>一个源只允许一个播放读取者。</summary>
    public Reader CreateReader()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _references) <= 0, this);
        if (Interlocked.Exchange(ref _readerTaken, 1) != 0) throw new InvalidOperationException("PCM源仅允许一个播放读取者。");
        return new(this, _path);
    }
    /// <summary>释放一个所有者引用；最后释放时删除本轮PCM。</summary>
    public void Dispose()
    {
        if (Interlocked.Decrement(ref _references) != 0) return;
        _writer.Dispose();
        try { File.Delete(_path); }
        catch (IOException) { /* 本轮缓存目录还会在宿主会话Dispose中清理。 */ }
        catch (UnauthorizedAccessException) { /* 清理失败不覆盖原始传输/播放错误。 */ }
    }

    /// <summary>单设备顺序读取增量PCM，缺料插零、完成返回0。</summary>
    public sealed class Reader : IWaveProvider, IDisposable
    {
        /// <summary>持有者在播放结束之后才释放源引用。</summary>
        private readonly StreamingPcmAudio _source;
        /// <summary>只读取生产者已提交的文件区域。</summary>
        private readonly FileStream _reader;
        /// <summary>连续缺料最长两秒，避免无限静音等待。</summary>
        private long? _gapStarted;
        /// <summary>建立一次播放的顺序读取句柄。</summary>
        internal Reader(StreamingPcmAudio source, string path)
        {
            _source = source;
            _reader = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        }
        /// <summary>PCM16输出格式。</summary>
        public WaveFormat WaveFormat => _source._format;
        /// <summary>按设备需要读取实际提交的PCM。</summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            if (Volatile.Read(ref _source._failure) is { } error) throw error;
            var size = count / WaveFormat.BlockAlign * WaveFormat.BlockAlign;
            if (size == 0) return 0;
            var available = _source.PcmBytes - _reader.Position;
            var real = (int)Math.Min(size, available);
            if (real > 0) _reader.ReadExactly(buffer.AsSpan(offset, real));
            // 完成标记在已提交长度之后发布；再次检查长度，避免EOF并发时漏掉最后一块。
            if (real < size && Volatile.Read(ref _source._completed) != 0)
            {
                var tail = (int)Math.Min(size - real, _source.PcmBytes - _reader.Position);
                if (tail > 0) _reader.ReadExactly(buffer.AsSpan(offset + real, tail));
                _gapStarted = null; return real + tail;
            }
            if (real == size) { _gapStarted = null; return real; }
            if (_gapStarted is null) { _gapStarted = Stopwatch.GetTimestamp(); Interlocked.Increment(ref _source._underruns); }
            if (Stopwatch.GetElapsedTime(_gapStarted.Value) > TimeSpan.FromSeconds(2))
                throw new VoiceException(VoiceErrorCode.PlaybackDevice, "流式音频连续缺料超过2秒；已停止本句输出，后台请求仍需排空。");
            Array.Clear(buffer, offset + real, size - real);
            Interlocked.Add(ref _source._silenceBytes, size - real);
            return size;
        }
        /// <summary>释放单次播放文件句柄。</summary>
        public void Dispose() => _reader.Dispose();
    }
}
