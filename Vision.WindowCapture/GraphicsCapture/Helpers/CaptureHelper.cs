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

using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Capture;
using WinRT;

namespace Vision.WindowCapture.GraphicsCapture.Helpers
{
    public static class CaptureHelper
    {
        static readonly Guid GraphicsCaptureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        [ComImport]
        [Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        interface IInitializeWithWindow
        {
            void Initialize(
                IntPtr hwnd);
        }

        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [ComVisible(true)]
        interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow(
                [In] IntPtr window,
                [In] ref Guid iid);

            IntPtr CreateForMonitor(
                [In] IntPtr monitor,
                [In] ref Guid iid);
        }

        public static void SetWindow(this GraphicsCapturePicker picker, IntPtr hwnd)
        {
            var interop = picker.As<IInitializeWithWindow>();
            interop.Initialize(hwnd);
        }

        public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
        {
            var factory = WinrtModule.GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem");
            var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
            var itemPointer = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
            return GraphicsCaptureItem.FromAbi(itemPointer);
        }

        //public static GraphicsCaptureItem CreateItemForMonitor(IntPtr hmon)
        //{
        //    var factory = WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
        //    var interop = (IGraphicsCaptureItemInterop)factory;
        //    var temp = typeof(GraphicsCaptureItem);         
        //    var itemPointer = interop.CreateForMonitor(hmon, GraphicsCaptureItemGuid);
        //    var item = Marshal.GetObjectForIUnknown(itemPointer) as GraphicsCaptureItem;
        //    Marshal.Release(itemPointer);

        //    return item;
        //}
    }
}
