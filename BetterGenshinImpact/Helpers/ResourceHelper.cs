using System;
using System.IO;
using System.IO.Packaging;
using System.Text;
using System.Windows;
using System.Windows.Resources;

namespace BetterGenshinImpact.Helpers;

internal static class ResourceHelper
{
    static ResourceHelper()
    {
        if (!UriParser.IsKnownScheme("pack"))
            _ = PackUriHelper.UriSchemePack;
    }

    public static byte[] GetBytes(string uriString)
    {
        Uri uri = new(uriString);
        StreamResourceInfo? info = Application.GetResourceStream(uri);
        using BinaryReader stream = new(info.Stream);
        return stream.ReadBytes((int)info.Stream.Length);
    }

    public static Stream GetStream(string uriString)
    {
        Uri uri = new(uriString);
        StreamResourceInfo? info = Application.GetResourceStream(uri);
        return info?.Stream!;
    }

    public static string GetString(string uriString, Encoding encoding = null!)
    {
        Uri uri = new(uriString);
        StreamResourceInfo? info = Application.GetResourceStream(uri);
        if (info == null)
        {
            throw new FileNotFoundException($"Resource not found: {uriString}");
        }
        using StreamReader stream = new(info.Stream, encoding ?? Encoding.UTF8);
        return stream.ReadToEnd();
    }
}
