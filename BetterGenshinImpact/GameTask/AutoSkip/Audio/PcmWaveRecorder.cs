using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoSkip.Audio;

internal sealed class PcmWaveRecorder : IDisposable
{
    private const short ChannelCount = 1;
    private const short BitsPerSample = 16;

    private readonly FileStream _stream;
    private readonly BinaryWriter _writer;
    private int _dataLength;
    private bool _disposed;

    public PcmWaveRecorder(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        FilePath = filePath;
        _stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        try
        {
            _writer = new BinaryWriter(_stream, Encoding.ASCII, true);
            WriteHeader(0);
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    public string FilePath { get; }

    public void Write(IReadOnlyList<float> samples)
    {
        foreach (var sample in samples)
        {
            var pcm = (short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
            _writer.Write(pcm);
            _dataLength += sizeof(short);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _writer.Flush();
            _stream.Position = 0;
            WriteHeader(_dataLength);
        }
        finally
        {
            try
            {
                _writer.Dispose();
            }
            finally
            {
                _stream.Dispose();
            }
        }
    }

    private void WriteHeader(int dataLength)
    {
        var byteRate = SileroVadDetector.SampleRate * ChannelCount * BitsPerSample / 8;
        var blockAlign = (short)(ChannelCount * BitsPerSample / 8);

        _writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        _writer.Write(36 + dataLength);
        _writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        _writer.Write(Encoding.ASCII.GetBytes("fmt "));
        _writer.Write(16);
        _writer.Write((short)1);
        _writer.Write(ChannelCount);
        _writer.Write(SileroVadDetector.SampleRate);
        _writer.Write(byteRate);
        _writer.Write(blockAlign);
        _writer.Write(BitsPerSample);
        _writer.Write(Encoding.ASCII.GetBytes("data"));
        _writer.Write(dataLength);
    }
}
