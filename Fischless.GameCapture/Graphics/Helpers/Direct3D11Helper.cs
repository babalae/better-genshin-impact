using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using WinRT;
using Device = SharpDX.Direct3D11.Device;

namespace Fischless.GameCapture.Graphics.Helpers;

public static class Direct3D11Helper
{
    internal static Guid ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    interface IDirect3DDxgiInterfaceAccess
    {
        nint GetInterface([In] ref Guid iid);
    };

    [DllImport(
        "d3d11.dll",
        EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        CallingConvention = CallingConvention.StdCall
       )]
    static extern uint CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    public static IDirect3DDevice CreateDevice()
    {
        return CreateDevice(false);
    }

    private static Device? d3dDevice;

    public static IDirect3DDevice CreateDevice(bool useWARP)
    {
        d3dDevice ??= new Device(
            useWARP ? SharpDX.Direct3D.DriverType.Software : SharpDX.Direct3D.DriverType.Hardware,
            DeviceCreationFlags.BgraSupport);
        var device = CreateDirect3DDeviceFromSharpDXDevice(d3dDevice);
        return device;
    }

    public static IDirect3DDevice CreateDirect3DDeviceFromSharpDXDevice(Device d3dDevice)
    {
        IDirect3DDevice device = default!;

        // Acquire the DXGI interface for the Direct3D device.
        using (var dxgiDevice = d3dDevice.QueryInterface<SharpDX.DXGI.Device3>())
        {
            // Wrap the native device using a WinRT interop object.
            uint hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out nint pUnknown);

            if (hr == 0)
            {
                device = MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
                Marshal.Release(pUnknown);
            }
        }

        return device;
    }

    public static Texture2D CreateSharpDXTexture2D(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var d3dPointer = access.GetInterface(ID3D11Texture2D);
        var d3dSurface = new Texture2D(d3dPointer);
        return d3dSurface;
    }

    public static Texture2D CreateStagingTexture(Device device, int width, int height, ResourceRegion? region)
    {
        return new Texture2D(device, new Texture2DDescription
        {
            Width = region == null ? width : region.Value.Right - region.Value.Left,
            Height = region == null ? height : region.Value.Bottom - region.Value.Top,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            Usage = ResourceUsage.Staging,
            SampleDescription = new SampleDescription(1, 0),
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None
        });
    }

    public static Texture2D CreateOutputTexture(Device device, int width, int height)
    {
        return new Texture2D(device, new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            Usage = ResourceUsage.Default,
            SampleDescription = new SampleDescription(1, 0),
            BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource,
            CpuAccessFlags = CpuAccessFlags.None,
            OptionFlags = ResourceOptionFlags.None
        });
    }
}
