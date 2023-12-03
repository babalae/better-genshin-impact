namespace MicaSetup.Shell.Dialogs;

public enum HResult
{
    Ok = 0x0000,
    False = 0x0001,
    InvalidArguments = unchecked((int)0x80070057),
    OutOfMemory = unchecked((int)0x8007000E),
    NoInterface = unchecked((int)0x80004002),
    Fail = unchecked((int)0x80004005),
    ElementNotFound = unchecked((int)0x80070490),
    TypeElementNotFound = unchecked((int)0x8002802B),
    NoObject = unchecked((int)0x800401E5),
    Win32ErrorCanceled = 1223,
    Canceled = unchecked((int)0x800704C7),
    ResourceInUse = unchecked((int)0x800700AA),
    AccessDenied = unchecked((int)0x80030005)
}

internal static class CoreErrorHelper
{
    public const int Ignored = (int)HResult.Ok;

    private const int FacilityWin32 = 7;

    public static bool Failed(HResult result) => !Succeeded(result);

    public static bool Failed(int result) => !Succeeded(result);

    public static int HResultFromWin32(int win32ErrorCode)
    {
        if (win32ErrorCode > 0)
        {
            win32ErrorCode =
                (int)(((uint)win32ErrorCode & 0x0000FFFF) | (FacilityWin32 << 16) | 0x80000000);
        }
        return win32ErrorCode;
    }

    public static bool Matches(int result, int win32ErrorCode) => (result == HResultFromWin32(win32ErrorCode));

    public static bool Succeeded(int result) => result >= 0;

    public static bool Succeeded(HResult result) => Succeeded((int)result);
}
