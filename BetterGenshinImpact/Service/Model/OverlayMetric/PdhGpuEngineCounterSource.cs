using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Service.Model.OverlayMetric;

internal sealed class PdhGpuEngineCounterSource : IDisposable
{
    private const string GpuEngineCounterPath = @"\GPU Engine(*)\Utilization Percentage";
    private const uint ErrorSuccess = 0x00000000;
    private const uint PdhCStatusNoInstance = 0x800007D1;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhRetry = 0x800007D4;
    private const uint PdhNoData = 0x800007D5;
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhFormatNoCap100 = 0x00008000;
    private const int MaximumBufferRetries = 3;

    private IntPtr _queryHandle;
    private IntPtr _counterHandle;
    private bool _baselineOnly = true;
    private bool _disposed;

    public PdhGpuEngineCounterSource()
    {
        var status = PdhOpenQueryW(null, UIntPtr.Zero, out _queryHandle);
        ThrowIfFailed(status, nameof(PdhOpenQueryW));

        try
        {
            status = PdhAddEnglishCounterW(_queryHandle, GpuEngineCounterPath, UIntPtr.Zero, out _counterHandle);
            ThrowIfFailed(status, nameof(PdhAddEnglishCounterW));

            // 百分比计数器需要两个时间点；构造时只采集基线，首轮无有效值时上层会自动隐藏。
            status = PdhCollectQueryData(_queryHandle);
            if (!IsNoData(status))
            {
                ThrowIfFailed(status, nameof(PdhCollectQueryData));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public IReadOnlyList<GpuEngineCounterValue> Sample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_baselineOnly)
        {
            _baselineOnly = false;
            return [];
        }

        var status = PdhCollectQueryData(_queryHandle);
        if (IsNoData(status))
        {
            return [];
        }

        ThrowIfFailed(status, nameof(PdhCollectQueryData));
        return ReadFormattedCounterArray();
    }

    private IReadOnlyList<GpuEngineCounterValue> ReadFormattedCounterArray()
    {
        for (var attempt = 0; attempt < MaximumBufferRetries; attempt++)
        {
            uint bufferSize = 0;
            uint itemCount = 0;
            var status = PdhGetFormattedCounterArrayW(
                _counterHandle,
                PdhFormatDouble | PdhFormatNoCap100,
                ref bufferSize,
                ref itemCount,
                IntPtr.Zero);

            if (IsNoData(status))
            {
                return [];
            }

            if (status != PdhMoreData && status != ErrorSuccess)
            {
                ThrowIfFailed(status, nameof(PdhGetFormattedCounterArrayW));
            }

            if (bufferSize == 0 || itemCount == 0)
            {
                return [];
            }

            if (bufferSize > int.MaxValue)
            {
                throw new InvalidOperationException($"PDH GPU 计数器缓冲区过大：{bufferSize} 字节");
            }

            var buffer = Marshal.AllocHGlobal((int)bufferSize);
            try
            {
                status = PdhGetFormattedCounterArrayW(
                    _counterHandle,
                    PdhFormatDouble | PdhFormatNoCap100,
                    ref bufferSize,
                    ref itemCount,
                    buffer);

                if (status == PdhMoreData)
                {
                    continue;
                }

                if (IsNoData(status) || itemCount == 0)
                {
                    return [];
                }

                ThrowIfFailed(status, nameof(PdhGetFormattedCounterArrayW));
                if (itemCount > int.MaxValue)
                {
                    throw new InvalidOperationException($"PDH GPU 计数器实例过多：{itemCount}");
                }

                var itemSize = Marshal.SizeOf<PdhFormattedCounterValueItem>();
                var values = new List<GpuEngineCounterValue>((int)itemCount);
                for (var index = 0; index < itemCount; index++)
                {
                    var itemPointer = IntPtr.Add(buffer, checked((int)index * itemSize));
                    var item = Marshal.PtrToStructure<PdhFormattedCounterValueItem>(itemPointer);
                    var instanceName = Marshal.PtrToStringUni(item.NamePointer);
                    if (!string.IsNullOrEmpty(instanceName))
                    {
                        values.Add(new GpuEngineCounterValue(
                            instanceName,
                            item.CounterValue.Status,
                            item.CounterValue.DoubleValue));
                    }
                }

                return values;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        throw new InvalidOperationException("PDH GPU 计数器实例在读取期间持续变化，无法取得稳定快照");
    }

    private static bool IsNoData(uint status)
    {
        return status is PdhCStatusNoInstance or PdhRetry or PdhNoData;
    }

    private static void ThrowIfFailed(uint status, string operation)
    {
        if (status != ErrorSuccess)
        {
            throw new InvalidOperationException($"{operation} 失败，PDH 状态码 0x{status:X8}");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_counterHandle != IntPtr.Zero)
        {
            _ = PdhRemoveCounter(_counterHandle);
            _counterHandle = IntPtr.Zero;
        }

        if (_queryHandle != IntPtr.Zero)
        {
            _ = PdhCloseQuery(_queryHandle);
            _queryHandle = IntPtr.Zero;
        }
    }

    ~PdhGpuEngineCounterSource()
    {
        Dispose(false);
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PdhFormattedCounterValue
    {
        [FieldOffset(0)]
        public uint Status;

        [FieldOffset(8)]
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValueItem
    {
        public IntPtr NamePointer;
        public PdhFormattedCounterValue CounterValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(
        string? dataSource,
        UIntPtr userData,
        out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(
        IntPtr query,
        string fullCounterPath,
        UIntPtr userData,
        out IntPtr counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterArrayW(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        ref uint itemCount,
        IntPtr itemBuffer);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhRemoveCounter(IntPtr counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(IntPtr query);
}
