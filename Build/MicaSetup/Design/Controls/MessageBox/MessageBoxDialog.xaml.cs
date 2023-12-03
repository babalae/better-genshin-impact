using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace MicaSetup.Design.Controls;

[INotifyPropertyChanged]
public partial class MessageBoxDialog : Window
{
    [ObservableProperty]
    private string message = null!;

    [ObservableProperty]
    protected bool okayVisiable = true;

    [ObservableProperty]
    protected bool yesVisiable = false;

    [ObservableProperty]
    protected bool noVisiable = false;

    [ObservableProperty]
    protected WindowDialogResult result = WindowDialogResult.None;

    partial void OnResultChanged(WindowDialogResult value)
    {
        _ = value;
        Close();
    }

    [ObservableProperty]
    private string iconString = "\xe915";

    [ObservableProperty]
    private MessageBoxType type = MessageBoxType.Info;

    partial void OnTypeChanged(MessageBoxType value)
    {
        IconString = value switch
        {
            MessageBoxType.Question => "\xe918",
            MessageBoxType.Info or _ => "\xe915",
        };

        OkayVisiable = value switch
        {
            MessageBoxType.Question => false,
            MessageBoxType.Info or _ => true,
        };

        YesVisiable = value switch
        {
            MessageBoxType.Question => true,
            MessageBoxType.Info or _ => false,
        };

        NoVisiable = value switch
        {
            MessageBoxType.Question => true,
            MessageBoxType.Info or _ => false,
        };
    }

    public MessageBoxDialog()
    {
        DataContext = this;
        InitializeComponent();
    }

    [RelayCommand]
    private void Okay()
    {
        Result = WindowDialogResult.OK;
    }

    [RelayCommand]
    private void Yes()
    {
        Result = WindowDialogResult.Yes;
    }

    [RelayCommand]
    private void No()
    {
        Result = WindowDialogResult.No;
    }

    public WindowDialogResult ShowDialog(Window owner)
    {
        Owner = owner;
        ShowDialog();
        return Result;
    }
}

public enum MessageBoxType
{
    Info,
    Question,
}

public enum WindowDialogResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Yes = 6,
    No = 7
}
