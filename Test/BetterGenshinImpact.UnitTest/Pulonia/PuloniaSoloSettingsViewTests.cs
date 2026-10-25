using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using BetterGenshinImpact.UnitTest.ViewModelTests.ResinTests;
using Newtonsoft.Json.Linq;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>专属设置的真实隐式模板、资源与离屏布局验收，无需运行主程序。</summary>
public sealed class PuloniaSoloSettingsViewTests
{
    /// <summary>逐一实例化全部专属设置页，在深浅主题和窄宽布局下展开所有设置并检查绑定。</summary>
    [Fact]
    public void AllDedicatedSettings_LoadAndLayoutWithBothThemes()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
                app.Resources.MergedDictionaries.Add(new ControlsDictionary());
                var vioDictionaryType = typeof(Wpf.Ui.Violeta.Controls.CascadingComboBox).Assembly.GetTypes()
                    .Single(type => type.Name == "ControlsDictionary");
                app.Resources.MergedDictionaries.Add((ResourceDictionary)Activator.CreateInstance(vioDictionaryType)!);
                app.Resources["TextThemeFontFamily"] = new FontFamily("Microsoft YaHei UI");
                app.Resources["ControlCornerRadius"] = new CornerRadius(6);
                var registry = new SoloTaskRegistry([typeof(SoloTaskRegistry).Assembly]);
                SoloTaskSettingsTemplates.Register(app.Resources, registry);
                var trace = new SettingsBindingErrors();
                PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
                try
                {
                    foreach (var theme in new[] { ApplicationTheme.Dark, ApplicationTheme.Light })
                    {
                        app.Resources.MergedDictionaries[0] = new ThemesDictionary { Theme = theme };
                        foreach (var descriptor in registry.All.Where(item => item.SettingsViewModelType is not null))
                        {
                            // 与实际 ContentControl 相同，通过 VM 的隐式模板实例化子视图。
                            var vm = (SoloTaskSettingsViewModel)Activator.CreateInstance(descriptor.SettingsViewModelType!,
                                descriptor.TaskId == "auto_boss" ? [new JObject(), null] : [new JObject()])!;
                            if (vm is AutoDomainSettingsViewModel domain) { domain.WeeklyEnabled = true; domain.AutoArtifactSalvage = true; }
                            if (vm is AutoStygianOnslaughtSettingsViewModel stygian) { stygian.AutoArtifactSalvage = true; }
                            var template = (DataTemplate)app.FindResource(new DataTemplateKey(vm.GetType()));
                            var view = Assert.IsAssignableFrom<FrameworkElement>(template.LoadContent());
                            view.DataContext = vm;
                            Assert.Equal(descriptor.SettingsViewType, view.GetType());
                            foreach (var width in new[] { 560d, 760d })
                            {
                                // 多轮布局让每周 ItemsControl 的子模板也实际构造并展开。
                                for (var round = 0; round < 4; round++)
                                {
                                    Layout(view, width);
                                    foreach (var expander in Descendants(view).OfType<Wpf.Ui.Controls.CardExpander>())
                                        expander.IsExpanded = true;
                                }
                                WaitForAnimations();
                                Layout(view, width);
                                Assert.Equal(width, view.ActualWidth);
                                Assert.True(view.ActualHeight > 40);
                                foreach (var control in Descendants(view).OfType<Control>().Where(control =>
                                             control.ActualWidth > 0 && control is ComboBox or Wpf.Ui.Controls.NumberBox))
                                {
                                    Assert.True(control.ActualWidth > 0);
                                    var bounds = control.TransformToAncestor(view).TransformBounds(new Rect(control.RenderSize));
                                    Assert.True(bounds.Right <= width + 1, $"{descriptor.TaskId}: {control.GetType().Name} 超出设置区 {bounds}");
                                }
                            }
                            SavePreview(view, descriptor.TaskId + "-" + theme);
                        }
                        // 树脂内容共用本测试的唯一 Application、STA、主题资源和绑定错误监听器，不额外创建窗口。
                        ResinSettingsViewAssertions.VerifyCurrentTheme();
                    }
                    Assert.Empty(trace.Errors);
                }
                finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(trace); }
            }
            catch (Exception ex) { error = ex; }
            finally { app.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>让 WPF-UI 的展开动画完成，避免截取动画中途的裁剪状态。</summary>
    private static void WaitForAnimations()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>执行真实布局并处理绑定队列，不显示窗口。</summary>
    private static void Layout(FrameworkElement view, double width)
    {
        view.Measure(new Size(width, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
        view.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        view.UpdateLayout();
    }

    /// <summary>递归枚举可视元素，覆盖 ItemsControl 生成的单日模板。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    /// <summary>按显式环境变量输出完整离屏预览，供人工核对主题、间距和控件位置。</summary>
    private static void SavePreview(FrameworkElement view, string name)
    {
        var directory = Environment.GetEnvironmentVariable("BGI_SOLO_SETTINGS_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        // 绘制背景而不重新挂接控件，避免重新触发 Loaded 和展开动画。
        var surface = new DrawingVisual();
        using (var drawing = surface.RenderOpen())
        {
            var bounds = new Rect(0, 0, view.ActualWidth, view.ActualHeight);
            drawing.DrawRectangle((Brush)Application.Current.FindResource("ApplicationBackgroundBrush"), null, bounds);
            drawing.DrawRectangle(new VisualBrush(view), null, bounds);
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }

    /// <summary>收集绑定错误，防止只读属性双向绑定或模板路径错误被布局静默忽略。</summary>
    private sealed class SettingsBindingErrors : TraceListener
    {
        /// <summary>绑定错误文本。</summary>
        public List<string> Errors { get; } = [];
        /// <inheritdoc />
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        /// <inheritdoc />
        public override void WriteLine(string? message) => Write(message);
    }
}
