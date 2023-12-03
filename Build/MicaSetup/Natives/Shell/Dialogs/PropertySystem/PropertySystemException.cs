using System;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

[Serializable]
public class PropertySystemException : ExternalException
{
    public PropertySystemException()
    {
    }

    public PropertySystemException(string message) : base(message)
    {
    }

    public PropertySystemException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public PropertySystemException(string message, int errorCode) : base(message, errorCode)
    {
    }

    protected PropertySystemException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context)
        : base(info, context)
    {
    }
}
