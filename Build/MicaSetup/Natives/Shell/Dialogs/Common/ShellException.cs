using System;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

[Serializable]
public class ShellException : ExternalException
{
    public ShellException()
    {
    }

    public ShellException(string message) : base(message)
    {
    }

    public ShellException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ShellException(string message, int errorCode) : base(message, errorCode)
    {
    }

    public ShellException(int errorCode)
        : base(LocalizedMessages.ShellExceptionDefaultText, errorCode)
    {
    }

    internal ShellException(HResult result) : this((int)result)
    {
    }

    internal ShellException(string message, HResult errorCode) : this(message, (int)errorCode)
    {
    }

    protected ShellException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context)
        : base(info, context)
    {
    }
}
