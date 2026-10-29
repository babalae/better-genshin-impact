using System.ComponentModel;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Guide;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 字幕浮窗的 ViewModel：显示文本、样式和调整位置模式（见设计文档第 11 节）
/// </summary>
public partial class GuideSubtitleWindowViewModel : ObservableObject, IViewModel
{
    /// <summary>
    /// 调整模式下没有字幕时显示的示例文本
    /// </summary>
    private const string PreviewText = "字幕位置预览：拖动移动位置，拖右侧边缘调整宽度，Ctrl+滚轮调整字号";

    private readonly IGuideWindowService _service;

    /// <summary>
    /// 字幕最大宽度（界面单位），由窗口按游戏画面宽度计算
    /// </summary>
    [ObservableProperty]
    private double _maxTextWidth = 900;

    /// <param name="service">小窗服务</param>
    public GuideSubtitleWindowViewModel(IGuideWindowService service)
    {
        _service = service;
        State.PropertyChanged += OnStatePropertyChanged;
    }

    /// <summary>
    /// 小窗状态
    /// </summary>
    public GuideWindowState State => _service.State;

    /// <summary>
    /// 小窗配置
    /// </summary>
    public GuideWindowConfig Config => _service.Config;

    /// <summary>
    /// 是否处于调整位置模式
    /// </summary>
    public bool IsAdjusting => State.IsSubtitleAdjusting;

    /// <summary>
    /// 是否鼠标穿透：调整位置时可以操作，其他时候不挡住游戏
    /// </summary>
    public bool IsClickThrough => !State.IsSubtitleAdjusting;

    /// <summary>
    /// 显示的文本：调整模式下没有字幕时显示示例
    /// </summary>
    public string DisplayText => State.IsSubtitleAdjusting && !State.HasSubtitle ? PreviewText : State.SubtitleText;

    /// <summary>
    /// 结束调整位置
    /// </summary>
    [RelayCommand]
    private void FinishAdjust()
    {
        _service.EndSubtitleAdjust();
    }

    /// <summary>
    /// 窗口关闭时取消订阅
    /// </summary>
    public void Detach()
    {
        State.PropertyChanged -= OnStatePropertyChanged;
    }

    /// <summary>
    /// 字幕或调整模式变化时刷新展示属性
    /// </summary>
    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GuideWindowState.SubtitleText):
                OnPropertyChanged(nameof(DisplayText));
                break;
            case nameof(GuideWindowState.IsSubtitleAdjusting):
                OnPropertyChanged(nameof(IsAdjusting));
                OnPropertyChanged(nameof(IsClickThrough));
                OnPropertyChanged(nameof(DisplayText));
                break;
        }
    }
}
