using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8618

public class ShellPropertyDescription : IDisposable
{
    private PropertyAggregationType? aggregationTypes;
    private string canonicalName;
    private PropertyColumnStateOptions? columnState;
    private PropertyConditionOperation? conditionOperation;
    private PropertyConditionType? conditionType;
    private uint? defaultColumWidth;
    private string displayName;
    private PropertyDisplayType? displayType;
    private string editInvitation;
    private PropertyGroupingRange? groupingRange;
    private IPropertyDescription nativePropertyDescription;
    private ReadOnlyCollection<ShellPropertyEnumType> propertyEnumTypes;
    private PropertyKey propertyKey;
    private PropertyTypeOptions? propertyTypeFlags;
    private PropertyViewOptions? propertyViewFlags;
    private PropertySortDescription? sortDescription;
    private Type valueType;
    private VarEnum? varEnumType = null;

    internal ShellPropertyDescription(PropertyKey key) => propertyKey = key;

    ~ShellPropertyDescription()
    {
        Dispose(false);
    }

    public PropertyAggregationType AggregationTypes
    {
        get
        {
            if (NativePropertyDescription != null && aggregationTypes == null)
            {
                var hr = NativePropertyDescription.GetAggregationType(out var tempAggregationTypes);

                if (CoreErrorHelper.Succeeded(hr))
                {
                    aggregationTypes = tempAggregationTypes;
                }
            }

            return aggregationTypes.HasValue ? aggregationTypes.Value : default(PropertyAggregationType);
        }
    }

    public string CanonicalName
    {
        get
        {
            if (canonicalName == null)
            {
                PropertySystemNativeMethods.PSGetNameFromPropertyKey(ref propertyKey, out canonicalName);
            }

            return canonicalName;
        }
    }

    public PropertyColumnStateOptions ColumnState
    {
        get
        {
            if (NativePropertyDescription != null && columnState == null)
            {
                var hr = NativePropertyDescription.GetColumnState(out var state);

                if (CoreErrorHelper.Succeeded(hr))
                {
                    columnState = state;
                }
            }

            return columnState.HasValue ? columnState.Value : default(PropertyColumnStateOptions);
        }
    }

    public PropertyConditionOperation ConditionOperation
    {
        get
        {
            if (NativePropertyDescription != null && conditionOperation == null)
            {
                var hr = NativePropertyDescription.GetConditionType(out var tempConditionType, out var tempConditionOperation);

                if (CoreErrorHelper.Succeeded(hr))
                {
                    conditionOperation = tempConditionOperation;
                    conditionType = tempConditionType;
                }
            }

            return conditionOperation.HasValue ? conditionOperation.Value : default(PropertyConditionOperation);
        }
    }

    public PropertyConditionType ConditionType
    {
        get
        {
            if (NativePropertyDescription != null && conditionType == null)
            {
                var hr = NativePropertyDescription.GetConditionType(out var tempConditionType, out var tempConditionOperation);

                if (CoreErrorHelper.Succeeded(hr))
                {
                    conditionOperation = tempConditionOperation;
                    conditionType = tempConditionType;
                }
            }

            return conditionType.HasValue ? conditionType.Value : default(PropertyConditionType);
        }
    }

    public uint DefaultColumWidth
    {
        get
        {
            if (NativePropertyDescription != null && !defaultColumWidth.HasValue)
            {
                var hr = NativePropertyDescription.GetDefaultColumnWidth(out var tempDefaultColumWidth);

                if (CoreErrorHelper.Succeeded(hr))
                {
                    defaultColumWidth = tempDefaultColumWidth;
                }
            }

            return defaultColumWidth.HasValue ? defaultColumWidth.Value : default(uint);
        }
    }

    public string DisplayName
    {
        get
        {
            if (NativePropertyDescription != null && displayName == null)
            {
                var hr = NativePropertyDescription.GetDisplayName(out nint dispNameptr);

                if (CoreErrorHelper.Succeeded(hr) && dispNameptr != 0)
                {
                    displayName = Marshal.PtrToStringUni(dispNameptr);

                    Marshal.FreeCoTaskMem(dispNameptr);
                }
            }

            return displayName!;
        }
    }

    public PropertyDisplayType DisplayType
    {
        get
        {
            if (NativePropertyDescription != null && displayType == null)
            {
                var hr = NativePropertyDescription.GetDisplayType(out var tempDisplayType);

                if (CoreErrorHelper.Succeeded(hr))
                {
                    displayType = tempDisplayType;
                }
            }

            return displayType.HasValue ? displayType.Value : default(PropertyDisplayType);
        }
    }

    public string EditInvitation
    {
        get
        {
            if (NativePropertyDescription != null && editInvitation == null)
            {
                var hr = NativePropertyDescription.GetEditInvitation(out nint ptr);

                if (CoreErrorHelper.Succeeded(hr) && ptr != 0)
                {
                    editInvitation = Marshal.PtrToStringUni(ptr);
                    Marshal.FreeCoTaskMem(ptr);
                }
            }

            return editInvitation!;
        }
    }

    public PropertyGroupingRange GroupingRange
    {
        get
        {
            if (NativePropertyDescription != null && groupingRange == null)
            {
                var hr = NativePropertyDescription.GetGroupingRange(out var tempGroupingRange);

                if (CoreErrorHelper.Succeeded(hr))
                {
                    groupingRange = tempGroupingRange;
                }
            }

            return groupingRange.HasValue ? groupingRange.Value : default(PropertyGroupingRange);
        }
    }

    public bool HasSystemDescription => NativePropertyDescription != null;

    public ReadOnlyCollection<ShellPropertyEnumType> PropertyEnumTypes
    {
        get
        {
            if (NativePropertyDescription != null && propertyEnumTypes == null)
            {
                var propEnumTypeList = new List<ShellPropertyEnumType>();

                var guid = new Guid(ShellIIDGuid.IPropertyEnumTypeList);
                var hr = NativePropertyDescription.GetEnumTypeList(ref guid, out var nativeList);

                if (nativeList != null && CoreErrorHelper.Succeeded(hr))
                {
                    nativeList.GetCount(out var count);
                    guid = new Guid(ShellIIDGuid.IPropertyEnumType);

                    for (uint i = 0; i < count; i++)
                    {
                        nativeList.GetAt(i, ref guid, out var nativeEnumType);
                        propEnumTypeList.Add(new ShellPropertyEnumType(nativeEnumType));
                    }
                }

                propertyEnumTypes = new ReadOnlyCollection<ShellPropertyEnumType>(propEnumTypeList);
            }

            return propertyEnumTypes;
        }
    }

    public PropertyKey PropertyKey => propertyKey;

    public PropertySortDescription SortDescription
    {
        get
        {
            if (NativePropertyDescription != null && sortDescription == null)
            {
                var hr = NativePropertyDescription.GetSortDescription(out var tempSortDescription);

                if (CoreErrorHelper.Succeeded(hr))
                {
                    sortDescription = tempSortDescription;
                }
            }

            return sortDescription.HasValue ? sortDescription.Value : default(PropertySortDescription);
        }
    }

    public PropertyTypeOptions TypeFlags
    {
        get
        {
            if (NativePropertyDescription != null && propertyTypeFlags == null)
            {
                var hr = NativePropertyDescription.GetTypeFlags(PropertyTypeOptions.MaskAll, out var tempFlags);

                propertyTypeFlags = CoreErrorHelper.Succeeded(hr) ? tempFlags : default(PropertyTypeOptions);
            }

            return propertyTypeFlags.HasValue ? propertyTypeFlags.Value : default(PropertyTypeOptions);
        }
    }

    public Type ValueType
    {
        get
        {
            if (valueType == null)
            {
                valueType = ShellPropertyFactory.VarEnumToSystemType(VarEnumType);
            }

            return valueType;
        }
    }

    public VarEnum VarEnumType
    {
        get
        {
            if (NativePropertyDescription != null && varEnumType == null)
            {
                var hr = NativePropertyDescription.GetPropertyType(out var tempType);

                if (CoreErrorHelper.Succeeded(hr))
                {
                    varEnumType = tempType;
                }
            }

            return varEnumType.HasValue ? varEnumType.Value : default(VarEnum);
        }
    }

    public PropertyViewOptions ViewFlags
    {
        get
        {
            if (NativePropertyDescription != null && propertyViewFlags == null)
            {
                var hr = NativePropertyDescription.GetViewFlags(out var tempFlags);

                propertyViewFlags = CoreErrorHelper.Succeeded(hr) ? tempFlags : default(PropertyViewOptions);
            }

            return propertyViewFlags.HasValue ? propertyViewFlags.Value : default(PropertyViewOptions);
        }
    }

    internal IPropertyDescription NativePropertyDescription
    {
        get
        {
            if (nativePropertyDescription == null)
            {
                var guid = new Guid(ShellIIDGuid.IPropertyDescription);
                PropertySystemNativeMethods.PSGetPropertyDescription(ref propertyKey, ref guid, out nativePropertyDescription);
            }

            return nativePropertyDescription;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public string GetSortDescriptionLabel(bool descending)
    {
        var label = string.Empty;

        if (NativePropertyDescription != null)
        {
            var hr = NativePropertyDescription.GetSortDescriptionLabel(descending, out nint ptr);

            if (CoreErrorHelper.Succeeded(hr) && ptr != 0)
            {
                label = Marshal.PtrToStringUni(ptr);
                Marshal.FreeCoTaskMem(ptr);
            }
        }

        return label;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (nativePropertyDescription != null)
        {
            Marshal.ReleaseComObject(nativePropertyDescription);
            nativePropertyDescription = null!;
        }

        if (disposing)
        {
            canonicalName = null!;
            displayName = null!;
            editInvitation = null!;
            defaultColumWidth = null!;
            valueType = null!;
            propertyEnumTypes = null!;
        }
    }
}
