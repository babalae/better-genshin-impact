using System.Windows;
using System.Windows.Controls;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.ViewModel.Controls.CombatStrategy;
using Wpf.Ui;

namespace BetterGenshinImpact.View.Controls.CombatStrategy;

/// <summary>战斗策略触发区和弹出层宿主；仅负责依赖属性同步与生命周期。</summary>
public partial class CombatStrategyPicker : UserControl
{
    /// <summary>策略字符串，默认双向写回宿主。</summary>
    public static readonly DependencyProperty StrategyProperty = DependencyProperty.Register(nameof(Strategy), typeof(string),
        typeof(CombatStrategyPicker), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            static (d, e) => ((CombatStrategyPicker)d).ViewModel.SetCurrent((string?)e.NewValue)));
    /// <summary>入口允许的策略类型。</summary>
    public static readonly DependencyProperty AllowedKindsProperty = DependencyProperty.Register(nameof(AllowedKinds), typeof(CombatStrategyKind),
        typeof(CombatStrategyPicker), new PropertyMetadata(CombatStrategyKind.All,
            static (d, e) => ((CombatStrategyPicker)d).ViewModel.SetAllowedKinds((CombatStrategyKind)e.NewValue)));
    /// <summary>当前策略的规范值、旧名称或绝对路径。</summary>
    public string? Strategy { get => (string?)GetValue(StrategyProperty); set => SetValue(StrategyProperty, value); }
    /// <summary>可选类型集合。</summary>
    public CombatStrategyKind AllowedKinds { get => (CombatStrategyKind)GetValue(AllowedKindsProperty); set => SetValue(AllowedKindsProperty, value); }
    /// <summary>每实例独立的选择器状态。</summary>
    public CombatStrategyPickerViewModel ViewModel { get; }

    /// <summary>创建独立 VM，保留宿主的 DataContext 供策略依赖属性绑定。</summary>
    public CombatStrategyPicker() : this(new CombatStrategyPickerViewModel(
        App.GetService<KeyMouseRecordService>()!, App.GetService<INavigationService>()!)) { }

    /// <summary>注入每实例的状态，XAML 默认构造复用应用服务。</summary>
    public CombatStrategyPicker(CombatStrategyPickerViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.StrategySelected += value => SetCurrentValue(StrategyProperty, value);
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>页面重复加载时重新挂接消息。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Attach(Application.Current?.MainWindow is { } mainWindow && Window.GetWindow(this) == mainWindow);
        ViewModel.SetCurrent(Strategy);
    }
    /// <summary>页面卸载时取消后台任务与消息。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Detach();
}
