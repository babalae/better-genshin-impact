using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.Input;
using System;

namespace BetterGenshinImpact.ViewModel.Windows;

public abstract partial class AutoPickConfigWindowViewModelBase : ViewModel
{
    public event Action<bool?>? CloseRequested;

    protected void SaveAndClose(Action saveAction)
    {
        try
        {
            saveAction();
            // 名单文件已修改，自动拾取在下一次拾取时重读
            AutoPickTrigger.ReloadLists();
            CloseRequested?.Invoke(true);
        }
        catch (Exception e)
        {
            ThemedMessageBox.Error("保存自动拾取名单配置失败：" + e);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(false);
    }
}
