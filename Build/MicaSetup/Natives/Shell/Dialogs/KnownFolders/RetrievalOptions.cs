namespace MicaSetup.Shell.Dialogs;

internal enum RetrievalOptions
{
    None = 0,
    Create = 0x00008000,
    DontVerify = 0x00004000,
    DontUnexpand = 0x00002000,
    NoAlias = 0x00001000,
    Init = 0x00000800,
    DefaultPath = 0x00000400,
    NotParentRelative = 0x00000200,
}
