using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Fischless.GameCapture.Graphics.Helpers;

[SuppressMessage("CodeQuality", "IDE0052:")]
public static class Direct3D11Helper
{
    internal static Guid IInspectable = new("AF86E2E0-B12D-4c6a-9C5A-D7AA65101E90");
    internal static Guid ID3D11Resource = new("dc8e63f3-d12b-4952-b47b-5e45026a862d");
    internal static Guid IDXGIAdapter3 = new("645967A4-1392-4310-A798-8053CE3E93FD");
    internal static Guid ID3D11Device = new("db6f6ddb-ac77-4e88-8253-819df9bbf140");
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

    [DllImport(
        "d3d11.dll",
        EntryPoint = "CreateDirect3D11SurfaceFromDXGISurface",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        CallingConvention = CallingConvention.StdCall
       )]
    static extern uint CreateDirect3D11SurfaceFromDXGISurface(nint dxgiSurface, out nint graphicsSurface);

    public static IDirect3DDevice CreateDevice()
    {
        return CreateDevice(false);
    }

    public static IDirect3DDevice CreateDevice(bool useWARP)
    {
        var d3dDevice = new SharpDX.Direct3D11.Device(
            useWARP ? SharpDX.Direct3D.DriverType.Software : SharpDX.Direct3D.DriverType.Hardware,
            SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport);
        var device = CreateDirect3DDeviceFromSharpDXDevice(d3dDevice);
        return device;
    }

    public static IDirect3DDevice CreateDirect3DDeviceFromSharpDXDevice(SharpDX.Direct3D11.Device d3dDevice)
    {
        IDirect3DDevice device = null;

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

    public static IDirect3DSurface CreateDirect3DSurfaceFromSharpDXTexture(SharpDX.Direct3D11.Texture2D texture)
    {
        IDirect3DSurface surface = null;

        // Acquire the DXGI interface for the Direct3D surface.
        using (var dxgiSurface = texture.QueryInterface<SharpDX.DXGI.Surface>())
        {
            // Wrap the native device using a WinRT interop object.
            uint hr = CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out nint pUnknown);

            if (hr == 0)
            {
                surface = Marshal.GetObjectForIUnknown(pUnknown) as IDirect3DSurface;
                Marshal.Release(pUnknown);
            }
        }

        return surface;
    }

    public static SharpDX.Direct3D11.Device CreateSharpDXDevice(IDirect3DDevice device)
    {
        var access = device.As<IDirect3DDxgiInterfaceAccess>();
        var d3dPointer = access.GetInterface(ID3D11Device);
        var d3dDevice = new SharpDX.Direct3D11.Device(d3dPointer);
        return d3dDevice;
    }

    public static SharpDX.Direct3D11.Texture2D CreateSharpDXTexture2D(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var d3dPointer = access.GetInterface(ID3D11Texture2D);
        var d3dSurface = new SharpDX.Direct3D11.Texture2D(d3dPointer);
        return d3dSurface;
    }
}
