using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages;

/// <summary>三条默认规则共用的声线选择控件，仅传递稳定引用。</summary>
public partial class QuestVoiceVoiceSelector : UserControl
{
    /// <summary>单一目录投影，列表重建不会默认选择第一项。</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(QuestVoiceVoiceSelector));
    /// <summary>保存的是 Profile ID，显示名称与资源路径不参与引用。</summary>
    public static readonly DependencyProperty SelectedVoiceIdProperty = DependencyProperty.Register(nameof(SelectedVoiceId), typeof(string), typeof(QuestVoiceVoiceSelector), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    /// <summary>全部可选声音，由共享管理器生成。</summary>
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    /// <summary>当前持久化引用。</summary>
    public string? SelectedVoiceId { get => (string?)GetValue(SelectedVoiceIdProperty); set => SetValue(SelectedVoiceIdProperty, value); }
    /// <summary>控件不读文件、不枚举声音、不拥有会话。</summary>
    public QuestVoiceVoiceSelector() => InitializeComponent();
}
