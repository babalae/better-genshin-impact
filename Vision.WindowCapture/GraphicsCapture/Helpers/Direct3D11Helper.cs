//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// 
//  The MIT License (MIT)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
// 
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.System.WinRT.Direct3D11;
using WinRT;
using static Windows.Win32.PInvoke;

namespace Vision.WindowCapture.GraphicsCapture.Helpers;

public static class Direct3D11Helper
{
    public static IDirect3DDevice? CreateDevice()
    {
        return CreateDirect3DDevice();
    }

    public static unsafe IDirect3DDevice? CreateDirect3DDevice()
    {
        ID3D11Device? d3d11Device;

        HRESULT hr = D3D11CreateDevice(
            default,
            D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
            default,
            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            default,
            0,
            D3D11_SDK_VERSION,
            out d3d11Device,
            default,
            out _);

        if (hr == HRESULT.DXGI_ERROR_UNSUPPORTED)
        {
            D3D11CreateDevice(
                default,
                D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_SOFTWARE,
                default,
                D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                default,
                0,
                D3D11_SDK_VERSION,
                out d3d11Device,
                default,
                out _);
        }

        return CreateDirect3DDeviceFromD3D11Device(d3d11Device);
    }

    public static IDirect3DDevice? CreateDirect3DDeviceFromD3D11Device(ID3D11Device d3d11Device)
    {
        // Acquire the DXGI interface for the Direct3D device.
        // Wrap the native device using a WinRT interop object.
        if (CreateDirect3D11DeviceFromDXGIDevice(d3d11Device.As<IDXGIDevice>(), out var iInspectable).Succeeded)
        {
            nint thisPtr = Marshal.GetIUnknownForObject(iInspectable);
            return MarshalInterface<IDirect3DDevice>.FromAbi(thisPtr);
        }

        return default;
    }

    public static IDirect3DSurface? CreateDirect3DSurfaceFromSharpDXTexture(ID3D11Texture2D texture)
    {
        // Acquire the DXGI interface for the Direct3D surface.
        // Wrap the native device using a WinRT interop object.
        if (CreateDirect3D11SurfaceFromDXGISurface(texture.As<IDXGISurface>(), out var iInspectable).Succeeded)
        {
            nint thisPtr = Marshal.GetIUnknownForObject(iInspectable);
            return MarshalInterface<IDirect3DSurface>.FromAbi(thisPtr);
        }

        return default;
    }

    public static ID3D11Device CreateD3D11Device(IDirect3DDevice device)
    {
        device
            .As<IDirect3DDxgiInterfaceAccess>()
            .GetInterface(typeof(ID3D11Device).GUID, out var d3dDevice);
        object obj = Marshal.GetObjectForIUnknown(d3dDevice);
        return obj.As<ID3D11Device>();
    }

    public static ID3D11Texture2D CreateD3D11Texture2D(IDirect3DSurface surface)
    {
        surface
            .As<IDirect3DDxgiInterfaceAccess>()
            .GetInterface(typeof(ID3D11Texture2D).GUID, out var d3dSurface);
        object obj = Marshal.GetObjectForIUnknown(d3dSurface);
        return obj.As<ID3D11Texture2D>();
    }
}
