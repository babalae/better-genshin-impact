using System;
using System.Runtime.InteropServices;
using System.Security;

namespace MicaSetup.Shell.Dialogs;

[SuppressUnmanagedCodeSecurity]
internal static class KnownFoldersSafeNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeFolderDefinition
    {
        internal FolderCategory category;
        internal nint name;
        internal nint description;
        internal Guid parentId;
        internal nint relativePath;
        internal nint parsingName;
        internal nint tooltip;
        internal nint localizedName;
        internal nint icon;
        internal nint security;
        internal uint attributes;
        internal DefinitionOptions definitionOptions;
        internal Guid folderTypeId;
    }
}
