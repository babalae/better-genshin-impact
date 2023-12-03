using System;
using System.Collections.ObjectModel;
using System.Text;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8618

public class CommonFileDialogFilter
{
    private readonly Collection<string> extensions;
    private string rawDisplayName;

    private bool showExtensions = true;

    public CommonFileDialogFilter() => extensions = new Collection<string>();

    public CommonFileDialogFilter(string rawDisplayName, string extensionList) : this()
    {
        if (string.IsNullOrEmpty(extensionList))
        {
            throw new ArgumentNullException("extensionList");
        }

        this.rawDisplayName = rawDisplayName;

        var rawExtensions = extensionList.Split(',', ';');
        foreach (var extension in rawExtensions)
        {
            extensions.Add(CommonFileDialogFilter.NormalizeExtension(extension));
        }
    }

    public string DisplayName
    {
        get
        {
            if (showExtensions)
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} ({1})",
                    rawDisplayName,
                    CommonFileDialogFilter.GetDisplayExtensionList(extensions));
            }

            return rawDisplayName;
        }

        set
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException("value");
            }
            rawDisplayName = value;
        }
    }

    public Collection<string> Extensions => extensions;

    public bool ShowExtensions
    {
        get => showExtensions;
        set => showExtensions = value;
    }

    public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0} ({1})",
            rawDisplayName,
            CommonFileDialogFilter.GetDisplayExtensionList(extensions));

    internal FilterSpec GetFilterSpec()
    {
        var filterList = new StringBuilder();
        foreach (var extension in extensions)
        {
            if (filterList.Length > 0) { filterList.Append(";"); }

            filterList.Append("*.");
            filterList.Append(extension);
        }
        return new FilterSpec(DisplayName, filterList.ToString());
    }

    private static string GetDisplayExtensionList(Collection<string> extensions)
    {
        var extensionList = new StringBuilder();
        foreach (var extension in extensions)
        {
            if (extensionList.Length > 0) { extensionList.Append(", "); }
            extensionList.Append("*.");
            extensionList.Append(extension);
        }

        return extensionList.ToString();
    }

    private static string NormalizeExtension(string rawExtension)
    {
        rawExtension = rawExtension.Trim();
        rawExtension = rawExtension.Replace("*.", null);

        int indexOfDot = rawExtension.IndexOf('.');
        if (indexOfDot != -1)
        {
            rawExtension.Remove(indexOfDot);
        }

        return rawExtension;
    }
}
