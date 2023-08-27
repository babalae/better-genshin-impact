using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Vision.WindowCapture.GraphicsCapture.Helpers;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using SharpDX.Direct3D11;
using WinRT.Interop;

namespace Vision.WindowCapture.GraphicsCapture
{
    public class GraphicsCapture : IWindowCapture
    {
        private IntPtr _hWnd;

        private Direct3D11CaptureFramePool _captureFramePool;
        private GraphicsCaptureItem _captureItem;
        private GraphicsCaptureSession _captureSession;

        public bool IsCapturing { get; private set; }

        public void Start(IntPtr hWnd)
        {
            _hWnd = hWnd;
            IsCapturing = true;

            /*
            // if use GraphicsCapturePicker, need to set this window handle
            // new WindowInteropHelper(this).Handle
            var picker = new GraphicsCapturePicker();
            picker.SetWindow(hWnd);
            _captureItem = picker.PickSingleItemAsync().AsTask().Result;
            */

            _captureItem = CaptureHelper.CreateItemForWindow(_hWnd);


            if (_captureItem == null)
            {
                throw new InvalidOperationException("Failed to create capture item.");
            }

            _captureItem.Closed += CaptureItemOnClosed;

            var device = Direct3D11Helper.CreateDevice();

            _captureFramePool = Direct3D11CaptureFramePool.Create(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2,
                _captureItem.Size);
            _captureSession = _captureFramePool.CreateCaptureSession(_captureItem);
            _captureSession.StartCapture();
            IsCapturing = true;
        }

        /// <summary>
        /// How to handle window size changes?
        /// </summary>
        /// <returns></returns>
        public Bitmap? Capture()
        {
            using var frame = _captureFramePool?.TryGetNextFrame();
            return frame?.ToBitmap();
        }

        public void Stop()
        {
            _captureSession?.Dispose();
            _captureFramePool?.Dispose();
            _captureSession = null;
            _captureFramePool = null;
            _captureItem = null;

            _hWnd = IntPtr.Zero;
            IsCapturing = false;
        }

        private void CaptureItemOnClosed(GraphicsCaptureItem sender, object args)
        {
            Stop();
        }
    }
}