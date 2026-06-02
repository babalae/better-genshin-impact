using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Vanara.PInvoke;
using static Vanara.PInvoke.CoreAudio;

namespace BetterGenshinImpact.GameTask.AutoSkip.Audio;

internal sealed class ProcessLoopbackAudioCapture : IDisposable
{
    private const string VirtualAudioDeviceProcessLoopback = @"VAD\Process_Loopback";
    private const long BufferDurationHundredNanoseconds = 1_000_000;
    private const ushort WaveFormatPcm = 1;
    private const ushort ChannelCount = 1;
    private const ushort BitsPerSample = 16;
    private const ushort BlockAlign = (ushort)(ChannelCount * BitsPerSample / 8);
    private const uint AverageBytesPerSecond = (uint)(SileroVadDetector.SampleRate * BlockAlign);

    private readonly IAudioClient _audioClient;
    private readonly IAudioCaptureClient _captureClient;
    private bool _started;

    public ProcessLoopbackAudioCapture(int targetProcessId)
    {
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        try
        {
            audioClient = ActivateAudioClient(targetProcessId);
            InitializeAudioClient(audioClient);
            var captureClientId = typeof(IAudioCaptureClient).GUID;
            captureClient = (IAudioCaptureClient)audioClient.GetService(ref captureClientId);
            audioClient.Start();

            _audioClient = audioClient;
            _captureClient = captureClient;
            _started = true;
        }
        catch
        {
            ReleaseComObject(captureClient);
            ReleaseComObject(audioClient);
            throw;
        }
    }

    public void ReadAvailableSamples(List<float> destination)
    {
        ReadAvailableSamplesCore(destination);
    }

    public void DiscardAvailableSamples()
    {
        ReadAvailableSamplesCore(null);
    }

    private void ReadAvailableSamplesCore(List<float>? destination)
    {
        while (true)
        {
            _captureClient.GetNextPacketSize(out var packetFrameCount).ThrowIfFailed();
            if (packetFrameCount == 0)
            {
                return;
            }

            _captureClient.GetBuffer(out var dataPointer, out var frameCount, out var flags, out _, out _).ThrowIfFailed();
            try
            {
                if (frameCount == 0)
                {
                    continue;
                }

                var sampleCount = checked((int)frameCount);
                if ((flags & AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                {
                    if (destination != null)
                    {
                        for (var i = 0; i < sampleCount; i++)
                        {
                            destination.Add(0f);
                        }
                    }

                    continue;
                }

                if (destination == null)
                {
                    continue;
                }

                var samples = new short[sampleCount];
                Marshal.Copy(dataPointer, samples, 0, samples.Length);
                for (var i = 0; i < samples.Length; i++)
                {
                    destination.Add(samples[i] / 32768f);
                }
            }
            finally
            {
                _captureClient.ReleaseBuffer(frameCount).ThrowIfFailed();
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (_started)
            {
                _audioClient.Stop();
            }
        }
        catch (Exception)
        {
            // Best-effort cleanup only.
        }
        finally
        {
            ReleaseComObject(_captureClient);
            ReleaseComObject(_audioClient);
        }
    }

    private static void InitializeAudioClient(IAudioClient audioClient)
    {
        var format = new WaveFormatEx
        {
            WFormatTag = WaveFormatPcm,
            NChannels = ChannelCount,
            NSamplesPerSec = SileroVadDetector.SampleRate,
            NAvgBytesPerSec = AverageBytesPerSecond,
            NBlockAlign = BlockAlign,
            WBitsPerSample = BitsPerSample,
            CbSize = 0
        };

        var formatPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        try
        {
            Marshal.StructureToPtr(format, formatPointer, false);
            var audioSessionGuid = Guid.Empty;
            var streamFlags =
                AUDCLNT_STREAMFLAGS.AUDCLNT_STREAMFLAGS_LOOPBACK |
                AUDCLNT_STREAMFLAGS.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM |
                AUDCLNT_STREAMFLAGS.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;

            audioClient.Initialize(
                AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
                streamFlags,
                BufferDurationHundredNanoseconds,
                0,
                formatPointer,
                ref audioSessionGuid).ThrowIfFailed();
        }
        finally
        {
            Marshal.FreeHGlobal(formatPointer);
        }
    }

    private static IAudioClient ActivateAudioClient(int targetProcessId)
    {
        var parameters = new AudioClientActivationParams
        {
            ActivationType = AudioClientActivationType.ProcessLoopback,
            ProcessLoopbackParams = new AudioClientProcessLoopbackParams
            {
                TargetProcessId = (uint)targetProcessId,
                ProcessLoopbackMode = ProcessLoopbackMode.IncludeTargetProcessTree
            }
        };

        var size = Marshal.SizeOf<AudioClientActivationParams>();
        var parametersPointer = Marshal.AllocHGlobal(size);
        IActivateAudioInterfaceAsyncOperation? operation = null;
        try
        {
            Marshal.StructureToPtr(parameters, parametersPointer, false);
            var propVariant = PropVariant.FromBlob(parametersPointer, (uint)size);

            using var handler = new AudioInterfaceActivationHandler();
            var audioClientId = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsyncNative(
                VirtualAudioDeviceProcessLoopback,
                ref audioClientId,
                ref propVariant,
                handler,
                out operation));

            var audioClient = handler.WaitForAudioClient();
            GC.KeepAlive(handler);
            return audioClient;
        }
        finally
        {
            if (operation != null)
            {
                ReleaseComObject(operation);
            }

            Marshal.FreeHGlobal(parametersPointer);
        }
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject == null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
        catch (InvalidComObjectException)
        {
        }
        catch (COMException)
        {
        }
    }

    [DllImport("Mmdevapi.dll", EntryPoint = "ActivateAudioInterfaceAsync", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = true)]
    private static extern int ActivateAudioInterfaceAsyncNative(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        ref PropVariant activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientProcessLoopbackParams
    {
        public uint TargetProcessId;
        public ProcessLoopbackMode ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParams
    {
        public AudioClientActivationType ActivationType;
        public AudioClientProcessLoopbackParams ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public uint CbSize;
        public IntPtr PBlobData;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        private const ushort VtBlob = 65;

        [FieldOffset(0)]
        public ushort Vt;

        [FieldOffset(2)]
        public ushort WReserved1;

        [FieldOffset(4)]
        public ushort WReserved2;

        [FieldOffset(6)]
        public ushort WReserved3;

        [FieldOffset(8)]
        public Blob Blob;

        public static PropVariant FromBlob(IntPtr data, uint size)
        {
            return new PropVariant
            {
                Vt = VtBlob,
                Blob = new Blob
                {
                    CbSize = size,
                    PBlobData = data
                }
            };
        }
    }

    private enum ProcessLoopbackMode
    {
        IncludeTargetProcessTree = 0,
        ExcludeTargetProcessTree = 1
    }

    private enum AudioClientActivationType
    {
        Default = 0,
        ProcessLoopback = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort WFormatTag;
        public ushort NChannels;
        public uint NSamplesPerSec;
        public uint NAvgBytesPerSec;
        public ushort NBlockAlign;
        public ushort WBitsPerSample;
        public ushort CbSize;
    }

    [ComVisible(true)]
    private sealed class AudioInterfaceActivationHandler : IActivateAudioInterfaceCompletionHandler, IDisposable
    {
        private readonly ManualResetEventSlim _completed = new(false);
        private volatile bool _disposed;
        private Exception? _exception;
        private IAudioClient? _audioClient;

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try
            {
                operation.GetActivateResult(out var activateResult, out var activatedInterface).ThrowIfFailed();
                activateResult.ThrowIfFailed();
                _audioClient = activatedInterface as IAudioClient
                    ?? throw new InvalidCastException("ActivateAudioInterfaceAsync 未返回 IAudioClient");
            }
            catch (Exception e)
            {
                _exception = e;
            }
            finally
            {
                if (!_disposed)
                {
                    try
                    {
                        _completed.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            }
        }

        public IAudioClient WaitForAudioClient()
        {
            if (!_completed.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("等待进程音频接口激活超时");
            }

            if (_exception != null)
            {
                throw _exception;
            }

            return _audioClient ?? throw new InvalidOperationException("进程音频接口激活失败");
        }

        public void Dispose()
        {
            _disposed = true;
            _completed.Dispose();
        }
    }
}
