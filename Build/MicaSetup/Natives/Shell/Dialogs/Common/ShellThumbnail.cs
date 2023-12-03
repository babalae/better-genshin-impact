using MicaSetup.Natives;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MicaSetup.Shell.Dialogs;

public class ShellThumbnail
{
    private readonly IShellItem shellItemNative;

    private System.Windows.Size currentSize = new System.Windows.Size(256, 256);

    private ShellThumbnailFormatOption formatOption = ShellThumbnailFormatOption.Default;

    internal ShellThumbnail(ShellObject shellObject)
    {
        if (shellObject == null! || shellObject.NativeShellItem == null)
        {
            throw new ArgumentNullException("shellObject");
        }

        shellItemNative = shellObject.NativeShellItem;
    }

    public bool AllowBiggerSize { get; set; }

    public BitmapSource BitmapSource => GetBitmapSource(CurrentSize);

    public System.Windows.Size CurrentSize
    {
        get => currentSize;
        set
        {
            if (value.Height == 0 || value.Width == 0)
            {
                throw new System.ArgumentOutOfRangeException("value", LocalizedMessages.ShellThumbnailSizeCannotBe0);
            }

            var size = (FormatOption == ShellThumbnailFormatOption.IconOnly) ?
                DefaultIconSize.Maximum : DefaultThumbnailSize.Maximum;

            if (value.Height > size.Height || value.Width > size.Width)
            {
                throw new System.ArgumentOutOfRangeException("value",
                    string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    LocalizedMessages.ShellThumbnailCurrentSizeRange, size.ToString()));
            }

            currentSize = value;
        }
    }

    public Bitmap ExtraLargeBitmap => GetBitmap(DefaultIconSize.ExtraLarge, DefaultThumbnailSize.ExtraLarge);

    public BitmapSource ExtraLargeBitmapSource => GetBitmapSource(DefaultIconSize.ExtraLarge, DefaultThumbnailSize.ExtraLarge);

    public Icon ExtraLargeIcon => Icon.FromHandle(ExtraLargeBitmap.GetHicon());

    public ShellThumbnailFormatOption FormatOption
    {
        get => formatOption;
        set
        {
            formatOption = value;

            if (FormatOption == ShellThumbnailFormatOption.IconOnly
                && (CurrentSize.Height > DefaultIconSize.Maximum.Height || CurrentSize.Width > DefaultIconSize.Maximum.Width))
            {
                CurrentSize = DefaultIconSize.Maximum;
            }
        }
    }

    public Icon Icon => Icon.FromHandle(Bitmap.GetHicon());

    public Bitmap LargeBitmap => GetBitmap(DefaultIconSize.Large, DefaultThumbnailSize.Large);

    public BitmapSource LargeBitmapSource => GetBitmapSource(DefaultIconSize.Large, DefaultThumbnailSize.Large);

    public Icon LargeIcon => Icon.FromHandle(LargeBitmap.GetHicon());

    public Bitmap MediumBitmap => GetBitmap(DefaultIconSize.Medium, DefaultThumbnailSize.Medium);

    public BitmapSource MediumBitmapSource => GetBitmapSource(DefaultIconSize.Medium, DefaultThumbnailSize.Medium);

    public Icon MediumIcon => Icon.FromHandle(MediumBitmap.GetHicon());

    public ShellThumbnailRetrievalOption RetrievalOption { get; set; }

    public Bitmap SmallBitmap => GetBitmap(DefaultIconSize.Small, DefaultThumbnailSize.Small);

    public BitmapSource SmallBitmapSource => GetBitmapSource(DefaultIconSize.Small, DefaultThumbnailSize.Small);

    public Icon SmallIcon => Icon.FromHandle(SmallBitmap.GetHicon());

    public Bitmap Bitmap => GetBitmap(CurrentSize);

    private SIIGBF CalculateFlags()
    {
        SIIGBF flags = 0x0000;

        if (AllowBiggerSize)
        {
            flags |= SIIGBF.BiggerSizeOk;
        }

        if (RetrievalOption == ShellThumbnailRetrievalOption.CacheOnly)
        {
            flags |= SIIGBF.InCacheOnly;
        }
        else if (RetrievalOption == ShellThumbnailRetrievalOption.MemoryOnly)
        {
            flags |= SIIGBF.MemoryOnly;
        }

        if (FormatOption == ShellThumbnailFormatOption.IconOnly)
        {
            flags |= SIIGBF.IconOnly;
        }
        else if (FormatOption == ShellThumbnailFormatOption.ThumbnailOnly)
        {
            flags |= SIIGBF.ThumbnailOnly;
        }

        return flags;
    }

    private Bitmap GetBitmap(System.Windows.Size iconOnlySize, System.Windows.Size thumbnailSize) => GetBitmap(FormatOption == ShellThumbnailFormatOption.IconOnly ? iconOnlySize : thumbnailSize);

    private Bitmap GetBitmap(System.Windows.Size size)
    {
        var hBitmap = GetHBitmap(size);

        var returnValue = Bitmap.FromHbitmap(hBitmap);

        Gdi32.DeleteObject(hBitmap);

        return returnValue;
    }

    private BitmapSource GetBitmapSource(System.Windows.Size iconOnlySize, System.Windows.Size thumbnailSize) => GetBitmapSource(FormatOption == ShellThumbnailFormatOption.IconOnly ? iconOnlySize : thumbnailSize);

    private BitmapSource GetBitmapSource(System.Windows.Size size)
    {
        var hBitmap = GetHBitmap(size);

        var returnValue = Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap,
            IntPtr.Zero,
            System.Windows.Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

        Gdi32.DeleteObject(hBitmap);

        return returnValue;
    }

    private nint GetHBitmap(System.Windows.Size size)
    {
        var nativeSIZE = new SIZE()
        {
            Width = Convert.ToInt32(size.Width),
            Height = Convert.ToInt32(size.Height)
        };

        var hr = ((IShellItemImageFactory)shellItemNative).GetImage(nativeSIZE, CalculateFlags(), out nint hbitmap);

        if (hr == HResult.Ok) { return hbitmap; }
        else if ((uint)hr == 0x8004B200 && FormatOption == ShellThumbnailFormatOption.ThumbnailOnly)
        {
            throw new InvalidOperationException(LocalizedMessages.ShellThumbnailDoesNotHaveThumbnail, Marshal.GetExceptionForHR((int)hr));
        }
        else if ((uint)hr == 0x80040154)
        {
            throw new NotSupportedException(LocalizedMessages.ShellThumbnailNoHandler, Marshal.GetExceptionForHR((int)hr));
        }

        throw new ShellException(hr);
    }
}
