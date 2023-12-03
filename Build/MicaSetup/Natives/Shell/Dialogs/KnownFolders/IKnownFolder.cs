using System;
using System.Collections.Generic;
using System.IO;

namespace MicaSetup.Shell.Dialogs;

public interface IKnownFolder : IDisposable, IEnumerable<ShellObject>
{
    string CanonicalName { get; }

    FolderCategory Category { get; }

    DefinitionOptions DefinitionOptions { get; }

    string Description { get; }

    FileAttributes FileAttributes { get; }

    Guid FolderId { get; }

    string FolderType { get; }

    Guid FolderTypeId { get; }

    string LocalizedName { get; }

    string LocalizedNameResourceId { get; }

    Guid ParentId { get; }

    string ParsingName { get; }

    string Path { get; }

    bool PathExists { get; }

    RedirectionCapability Redirection { get; }

    string RelativePath { get; }

    string Security { get; }

    string Tooltip { get; }

    string TooltipResourceId { get; }
}
