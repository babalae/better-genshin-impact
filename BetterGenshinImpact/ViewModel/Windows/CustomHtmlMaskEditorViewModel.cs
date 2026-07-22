using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace BetterGenshinImpact.ViewModel.Windows;

public partial class CustomHtmlMaskEditorViewModel : ObservableObject
{
    private readonly CustomHtmlMaskService _customHtmlMaskService;
    private readonly MaskWindowConfig _maskWindowConfig;
    private string _savedCustomHtmlMaskContent = string.Empty;

    [ObservableProperty]
    private string _customHtmlMaskContent = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewButtonText))]
    private bool _customHtmlMaskPreviewOpen;

    public CustomHtmlMaskEditorViewModel(CustomHtmlMaskService customHtmlMaskService, MaskWindowConfig maskWindowConfig)
    {
        _customHtmlMaskService = customHtmlMaskService;
        _maskWindowConfig = maskWindowConfig;
        var content = _customHtmlMaskService.ReadHtml();
        _savedCustomHtmlMaskContent = content;
        CustomHtmlMaskContent = content;
    }

    public string PreviewButtonText => CustomHtmlMaskPreviewOpen ? "关闭预览" : "预览 HTML";

    public bool HasUnsavedChanges => !string.Equals(CustomHtmlMaskContent, _savedCustomHtmlMaskContent, StringComparison.Ordinal);

    partial void OnCustomHtmlMaskContentChanged(string value)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    [RelayCommand]
    private void OnSaveCustomHtmlMask()
    {
        _customHtmlMaskService.SaveHtml(CustomHtmlMaskContent);
        MarkCustomHtmlMaskSaved();
        if (_maskWindowConfig.CustomHtmlMaskAutoReloadOnSave)
        {
            _customHtmlMaskService.RefreshAutoWindow();
        }

        if (CustomHtmlMaskPreviewOpen)
        {
            _customHtmlMaskService.Preview();
        }
    }

    [RelayCommand]
    private void OnToggleCustomHtmlMaskPreview()
    {
        if (CustomHtmlMaskPreviewOpen)
        {
            ClosePreview();
            return;
        }

        _customHtmlMaskService.SaveHtml(CustomHtmlMaskContent);
        MarkCustomHtmlMaskSaved();
        _customHtmlMaskService.Preview();
        CustomHtmlMaskPreviewOpen = true;
    }

    public void ClosePreview()
    {
        _customHtmlMaskService.ClosePreview();
        CustomHtmlMaskPreviewOpen = false;
    }

    [RelayCommand]
    private void OnRestoreDefaultCustomHtmlMask()
    {
        _customHtmlMaskService.RestoreDefaultHtml();
        CustomHtmlMaskContent = _customHtmlMaskService.ReadHtml();
        MarkCustomHtmlMaskSaved();
        if (_maskWindowConfig.CustomHtmlMaskAutoReloadOnSave)
        {
            _customHtmlMaskService.RefreshAutoWindow();
        }

        if (CustomHtmlMaskPreviewOpen)
        {
            _customHtmlMaskService.Preview();
        }
    }

    [RelayCommand]
    private void OnOpenCustomHtmlMaskFolder()
    {
        Directory.CreateDirectory(_customHtmlMaskService.DirectoryPath);
        Process.Start("explorer.exe", _customHtmlMaskService.DirectoryPath);
    }

    public bool ConfirmClose(Window? owner)
    {
        if (!HasUnsavedChanges)
        {
            return true;
        }

        var result = ThemedMessageBox.Show(
            "你还有未保存的 HTML 遮罩修改。\r\n\r\n现在关闭会丢失这些内容，要继续关闭吗？",
            "关闭编辑器？",
            MessageBoxButton.YesNo,
            ThemedMessageBox.MessageBoxIcon.Question,
            MessageBoxResult.No,
            owner);
        return result == MessageBoxResult.Yes;
    }

    private void MarkCustomHtmlMaskSaved()
    {
        _savedCustomHtmlMaskContent = CustomHtmlMaskContent;
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    [RelayCommand]
    private void OnClose()
    {
        Application.Current.Windows
            .Cast<Window>()
            .FirstOrDefault(window => window.Tag?.Equals(CustomHtmlMaskEditorWindow.WindowTag) ?? false)
            ?.Close();
    }
}
