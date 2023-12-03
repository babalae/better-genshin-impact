using MicaSetup.Helper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;

namespace MicaSetup.Shell.Dialogs;

#pragma warning disable CS8618

[ContentProperty("Controls")]
public abstract class CommonFileDialog : IDialogControlHost, IDisposable
{
    internal readonly Collection<IShellItem> items;

    internal DialogShowState showState = DialogShowState.PreShow;

    private readonly CommonFileDialogControlCollection<CommonFileDialogControl> controls;

    private readonly Collection<string> filenames;

    private readonly CommonFileDialogFilterCollection filters;

    private bool addToMruList = true;

    private bool allowPropertyEditing;

    private bool? canceled;

    private Guid cookieIdentifier;

    private IFileDialogCustomize customize;

    private string defaultDirectory;

    private ShellContainer defaultDirectoryShellContainer;

    private bool ensureFileExists;

    private bool ensurePathExists;

    private bool ensureReadOnly;

    private bool ensureValidNames;

    private bool filterSet;

    private string initialDirectory;

    private ShellContainer initialDirectoryShellContainer;

    private IFileDialog nativeDialog;

    private NativeDialogEventSink nativeEventSink;

    private bool navigateToShortcut = true;

    private nint parentWindow = 0;

    private bool resetSelections;

    private bool restoreDirectory;

    private bool showHiddenItems;

    private bool showPlacesList = true;

    private string title;

    protected CommonFileDialog()
    {
        if (!OsVersionHelper.IsWindowsVista_OrGreater)
        {
            throw new PlatformNotSupportedException(LocalizedMessages.CommonFileDialogRequiresVista);
        }

        filenames = new Collection<string>();
        filters = new CommonFileDialogFilterCollection();
        items = new Collection<IShellItem>();
        controls = new CommonFileDialogControlCollection<CommonFileDialogControl>(this);
    }

    protected CommonFileDialog(string title)
        : this() => this.title = title;

    public event EventHandler DialogOpening;

    public event CancelEventHandler FileOk;

    public event EventHandler FileTypeChanged;

    public event EventHandler FolderChanged;

    public event EventHandler<CommonFileDialogFolderChangeEventArgs> FolderChanging;

    public event EventHandler SelectionChanged;

    public static bool IsPlatformSupported =>
            OsVersionHelper.IsWindowsVista_OrGreater;

    public bool AddToMostRecentlyUsedList
    {
        get => addToMruList;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.AddToMostRecentlyUsedListCannotBeChanged);
            addToMruList = value;
        }
    }

    public bool AllowPropertyEditing
    {
        get => allowPropertyEditing;
        set => allowPropertyEditing = value;
    }

    public CommonFileDialogControlCollection<CommonFileDialogControl> Controls => controls;

    public Guid CookieIdentifier
    {
        get => cookieIdentifier;
        set => cookieIdentifier = value;
    }

    public string DefaultDirectory
    {
        get => defaultDirectory;
        set => defaultDirectory = value;
    }

    public ShellContainer DefaultDirectoryShellContainer
    {
        get => defaultDirectoryShellContainer;
        set => defaultDirectoryShellContainer = value;
    }

    public string DefaultExtension { get; set; }

    public string DefaultFileName { get; set; } = string.Empty;

    public bool EnsureFileExists
    {
        get => ensureFileExists;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.EnsureFileExistsCannotBeChanged);
            ensureFileExists = value;
        }
    }

    public bool EnsurePathExists
    {
        get => ensurePathExists;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.EnsurePathExistsCannotBeChanged);
            ensurePathExists = value;
        }
    }

    public bool EnsureReadOnly
    {
        get => ensureReadOnly;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.EnsureReadonlyCannotBeChanged);
            ensureReadOnly = value;
        }
    }

    public bool EnsureValidNames
    {
        get => ensureValidNames;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.EnsureValidNamesCannotBeChanged);
            ensureValidNames = value;
        }
    }

    public ShellObject FileAsShellObject
    {
        get
        {
            CheckFileItemsAvailable();

            if (items.Count > 1)
            {
                throw new InvalidOperationException(LocalizedMessages.CommonFileDialogMultipleItems);
            }

            if (items.Count == 0) { return null!; }

            return ShellObjectFactory.Create(items[0]);
        }
    }

    public string FileName
    {
        get
        {
            CheckFileNamesAvailable();

            if (filenames.Count > 1)
            {
                throw new InvalidOperationException(LocalizedMessages.CommonFileDialogMultipleFiles);
            }

            var returnFilename = filenames[0];

            if (this is CommonSaveFileDialog)
            {
                returnFilename = System.IO.Path.ChangeExtension(returnFilename, this.filters[this.SelectedFileTypeIndex - 1].Extensions[0]);
            }

            if (!string.IsNullOrEmpty(DefaultExtension))
            {
                returnFilename = System.IO.Path.ChangeExtension(returnFilename, DefaultExtension);
            }

            return returnFilename;
        }
    }

    public CommonFileDialogFilterCollection Filters => filters;

    public string InitialDirectory
    {
        get => initialDirectory;
        set => initialDirectory = value;
    }

    public ShellContainer InitialDirectoryShellContainer
    {
        get => initialDirectoryShellContainer;
        set => initialDirectoryShellContainer = value;
    }

    public bool NavigateToShortcut
    {
        get => navigateToShortcut;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.NavigateToShortcutCannotBeChanged);
            navigateToShortcut = value;
        }
    }

    public bool RestoreDirectory
    {
        get => restoreDirectory;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.RestoreDirectoryCannotBeChanged);
            restoreDirectory = value;
        }
    }

    public int SelectedFileTypeIndex
    {
        get
        {
            if (nativeDialog != null)
            {
                nativeDialog.GetFileTypeIndex(out var fileType);
                return (int)fileType;
            }

            return -1;
        }
    }

    public bool ShowHiddenItems
    {
        get => showHiddenItems;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.ShowHiddenItemsCannotBeChanged);
            showHiddenItems = value;
        }
    }

    public bool ShowPlacesList
    {
        get => showPlacesList;
        set
        {
            ThrowIfDialogShowing(LocalizedMessages.ShowPlacesListCannotBeChanged);
            showPlacesList = value;
        }
    }

    public string Title
    {
        get => title;
        set
        {
            title = value;
            if (NativeDialogShowing) { nativeDialog.SetTitle(value); }
        }
    }

    protected IEnumerable<string> FileNameCollection
    {
        get
        {
            foreach (var name in filenames)
            {
                yield return name;
            }
        }
    }

    private bool NativeDialogShowing => (nativeDialog != null)
                        && (showState == DialogShowState.Showing || showState == DialogShowState.Closing);

    public void AddPlace(ShellContainer place, FileDialogAddPlaceLocation location)
    {
        if (place == null!)
        {
            throw new ArgumentNullException("place");
        }

        if (nativeDialog == null)
        {
            InitializeNativeFileDialog();
            nativeDialog = GetNativeFileDialog();
        }
        nativeDialog?.AddPlace(place.NativeShellItem, (FileDialogAddPlacement)location);
    }

    public void AddPlace(string path, FileDialogAddPlaceLocation location)
    {
        if (string.IsNullOrEmpty(path)) { throw new ArgumentNullException("path"); }

        if (nativeDialog == null)
        {
            InitializeNativeFileDialog();
            nativeDialog = GetNativeFileDialog();
        }

        var guid = new Guid(ShellIIDGuid.IShellItem2);
        var retCode = Shell32.SHCreateItemFromParsingName(path, 0, ref guid, out
        IShellItem2 nativeShellItem);

        if (!CoreErrorHelper.Succeeded(retCode))
        {
            throw new CommonControlException(LocalizedMessages.CommonFileDialogCannotCreateShellItem, Marshal.GetExceptionForHR(retCode));
        }

        nativeDialog?.AddPlace(nativeShellItem, (FileDialogAddPlacement)location);
    }

    public virtual void ApplyCollectionChanged()
    {
        GetCustomizedFileDialog();
        foreach (var control in controls)
        {
            if (!control.IsAdded)
            {
                control.HostingDialog = this;
                control.Attach(customize);
                control.IsAdded = true;
            }
        }
    }

    public virtual void ApplyControlPropertyChange(string propertyName, DialogControl control)
    {
        if (control == null)
        {
            throw new ArgumentNullException("control");
        }

        CommonFileDialogControl dialogControl;
        if (propertyName == "Text")
        {
            if (control is CommonFileDialogTextBox textBox)
            {
                customize.SetEditBoxText(control.Id, textBox.Text);
            }
            else if (control is CommonFileDialogLabel label)
            {
                customize.SetControlLabel(control.Id, label.Text);
            }
        }
        else if (propertyName == "Visible" && (dialogControl = (control as CommonFileDialogControl)!) != null)
        {
            customize.GetControlState(control.Id, out var state);

            if (dialogControl.Visible == true)
            {
                state |= ControlState.Visible;
            }
            else if (dialogControl.Visible == false)
            {
                state &= ~ControlState.Visible;
            }

            customize.SetControlState(control.Id, state);
        }
        else if (propertyName == "Enabled" && (dialogControl = (control as CommonFileDialogControl)!) != null)
        {
            customize.GetControlState(control.Id, out var state);

            if (dialogControl.Enabled == true)
            {
                state |= ControlState.Enable;
            }
            else if (dialogControl.Enabled == false)
            {
                state &= ~ControlState.Enable;
            }

            customize.SetControlState(control.Id, state);
        }
        else if (propertyName == "SelectedIndex")
        {
            CommonFileDialogRadioButtonList list;
            CommonFileDialogComboBox box;

            if ((list = (control as CommonFileDialogRadioButtonList)!) != null)
            {
                customize.SetSelectedControlItem(list.Id, list.SelectedIndex);
            }
            else if ((box = (control as CommonFileDialogComboBox)!) != null)
            {
                customize.SetSelectedControlItem(box.Id, box.SelectedIndex);
            }
        }
        else if (propertyName == "IsChecked")
        {
            if (control is CommonFileDialogCheckBox checkBox)
            {
                customize.SetCheckButtonState(checkBox.Id, checkBox.IsChecked);
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public virtual bool IsCollectionChangeAllowed() => true;

    public virtual bool IsControlPropertyChangeAllowed(string propertyName, DialogControl control)
    {
        CommonFileDialog.GenerateNotImplementedException();
        return false;
    }

    public void ResetUserSelections() => resetSelections = true;

    public CommonFileDialogResult ShowDialog(nint ownerWindowHandle)
    {
        if (ownerWindowHandle == 0)
        {
            throw new ArgumentException(LocalizedMessages.CommonFileDialogInvalidHandle, "ownerWindowHandle");
        }

        parentWindow = ownerWindowHandle;

        return ShowDialog();
    }

    public CommonFileDialogResult ShowDialog(Window window)
    {
        if (window == null)
        {
            throw new ArgumentNullException("window");
        }

        parentWindow = (new WindowInteropHelper(window)).Handle;

        return ShowDialog();
    }

    public CommonFileDialogResult ShowDialog()
    {
        CommonFileDialogResult result;

        InitializeNativeFileDialog();
        nativeDialog = GetNativeFileDialog();

        ApplyNativeSettings(nativeDialog);
        InitializeEventSink(nativeDialog);

        if (resetSelections)
        {
            resetSelections = false;
        }

        showState = DialogShowState.Showing;
        var hresult = nativeDialog.Show(parentWindow);
        showState = DialogShowState.Closed;

        if (CoreErrorHelper.Matches(hresult, (int)HResult.Win32ErrorCanceled))
        {
            canceled = true;
            result = CommonFileDialogResult.Cancel;
            filenames.Clear();
        }
        else
        {
            canceled = false;
            result = CommonFileDialogResult.Ok;

            PopulateWithFileNames(filenames);

            PopulateWithIShellItems(items);
        }

        return result;
    }

    internal static string GetFileNameFromShellItem(IShellItem item)
    {
        string filename = null!;
        var hr = item.GetDisplayName(ShellItemDesignNameOptions.DesktopAbsoluteParsing, out var pszString);
        if (hr == HResult.Ok && pszString != 0)
        {
            filename = Marshal.PtrToStringAuto(pszString);
            Marshal.FreeCoTaskMem(pszString);
        }
        return filename;
    }

    internal static IShellItem GetShellItemAt(IShellItemArray array, int i)
    {
        var index = (uint)i;
        array.GetItemAt(index, out var result);
        return result;
    }

    internal abstract void CleanUpNativeFileDialog();

    internal abstract FileOpenOptions GetDerivedOptionFlags(FileOpenOptions flags);

    internal abstract IFileDialog GetNativeFileDialog();

    internal abstract void InitializeNativeFileDialog();

    internal abstract void PopulateWithFileNames(Collection<string> names);

    internal abstract void PopulateWithIShellItems(Collection<IShellItem> shellItems);

    protected void CheckFileItemsAvailable()
    {
        if (showState != DialogShowState.Closed)
        {
            throw new InvalidOperationException(LocalizedMessages.CommonFileDialogNotClosed);
        }

        if (canceled.GetValueOrDefault())
        {
            throw new InvalidOperationException(LocalizedMessages.CommonFileDialogCanceled);
        }

        Debug.Assert(items.Count != 0,
          "Items list empty - shouldn't happen unless dialog canceled or not yet shown.");
    }

    protected void CheckFileNamesAvailable()
    {
        if (showState != DialogShowState.Closed)
        {
            throw new InvalidOperationException(LocalizedMessages.CommonFileDialogNotClosed);
        }

        if (canceled.GetValueOrDefault())
        {
            throw new InvalidOperationException(LocalizedMessages.CommonFileDialogCanceled);
        }

        Debug.Assert(filenames.Count != 0,
          "FileNames empty - shouldn't happen unless dialog canceled or not yet shown.");
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            CleanUpNativeFileDialog();
        }
    }

    protected virtual void OnFileOk(CancelEventArgs e)
    {
        FileOk?.Invoke(this, e);
    }

    protected virtual void OnFileTypeChanged(EventArgs e)
    {
        FileTypeChanged?.Invoke(this, e);
    }

    protected virtual void OnFolderChanged(EventArgs e)
    {
        FolderChanged?.Invoke(this, e);
    }

    protected virtual void OnFolderChanging(CommonFileDialogFolderChangeEventArgs e)
    {
        FolderChanging?.Invoke(this, e);
    }

    protected virtual void OnOpening(EventArgs e)
    {
        DialogOpening?.Invoke(this, e);
    }

    protected virtual void OnSelectionChanged(EventArgs e)
    {
        SelectionChanged?.Invoke(this, e);
    }

    protected void ThrowIfDialogShowing(string message)
    {
        if (NativeDialogShowing)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void GenerateNotImplementedException() => throw new NotImplementedException(LocalizedMessages.NotImplementedException);

    private void ApplyNativeSettings(IFileDialog dialog)
    {
        Debug.Assert(dialog != null, "No dialog instance to configure");

        if (parentWindow == 0)
        {
            if (Application.Current != null && Application.Current.MainWindow != null)
            {
                parentWindow = (new WindowInteropHelper(Application.Current.MainWindow)).Handle;
            }
            else if (System.Windows.Forms.Application.OpenForms.Count > 0)
            {
                parentWindow = System.Windows.Forms.Application.OpenForms[0].Handle;
            }
        }

        var guid = new Guid(ShellIIDGuid.IShellItem2);

        dialog!.SetOptions(CalculateNativeDialogOptionFlags());

        if (title != null) { dialog.SetTitle(title); }

        if (initialDirectoryShellContainer != null!)
        {
            dialog.SetFolder(initialDirectoryShellContainer.NativeShellItem);
        }

        if (defaultDirectoryShellContainer != null!)
        {
            dialog.SetDefaultFolder(defaultDirectoryShellContainer.NativeShellItem);
        }

        if (!string.IsNullOrEmpty(initialDirectory))
        {
            Shell32.SHCreateItemFromParsingName(initialDirectory, 0, ref guid, out
            IShellItem2 initialDirectoryShellItem);

            if (initialDirectoryShellItem != null)
                dialog.SetFolder(initialDirectoryShellItem);
        }

        if (!string.IsNullOrEmpty(defaultDirectory))
        {
            Shell32.SHCreateItemFromParsingName(defaultDirectory, 0, ref guid, out
            IShellItem2 defaultDirectoryShellItem);

            if (defaultDirectoryShellItem != null)
            {
                dialog.SetDefaultFolder(defaultDirectoryShellItem);
            }
        }

        if (filters.Count > 0 && !filterSet)
        {
            dialog.SetFileTypes(
                (uint)filters.Count,
                filters.GetAllFilterSpecs());

            filterSet = true;

            SyncFileTypeComboToDefaultExtension(dialog);
        }

        if (cookieIdentifier != Guid.Empty)
        {
            dialog.SetClientGuid(ref cookieIdentifier);
        }

        if (!string.IsNullOrEmpty(DefaultExtension))
        {
            dialog.SetDefaultExtension(DefaultExtension);
        }

        dialog.SetFileName(DefaultFileName);
    }

    private FileOpenOptions CalculateNativeDialogOptionFlags()
    {
        var flags = FileOpenOptions.NoTestFileCreate;

        flags = GetDerivedOptionFlags(flags);

        if (ensureFileExists)
        {
            flags |= FileOpenOptions.FileMustExist;
        }
        if (ensurePathExists)
        {
            flags |= FileOpenOptions.PathMustExist;
        }
        if (!ensureValidNames)
        {
            flags |= FileOpenOptions.NoValidate;
        }
        if (!EnsureReadOnly)
        {
            flags |= FileOpenOptions.NoReadOnlyReturn;
        }
        if (restoreDirectory)
        {
            flags |= FileOpenOptions.NoChangeDirectory;
        }
        if (!showPlacesList)
        {
            flags |= FileOpenOptions.HidePinnedPlaces;
        }
        if (!addToMruList)
        {
            flags |= FileOpenOptions.DontAddToRecent;
        }
        if (showHiddenItems)
        {
            flags |= FileOpenOptions.ForceShowHidden;
        }
        if (!navigateToShortcut)
        {
            flags |= FileOpenOptions.NoDereferenceLinks;
        }
        return flags;
    }

    private void GetCustomizedFileDialog()
    {
        if (customize == null)
        {
            if (nativeDialog == null)
            {
                InitializeNativeFileDialog();
                nativeDialog = GetNativeFileDialog();
            }
            customize = (IFileDialogCustomize)nativeDialog;
        }
    }

    private void InitializeEventSink(IFileDialog nativeDlg)
    {
        if (FileOk != null
            || FolderChanging != null
            || FolderChanged != null
            || SelectionChanged != null
            || FileTypeChanged != null
            || DialogOpening != null
            || (controls != null && controls.Count > 0))
        {
            nativeEventSink = new NativeDialogEventSink(this);
            nativeDlg.Advise(nativeEventSink, out var cookie);
            nativeEventSink.Cookie = cookie;
        }
    }

    private void SyncFileTypeComboToDefaultExtension(IFileDialog dialog)
    {
        if (this is not CommonSaveFileDialog || DefaultExtension == null ||
            filters.Count <= 0)
        {
            return;
        }

        CommonFileDialogFilter filter;

        for (uint filtersCounter = 0; filtersCounter < filters.Count; filtersCounter++)
        {
            filter = filters[(int)filtersCounter];

            if (filter.Extensions.Contains(DefaultExtension))
            {
                dialog.SetFileTypeIndex(filtersCounter + 1);
                break;
            }
        }
    }

    private class NativeDialogEventSink : IFileDialogEvents, IFileDialogControlEvents
    {
        private readonly CommonFileDialog parent;
        private bool firstFolderChanged = true;

        public NativeDialogEventSink(CommonFileDialog commonDialog) => parent = commonDialog;

        public uint Cookie { get; set; }

        public void OnButtonClicked(IFileDialogCustomize pfdc, int dwIDCtl)
        {
            var control = parent.controls.GetControlbyId(dwIDCtl);
            var button = control as CommonFileDialogButton;
            button?.RaiseClickEvent();
        }

        public void OnCheckButtonToggled(IFileDialogCustomize pfdc, int dwIDCtl, bool bChecked)
        {
            var control = parent.controls.GetControlbyId(dwIDCtl);

            if (control is CommonFileDialogCheckBox box)
            {
                box.IsChecked = bChecked;
                box.RaiseCheckedChangedEvent();
            }
        }

        public void OnControlActivating(IFileDialogCustomize pfdc, int dwIDCtl)
        {
        }

        public HResult OnFileOk(IFileDialog pfd)
        {
            var args = new CancelEventArgs();
            parent.OnFileOk(args);

            if (!args.Cancel)
            {
                if (parent.Controls != null)
                {
                    foreach (var control in parent.Controls)
                    {
                        CommonFileDialogTextBox textBox;
                        CommonFileDialogGroupBox groupBox; ;

                        if ((textBox = (control as CommonFileDialogTextBox)!) != null)
                        {
                            textBox.SyncValue();
                            textBox.Closed = true;
                        }
                        else if ((groupBox = (control as CommonFileDialogGroupBox)!) != null)
                        {
                            foreach (CommonFileDialogControl subcontrol in groupBox.Items.Cast<CommonFileDialogControl>())
                            {
                                if (subcontrol is CommonFileDialogTextBox textbox)
                                {
                                    textbox.SyncValue();
                                    textbox.Closed = true;
                                }
                            }
                        }
                    }
                }
            }

            return (args.Cancel ? HResult.False : HResult.Ok);
        }

        public void OnFolderChange(IFileDialog pfd)
        {
            if (firstFolderChanged)
            {
                firstFolderChanged = false;
                parent.OnOpening(EventArgs.Empty);
            }
            else
            {
                parent.OnFolderChanged(EventArgs.Empty);
            }
        }

        public HResult OnFolderChanging(IFileDialog pfd, IShellItem psiFolder)
        {
            var args = new CommonFileDialogFolderChangeEventArgs(
                CommonFileDialog.GetFileNameFromShellItem(psiFolder));

            if (!firstFolderChanged) { parent.OnFolderChanging(args); }

            return (args.Cancel ? HResult.False : HResult.Ok);
        }

        public void OnItemSelected(IFileDialogCustomize pfdc, int dwIDCtl, int dwIDItem)
        {
            var control = parent.controls.GetControlbyId(dwIDCtl);

            if (control is ICommonFileDialogIndexedControls controlInterface)
            {
                controlInterface.SelectedIndex = dwIDItem;
                controlInterface.RaiseSelectedIndexChangedEvent();
            }
            else if (control is CommonFileDialogMenu menu)
            {
                foreach (var item in menu.Items)
                {
                    if (item.Id == dwIDItem)
                    {
                        item.RaiseClickEvent();
                        break;
                    }
                }
            }
        }

        public void OnOverwrite(IFileDialog pfd, IShellItem psi, out FileDialogEventOverwriteResponse pResponse) =>
            pResponse = FileDialogEventOverwriteResponse.Default;

        public void OnSelectionChange(IFileDialog pfd) => parent.OnSelectionChanged(EventArgs.Empty);

        public void OnShareViolation(
            IFileDialog pfd,
            IShellItem psi,
            out FileDialogEventShareViolationResponse pResponse) =>
            pResponse = FileDialogEventShareViolationResponse.Accept;

        public void OnTypeChange(IFileDialog pfd) => parent.OnFileTypeChanged(EventArgs.Empty);
    }
}
