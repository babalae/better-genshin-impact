using System;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

public static class SystemProperties
{
    public static ShellPropertyDescription GetPropertyDescription(PropertyKey propertyKey) => ShellPropertyDescriptionsCache.Cache.GetPropertyDescription(propertyKey);

    public static ShellPropertyDescription GetPropertyDescription(string canonicalName)
    {
        var result = PropertySystemNativeMethods.PSGetPropertyKeyFromName(canonicalName, out var propKey);

        if (!CoreErrorHelper.Succeeded(result))
        {
            throw new ArgumentException(LocalizedMessages.ShellInvalidCanonicalName, Marshal.GetExceptionForHR(result));
        }
        return ShellPropertyDescriptionsCache.Cache.GetPropertyDescription(propKey);
    }
}
