using MicaSetup.Natives;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MicaSetup.Shell.Dialogs;

public class StockIcon : IDisposable
{
    private StockIconIdentifier identifier = StockIconIdentifier.Application;
    private StockIconSize currentSize = StockIconSize.Large;
    private bool linkOverlay;
    private bool selected;
    private bool invalidateIcon = true;
    private nint hIcon = 0;

    public StockIcon(StockIconIdentifier id)
    {
        identifier = id;
        invalidateIcon = true;
    }

    public StockIcon(StockIconIdentifier id, StockIconSize size, bool isLinkOverlay, bool isSelected)
    {
        identifier = id;
        linkOverlay = isLinkOverlay;
        selected = isSelected;
        currentSize = size;
        invalidateIcon = true;
    }

    public bool Selected
    {
        get => selected;
        set
        {
            selected = value;
            invalidateIcon = true;
        }
    }

    public bool LinkOverlay
    {
        get => linkOverlay;
        set
        {
            linkOverlay = value;
            invalidateIcon = true;
        }
    }

    public StockIconSize CurrentSize
    {
        get => currentSize;
        set
        {
            currentSize = value;
            invalidateIcon = true;
        }
    }

    public StockIconIdentifier Identifier
    {
        get => identifier;
        set
        {
            identifier = value;
            invalidateIcon = true;
        }
    }

    public Bitmap Bitmap
    {
        get
        {
            UpdateHIcon();

            return hIcon != 0 ? Bitmap.FromHicon(hIcon) : null!;
        }
    }

    public BitmapSource BitmapSource
    {
        get
        {
            UpdateHIcon();

            return (hIcon != 0) ?
                Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, null!) : null!;
        }
    }

    public Icon Icon
    {
        get
        {
            UpdateHIcon();

            return hIcon != 0 ? Icon.FromHandle(hIcon) : null!;
        }
    }

    private void UpdateHIcon()
    {
        if (invalidateIcon)
        {
            if (hIcon != 0)
                _ = User32.DestroyIcon(hIcon);

            hIcon = GetHIcon();

            invalidateIcon = false;
        }
    }

    private nint GetHIcon()
    {
        var flags = StockIconsNativeMethods.StockIconOptions.Handle;

        if (CurrentSize == StockIconSize.Small)
        {
            flags |= StockIconsNativeMethods.StockIconOptions.Small;
        }
        else if (CurrentSize == StockIconSize.ShellSize)
        {
            flags |= StockIconsNativeMethods.StockIconOptions.ShellSize;
        }
        else
        {
            flags |= StockIconsNativeMethods.StockIconOptions.Large;
        }

        if (Selected)
        {
            flags |= StockIconsNativeMethods.StockIconOptions.Selected;
        }

        if (LinkOverlay)
        {
            flags |= StockIconsNativeMethods.StockIconOptions.LinkOverlay;
        }

        var info = new StockIconsNativeMethods.StockIconInfo
        {
            StuctureSize = (uint)Marshal.SizeOf(typeof(StockIconsNativeMethods.StockIconInfo))
        };

        var hr = StockIconsNativeMethods.SHGetStockIconInfo(identifier, flags, ref info);

        if (hr != HResult.Ok)
        {
            if (hr == HResult.InvalidArguments)
            {
                throw new InvalidOperationException(
                    string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    LocalizedMessages.StockIconInvalidGuid,
                    identifier));
            }

            return 0;
        }

        return info.Handle;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
        }

        if (hIcon != 0)
            _ = User32.DestroyIcon(hIcon);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~StockIcon()
    {
        Dispose(false);
    }
}
