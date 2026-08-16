using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Windows;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 协调开发者快捷键截图与 Recognition 模板编辑窗口的生命周期。
/// </summary>
public sealed class RecognitionTemplateEditorService
{
    private readonly RecognitionTemplateAssetService _assetService;
    private readonly IConfigService _configService;
    private readonly ILogger<RecognitionTemplateEditorService> _logger;
    private RecognitionTemplateEditorWindow? _window;
    private int _isOpening;

    public RecognitionTemplateEditorService(
        RecognitionTemplateAssetService assetService,
        IConfigService configService,
        ILogger<RecognitionTemplateEditorService> logger)
    {
        _assetService = assetService;
        _configService = configService;
        _logger = logger;
    }

    public async Task OpenAsync()
    {
        if (Interlocked.Exchange(ref _isOpening, 1) != 0)
        {
            return;
        }

        Mat? screenshot = null;
        try
        {
            var dispatcher = Application.Current?.Dispatcher
                             ?? throw new InvalidOperationException("WPF Dispatcher 尚未初始化。");
            var activatedExistingWindow = await dispatcher.InvokeAsync(() =>
            {
                if (_window is not { IsVisible: true })
                {
                    return false;
                }

                if (_window.WindowState == WindowState.Minimized)
                {
                    _window.WindowState = WindowState.Normal;
                }

                _window.Activate();
                return true;
            });
            if (activatedExistingWindow)
            {
                return;
            }

            screenshot = await Task.Run(CaptureRecognitionCanvas);
            var capturedScreenshot = screenshot ?? throw new InvalidOperationException("未获取到有效的游戏截图。");
            await dispatcher.InvokeAsync(() =>
            {
                var viewModel = new RecognitionTemplateEditorViewModel(capturedScreenshot, _assetService, _configService);
                screenshot = null; // 所有权已经转移给 ViewModel。
                RecognitionTemplateEditorWindow? window = null;
                try
                {
                    window = new RecognitionTemplateEditorWindow(viewModel);
                    if (Application.Current.MainWindow is { IsVisible: true, WindowState: not WindowState.Minimized } owner)
                    {
                        window.Owner = owner;
                    }
                    else
                    {
                        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    }

                    window.Closed += OnWindowClosed;
                    _window = window;
                    window.Show();
                    window.Activate();
                }
                catch
                {
                    if (window != null)
                    {
                        window.Closed -= OnWindowClosed;
                    }

                    _window = null;
                    viewModel.Dispose();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开 Recognition 模板素材制作窗口失败");
            await ShowCaptureErrorAsync(ex.Message);
        }
        finally
        {
            screenshot?.Dispose();
            Interlocked.Exchange(ref _isOpening, 0);
        }
    }

    private static Mat CaptureRecognitionCanvas()
    {
        using var captureRegion = TaskControl.CaptureToRectArea();
        if (captureRegion.SrcMat.Empty())
        {
            throw new InvalidOperationException("未获取到有效的游戏截图。");
        }

        return captureRegion.SrcMat.Clone();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is RecognitionTemplateEditorWindow window)
        {
            window.Closed -= OnWindowClosed;
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        }
    }

    private static async Task ShowCaptureErrorAsync(string detail)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            ThemedMessageBox.Show(
                $"无法截取当前游戏画面并打开模板制作工具。{Environment.NewLine}{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}请先在启动页启动截图器，并确认游戏窗口未最小化。",
                "模板素材制作",
                MessageBoxButton.OK,
                ThemedMessageBox.MessageBoxIcon.Warning,
                MessageBoxResult.OK,
                Application.Current.MainWindow);
        });
    }
}
