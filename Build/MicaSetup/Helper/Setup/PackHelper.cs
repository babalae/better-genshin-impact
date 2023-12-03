using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MicaSetup.Helper;

public static class PackHelper
{
    public static string AssemblyTitle => GetAssemblyTitle(typeof(Option).Assembly);
    public static readonly string AssemblyCopyright = GetAssemblyCopyright(typeof(Option).Assembly);
    public static readonly string AssemblyVersion = GetAssemblyVersion(typeof(Option).Assembly);

    public static string GetAssemblyTitle(Assembly assembly = null!)
    {
        AssemblyTitleAttribute attr = GetAssembly<AssemblyTitleAttribute>(assembly);
        return attr?.Title ?? Path.GetFileNameWithoutExtension((assembly ?? Assembly.GetExecutingAssembly()).CodeBase);
    }

    public static string GetAssemblyCopyright(Assembly assembly = null!)
        => GetAssembly<AssemblyCopyrightAttribute>(assembly)?.Copyright!;

    [Flags]
    private enum EVersionType
    {
        Major = 0,
        Minor = 1,
        Build = 2,
        Revision = 4,
    }

    private static string GetAssemblyVersion(this Assembly assembly, EVersionType type = EVersionType.Major | EVersionType.Minor | EVersionType.Build)
    {
        Version version = assembly.GetName().Version;
        StringBuilder sb = new();

        if (type.HasFlag(EVersionType.Major))
        {
            sb.Append(version.Major);
        }
        if (type.HasFlag(EVersionType.Minor))
        {
            if (sb.Length > 0)
                sb.Append(".");
            sb.Append(version.Minor);
        }
        if (type.HasFlag(EVersionType.Build))
        {
            if (sb.Length > 0)
                sb.Append(".");
            sb.Append(version.Build);
        }
        if (type.HasFlag(EVersionType.Revision))
        {
            if (sb.Length > 0)
                sb.Append(".");
            sb.Append(version.Revision);
        }
        return sb.ToString();
    }

    private static TAssy GetAssembly<TAssy>(Assembly assembly = null!)
    {
        TAssy[] assemblies = GetAssemblies<TAssy>(assembly);

        if (assemblies.Length > 0)
        {
            return assemblies[0];
        }
        return default!;
    }

    private static TAssy[] GetAssemblies<TAssy>(Assembly assembly = null!)
    {
        object[] attributes = (assembly ?? Assembly.GetExecutingAssembly()).GetCustomAttributes(typeof(TAssy), false);
        List<TAssy> attributeList = new();

        foreach (object attribute in attributes)
        {
            if (attribute is TAssy assy)
            {
                attributeList.Add(assy);
            }
        }
        return attributeList.ToArray();
    }
}
