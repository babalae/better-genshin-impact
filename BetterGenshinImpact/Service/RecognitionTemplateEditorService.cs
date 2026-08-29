using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Windows;
using Microsoft.Extensions.Logging;
using Ookii.Dialogs.Wpf;
using OpenCvSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 协调游戏截图、本地图片与 Recognition 模板编辑窗口的生命周期。
/// </summary>
public sealed class RecognitionTemplateEditorService
{
    private sealed record EditorSource(Mat Image, string InitialTemplateName);

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

    public Task OpenAsync()
    {
        return OpenCoreAsync(
            () => Task.Run<EditorSource?>(() => new EditorSource(CaptureRecognitionCanvas(), "")),
            "无法截取当前游戏画面并打开模板制作工具。",
            "请先在启动页启动截图器，并确认游戏窗口未最小化。");
    }

    public Task OpenFromImageAsync()
    {
        return OpenCoreAsync(
            SelectLocalImageAsync,
            "无法读取所选图片并打开模板制作工具。",
            "请选择有效的 PNG、JPEG、BMP 或 WebP 图片。");
    }

    private async Task OpenCoreAsync(
        Func<Task<EditorSource?>> sourceFactory,
        string failureMessage,
        string guidance)
    {
        if (Interlocked.Exchange(ref _isOpening, 1) != 0)
        {
            return;
        }

        Mat? sourceImage = null;
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

            var source = await sourceFactory();
            if (source == null)
            {
                return;
            }

            var editorImage = source.Image;
            sourceImage = editorImage;
            if (editorImage.Empty())
            {
                throw new InvalidOperationException("未获取到有效图片。");
            }

            await dispatcher.InvokeAsync(() =>
            {
                var viewModel = new RecognitionTemplateEditorViewModel(
                    editorImage,
                    _assetService,
                    _configService,
                    source.InitialTemplateName);
                sourceImage = null; // 所有权已经转移给 ViewModel。
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
            await ShowOpenErrorAsync(failureMessage, ex.Message, guidance);
        }
        finally
        {
            sourceImage?.Dispose();
            Interlocked.Exchange(ref _isOpening, 0);
        }
    }

    private static async Task<EditorSource?> SelectLocalImageAsync()
    {
        var dispatcher = Application.Current?.Dispatcher
                         ?? throw new InvalidOperationException("WPF Dispatcher 尚未初始化。");
        var imagePath = await dispatcher.InvokeAsync(() =>
        {
            var dialog = new VistaOpenFileDialog
            {
                Title = "选择模板制作参考图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|PNG 图片|*.png|所有文件|*.*",
                CheckFileExists = true
            };
            return dialog.ShowDialog(Application.Current.MainWindow) == true
                ? dialog.FileName
                : null;
        });
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var image = await Task.Run(() => LoadLocalImage(imagePath));
        return new EditorSource(image, Path.GetFileNameWithoutExtension(imagePath));
    }

    private static Mat LoadLocalImage(string imagePath)
    {
        using var stream = File.OpenRead(imagePath);
        var image = Mat.FromStream(stream, ImreadModes.Color);
        if (image.Empty())
        {
            image.Dispose();
            throw new InvalidOperationException("所选文件不是可读取的图片。");
        }

        return image;
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

    private static async Task ShowOpenErrorAsync(string failureMessage, string detail, string guidance)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            ThemedMessageBox.Show(
                $"{failureMessage}{Environment.NewLine}{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}{guidance}",
                "模板素材制作",
                MessageBoxButton.OK,
                ThemedMessageBox.MessageBoxIcon.Warning,
                MessageBoxResult.OK,
                Application.Current.MainWindow);
        });
    }
}
