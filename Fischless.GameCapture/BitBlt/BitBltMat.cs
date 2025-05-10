using OpenCvSharp;
using OpenCvSharp.Internal;

namespace Fischless.GameCapture.BitBlt;

public class BitBltMat : Mat
{
    private readonly BitBltSession _session;
    private readonly IntPtr _data;

    private BitBltMat(IntPtr ptr, BitBltSession session, IntPtr data)
    {
        if (ptr == IntPtr.Zero)
            throw new OpenCvSharpException("Native object address is NULL");
        this.ptr = ptr;
        _session = session;
        _data = data;
    }

    public static Mat FromPixelData(BitBltSession session, int rows, int cols, MatType type, IntPtr data, long step = 0)
    {
        NativeMethods.HandleException(
            NativeMethods.core_Mat_new8(rows, cols, type, data, new IntPtr(step), out var ptr));
        return new BitBltMat(ptr, session, data);
    }

    protected override void DisposeUnmanaged()
    {
        base.DisposeUnmanaged();
        _session.ReleaseBuffer(_data);
    }
}
