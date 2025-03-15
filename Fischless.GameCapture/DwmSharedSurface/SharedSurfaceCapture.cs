using Fischless.GameCapture.DwmSharedSurface.Helpers;
using Fischless.GameCapture.Graphics.Helpers;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System.Diagnostics;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Vanara.PInvoke;
using Device = SharpDX.Direct3D11.Device;

namespace Fischless.GameCapture.DwmSharedSurface
{
    public class SharedSurfaceCapture : IGameCapture
    {
        private nint _hWnd;
        private static readonly object LockObject = new object();
        private Device? _d3dDevice;

        public void Dispose() => Stop();

        public bool IsCapturing { get; private set; }

        private ResourceRegion? _region;

        public CaptureModes Mode => CaptureModes.DwmGetDxSharedSurface;

        public void Start(nint hWnd, Dictionary<string, object>? settings = null)
        {
            _hWnd = hWnd;
            User32.ShowWindow(hWnd, ShowWindowCommand.SW_RESTORE);
            User32.SetForegroundWindow(hWnd);
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
        private ResourceRegion? GetGameScreenRegion(nint hWnd)
        {
            var exStyle = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
            if ((exStyle & (int)User32.WindowStylesEx.WS_EX_TOPMOST) != 0)
            {
                return null;
            }

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

        public Mat? Capture()
        {
            if (_hWnd == nint.Zero)
            {
                return null;
            }

            lock (LockObject)
            {
                NativeMethods.DwmGetDxSharedSurface(_hWnd, out var phSurface, out _, out _, out _, out _);
                if (phSurface == nint.Zero)
                {
                    return null;
                }

       
                if (_d3dDevice == null)
                {
                    Debug.WriteLine("D3Device is null.");
                    return null;
                }

                using var surfaceTexture = _d3dDevice.OpenSharedResource<Texture2D>(phSurface);
                using var stagingTexture = CreateStagingTexture(surfaceTexture, _d3dDevice);
                var mat = stagingTexture.CreateMat(_d3dDevice, surfaceTexture, _region);
                if (mat == null)
                {
                    return null;
                }
                var bgrMat = new Mat();
                Cv2.CvtColor(mat, bgrMat, ColorConversionCodes.BGRA2BGR);
                return bgrMat;
            }
        }
        

        private Texture2D CreateStagingTexture(Texture2D surfaceTexture, Device device)
        {
            return new Texture2D(device, new Texture2DDescription
            {
                Width = _region == null ? surfaceTexture.Description.Width : _region.Value.Right - _region.Value.Left,
                Height = _region == null ? surfaceTexture.Description.Height : _region.Value.Bottom - _region.Value.Top,
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

        public void Stop()
        {
            _hWnd = nint.Zero;
            IsCapturing = false;
        }
    }
}