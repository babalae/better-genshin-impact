using System.IO;

namespace MicaSetup.Shell.Dialogs;

public class ShellFile : ShellObject
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
    internal ShellFile(string path)
    {
        var absPath = ShellHelper.GetAbsolutePath(path);

        if (!File.Exists(absPath))
        {
            throw new FileNotFoundException(
                string.Format(System.Globalization.CultureInfo.InvariantCulture,
                LocalizedMessages.FilePathNotExist, path));
        }

        ParsingName = absPath;
    }

    internal ShellFile(IShellItem2 shellItem) => nativeShellItem = shellItem;

    public virtual string Path => ParsingName;

    public static ShellFile FromFilePath(string path) => new ShellFile(path);
}
