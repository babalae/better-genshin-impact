using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BetterGenshinImpact.View.Controls.Resin;

/// <summary>树脂设置入口卡片，依赖属性沿用宿主的绑定上下文。</summary>
public partial class ResinSettingsCard : UserControl
{
    /// <summary>卡片标题依赖属性。</summary>
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(string), typeof(ResinSettingsCard), new PropertyMetadata("树脂使用", OnPresentationChanged));
    /// <summary>卡片摘要依赖属性。</summary>
    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(nameof(Summary), typeof(string), typeof(ResinSettingsCard), new PropertyMetadata(string.Empty, OnPresentationChanged));
    /// <summary>无效原因依赖属性。</summary>
    public static readonly DependencyProperty ProblemProperty = DependencyProperty.Register(nameof(Problem), typeof(string), typeof(ResinSettingsCard), new PropertyMetadata(null, OnPresentationChanged));
    /// <summary>警告状态只读依赖属性的写入键。</summary>
    private static readonly DependencyPropertyKey HasProblemPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasProblem), typeof(bool), typeof(ResinSettingsCard), new PropertyMetadata(false));
    /// <summary>是否显示警告、隐藏普通摘要。</summary>
    public static readonly DependencyProperty HasProblemProperty = HasProblemPropertyKey.DependencyProperty;
    /// <summary>读屏名称只读依赖属性的写入键。</summary>
    private static readonly DependencyPropertyKey AutomationNamePropertyKey = DependencyProperty.RegisterReadOnly(nameof(AutomationName), typeof(string), typeof(ResinSettingsCard), new PropertyMetadata("树脂使用"));
    /// <summary>不会重复朗读摘要与原因的读屏名称。</summary>
    public static readonly DependencyProperty AutomationNameProperty = AutomationNamePropertyKey.DependencyProperty;
    /// <summary>整张卡片的编辑命令。</summary>
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(ResinSettingsCard), new PropertyMetadata(null));
    /// <summary>卡片标题。</summary>
    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    /// <summary>当前设置摘要。</summary>
    public string Summary { get => (string)GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    /// <summary>无效时显示的原因。</summary>
    public string? Problem { get => (string?)GetValue(ProblemProperty); set => SetValue(ProblemProperty, value); }
    /// <summary>打开编辑弹窗的命令。</summary>
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    /// <summary>当前是否有非空问题。</summary>
    public bool HasProblem => (bool)GetValue(HasProblemProperty);
    /// <summary>标题与当前唯一可见的摘要或原因。</summary>
    public string AutomationName => (string)GetValue(AutomationNameProperty);

    /// <summary>依赖属性同步显示状态，不改变宿主 DataContext 或编辑值。</summary>
    private static void OnPresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var card = (ResinSettingsCard)sender;
        var hasProblem = !string.IsNullOrEmpty(card.Problem);
        card.SetValue(HasProblemPropertyKey, hasProblem);
        card.SetValue(AutomationNamePropertyKey, $"{card.Header}：{(hasProblem ? card.Problem : card.Summary)}");
    }

    /// <summary>初始化独立资源，保留宿主 DataContext。</summary>
    public ResinSettingsCard() => InitializeComponent();
}
