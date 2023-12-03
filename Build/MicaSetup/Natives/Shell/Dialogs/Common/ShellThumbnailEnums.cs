namespace MicaSetup.Shell.Dialogs;

public enum ShellThumbnailFormatOption
{
    Default,
    ThumbnailOnly = SIIGBF.ThumbnailOnly,
    IconOnly = SIIGBF.IconOnly,
}

public enum ShellThumbnailRetrievalOption
{
    Default,
    CacheOnly = SIIGBF.InCacheOnly,
    MemoryOnly = SIIGBF.MemoryOnly,
}
