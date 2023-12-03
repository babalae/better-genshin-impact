using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

[StructLayout(LayoutKind.Explicit)]
public sealed class PropVariant : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public int Number;
        public nint Pointer;
    }

    private static Dictionary<Type, Action<PropVariant, Array, uint>> _vectorActions = null!;

    private static Dictionary<Type, Action<PropVariant, Array, uint>> GenerateVectorActions()
    {
        Dictionary<Type, Action<PropVariant, Array, uint>> cache = new()
        {
            {
                typeof(short),
                (pv, array, i) =>
                {
                    PropVariantNativeMethods.PropVariantGetInt16Elem(pv, i, out short val);
                    array.SetValue(val, i);
                }
            },

            {
                typeof(ushort),
                (pv, array, i) =>
                {
                    PropVariantNativeMethods.PropVariantGetUInt16Elem(pv, i, out ushort val);
                    array.SetValue(val, i);
                }
            },

            {
                typeof(int),
                (pv, array, i) =>
                {
                    PropVariantNativeMethods.PropVariantGetInt32Elem(pv, i, out int val);
                    array.SetValue(val, i);
                }
            },

            {
                typeof(uint),
                (pv, array, i) =>
                {
                    PropVariantNativeMethods.PropVariantGetUInt32Elem(pv, i, out uint val);
                    array.SetValue(val, i);
                }
            },

            {
                typeof(long),
                (pv, array, i) =>
                {
                    PropVariantNativeMethods.PropVariantGetInt64Elem(pv, i, out long val);
                    array.SetValue(val, i);
                }
            },

            {
                typeof(ulong),
                (pv, array, i) =>
                {
                    PropVariantNativeMethods.PropVariantGetUInt64Elem(pv, i, out ulong val);
                    array.SetValue(val, i);
                }
            },

            {
                typeof(DateTime),
                (pv, array, i) =>
                {
                    PropVariantNativeMethods.PropVariantGetFileTimeElem(pv, i, out System.Runtime.InteropServices.ComTypes.FILETIME val);

                    long fileTime = GetFileTimeAsLong(ref val);

                    array.SetValue(DateTime.FromFileTime(fileTime), i);
                }
            },

            {
                typeof(bool),
                (pv, array, i) =>
                {
                    PropVariantNativeMethods.PropVariantGetBooleanElem(pv, i, out bool val);
                    array.SetValue(val, i);
                }
            },

            {
                typeof(double),
                (pv, array, i) =>
                {
                    PropVariantNativeMethods.PropVariantGetDoubleElem(pv, i, out double val);
                    array.SetValue(val, i);
                }
            },

            {
                typeof(float),
                (pv, array, i) =>
                {
                    float[] val = new float[1];
                    Marshal.Copy(pv._blob.Pointer, val, (int)i, 1);
                    array.SetValue(val[0], (int)i);
                }
            },

            {
                typeof(decimal),
                (pv, array, i) =>
                {
                    int[] val = new int[4];
                    for (int a = 0; a < val.Length; a++)
                    {
                        val[a] = Marshal.ReadInt32(pv._blob.Pointer,
                            (int)i * sizeof(decimal) + a * sizeof(int));
                              }
                    array.SetValue(new decimal(val), i);
                }
            },

            {
                typeof(string),
                (pv, array, i) =>
                {
                    string val = string.Empty;
                    PropVariantNativeMethods.PropVariantGetStringElem(pv, i, ref val);
                    array.SetValue(val, i);
                }
            }
        };

        return cache;
    }

    public static PropVariant FromObject(object value)
    {
        if (value == null)
        {
            return new PropVariant();
        }
        else
        {
            Func<object, PropVariant> func = GetDynamicConstructor(value.GetType());
            return func(value);
        }
    }

    private static readonly Dictionary<Type, Func<object, PropVariant>> _cache = new();

    private static readonly object _padlock = new();

    private static Func<object, PropVariant> GetDynamicConstructor(Type type)
    {
        lock (_padlock)
        {
            if (!_cache.TryGetValue(type, out var action))
            {
                System.Reflection.ConstructorInfo constructor = typeof(PropVariant)
                    .GetConstructor(new Type[] { type });

                if (constructor == null)
                {
                    throw new ArgumentException(LocalizedMessages.PropVariantTypeNotSupported);
                }
                else
                {
                    ParameterExpression arg = Expression.Parameter(typeof(object), "arg");
                    NewExpression create = Expression.New(constructor, Expression.Convert(arg, type));

                    action = Expression.Lambda<Func<object, PropVariant>>(create, arg).Compile();
                    _cache.Add(type, action);
                }
            }
            return action;
        }
    }

    [FieldOffset(0)]
    private readonly decimal _decimal;

    [FieldOffset(0)]
    private ushort _valueType;

    [FieldOffset(8)]
    private readonly Blob _blob;

    [FieldOffset(8)]
    private nint _ptr;

    [FieldOffset(8)]
    private readonly int _int32;

    [FieldOffset(8)]
    private readonly uint _uint32;

    [FieldOffset(8)]
    private readonly byte _byte;

    [FieldOffset(8)]
    private readonly sbyte _sbyte;

    [FieldOffset(8)]
    private readonly short _short;

    [FieldOffset(8)]
    private readonly ushort _ushort;

    [FieldOffset(8)]
    private readonly long _long;

    [FieldOffset(8)]
    private readonly ulong _ulong;

    [FieldOffset(8)]
    private readonly double _double;

    [FieldOffset(8)]
    private readonly float _float;

    public PropVariant()
    {
    }

    public PropVariant(string value)
    {
        if (value == null)
        {
            throw new ArgumentException(LocalizedMessages.PropVariantNullString, "value");
        }

        _valueType = (ushort)VarEnum.VT_LPWSTR;
        _ptr = Marshal.StringToCoTaskMemUni(value);
    }

    public PropVariant(bool value)
    {
        _valueType = (ushort)VarEnum.VT_BOOL;
        _int32 = (value == true) ? -1 : 0;
    }

    public PropVariant(DateTime value)
    {
        _valueType = (ushort)VarEnum.VT_FILETIME;

        System.Runtime.InteropServices.ComTypes.FILETIME ft = DateTimeToFileTime(value);
        PropVariantNativeMethods.InitPropVariantFromFileTime(ref ft, this);
    }

    public PropVariant(int value)
    {
        _valueType = (ushort)VarEnum.VT_I4;
        _int32 = value;
    }

    public PropVariant(double value)
    {
        _valueType = (ushort)VarEnum.VT_R8;
        _double = value;
    }

    public VarEnum VarType
    {
        get => (VarEnum)_valueType;
        set => _valueType = (ushort)value;
    }

    public bool IsNullOrEmpty => (_valueType == (ushort)VarEnum.VT_EMPTY || _valueType == (ushort)VarEnum.VT_NULL);

    public object Value
    {
        get
        {
            return (VarEnum)_valueType switch
            {
                VarEnum.VT_I1 => _sbyte,
                VarEnum.VT_UI1 => _byte,
                VarEnum.VT_I2 => _short,
                VarEnum.VT_UI2 => _ushort,
                VarEnum.VT_I4 or VarEnum.VT_INT => _int32,
                VarEnum.VT_UI4 or VarEnum.VT_UINT => _uint32,
                VarEnum.VT_I8 => _long,
                VarEnum.VT_UI8 => _ulong,
                VarEnum.VT_R4 => _float,
                VarEnum.VT_R8 => _double,
                VarEnum.VT_BOOL => _int32 == -1,
                VarEnum.VT_ERROR => _long,
                VarEnum.VT_CY => _decimal,
                VarEnum.VT_DATE => DateTime.FromOADate(_double),
                VarEnum.VT_FILETIME => DateTime.FromFileTime(_long),
                VarEnum.VT_BSTR => Marshal.PtrToStringBSTR(_ptr),
                VarEnum.VT_BLOB => GetBlobData(),
                VarEnum.VT_LPSTR => Marshal.PtrToStringAnsi(_ptr),
                VarEnum.VT_LPWSTR => Marshal.PtrToStringUni(_ptr),
                VarEnum.VT_UNKNOWN => Marshal.GetObjectForIUnknown(_ptr),
                VarEnum.VT_DISPATCH => Marshal.GetObjectForIUnknown(_ptr),
                VarEnum.VT_DECIMAL => _decimal,
                VarEnum.VT_ARRAY | VarEnum.VT_UNKNOWN => CrackSingleDimSafeArray(_ptr),
                (VarEnum.VT_VECTOR | VarEnum.VT_LPWSTR) => GetVector<string>(),
                (VarEnum.VT_VECTOR | VarEnum.VT_I2) => GetVector<short>(),
                (VarEnum.VT_VECTOR | VarEnum.VT_UI2) => GetVector<ushort>(),
                (VarEnum.VT_VECTOR | VarEnum.VT_I4) => GetVector<int>(),
                (VarEnum.VT_VECTOR | VarEnum.VT_UI4) => GetVector<uint>(),
                (VarEnum.VT_VECTOR | VarEnum.VT_I8) => GetVector<long>(),
                (VarEnum.VT_VECTOR | VarEnum.VT_UI8) => GetVector<ulong>(),
                (VarEnum.VT_VECTOR | VarEnum.VT_R4) => GetVector<float>(),
                (VarEnum.VT_VECTOR | VarEnum.VT_R8) => GetVector<double>(),
                (VarEnum.VT_VECTOR | VarEnum.VT_BOOL) => GetVector<bool>(),
                (VarEnum.VT_VECTOR | VarEnum.VT_FILETIME) => GetVector<DateTime>(),
                (VarEnum.VT_VECTOR | VarEnum.VT_DECIMAL) => GetVector<decimal>(),
                _ => null!,
            };
        }
    }

    private static long GetFileTimeAsLong(ref System.Runtime.InteropServices.ComTypes.FILETIME val) => (((long)val.dwHighDateTime) << 32) + val.dwLowDateTime;

    private static System.Runtime.InteropServices.ComTypes.FILETIME DateTimeToFileTime(DateTime value)
    {
        long hFT = value.ToFileTime();
        System.Runtime.InteropServices.ComTypes.FILETIME ft =
            new()
            {
                dwLowDateTime = (int)(hFT & 0xFFFFFFFF),
                dwHighDateTime = (int)(hFT >> 32)
            };
        return ft;
    }

    private object GetBlobData()
    {
        byte[] blobData = new byte[_int32];

        nint pBlobData = _blob.Pointer;
        Marshal.Copy(pBlobData, blobData, 0, _int32);

        return blobData;
    }

    private Array GetVector<T>()
    {
        int count = PropVariantNativeMethods.PropVariantGetElementCount(this);
        if (count <= 0) { return null!; }

        lock (_padlock)
        {
            _vectorActions ??= GenerateVectorActions();
        }

        if (!_vectorActions.TryGetValue(typeof(T), out var action))
        {
            throw new InvalidCastException(LocalizedMessages.PropVariantUnsupportedType);
        }

        Array array = new T[count];
        for (uint i = 0; i < count; i++)
        {
            action(this, array, i);
        }

        return array;
    }

    private static Array CrackSingleDimSafeArray(nint psa)
    {
        uint cDims = PropVariantNativeMethods.SafeArrayGetDim(psa);
        if (cDims != 1)
            throw new ArgumentException(LocalizedMessages.PropVariantMultiDimArray, "psa");

        int lBound = PropVariantNativeMethods.SafeArrayGetLBound(psa, 1U);
        int uBound = PropVariantNativeMethods.SafeArrayGetUBound(psa, 1U);

        int n = uBound - lBound + 1;

        object[] array = new object[n];
        for (int i = lBound; i <= uBound; ++i)
        {
            array[i] = PropVariantNativeMethods.SafeArrayGetElement(psa, ref i);
        }

        return array;
    }

    public void Dispose()
    {
        PropVariantNativeMethods.PropVariantClear(this);

        GC.SuppressFinalize(this);
    }

    ~PropVariant()
    {
        Dispose();
    }

    public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0}: {1}", Value, VarType.ToString());
}
