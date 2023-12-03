using System;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

[Serializable]
public class CommonControlException : COMException
{
    public CommonControlException()
    {
    }

    public CommonControlException(string message) : base(message)
    {
    }

    public CommonControlException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CommonControlException(string message, int errorCode) : base(message, errorCode)
    {
    }

    internal CommonControlException(string message, HResult errorCode) : this(message, (int)errorCode)
    {
    }

    protected CommonControlException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context)
        : base(info, context)
    {
    }
}
