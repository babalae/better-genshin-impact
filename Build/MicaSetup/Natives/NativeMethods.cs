using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace MicaSetup.Natives;

public static class NativeMethods
{
    public static void HideAllWindowButton(nint hwnd)
    {
        _ = User32.SetWindowLong(hwnd, (int)WindowLongFlags.GWL_STYLE, User32.GetWindowLong(hwnd, (int)WindowLongFlags.GWL_STYLE) & ~(int)WindowStyles.WS_SYSMENU);
    }

    public static int SetWindowAttribute(nint hwnd, DWMWINDOWATTRIBUTE attribute, int parameter)
    {
        return DwmApi.DwmSetWindowAttribute(hwnd, attribute, ref parameter, Marshal.SizeOf<int>());
    }

    public static string GetStringResource(string resourceId)
    {
        string[] parts;
        string library;
        int index;

        if (string.IsNullOrEmpty(resourceId)) { return string.Empty; }

        resourceId = resourceId.Replace("shell32,dll", "shell32.dll");
        parts = resourceId.Split(new char[] { ',' });

        library = parts[0];
        library = library.Replace(@"@", string.Empty);
        library = Environment.ExpandEnvironmentVariables(library);
        var handle = Kernel32.LoadLibrary(library);

        parts[1] = parts[1].Replace("-", string.Empty);
        index = int.Parse(parts[1], CultureInfo.InvariantCulture);

        var stringValue = new StringBuilder(255);
        var retval = User32.LoadString(handle, index, stringValue, 255);

        return retval != 0 ? stringValue.ToString() : null!;
    }

    public static ushort MakeLangId(PrimaryLanguageID primaryLangId, SublanguageID subLangId)
    {
        return (ushort)(((ushort)subLangId << 10) | (ushort)primaryLangId);
    }
}
