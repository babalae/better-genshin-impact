using System;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

internal interface NativeCommonFileDialog
{
}

[ComImport,
Guid(ShellIIDGuid.IFileOpenDialog),
CoClass(typeof(FileOpenDialogRCW))]
internal interface NativeFileOpenDialog : IFileOpenDialog
{
}

[ComImport,
Guid(ShellIIDGuid.IFileSaveDialog),
CoClass(typeof(FileSaveDialogRCW))]
internal interface NativeFileSaveDialog : IFileSaveDialog
{
}

[ComImport,
ClassInterface(ClassInterfaceType.None),
TypeLibType(TypeLibTypeFlags.FCanCreate),
Guid(ShellCLSIDGuid.FileOpenDialog)]
internal class FileOpenDialogRCW
{
}

[ComImport,
ClassInterface(ClassInterfaceType.None),
TypeLibType(TypeLibTypeFlags.FCanCreate),
Guid(ShellCLSIDGuid.FileSaveDialog)]
internal class FileSaveDialogRCW
{
}
