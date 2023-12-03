using System;

namespace MicaSetup.Shell.Dialogs;

public interface IShellProperty
{
    string CanonicalName { get; }

    ShellPropertyDescription Description { get; }

    IconReference IconReference { get; }

    PropertyKey PropertyKey { get; }

    object ValueAsObject { get; }

    Type ValueType { get; }

    string FormatForDisplay(PropertyDescriptionFormatOptions format);
}
