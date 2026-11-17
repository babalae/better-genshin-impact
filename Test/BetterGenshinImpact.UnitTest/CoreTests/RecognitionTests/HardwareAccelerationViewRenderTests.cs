using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Pages.View;
using BetterGenshinImpact.ViewModel.Pages.View;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

/// <summary>
///     硬件加速设置页的离屏渲染测试：真实 WPF 视图 + ViewModel（fake 依赖），
///     三种后端状态（ONNX / OpenVINO / ncnn）各渲染一张 PNG，同时捕获 WPF 绑定错误。
///     绑定错误（路径写错等编译期查不出的问题）会导致对应 UI 元素静默空白，这里必须为零。
/// </summary>
[Collection("WpfRendering")]
public class HardwareAccelerationViewRenderTests : IDisposable
{
    private readonly BindingErrorListener _listener = new();

    public void Dispose() => _listener.Dispose();

    [Fact]
    public async Task SettingsView_RendersThreeBackendStates_WithoutBindingErrors()
    {
        var artifacts = Path.Combine(AppContext.BaseDirectory, "TestArtifacts");
        Directory.CreateDirectory(artifacts);

        Exception? failure = null;
        var rendered = new List<string>();
        var thread = new Thread(() =>
        {
            try
            {
                // pack://application URI 相对主程序集解析（TitleBar 图标等资源）。
                // 测试宿主没有 Application：创建一个并指向 BetterGI 主程序集，
                // 否则 logo.png 等打包资源按测试程序集解析而失败。
                // 必须先设 ResourceAssembly 再建 Application（Application 构造会占用一次设置机会）
                if (System.Windows.Application.ResourceAssembly == null)
                    System.Windows.Application.ResourceAssembly = typeof(BgiOnnxFactory).Assembly;
                if (System.Windows.Application.Current == null)
                {
                    var app = new System.Windows.Application();
                    // 硬件加速页依赖 App.xaml 级别的资源：转换器与字体图标样式。
                    // DynamicResource（主题刷子等）在无主题时按缺省渲染，不影响结构验证。
                    app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/BetterGI;component/View/Controls/WpfUi/FaFontIconStyle.xaml")
                    });
                    app.Resources["BooleanToVisibilityConverter"] = new System.Windows.Controls.BooleanToVisibilityConverter();
                    // 最小暗色主题：设置页用到的 DynamicResource 刷子逐个给出近似值（真实主题由应用启动时注入，
                    // 见 MainWindowViewModel 的 ApplicationThemeManager.Apply；这里只为离屏渲染提供可读配色）。
                    foreach (var (key, color) in new[]
                             {
                                 ("ApplicationBackgroundBrush", FromHex("#FF202020")),
                                 ("CardBackgroundFillColorDefaultBrush", FromHex("#FF2D2D2D")),
                                 ("CardBackgroundFillColorSecondaryBrush", FromHex("#FF333333")),
                                 ("ControlFillColorSecondaryBrush", FromHex("#FF3A3A3A")),
                                 ("ControlStrokeColorDefaultBrush", FromHex("#FF4A4A4A")),
                                 ("SystemAccentColorPrimaryBrush", FromHex("#FF60A5FA")),
                                 ("SystemFillColorCautionBrush", FromHex("#FFF5C518")),
                                 ("SystemFillColorCriticalBrush", FromHex("#FFE05252")),
                                 ("SystemFillColorSuccessBrush", FromHex("#FF5CD08A")),
                                 ("TextFillColorPrimaryBrush", FromHex("#FFFFFFFF")),
                                 ("TextFillColorSecondaryBrush", FromHex("#FFCFCFCF")),
                                 ("TextFillColorTertiaryBrush", FromHex("#FF9A9A9A")),
                             })
                    {
                        var brush = new SolidColorBrush(color);
                        brush.Freeze();
                        app.Resources[key] = brush;
                    }
                }
                var config = new HardwareAccelerationConfig();
                var store = new TestConfigService(config);
                InferenceRuntimeService.PrepareStartup(config);
                using var runtime = new InferenceRuntimeService(store, new FakeRuntime(InferenceDeviceType.Cpu),
                    new FakeDevices(), new FakeDiagnostics(), NullLogger<BgiOnnxFactory>.Instance);
                runtime.InitializeAsync().GetAwaiter().GetResult();

                foreach (var (backend, name) in new[]
                         {
                             (InferenceBackend.Onnx, "backend-onnx"),
                             (InferenceBackend.OpenVino, "backend-openvino"),
                             (InferenceBackend.Ncnn, "backend-ncnn"),
                         })
                {
                    using var vm = CreateViewModel(store, runtime);
                    vm.SelectedBackendDraft = backend;
                    var view = new HardwareAccelerationView(vm);
                    RenderToFile(view, 1080, 860, Path.Combine(artifacts, $"hw-accel-{name}.png"));
                    rendered.Add(name);
                }
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(120));

        Assert.Null(failure);
        Assert.Equal(3, rendered.Count);
        // 渲染全程不允许出现 WPF 绑定错误（System.Windows.Data Error）
        Assert.True(_listener.Errors.Count == 0,
            "发现 WPF 绑定错误：\r\n" + string.Join("\r\n", _listener.Errors.Take(10)));
    }

    private static HardwareAccelerationViewModel CreateViewModel(TestConfigService store, InferenceRuntimeService runtime)
        => new(runtime, new FakeInstaller(), new FakeDevices(), new FakeDiagnostics(),
            new BgiOnnxFactory(NullLogger<BgiOnnxFactory>.Instance, runtime), store,
            new FakeLeases(), new FakeInteraction(), new FakeOpenVinoInstaller());

    /// <summary>强制布局 + 渲染为 PNG（尺寸取实际期望大小）。</summary>
    private static void RenderToFile(FrameworkElement element, int width, int height, string path)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.ApplyTemplate();
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Assert.True(new FileInfo(path).Length > 10_000, $"渲染结果过小，疑似空白: {path}");
    }

    private sealed class BindingErrorListener : TraceListener, IDisposable
    {
        public List<string> Errors { get; } = [];

        public BindingErrorListener()
        {
            PresentationTraceSources.DataBindingSource.Listeners.Add(this);
            // Refresh 使已存在的绑定表达式重新套用监听器配置；Error 级别覆盖
            // 「BindingExpression path error」等常见绑定路径问题。
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        }

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (message != null && message.Contains("BindingExpression", StringComparison.OrdinalIgnoreCase))
                Errors.Add(message);
        }

        public void Dispose()
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(this);
            PresentationTraceSources.Refresh();
        }
    }

    private static System.Windows.Media.Color FromHex(string hex)
        => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

    private sealed class TestConfigService(HardwareAccelerationConfig config) : IConfigService
    {
        private readonly AllConfig _all = new() { HardwareAccelerationConfig = config };
        public AllConfig Get() => _all;
        public AllConfig Read() => _all;
        public void Save() { }
        public void Flush() { }
        public void Write(AllConfig value) { }
    }

    private sealed class FakeRuntime(InferenceDeviceType engine) : IOnnxRuntimeSelection
    {
        public InferenceDeviceType InferenceDevice => engine;
        public string RuntimeDirectory => "test-runtime";
        public string StatusText => "test";
        public string FailureReason => "";
    }

    private sealed class FakeDevices : IInferenceDeviceDiscovery
    {
        public System.Collections.Generic.IReadOnlyList<InferenceDeviceInfo> GetDevices(InferenceDeviceType engine, string directory)
            => [ResolvedInferenceRuntime.CpuDevice];
    }

    private sealed class FakeDiagnostics : IInferenceModelDiagnostics
    {
        public bool HasDomainTreeImage => false;
        public Task ValidateTreeAsync(BgiOnnxSessionFactory factory, CancellationToken token) => Task.CompletedTask;
        public Task ValidateOcrAsync(BgiOnnxSessionFactory factory, CancellationToken token) => Task.CompletedTask;
        public Task PreheatOcrAsync() => Task.CompletedTask;
        public Task<InferenceBenchmarkResult> BenchmarkAsync(InferenceBenchmarkScene scene, BgiOnnxSessionFactory factory,
            IProgress<InferenceBenchmarkProgress>? progress, CancellationToken token)
            => Task.FromResult(new InferenceBenchmarkResult("OCR", "test", [1d]));
    }

    private sealed class FakeInstaller : IOnnxRuntimeInstaller
    {
        public string PluginsDirectory => "test-plugins";
        public RuntimeInstallation Inspect(InferenceDeviceType engine)
            => new(InferenceInstallationState.Installed, "test", "内置");
        public Task<IReadOnlyList<RuntimeDownloadPackage>> GetDownloadPlanAsync(InferenceDeviceType engine, bool dependencies, CancellationToken token)
            => Task.FromResult<IReadOnlyList<RuntimeDownloadPackage>>([]);
        public Task InstallAsync(InferenceDeviceType engine, IReadOnlyList<RuntimeDownloadPackage> packages,
            IProgress<RuntimeDownloadProgress>? progress, CancellationToken token) => Task.CompletedTask;
        public Task CleanupInstalledDownloadsAsync(CancellationToken token) => Task.CompletedTask;
    }

    private sealed class FakeLeases : IInferenceBenchmarkLeaseProvider
    {
        public bool IsAvailable => true;
        public IDisposable Acquire() => new Disposable();
        private sealed class Disposable : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeInteraction : IInferenceSettingsInteraction
    {
        public Task<bool> ConfirmDownloadAsync(IReadOnlyList<RuntimeDownloadPackage> packages, bool dependencies) => Task.FromResult(false);
        public Task<bool> ConfirmRestartAsync(string pending, string validationHint, string currentEngine, bool interruptsTask) => Task.FromResult(false);
        public void Restart() { }
    }
}
