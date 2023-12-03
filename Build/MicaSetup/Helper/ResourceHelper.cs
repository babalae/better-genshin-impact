using System;
using System.IO;
using System.IO.Packaging;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Resources;

namespace MicaSetup.Helper;

public static class ResourceHelper
{
    static ResourceHelper()
    {
        if (!UriParser.IsKnownScheme("pack"))
            _ = PackUriHelper.UriSchemePack;
    }

    public static bool HasResource(string uriString)
    {
        try
        {
            using Stream stream = GetStream(uriString);
            _ = stream;
            return true;
        }
        catch
        {
        }
        return false;
    }

    public static byte[] GetBytes(string uriString)
    {
        Uri uri = new(uriString);
        StreamResourceInfo info = Application.GetResourceStream(uri);
        using BinaryReader stream = new(info.Stream);
        return stream.ReadBytes((int)info.Stream.Length);
    }

    public static Stream GetStream(string uriString)
    {
        Uri uri = new(uriString);
        StreamResourceInfo info = Application.GetResourceStream(uri);
        return info?.Stream!;
    }

    public static string GetString(string uriString, Encoding encoding = null!)
    {
        Uri uri = new(uriString);
        StreamResourceInfo info = Application.GetResourceStream(uri);
        using StreamReader stream = new(info.Stream, encoding ?? Encoding.UTF8);
        return stream.ReadToEnd();
    }

    public static object GetResourceDictionary<T>(string uriString, string key) where T : class
    {
        ResourceDictionary rd = new()
        {
            Source = new Uri(uriString),
        };
        return (rd[key] as T)!;
    }

    public static Stream GetManifestResourceStream(string name, Assembly assembly = null!)
    {
        Stream stream = (assembly ?? Assembly.GetExecutingAssembly()).GetManifestResourceStream(name);
        return stream;
    }

    public static byte[] GetManifestResourceBytes(string name, Assembly assembly = null!)
    {
        using Stream stream = GetManifestResourceStream(name, assembly);
        using BinaryReader reader = new(stream);
        return reader.ReadBytes((int)stream.Length);
    }
}
