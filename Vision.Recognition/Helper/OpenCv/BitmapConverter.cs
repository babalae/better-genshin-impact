using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Vision.Recognition.Helper.OpenCv
{
    /// <summary>
    /// static class which provides conversion between System.Drawing.Bitmap and Mat
    /// copy of https://github.com/shimat/opencvsharp/blob/main/src/OpenCvSharp.Extensions/BitmapConverter.cs
    /// </summary>
    public static class BitmapConverter
    {
        #region ToMat

        /// <summary>
        /// Converts System.Drawing.Bitmap to Mat
        /// </summary>
        /// <param name="src">System.Drawing.Bitmap object to be converted</param>
        /// <returns>A Mat object which is converted from System.Drawing.Bitmap</returns>
        public static Mat ToMat(this Bitmap src)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new NotSupportedException("Non-Windows OS are not supported");
            if (src is null)
                throw new ArgumentNullException(nameof(src));

            int w = src.Width;
            int h = src.Height;
            int channels;
            switch (src.PixelFormat)
            {
                case PixelFormat.Format24bppRgb:
                case PixelFormat.Format32bppRgb:
                    channels = 3; break;
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                    channels = 4; break;
                case PixelFormat.Format8bppIndexed:
                case PixelFormat.Format1bppIndexed:
                    channels = 1; break;
                default:
                    throw new NotImplementedException();
            }

            Mat dst = new Mat(h, w, MatType.CV_8UC(channels));
            ToMat(src, dst);
            return dst;
        }

        /// <summary>
        /// Converts System.Drawing.Bitmap to Mat
        /// </summary>
        /// <param name="src">System.Drawing.Bitmap object to be converted</param>
        /// <param name="dst">A Mat object which is converted from System.Drawing.Bitmap</param>
        public static unsafe void ToMat(this Bitmap src, Mat dst)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new NotSupportedException("Non-Windows OS are not supported");
            if (src is null)
                throw new ArgumentNullException(nameof(src));
            if (dst is null)
                throw new ArgumentNullException(nameof(dst));
            if (dst.IsDisposed)
                throw new ArgumentException("The specified dst is disposed.", nameof(dst));
            if (dst.Depth() != MatType.CV_8U)
                throw new NotSupportedException("Mat depth != CV_8U");
            if (dst.Dims != 2)
                throw new NotSupportedException("Mat dims != 2");
            if (src.Width != dst.Width || src.Height != dst.Height)
                throw new ArgumentException("src.Size != dst.Size");

            int w = src.Width;
            int h = src.Height;
            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData? bd = null;
            try
            {
                bd = src.LockBits(rect, ImageLockMode.ReadOnly, src.PixelFormat);

                switch (src.PixelFormat)
                {
                    case PixelFormat.Format1bppIndexed:
                        Format1bppIndexed();
                        break;

                    case PixelFormat.Format8bppIndexed:
                        Format8bppIndexed();
                        break;

                    case PixelFormat.Format24bppRgb:
                        Format24bppRgb();
                        break;

                    case PixelFormat.Format32bppRgb:
                    case PixelFormat.Format32bppArgb:
                    case PixelFormat.Format32bppPArgb:
                        Format32bppRgb();
                        break;
                }
            }
            finally
            {
                if (bd is not null)
                    src.UnlockBits(bd);
            }

            // ReSharper disable once InconsistentNaming
            void Format1bppIndexed()
            {
                if (dst.Channels() != 1)
                    throw new ArgumentException("Invalid nChannels");
                if (dst.IsSubmatrix())
                    throw new NotImplementedException("submatrix not supported");
                if (bd is null)
                    throw new NotSupportedException("BitmapData is null (Format1bppIndexed)");

                byte* srcPtr = (byte*)bd.Scan0.ToPointer();
                byte* dstPtr = dst.DataPointer;
                int srcStep = bd.Stride;
                uint dstStep = (uint)dst.Step();
                int x = 0;

                for (int y = 0; y < h; y++)
                {
                    // 横は必ず4byte幅に切り上げられる。
                    // この行の各バイトを調べていく
                    for (int bytePos = 0; bytePos < srcStep; bytePos++)
                    {
                        if (x < w)
                        {
                            // 現在の位置のバイトからそれぞれのビット8つを取り出す
                            byte b = srcPtr[bytePos];
                            for (int i = 0; i < 8; i++)
                            {
                                if (x >= w)
                                {
                                    break;
                                }
                                // IplImageは8bit/pixel
                                dstPtr[dstStep * y + x] = ((b & 0x80) == 0x80) ? (byte)255 : (byte)0;
                                b <<= 1;
                                x++;
                            }
                        }
                    }
                    // 次の行へ
                    x = 0;
                    srcPtr += srcStep;
                }
            }

            // ReSharper disable once InconsistentNaming
            void Format8bppIndexed()
            {
                static void Ch1(Mat dst, int height, int srcStep, uint dstStep, IntPtr srcData, byte[] palette)
                {
                    if (dstStep == srcStep && !dst.IsSubmatrix() && dst.IsContinuous())
                    {
                        // Read Bitmap pixel data to managed array
                        long length = dst.DataEnd.ToInt64() - dst.Data.ToInt64();
                        if (length > int.MaxValue)
                            throw new NotSupportedException("Too big dst Mat");
                        var buffer = new byte[length];
                        Marshal.Copy(srcData, buffer, 0, buffer.Length);
                        // Apply conversion by palette 
                        buffer = buffer.Select(b => palette[b]).ToArray();
                        // Write to dst Mat
                        Marshal.Copy(buffer, 0, dst.Data, buffer.Length);
                    }
                    else
                    {
                        // Copy line bytes from src to dst for each line
                        byte* sp = (byte*)srcData;
                        byte* dp = (byte*)dst.Data;
                        var buffer = new byte[srcStep];
                        for (int y = 0; y < height; y++)
                        {
                            // Read Bitmap pixel data to managed array
                            Marshal.Copy(new IntPtr(sp), buffer, 0, buffer.Length);
                            // Apply conversion by palette 
                            buffer = buffer.Select(b => palette[b]).ToArray();
                            // Write to dst Mat
                            Marshal.Copy(buffer, 0, new IntPtr(dp), buffer.Length);

                            sp += srcStep;
                            dp += dstStep;
                        }
                    }
                }

                int srcStep = bd.Stride;
                uint dstStep = (uint)dst.Step();

                int channels = dst.Channels();
                if (channels == 1)
                {
                    var palette = new byte[256];
                    var paletteLength = Math.Min(256, src.Palette.Entries.Length);
                    for (int i = 0; i < paletteLength; i++)
                    {
                        // TODO src.Palette.Flags & 2 == 2
                        // https://docs.microsoft.com/ja-jp/dotnet/api/system.drawing.imaging.colorpalette.flags?view=netframework-4.8
                        palette[i] = src.Palette.Entries[i].R;
                    }
                    Ch1(dst, h, srcStep, dstStep, bd.Scan0, palette);
                }
                else if (channels == 3)
                {
                    // Palette
                    var paletteR = new byte[256];
                    var paletteG = new byte[256];
                    var paletteB = new byte[256];
                    var paletteLength = Math.Min(256, src.Palette.Entries.Length);
                    for (int i = 0; i < paletteLength; i++)
                    {
                        var c = src.Palette.Entries[i];
                        paletteR[i] = c.R;
                        paletteG[i] = c.G;
                        paletteB[i] = c.B;
                    }

                    using var dstR = new Mat(h, w, MatType.CV_8UC1);
                    using var dstG = new Mat(h, w, MatType.CV_8UC1);
                    using var dstB = new Mat(h, w, MatType.CV_8UC1);

                    Ch1(dstR, h, srcStep, (uint)dstR.Step(), bd.Scan0, paletteR);
                    Ch1(dstG, h, srcStep, (uint)dstG.Step(), bd.Scan0, paletteG);
                    Ch1(dstB, h, srcStep, (uint)dstB.Step(), bd.Scan0, paletteB);
                    Cv2.Merge(new[] { dstB, dstG, dstR }, dst);
                }
                else
                {
                    throw new ArgumentException($"Invalid channels of dst Mat ({channels})");
                }
            }

            // ReSharper disable once InconsistentNaming
            void Format24bppRgb()
            {
                if (dst.Channels() != 3)
                    throw new ArgumentException("Invalid nChannels");
                if (dst.Depth() != MatType.CV_8U && dst.Depth() != MatType.CV_8S)
                    throw new ArgumentException("Invalid depth of dst Mat");

                int srcStep = bd.Stride;
                long dstStep = dst.Step();
                if (dstStep == srcStep && !dst.IsSubmatrix() && dst.IsContinuous())
                {
                    IntPtr dstData = dst.Data;
                    long bytesToCopy = dst.DataEnd.ToInt64() - dstData.ToInt64();
                    Buffer.MemoryCopy(bd.Scan0.ToPointer(), dstData.ToPointer(), bytesToCopy, bytesToCopy);
                }
                else
                {
                    // Copy line bytes from src to dst for each line
                    byte* sp = (byte*)bd.Scan0;
                    byte* dp = (byte*)dst.Data;
                    for (int y = 0; y < h; y++)
                    {
                        Buffer.MemoryCopy(sp, dp, dstStep, dstStep);
                        sp += srcStep;
                        dp += dstStep;
                    }
                }
            }

            // ReSharper disable once InconsistentNaming
            void Format32bppRgb()
            {
                int srcStep = bd.Stride;
                long dstStep = dst.Step();

                switch (dst.Channels())
                {
                    case 4:
                        if (!dst.IsSubmatrix() && dst.IsContinuous())
                        {
                            IntPtr dstData = dst.Data;
                            long bytesToCopy = dst.DataEnd.ToInt64() - dstData.ToInt64();
                            Buffer.MemoryCopy(bd.Scan0.ToPointer(), dstData.ToPointer(), bytesToCopy, bytesToCopy);
                        }
                        else
                        {
                            byte* sp = (byte*)bd.Scan0;
                            byte* dp = (byte*)dst.Data;
                            for (int y = 0; y < h; y++)
                            {
                                Buffer.MemoryCopy(sp, dp, dstStep, dstStep);
                                sp += srcStep;
                                dp += dstStep;
                            }
                        }

                        break;
                    case 3:
                        byte* srcPtr = (byte*)bd.Scan0.ToPointer();
                        byte* dstPtr = (byte*)dst.Data.ToPointer();
                        for (int y = 0; y < h; y++)
                        {
                            for (int x = 0; x < w; x++)
                            {
                                dstPtr[y * dstStep + x * 3 + 0] = srcPtr[y * srcStep + x * 4 + 0];
                                dstPtr[y * dstStep + x * 3 + 1] = srcPtr[y * srcStep + x * 4 + 1];
                                dstPtr[y * dstStep + x * 3 + 2] = srcPtr[y * srcStep + x * 4 + 2];
                            }
                        }

                        break;
                    default:
                        throw new ArgumentException("Invalid nChannels");
                }
            }
        }
        #endregion

        #region ToBitmap

        /// <summary>
        /// Converts Mat to System.Drawing.Bitmap
        /// </summary>
        /// <param name="src">Mat</param>
        /// <returns></returns>
        public static Bitmap ToBitmap(this Mat src)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new NotSupportedException("Non-Windows OS are not supported");
            if (src is null)
                throw new ArgumentNullException(nameof(src));

            PixelFormat pf;
            switch (src.Channels())
            {
                case 1:
                    pf = PixelFormat.Format8bppIndexed; break;
                case 3:
                    pf = PixelFormat.Format24bppRgb; break;
                case 4:
                    pf = PixelFormat.Format32bppArgb; break;
                default:
                    throw new ArgumentException("Number of channels must be 1, 3 or 4.", nameof(src));
            }
            return ToBitmap(src, pf);
        }

        /// <summary>
        /// Converts Mat to System.Drawing.Bitmap
        /// </summary>
        /// <param name="src">Mat</param>
        /// <param name="pf">Pixel Depth</param>
        /// <returns></returns>
        public static Bitmap ToBitmap(this Mat src, PixelFormat pf)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new NotSupportedException("Non-Windows OS are not supported");
            if (src is null)
                throw new ArgumentNullException(nameof(src));
            src.ThrowIfDisposed();

            Bitmap bitmap = new Bitmap(src.Width, src.Height, pf);
            ToBitmap(src, bitmap);
            return bitmap;
        }

        /// <summary>
        /// Converts Mat to System.Drawing.Bitmap
        /// </summary>
        /// <param name="src">Mat</param>
        /// <param name="dst">Mat</param>
        /// <remarks>Author: shimat, Gummo (ROI support)</remarks>
        public static unsafe void ToBitmap(this Mat src, Bitmap dst)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new NotSupportedException("Non-Windows OS are not supported");
            if (src is null)
                throw new ArgumentNullException(nameof(src));
            if (dst is null)
                throw new ArgumentNullException(nameof(dst));
            if (src.IsDisposed)
                throw new ArgumentException("The image is disposed.", nameof(src));
            if (src.Depth() != MatType.CV_8U)
                throw new ArgumentException("Depth of the image must be CV_8U");
            //if (src.IsSubmatrix())
            //    throw new ArgumentException("Submatrix is not supported");
            if (src.Width != dst.Width || src.Height != dst.Height)
                throw new ArgumentException("");

            PixelFormat pf = dst.PixelFormat;

            // 1プレーン用の場合、グレースケールのパレット情報を生成する
            if (pf == PixelFormat.Format8bppIndexed)
            {
                ColorPalette plt = dst.Palette;
                for (int x = 0; x < 256; x++)
                {
                    plt.Entries[x] = Color.FromArgb(x, x, x);
                }
                dst.Palette = plt;
            }

            int w = src.Width;
            int h = src.Height;
            Rectangle rect = new Rectangle(0, 0, w, h);
            BitmapData? bd = null;

            bool submat = src.IsSubmatrix();
            bool continuous = src.IsContinuous();

            try
            {
                bd = dst.LockBits(rect, ImageLockMode.WriteOnly, pf);

                IntPtr srcData = src.Data;
                byte* pSrc = (byte*)(srcData.ToPointer());
                byte* pDst = (byte*)(bd.Scan0.ToPointer());
                int ch = src.Channels();
                int srcStep = (int)src.Step();
                int dstStep = ((src.Width * ch) + 3) / 4 * 4; // 4の倍数に揃える
                int stride = bd.Stride;

                switch (pf)
                {
                    case PixelFormat.Format1bppIndexed:
                        {
                            if (submat)
                                throw new NotImplementedException("submatrix not supported");

                            // BitmapDataは4byte幅だが、IplImageは1byte幅
                            // 手作業で移し替える                 
                            //int offset = stride - (w / 8);
                            int x = 0;
                            byte b = 0;
                            for (int y = 0; y < h; y++)
                            {
                                for (int bytePos = 0; bytePos < stride; bytePos++)
                                {
                                    if (x < w)
                                    {
                                        for (int i = 0; i < 8; i++)
                                        {
                                            var mask = (byte)(0x80 >> i);
                                            if (x < w && pSrc[srcStep * y + x] == 0)
                                                b &= (byte)(mask ^ 0xff);
                                            else
                                                b |= mask;

                                            x++;
                                        }
                                        pDst[bytePos] = b;
                                    }
                                }
                                x = 0;
                                pDst += stride;
                            }
                            break;
                        }

                    case PixelFormat.Format8bppIndexed:
                    case PixelFormat.Format24bppRgb:
                    case PixelFormat.Format32bppArgb:
                        if (srcStep == dstStep && !submat && continuous)
                        {
                            long bytesToCopy = src.DataEnd.ToInt64() - src.Data.ToInt64();
                            Buffer.MemoryCopy(pSrc, pDst, bytesToCopy, bytesToCopy);
                        }
                        else
                        {
                            for (int y = 0; y < h; y++)
                            {
                                long offsetSrc = (y * srcStep);
                                long offsetDst = (y * dstStep);
                                long bytesToCopy = w * ch;
                                // 一列ごとにコピー
                                Buffer.MemoryCopy(pSrc + offsetSrc, pDst + offsetDst, bytesToCopy, bytesToCopy);
                            }
                        }
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }
            finally
            {
                if (bd is not null)
                    dst.UnlockBits(bd);
            }
        }
        #endregion
    }
}
