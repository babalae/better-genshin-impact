using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Controls;
using System.Windows.Forms;
using Fischless.GameCapture.DwmSharedSurface.Helpers;
using Fischless.GameCapture.Graphics.Helpers;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Vanara.PInvoke;
using Windows.Devices.HumanInterfaceDevice;
using Device = SharpDX.Direct3D11.Device;

namespace Fischless.GameCapture.DwmSharedSurface
{
    public class SharedSurfaceCapture : IGameCapture
    {
        private nint _hWnd;
        private Device? _d3dDevice;

        public void Dispose() => Stop();

        public bool IsCapturing { get; private set; }

        private ResourceRegion _region;

        public void Start(nint hWnd)
        {
            _hWnd = hWnd;
            _region = GetGameScreenRegion(hWnd);
            _d3dDevice = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport); // Software/Hardware

            //var device = Direct3D11Helper.CreateDevice();
            //_d3dDevice = Direct3D11Helper.CreateSharpDXDevice(device);

            IsCapturing = true;
        }

        /// <summary>
        /// 从 GetWindowRect 的带窗口阴影面积矩形 截取出 GetClientRect的矩形（游戏区域）
        /// </summary>
        /// <param name="hWnd"></param>
        /// <returns></returns>
        private ResourceRegion GetGameScreenRegion(nint hWnd)
        {
            ResourceRegion region = new();
            User32.GetWindowRect(hWnd, out var windowWithShadowRect);
            DwmApi.DwmGetWindowAttribute<RECT>(hWnd, DwmApi.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, out var windowRect);
            User32.GetClientRect(_hWnd, out var clientRect);

            region.Left = windowRect.Left - windowWithShadowRect.Left;
            // 标题栏 windowRect.Height - clientRect.Height 上阴影 windowRect.Top - windowWithShadowRect.Top
            region.Top = windowRect.Height - clientRect.Height + windowRect.Top - windowWithShadowRect.Top;
            region.Right = region.Left + clientRect.Width;
            region.Bottom = region.Top + clientRect.Height;
            region.Front = 0;
            region.Back = 1;

            return region;
        }

        public Bitmap? Capture()
        {
            if (_hWnd == nint.Zero)
            {
                return null;
            }


            NativeMethods.DwmGetDxSharedSurface(_hWnd, out var phSurface, out _, out _, out _, out _);
            if (phSurface == nint.Zero)
            {
                return null;
            }

            return ToBitmap(phSurface);
        }

        private Bitmap? ToBitmap(nint phSurface)
        {
            if (_d3dDevice == null)
            {
                Debug.WriteLine("D3Device is null.");
                return null;
            }

            using var surfaceTexture = _d3dDevice.OpenSharedResource<Texture2D>(phSurface);

            var staging = new Texture2D(_d3dDevice, new Texture2DDescription
            {
                Width =  _region.Right - _region.Left,
                Height = _region.Bottom - _region.Top,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                Usage = ResourceUsage.Staging,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags = BindFlags.None,
                CpuAccessFlags = CpuAccessFlags.Read,
                OptionFlags = ResourceOptionFlags.None
            });


            return staging.CreateBitmap(_d3dDevice, surfaceTexture, _region);
        }

        public void Stop()
        {
            _hWnd = nint.Zero;
            IsCapturing = false;
        }
    }
}