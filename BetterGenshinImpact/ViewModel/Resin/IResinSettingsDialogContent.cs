using System.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Resin;

/// <summary>树脂设置窗口的内容合同，窗口外框不依赖具体编辑器。</summary>
public interface IResinSettingsDialogContent : INotifyPropertyChanged
{
    /// <summary>标题。</summary>
    string Title { get; }
    /// <summary>当前设置摘要。</summary>
    string Summary { get; }
    /// <summary>无效原因，合法时为 null。</summary>
    string? ValidationMessage { get; }
    /// <summary>当前能否确认。</summary>
    bool CanConfirm { get; }
}
