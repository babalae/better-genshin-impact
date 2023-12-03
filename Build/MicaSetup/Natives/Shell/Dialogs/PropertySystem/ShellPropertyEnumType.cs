namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8618

public class ShellPropertyEnumType
{
    private string displayText;
    private PropEnumType? enumType;
    private object minValue, setValue, enumerationValue;

    internal ShellPropertyEnumType(IPropertyEnumType nativePropertyEnumType) => NativePropertyEnumType = nativePropertyEnumType;

    public string DisplayText
    {
        get
        {
            if (displayText == null)
            {
                NativePropertyEnumType.GetDisplayText(out displayText);
            }
            return displayText;
        }
    }

    public PropEnumType EnumType
    {
        get
        {
            if (!enumType.HasValue)
            {
                NativePropertyEnumType.GetEnumType(out var tempEnumType);
                enumType = tempEnumType;
            }
            return enumType.Value;
        }
    }

    public object RangeMinValue
    {
        get
        {
            if (minValue == null)
            {
                using (var propVar = new PropVariant())
                {
                    NativePropertyEnumType.GetRangeMinValue(propVar);
                    minValue = propVar.Value;
                }
            }
            return minValue;
        }
    }

    public object RangeSetValue
    {
        get
        {
            if (setValue == null)
            {
                using (var propVar = new PropVariant())
                {
                    NativePropertyEnumType.GetRangeSetValue(propVar);
                    setValue = propVar.Value;
                }
            }
            return setValue;
        }
    }

    public object RangeValue
    {
        get
        {
            if (enumerationValue == null)
            {
                using (var propVar = new PropVariant())
                {
                    NativePropertyEnumType.GetValue(propVar);
                    enumerationValue = propVar.Value;
                }
            }
            return enumerationValue;
        }
    }

    private IPropertyEnumType NativePropertyEnumType
    {
        set;
        get;
    }
}
