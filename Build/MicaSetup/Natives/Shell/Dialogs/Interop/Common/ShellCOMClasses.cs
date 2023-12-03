using System;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

[ComImport,
Guid(ShellIIDGuid.IShellLibrary),
CoClass(typeof(ShellLibraryCoClass))]
internal interface INativeShellLibrary : IShellLibrary
{
}

[ComImport,
ClassInterface(ClassInterfaceType.None),
TypeLibType(TypeLibTypeFlags.FCanCreate),
Guid(ShellCLSIDGuid.ShellLibrary)]
internal class ShellLibraryCoClass
{
}
