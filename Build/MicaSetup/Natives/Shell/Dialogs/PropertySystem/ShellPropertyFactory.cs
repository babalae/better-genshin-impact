using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace MicaSetup.Shell.Dialogs;

internal static class ShellPropertyFactory
{
    private static readonly Dictionary<int, Func<PropertyKey, ShellPropertyDescription, object, IShellProperty>> _storeCache = new();

    public static IShellProperty CreateShellProperty(PropertyKey propKey, ShellObject shellObject) => GenericCreateShellProperty(propKey, shellObject);

    public static IShellProperty CreateShellProperty(PropertyKey propKey, IPropertyStore store) => GenericCreateShellProperty(propKey, store);

    [SuppressMessage("Microsoft.Maintainability", "CA1502:")]
    public static Type VarEnumToSystemType(VarEnum VarEnumType)
    {
        return VarEnumType switch
        {
            (VarEnum.VT_EMPTY) or (VarEnum.VT_NULL) => typeof(object),
            (VarEnum.VT_UI1) => typeof(byte?),
            (VarEnum.VT_I2) => typeof(short?),
            (VarEnum.VT_UI2) => typeof(ushort?),
            (VarEnum.VT_I4) => typeof(int?),
            (VarEnum.VT_UI4) => typeof(uint?),
            (VarEnum.VT_I8) => typeof(long?),
            (VarEnum.VT_UI8) => typeof(ulong?),
            (VarEnum.VT_R8) => typeof(double?),
            (VarEnum.VT_BOOL) => typeof(bool?),
            (VarEnum.VT_FILETIME) => typeof(DateTime?),
            (VarEnum.VT_CLSID) => typeof(IntPtr?),
            (VarEnum.VT_CF) => typeof(IntPtr?),
            (VarEnum.VT_BLOB) => typeof(byte[]),
            (VarEnum.VT_LPWSTR) => typeof(string),
            (VarEnum.VT_UNKNOWN) => typeof(IntPtr?),
            (VarEnum.VT_STREAM) => typeof(IStream),
            (VarEnum.VT_VECTOR | VarEnum.VT_UI1) => typeof(byte[]),
            (VarEnum.VT_VECTOR | VarEnum.VT_I2) => typeof(short[]),
            (VarEnum.VT_VECTOR | VarEnum.VT_UI2) => typeof(ushort[]),
            (VarEnum.VT_VECTOR | VarEnum.VT_I4) => typeof(int[]),
            (VarEnum.VT_VECTOR | VarEnum.VT_UI4) => typeof(uint[]),
            (VarEnum.VT_VECTOR | VarEnum.VT_I8) => typeof(long[]),
            (VarEnum.VT_VECTOR | VarEnum.VT_UI8) => typeof(ulong[]),
            (VarEnum.VT_VECTOR | VarEnum.VT_R8) => typeof(double[]),
            (VarEnum.VT_VECTOR | VarEnum.VT_BOOL) => typeof(bool[]),
            (VarEnum.VT_VECTOR | VarEnum.VT_FILETIME) => typeof(DateTime[]),
            (VarEnum.VT_VECTOR | VarEnum.VT_CLSID) => typeof(IntPtr[]),
            (VarEnum.VT_VECTOR | VarEnum.VT_CF) => typeof(IntPtr[]),
            (VarEnum.VT_VECTOR | VarEnum.VT_LPWSTR) => typeof(string[]),
            _ => typeof(object),
        };
    }

    private static Func<PropertyKey, ShellPropertyDescription, object, IShellProperty> ExpressConstructor(Type type, Type[] argTypes)
    {
        var typeHash = GetTypeHash(argTypes);

        var ctorInfo = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(x => typeHash == GetTypeHash(x.GetParameters().Select(a => a.ParameterType)));

        if (ctorInfo == null)
        {
            throw new ArgumentException(LocalizedMessages.ShellPropertyFactoryConstructorNotFound, "type");
        }

        var key = Expression.Parameter(argTypes[0], "propKey");
        var desc = Expression.Parameter(argTypes[1], "desc");
        var third = Expression.Parameter(typeof(object), "third");

        var create = Expression.New(ctorInfo, key, desc,
            Expression.Convert(third, argTypes[2]));

        return Expression.Lambda<Func<PropertyKey, ShellPropertyDescription, object, IShellProperty>>(
            create, key, desc, third).Compile();
    }

    private static IShellProperty GenericCreateShellProperty<T>(PropertyKey propKey, T thirdArg)
    {
        var thirdType = (thirdArg is ShellObject) ? typeof(ShellObject) : typeof(T);

        var propDesc = ShellPropertyDescriptionsCache.Cache.GetPropertyDescription(propKey);

        var type = typeof(ShellProperty<>).MakeGenericType(VarEnumToSystemType(propDesc.VarEnumType));

        var hash = GetTypeHash(type, thirdType);

        if (!_storeCache.TryGetValue(hash, out var ctor))
        {
            Type[] argTypes = { typeof(PropertyKey), typeof(ShellPropertyDescription), thirdType };
            ctor = ExpressConstructor(type, argTypes);
            _storeCache.Add(hash, ctor);
        }

        return ctor(propKey, propDesc, thirdArg!);
    }

    private static int GetTypeHash(params Type[] types) => GetTypeHash((IEnumerable<Type>)types);

    private static int GetTypeHash(IEnumerable<Type> types)
    {
        var hash = 0;
        foreach (var type in types)
        {
            hash = hash * 31 + type.GetHashCode();
        }
        return hash;
    }
}
