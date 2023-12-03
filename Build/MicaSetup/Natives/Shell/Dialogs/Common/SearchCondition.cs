using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8618

public class SearchCondition : IDisposable
{
    private readonly string canonicalName;

    private readonly SearchConditionOperation conditionOperation = SearchConditionOperation.Implicit;

    private readonly SearchConditionType conditionType = SearchConditionType.Leaf;

    private PropertyKey emptyPropertyKey = new();

    private PropertyKey propertyKey;

    internal SearchCondition(ICondition nativeSearchCondition)
    {
        NativeSearchCondition = nativeSearchCondition
            ?? throw new ArgumentNullException("nativeSearchCondition");

        var hr = NativeSearchCondition.GetConditionType(out conditionType);

        if (!CoreErrorHelper.Succeeded(hr))
        {
            throw new ShellException(hr);
        }

        if (ConditionType == SearchConditionType.Leaf)
        {
            using var propVar = new PropVariant();
            hr = NativeSearchCondition.GetComparisonInfo(out canonicalName, out conditionOperation, propVar);

            if (!CoreErrorHelper.Succeeded(hr))
            {
                throw new ShellException(hr);
            }

            PropertyValue = propVar.Value.ToString();
        }
    }

    ~SearchCondition()
    {
        Dispose(false);
    }

    public SearchConditionOperation ConditionOperation => conditionOperation;

    public SearchConditionType ConditionType => conditionType;

    public string PropertyCanonicalName => canonicalName;

    public PropertyKey PropertyKey
    {
        get
        {
            if (propertyKey == emptyPropertyKey)
            {
                var hr = PropertySystemNativeMethods.PSGetPropertyKeyFromName(PropertyCanonicalName, out propertyKey);
                if (!CoreErrorHelper.Succeeded(hr))
                {
                    throw new ShellException(hr);
                }
            }
            return propertyKey;
        }
    }

    public string PropertyValue { get; internal set; }

    internal ICondition NativeSearchCondition { get; set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public IEnumerable<SearchCondition> GetSubConditions()
    {
        var subConditionsList = new List<SearchCondition>();

        var guid = new Guid(ShellIIDGuid.IEnumUnknown);

        var hr = NativeSearchCondition.GetSubConditions(ref guid, out var subConditionObj);

        if (!CoreErrorHelper.Succeeded(hr))
        {
            throw new ShellException(hr);
        }

        if (subConditionObj != null)
        {
            var enumUnknown = subConditionObj as IEnumUnknown;

            nint buffer = 0;
            uint fetched = 0;

            while (hr == HResult.Ok)
            {
                hr = enumUnknown!.Next(1, ref buffer, ref fetched);

                if (hr == HResult.Ok && fetched == 1)
                {
                    subConditionsList.Add(new SearchCondition((ICondition)Marshal.GetObjectForIUnknown(buffer)));
                }
            }
        }

        return subConditionsList;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (NativeSearchCondition != null)
        {
            Marshal.ReleaseComObject(NativeSearchCondition);
            NativeSearchCondition = null!;
        }
    }
}
