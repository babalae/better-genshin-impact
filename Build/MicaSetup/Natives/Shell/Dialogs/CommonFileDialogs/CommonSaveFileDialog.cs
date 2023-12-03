using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8073
#pragma warning disable CS8618

public sealed class CommonSaveFileDialog : CommonFileDialog
{
    private bool alwaysAppendDefaultExtension;
    private bool createPrompt;
    private bool isExpandedMode;
    private bool overwritePrompt = true;
    private NativeFileSaveDialog saveDialogCoClass;

    public CommonSaveFileDialog()
    {
    }

    public CommonSaveFileDialog(string name) : base(name)
    {
    }

    public bool AlwaysAppendDefaultExtension
    {
        get => alwaysAppendDefaultExtension;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.AlwaysAppendDefaultExtensionCannotBeChanged);
            alwaysAppendDefaultExtension = value;
        }
    }

    public ShellPropertyCollection CollectedProperties
    {
        get
        {
            InitializeNativeFileDialog();
            var nativeDialog = GetNativeFileDialog() as IFileSaveDialog;

            if (nativeDialog != null)
            {
                var hr = nativeDialog.GetProperties(out var propertyStore);

                if (propertyStore != null && CoreErrorHelper.Succeeded(hr))
                {
                    return new ShellPropertyCollection(propertyStore);
                }
            }

            return null!;
        }
    }

    public bool CreatePrompt
    {
        get => createPrompt;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.CreatePromptCannotBeChanged);
            createPrompt = value;
        }
    }

    public bool IsExpandedMode
    {
        get => isExpandedMode;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.IsExpandedModeCannotBeChanged);
            isExpandedMode = value;
        }
    }

    public bool OverwritePrompt
    {
        get => overwritePrompt;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.OverwritePromptCannotBeChanged);
            overwritePrompt = value;
        }
    }

    public void SetCollectedPropertyKeys(bool appendDefault, params PropertyKey[] propertyList)
    {
        if (propertyList != null && propertyList.Length > 0 && propertyList[0] != null)
        {
            var sb = new StringBuilder("prop:");
            foreach (var key in propertyList)
            {
                var canonicalName = ShellPropertyDescriptionsCache.Cache.GetPropertyDescription(key).CanonicalName;
                if (!string.IsNullOrEmpty(canonicalName)) { sb.AppendFormat("{0};", canonicalName); }
            }

            var guid = new Guid(ShellIIDGuid.IPropertyDescriptionList);
            IPropertyDescriptionList propertyDescriptionList = null!;

            try
            {
                var hr = PropertySystemNativeMethods.PSGetPropertyDescriptionListFromString(
                    sb.ToString(),
                    ref guid,
                    out propertyDescriptionList);

                if (CoreErrorHelper.Succeeded(hr))
                {
                    InitializeNativeFileDialog();
                    var nativeDialog = GetNativeFileDialog() as IFileSaveDialog;

                    if (nativeDialog != null)
                    {
                        hr = nativeDialog.SetCollectedProperties(propertyDescriptionList, appendDefault);

                        if (!CoreErrorHelper.Succeeded(hr))
                        {
                            throw new ShellException(hr);
                        }
                    }
                }
            }
            finally
            {
                if (propertyDescriptionList != null)
                {
                    Marshal.ReleaseComObject(propertyDescriptionList);
                }
            }
        }
    }

    public void SetSaveAsItem(ShellObject item)
    {
        if (item == null!)
        {
            throw new ArgumentNullException("item");
        }

        InitializeNativeFileDialog();
        var nativeDialog = GetNativeFileDialog() as IFileSaveDialog;

        if (nativeDialog != null)
        {
            nativeDialog.SetSaveAsItem(item.NativeShellItem);
        }
    }

    internal override void CleanUpNativeFileDialog()
    {
        if (saveDialogCoClass != null)
        {
            Marshal.ReleaseComObject(saveDialogCoClass);
        }
    }

    internal override FileOpenOptions GetDerivedOptionFlags(FileOpenOptions flags)
    {
        if (overwritePrompt)
        {
            flags |= FileOpenOptions.OverwritePrompt;
        }
        if (createPrompt)
        {
            flags |= FileOpenOptions.CreatePrompt;
        }
        if (!isExpandedMode)
        {
            flags |= FileOpenOptions.DefaultNoMiniMode;
        }
        if (alwaysAppendDefaultExtension)
        {
            flags |= FileOpenOptions.StrictFileTypes;
        }
        return flags;
    }

    internal override IFileDialog GetNativeFileDialog()
    {
        Debug.Assert(saveDialogCoClass != null, "Must call Initialize() before fetching dialog interface");
        return saveDialogCoClass!;
    }

    internal override void InitializeNativeFileDialog()
    {
        if (saveDialogCoClass == null)
        {
            saveDialogCoClass = new NativeFileSaveDialog();
        }
    }

    internal override void PopulateWithFileNames(
        System.Collections.ObjectModel.Collection<string> names)
    {
        saveDialogCoClass.GetResult(out var item);

        if (item == null)
        {
            throw new InvalidOperationException(LocalizedMessages.SaveFileNullItem);
        }
        names.Clear();
        names.Add(GetFileNameFromShellItem(item));
    }

    internal override void PopulateWithIShellItems(System.Collections.ObjectModel.Collection<IShellItem> items)
    {
        saveDialogCoClass.GetResult(out var item);

        if (item == null)
        {
            throw new InvalidOperationException(LocalizedMessages.SaveFileNullItem);
        }
        items.Clear();
        items.Add(item);
    }
}
