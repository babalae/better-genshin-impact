using MicaSetup.Natives;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MicaSetup.Shell.Dialogs;

internal class KnownFolderSettings
{
    private FolderProperties knownFolderProperties;

    internal KnownFolderSettings(IKnownFolderNative knownFolderNative) => GetFolderProperties(knownFolderNative);

    public string CanonicalName => knownFolderProperties.canonicalName;

    public FolderCategory Category => knownFolderProperties.category;

    public DefinitionOptions DefinitionOptions => knownFolderProperties.definitionOptions;

    public string Description => knownFolderProperties.description;

    public Guid FolderId => knownFolderProperties.folderId;

    public string FolderType => knownFolderProperties.folderType;

    public Guid FolderTypeId => knownFolderProperties.folderTypeId;

    public string LocalizedName => knownFolderProperties.localizedName;

    public string LocalizedNameResourceId => knownFolderProperties.localizedNameResourceId;

    public Guid ParentId => knownFolderProperties.parentId;

    public string Path => knownFolderProperties.path;

    public bool PathExists => knownFolderProperties.pathExists;

    public RedirectionCapability Redirection => knownFolderProperties.redirection;

    public string RelativePath => knownFolderProperties.relativePath;

    public string Security => knownFolderProperties.security;

    public string Tooltip => knownFolderProperties.tooltip;

    public string TooltipResourceId => knownFolderProperties.tooltipResourceId;

    public System.IO.FileAttributes FileAttributes => knownFolderProperties.fileAttributes;

    private void GetFolderProperties(IKnownFolderNative knownFolderNative)
    {
        Debug.Assert(knownFolderNative != null);

        knownFolderNative!.GetFolderDefinition(out var nativeFolderDefinition);

        try
        {
            knownFolderProperties.category = nativeFolderDefinition.category;
            knownFolderProperties.canonicalName = Marshal.PtrToStringUni(nativeFolderDefinition.name);
            knownFolderProperties.description = Marshal.PtrToStringUni(nativeFolderDefinition.description);
            knownFolderProperties.parentId = nativeFolderDefinition.parentId;
            knownFolderProperties.relativePath = Marshal.PtrToStringUni(nativeFolderDefinition.relativePath);
            knownFolderProperties.parsingName = Marshal.PtrToStringUni(nativeFolderDefinition.parsingName);
            knownFolderProperties.tooltipResourceId = Marshal.PtrToStringUni(nativeFolderDefinition.tooltip);
            knownFolderProperties.localizedNameResourceId = Marshal.PtrToStringUni(nativeFolderDefinition.localizedName);
            knownFolderProperties.iconResourceId = Marshal.PtrToStringUni(nativeFolderDefinition.icon);
            knownFolderProperties.security = Marshal.PtrToStringUni(nativeFolderDefinition.security);
            knownFolderProperties.fileAttributes = (System.IO.FileAttributes)nativeFolderDefinition.attributes;
            knownFolderProperties.definitionOptions = nativeFolderDefinition.definitionOptions;
            knownFolderProperties.folderTypeId = nativeFolderDefinition.folderTypeId;
            knownFolderProperties.folderType = FolderTypes.GetFolderType(knownFolderProperties.folderTypeId);

            knownFolderProperties.path = GetPath(out var pathExists, knownFolderNative);
            knownFolderProperties.pathExists = pathExists;

            knownFolderProperties.redirection = knownFolderNative.GetRedirectionCapabilities();

            knownFolderProperties.tooltip = NativeMethods.GetStringResource(knownFolderProperties.tooltipResourceId);
            knownFolderProperties.localizedName = NativeMethods.GetStringResource(knownFolderProperties.localizedNameResourceId);

            knownFolderProperties.folderId = knownFolderNative.GetId();
        }
        finally
        {
            Marshal.FreeCoTaskMem(nativeFolderDefinition.name);
            Marshal.FreeCoTaskMem(nativeFolderDefinition.description);
            Marshal.FreeCoTaskMem(nativeFolderDefinition.relativePath);
            Marshal.FreeCoTaskMem(nativeFolderDefinition.parsingName);
            Marshal.FreeCoTaskMem(nativeFolderDefinition.tooltip);
            Marshal.FreeCoTaskMem(nativeFolderDefinition.localizedName);
            Marshal.FreeCoTaskMem(nativeFolderDefinition.icon);
            Marshal.FreeCoTaskMem(nativeFolderDefinition.security);
        }
    }

    private string GetPath(out bool fileExists, IKnownFolderNative knownFolderNative)
    {
        Debug.Assert(knownFolderNative != null);

        var kfPath = string.Empty;
        fileExists = true;

        if (knownFolderProperties.category == FolderCategory.Virtual)
        {
            fileExists = false;
            return kfPath;
        }

        try
        {
            kfPath = knownFolderNative!.GetPath(0);
        }
        catch (System.IO.FileNotFoundException)
        {
            fileExists = false;
        }
        catch (System.IO.DirectoryNotFoundException)
        {
            fileExists = false;
        }

        return kfPath;
    }
}
