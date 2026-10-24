using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.View.Controls.CombatStrategy;
using BetterGenshinImpact.ViewModel.Controls.CombatStrategy;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

/// <summary>验证类别模板、真实数据绑定和主题布局，不需要启动主程序。</summary>
[Collection("CombatStrategyFiles")]
public sealed class CombatStrategyViewTests
{
    /// <summary>覆盖独立模板构造、深浅主题、各类别及宿主双向绑定，并可选输出离屏预览。</summary>
    [Fact]
    public void Views_LoadWithThemesAndData_AndPreserveHostBindings()
    {
        RunOnSta(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
                app.Resources.MergedDictionaries.Add(new ControlsDictionary());
                app.Resources["TextThemeFontFamily"] = new FontFamily("pack://application:,,,/BetterGI;component/Resources/Fonts/MiSans-Regular.ttf#MiSans");
                app.Resources["ControlCornerRadius"] = new CornerRadius(6);
                VerifyPendingFocusIsCancelled();
                foreach (var theme in new[] { ApplicationTheme.Dark, ApplicationTheme.Light })
                {
                    app.Resources.MergedDictionaries[0] = new ThemesDictionary { Theme = theme };
                    VerifyTemplates();
                    VerifyPanel(theme);
                }
            }
            finally { app.Shutdown(); }
        });
    }

    /// <summary>模拟切换类别时的模板实例化，覆盖构造及布局期间的静态资源解析。</summary>
    private static void VerifyTemplates()
    {
        var panel = new CombatStrategyPickerPanel();
        foreach (var (categoryType, viewType) in new[]
        {
            (typeof(StrategyBrowserViewModel), typeof(StrategyBrowserView)),
            (typeof(AutoMatchCategoryViewModel), typeof(AutoMatchView)),
            (typeof(ComboCategoryViewModel), typeof(ComboView)),
        })
        {
            var template = Assert.IsType<DataTemplate>(panel.FindResource(new DataTemplateKey(categoryType)));
            var view = Assert.IsAssignableFrom<FrameworkElement>(template.LoadContent());
            Assert.IsType(viewType, view);
            if (view is StrategyBrowserView browser)
            {
                var list = Assert.IsType<ListBox>(browser.FindName("EntryList"));
                Assert.NotNull(list.ItemTemplate);
                Assert.IsAssignableFrom<FrameworkElement>(list.ItemTemplate.LoadContent());
                Assert.IsType<BooleanToVisibilityConverter>(browser.FindResource("StrategyVisibility"));
            }
            Layout(view, 440, 400);
        }
    }

    /// <summary>用包含文件夹、有效策略和解析错误的快照验证真实面板，而非只加载空模板。</summary>
    private static void VerifyPanel(ApplicationTheme theme)
    {
        using var files = new StrategyTestFiles();
        var first = StrategyFile(files.Fight("深渊/12层/钟离万叶班尼特香菱.txt"), "深渊\\12层\\钟离万叶班尼特香菱.txt");
        var second = StrategyFile(files.Fight("深渊/12层/那维莱特芙宁娜.txt"), "深渊\\12层\\那维莱特芙宁娜.txt");
        var broken = StrategyFile(files.Fight("深渊/12层/旧版雷国.txt"), "深渊\\12层\\旧版雷国.txt");
        var floor = Folder("12层", "深渊\\12层", Folder("上半", "深渊\\12层\\上半"), Folder("下半", "深渊\\12层\\下半"), first, second, broken);
        var abyss = Folder("深渊", "深渊", floor);
        var root = Folder("AutoFight", "", abyss, Folder("地图追踪", "地图追踪"), Folder("秘境", "秘境"));
        var snapshot = new CombatStrategyCatalogSnapshot(new Dictionary<CombatStrategyKind, CombatStrategyNode>
        {
            [CombatStrategyKind.Txt] = root,
            [CombatStrategyKind.Json] = Folder("JSON", ""),
            [CombatStrategyKind.KeyMouse] = Folder("键鼠录制", ""),
        }, [root, abyss, floor]);
        var vm = new CombatStrategyPickerViewModel(new KeyMouseRecordService(null!, NullLogger<KeyMouseRecordService>.Instance), null!);
        var host = new HostState { Strategy = first.Strategy!.Path };
        var picker = new CombatStrategyPicker(vm) { Width = 288, DataContext = host };
        picker.SetBinding(CombatStrategyPicker.StrategyProperty, new Binding(nameof(HostState.Strategy)) { Mode = BindingMode.TwoWay });
        vm.ApplySnapshot(snapshot, locateCurrent: true);
        var browser = vm.Categories.OfType<StrategyBrowserViewModel>().Single(b => b.Kind == CombatStrategyKind.Txt);
        browser.ApplyMetadata(new Dictionary<string, CombatStrategyMetadata>
        {
            [first.Strategy.Path] = new("钟离 · 万叶 · 班尼特 · 香菱", null),
            [second.Strategy!.Path] = new("那维莱特 · 芙宁娜 · 万叶 · 白术", null),
            [broken.Strategy!.Path] = new("第 3 行：未知指令 aim", "未知指令 aim"),
        });
        var panel = new CombatStrategyPickerPanel { DataContext = vm };
        var chrome = new Border
        {
            Child = panel, Width = 640, Height = 520, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            BorderBrush = (Brush)Application.Current.FindResource("SurfaceStrokeColorDefaultBrush"),
            Background = (Brush)Application.Current.FindResource("SolidBackgroundFillColorSecondaryBrush"),
        };
        var errors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        try
        {
            // 初始化只更新显示，不可把旧路径自动规范化并写回宿主。
            vm.SelectedCategory = browser;
            Layout(picker, 288, 36);
            var trigger = Assert.IsType<ToggleButton>(picker.FindName("Trigger"));
            Assert.Equal(288, trigger.ActualWidth);
            var popup = Assert.IsType<Popup>(picker.FindName("StrategyPopup"));
            Assert.Same(vm, ((FrameworkElement)popup.Child).DataContext);
            Layout(chrome, 640, 520);
            var content = Assert.IsType<ContentControl>(panel.FindName("CategoryContent"));
            Assert.Equal(HorizontalAlignment.Stretch, content.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Stretch, content.VerticalContentAlignment);
            Assert.True(content.ActualWidth > 400);
            var view = Find<StrategyBrowserView>(panel);
            Assert.NotNull(view);
            var list = Assert.IsType<ListBox>(view!.FindName("EntryList"));
            Assert.Equal(5, list.Items.Count);
            Assert.True(list.ActualWidth > 390);
            var footerButton = Find<Wpf.Ui.Controls.Button>(panel);
            Assert.NotNull(footerButton);
            Assert.Equal(typeof(Wpf.Ui.Controls.Button), footerButton!.Template.TargetType);
            Assert.Equal(first.Strategy.Path, host.Strategy);
            Assert.NotNull(BindingOperations.GetBindingExpression(picker, CombatStrategyPicker.StrategyProperty));
            SavePreview(chrome, theme + "-manual");
            SavePreview(picker, theme + "-trigger-manual");

            // 未完成的搜索必须被立即清空，不能在 150 ms 后覆盖新筛选。
            vm.SearchText = "旧版";
            vm.ClearSearchCommand.Execute(null);
            Assert.False(browser.IsSearching);
            Assert.False(vm.HasSearchText);
            Assert.Equal(5, browser.Entries.Count);

            // 实际选择通过 SetCurrentValue 写回，宿主原有 Binding 保持有效。
            browser.Enter(browser.Entries.Single(e => e.Node == second));
            Assert.Equal(second.Strategy.Value, host.Strategy);
            Assert.NotNull(BindingOperations.GetBindingExpression(picker, CombatStrategyPicker.StrategyProperty));
            host.Strategy = first.Strategy.Path;
            Assert.Equal(first.Strategy.Path, picker.Strategy);

            foreach (var category in vm.Categories)
            {
                vm.SelectedCategory = category;
                if (category is AutoMatchCategoryViewModel auto)
                {
                    // 外部配置切换为自动模式后，范围勾选与触发标签必须同步。
                    host.Strategy = CombatStrategyResolver.AutoSelectName;
                    Assert.True(vm.IsAutomatic);
                    Assert.True(auto.Scopes[0].IsCurrent);
                    auto.ErrorSummary = "3 个文本策略解析失败，匹配时会被跳过";
                    Layout(picker, 288, 36);
                    SavePreview(picker, theme + "-trigger-auto");
                }
                Layout(chrome, 640, 520);
                if (category is AutoMatchCategoryViewModel) SavePreview(chrome, theme + "-auto");
                if (category.Kind == CombatStrategyKind.KeyMouse) SavePreview(chrome, theme + "-keymouse");
            }
            vm.SelectedCategory = vm.Categories.OfType<AutoMatchCategoryViewModel>().Single();
            ((AutoMatchCategoryViewModel)vm.SelectedCategory).ShowErrorsCommand.Execute(null);
            Assert.Same(browser, vm.SelectedCategory);
            Assert.Equal(broken, Assert.Single(browser.Entries).Node);
            vm.EscapeCommand.Execute(null);
            Assert.False(browser.IsSearching);

            vm.SetCurrent("不存在的旧策略");
            Layout(picker, 288, 36);
            Assert.True(vm.HasWarning);
            SavePreview(picker, theme + "-trigger-missing");
            // 所有主题资源必须可解析，不能静默退化为默认 WPF 控件样式。
            Assert.NotNull(list.ItemContainerStyle.BasedOn);
            Assert.Empty(errors.Messages);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
            vm.Detach();
        }
    }

    /// <summary>构造文件节点，真实文件只用于存在性解析，元数据由快照注入。</summary>
    private static CombatStrategyNode StrategyFile(string path, string relative) => new()
    {
        Name = Path.GetFileNameWithoutExtension(path), RelativePath = relative, Strategy = CombatStrategyResolver.Parse(path), FileCount = 1,
    };

    /// <summary>构造带子树文件数的目录节点。</summary>
    private static CombatStrategyNode Folder(string name, string relative, params CombatStrategyNode[] children) => new()
    {
        Name = name, RelativePath = relative, IsFolder = true, Children = children, FileCount = children.Sum(c => c.FileCount),
    };

    /// <summary>执行实际模板布局，并处理绑定和渲染队列。</summary>
    private static void Layout(FrameworkElement view, double width, double height)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        view.UpdateLayout();
    }

    /// <summary>在生成的可视树中查找类别视图。</summary>
    private static T? Find<T>(DependencyObject element) where T : DependencyObject
    {
        if (element is T found) return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (Find<T>(VisualTreeHelper.GetChild(element, i)) is { } child) return child;
        return null;
    }

    /// <summary>仅在显式指定预览目录时输出真实 WPF 渲染图。</summary>
    private static void SavePreview(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("BETTERGI_STRATEGY_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        // 渲染不显示窗口；为半透明触发区补上应用背景。
        var backdrop = new DrawingVisual();
        using (var context = backdrop.RenderOpen())
            context.DrawRectangle((Brush)Application.Current.FindResource("SolidBackgroundFillColorSecondaryBrush"), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        bitmap.Render(backdrop);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    /// <summary>关闭或卸载时必须取消尚未执行的焦点回调，避免访问已解除挂接的弹层。</summary>
    private static void VerifyPendingFocusIsCancelled()
    {
        var popup = new TestPopup { Child = new Border(), PlacementTarget = new Button() };
        var behavior = new PopupScrollIsolationBehavior();
        behavior.Attach(popup);
        popup.NotifyOpened();
        behavior.Detach();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    /// <summary>在独立 STA 线程加载 WPF 控件，并将原始异常传回测试线程。</summary>
    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "策略类别视图加载超时");
        failure?.Throw();
    }

    /// <summary>只触发生命周期事件，不创建原生弹出窗口。</summary>
    private sealed class TestPopup : Popup
    {
        /// <summary>模拟打开后排队的焦点操作。</summary>
        public void NotifyOpened() => OnOpened(EventArgs.Empty);
    }

    /// <summary>记录真实布局过程中的绑定错误。</summary>
    private sealed class BindingErrors : TraceListener
    {
        /// <summary>捕获的错误文本。</summary>
        public List<string> Messages { get; } = [];
        /// <summary>记录非空诊断输出。</summary>
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); }
        /// <summary>记录非空诊断行。</summary>
        public override void WriteLine(string? message) => Write(message);
    }

    /// <summary>模拟宿主配置，验证选择器保留外部 DataContext 和双向绑定。</summary>
    private sealed class HostState : ObservableObject
    {
        /// <summary>宿主保存的策略值。</summary>
        private string? _strategy;
        /// <summary>可通知变化的宿主策略配置。</summary>
        public string? Strategy { get => _strategy; set => SetProperty(ref _strategy, value); }
    }
}
