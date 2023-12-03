using System;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PropertyKey : IEquatable<PropertyKey>
{
    private Guid formatId;
    private readonly int propertyId;

    public Guid FormatId => formatId;

    public int PropertyId => propertyId;

    public PropertyKey(Guid formatId, int propertyId)
    {
        this.formatId = formatId;
        this.propertyId = propertyId;
    }

    public PropertyKey(string formatId, int propertyId)
    {
        this.formatId = new Guid(formatId);
        this.propertyId = propertyId;
    }

    public bool Equals(PropertyKey other) => other.Equals((object)this);

    public override int GetHashCode() => formatId.GetHashCode() ^ propertyId;

    public override bool Equals(object obj)
    {
        if (obj == null)
            return false;

        if (!(obj is PropertyKey))
            return false;

        var other = (PropertyKey)obj;
        return other.formatId.Equals(formatId) && (other.propertyId == propertyId);
    }

    public static bool operator ==(PropertyKey propKey1, PropertyKey propKey2) => propKey1.Equals(propKey2);

    public static bool operator !=(PropertyKey propKey1, PropertyKey propKey2) => !propKey1.Equals(propKey2);

    public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            LocalizedMessages.PropertyKeyFormatString,
            formatId.ToString("B"), propertyId);
}
