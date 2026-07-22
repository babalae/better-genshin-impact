using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfPixelFormats = System.Windows.Media.PixelFormats;

namespace BetterGenshinImpact.View.Controls.Markdown;

public interface IMarkdownImageLoader
{
    Uri? Resolve(string rawSource, string? basePath);

    Task<MarkdownImageResult> LoadAsync(Uri source, CancellationToken cancellationToken);
}

public sealed class MarkdownImageLoader : IMarkdownImageLoader
{
    private const int MaxDownloadBytes = 20 * 1024 * 1024;
    private const long MaxPixelCount = 40_000_000;

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly ConcurrentDictionary<string, WeakReference<MarkdownImageResult>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static MarkdownImageLoader Default { get; } = new();

    public Uri? Resolve(string rawSource, string? basePath)
    {
        if (string.IsNullOrWhiteSpace(rawSource))
        {
            return null;
        }

        var source = rawSource.Trim();
        try
        {
            if (Path.IsPathRooted(source))
            {
                return new Uri(Path.GetFullPath(source), UriKind.Absolute);
            }

            if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri))
            {
                return absoluteUri;
            }

            if (string.IsNullOrWhiteSpace(basePath))
            {
                return null;
            }

            return new Uri(Path.GetFullPath(Path.Combine(basePath, source)), UriKind.Absolute);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<MarkdownImageResult> LoadAsync(Uri source, CancellationToken cancellationToken)
    {
        var cacheKey = GetCacheKey(source);
        if (_cache.TryGetValue(cacheKey, out var weakReference) && weakReference.TryGetTarget(out var cachedResult))
        {
            return cachedResult;
        }

        byte[] bytes;
        if (source.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            bytes = DecodeDataUri(source.OriginalString);
        }
        else if (source.IsFile)
        {
            var fileInfo = new FileInfo(source.LocalPath);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException("Markdown 图片不存在。", source.LocalPath);
            }

            if (fileInfo.Length > MaxDownloadBytes)
            {
                throw new InvalidDataException($"图片大小超过 {MaxDownloadBytes / 1024 / 1024} MB 限制。");
            }

            bytes = await File.ReadAllBytesAsync(fileInfo.FullName, cancellationToken);
        }
        else if (source.Scheme.Equals("pack", StringComparison.OrdinalIgnoreCase))
        {
            var resourceInfo = Application.GetResourceStream(source)
                               ?? throw new FileNotFoundException("找不到 Markdown pack 资源。", source.ToString());
            await using var resourceStream = resourceInfo.Stream;
            bytes = await ReadLimitedAsync(resourceStream, cancellationToken);
        }
        else if (source.Scheme is "http" or "https")
        {
            using var response = await HttpClient.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxDownloadBytes)
            {
                throw new InvalidDataException($"图片大小超过 {MaxDownloadBytes / 1024 / 1024} MB 限制。");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            bytes = await ReadLimitedAsync(responseStream, cancellationToken);
        }
        else
        {
            throw new NotSupportedException($"不支持的图片协议：{source.Scheme}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = DecodeImage(bytes);
        _cache[cacheKey] = new WeakReference<MarkdownImageResult>(result);
        return result;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseCookies = false,
            UseDefaultCredentials = false
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BetterGI-Markdown", "1.0"));
        return client;
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaxDownloadBytes)
            {
                throw new InvalidDataException($"图片大小超过 {MaxDownloadBytes / 1024 / 1024} MB 限制。");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    private static byte[] DecodeDataUri(string dataUri)
    {
        var commaIndex = dataUri.IndexOf(',');
        if (commaIndex < 0)
        {
            throw new InvalidDataException("无效的图片 data URI。");
        }

        var metadata = dataUri[..commaIndex];
        var data = dataUri[(commaIndex + 1)..];
        var bytes = metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(data)
            : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data));

        if (bytes.Length > MaxDownloadBytes)
        {
            throw new InvalidDataException($"图片大小超过 {MaxDownloadBytes / 1024 / 1024} MB 限制。");
        }

        return bytes;
    }

    private static MarkdownImageResult DecodeImage(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            ValidatePixelCount(frame.PixelWidth, frame.PixelHeight);
            if (frame.CanFreeze)
            {
                frame.Freeze();
            }

            return new MarkdownImageResult(frame, frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception wpfException) when (wpfException is NotSupportedException or FileFormatException)
        {
            try
            {
                using var image = SixLabors.ImageSharp.Image.Load<Bgra32>(bytes);
                ValidatePixelCount(image.Width, image.Height);
                var pixels = new byte[checked(image.Width * image.Height * 4)];
                image.CopyPixelDataTo(pixels);
                var bitmapSource = BitmapSource.Create(
                    image.Width,
                    image.Height,
                    96,
                    96,
                    WpfPixelFormats.Bgra32,
                    null,
                    pixels,
                    image.Width * 4);
                bitmapSource.Freeze();
                return new MarkdownImageResult(bitmapSource, image.Width, image.Height);
            }
            catch (Exception imageSharpException)
            {
                throw new InvalidDataException("无法解码 Markdown 图片。", imageSharpException);
            }
        }
    }

    private static void ValidatePixelCount(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixelCount)
        {
            throw new InvalidDataException("图片像素尺寸无效或超过安全限制。");
        }
    }

    private static string GetCacheKey(Uri source)
    {
        if (!source.IsFile)
        {
            return source.AbsoluteUri;
        }

        var fileInfo = new FileInfo(source.LocalPath);
        return fileInfo.Exists
            ? $"{fileInfo.FullName}|{fileInfo.LastWriteTimeUtc.Ticks}|{fileInfo.Length}"
            : fileInfo.FullName;
    }
}

internal sealed class MarkdownImagePresenter : Grid
{
    private readonly MarkdownImageRequest _request;
    private readonly IMarkdownImageLoader _imageLoader;
    private readonly MarkdownView _owner;
    private readonly CancellationToken _renderCancellationToken;
    private readonly Border _placeholder;
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _statusText;
    private readonly TextBlock _sourceText;
    private readonly Button _retryButton;
    private CancellationTokenSource? _loadCancellationTokenSource;
    private bool _isLoading;
    private bool _isImageLoaded;

    public MarkdownImagePresenter(
        MarkdownImageRequest request,
        IMarkdownImageLoader imageLoader,
        MarkdownView owner,
        CancellationToken renderCancellationToken)
    {
        _request = request;
        _imageLoader = imageLoader;
        _owner = owner;
        _renderCancellationToken = renderCancellationToken;

        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        MaxWidth = request.IsInline ? 320 : 960;
        MaxHeight = request.IsInline ? 64 : 720;
        Margin = request.IsInline ? new Thickness(2, 0, 2, 0) : new Thickness(0, 6, 0, 10);
        Cursor = owner.ImageCommand is null ? Cursors.Arrow : Cursors.Hand;
        ToolTip = request.Title ?? GetSourceDisplayText();
        AutomationProperties.SetName(this, string.IsNullOrWhiteSpace(request.AlternativeText)
            ? "Markdown 图片"
            : request.AlternativeText);

        _placeholder = new Border
        {
            MinWidth = request.IsInline ? 96 : 240,
            MinHeight = request.IsInline ? 28 : 112,
            Padding = request.IsInline ? new Thickness(8, 5, 8, 5) : new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(6)
        };
        _placeholder.SetResourceReference(Border.BackgroundProperty, "MarkdownImagePlaceholderBrush");
        _placeholder.SetResourceReference(Border.BorderBrushProperty, "MarkdownBorderBrush");
        _placeholder.BorderThickness = new Thickness(1);

        var contentPanel = new StackPanel
        {
            Orientation = request.IsInline ? Orientation.Horizontal : Orientation.Vertical,
            HorizontalAlignment = request.IsInline ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _progressBar = new ProgressBar
        {
            IsIndeterminate = true,
            IsHitTestVisible = false,
            Width = request.IsInline ? 36 : 160,
            Height = request.IsInline ? 4 : 5,
            Margin = request.IsInline ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 0, 10)
        };
        AutomationProperties.SetName(_progressBar, "图片加载进度");
        contentPanel.Children.Add(_progressBar);

        _statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = request.IsInline ? 240 : 680,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "MarkdownSecondaryForegroundBrush");
        contentPanel.Children.Add(_statusText);

        _sourceText = new TextBlock
        {
            Text = GetSourceDisplayText(),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 680,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = request.IsInline ? Visibility.Collapsed : Visibility.Visible,
            ToolTip = GetSourceDisplayText()
        };
        _sourceText.SetResourceReference(TextBlock.ForegroundProperty, "MarkdownSecondaryForegroundBrush");
        contentPanel.Children.Add(_sourceText);

        _retryButton = new Button
        {
            Content = "重新加载",
            MinWidth = 88,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = request.IsInline ? new Thickness(8, 0, 0, 0) : new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(_retryButton, "重新加载图片");
        _retryButton.Click += OnRetryButtonClick;
        contentPanel.Children.Add(_retryButton);

        _placeholder.Child = contentPanel;
        ShowLoadingState();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ = StartLoadAsync();
    }

    private async Task StartLoadAsync()
    {
        if (_isLoading || _isImageLoaded || !IsLoaded)
        {
            return;
        }

        _isLoading = true;
        ShowLoadingState();
        var loadCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_renderCancellationToken);
        _loadCancellationTokenSource = loadCancellationTokenSource;
        try
        {
            var result = await _imageLoader.LoadAsync(_request.Source, loadCancellationTokenSource.Token);
            loadCancellationTokenSource.Token.ThrowIfCancellationRequested();

            var image = new Image
            {
                Source = result.ImageSource,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(image, _request.AlternativeText);
            SetContent(image);
            _isImageLoaded = true;
        }
        catch (OperationCanceledException) when (loadCancellationTokenSource.IsCancellationRequested
                                                  || _renderCancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException ex)
        {
            ShowFailureState("连接超时（15 秒），请检查网络后重试。", ex);
        }
        catch (Exception ex)
        {
            ShowFailureState(GetFriendlyErrorMessage(ex), ex);
        }
        finally
        {
            if (ReferenceEquals(_loadCancellationTokenSource, loadCancellationTokenSource))
            {
                _loadCancellationTokenSource = null;
                _isLoading = false;
            }

            loadCancellationTokenSource.Dispose();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var loadCancellationTokenSource = _loadCancellationTokenSource;
        _loadCancellationTokenSource = null;
        _isLoading = false;
        loadCancellationTokenSource?.Cancel();
    }

    private void OnRetryButtonClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _isImageLoaded = false;
        _ = StartLoadAsync();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_retryButton.IsMouseOver)
        {
            return;
        }

        _owner.NotifyImageClicked(_request);
    }

    private void ShowLoadingState()
    {
        _progressBar.Visibility = Visibility.Visible;
        _progressBar.IsIndeterminate = true;
        _retryButton.Visibility = Visibility.Collapsed;
        _sourceText.Visibility = _request.IsInline ? Visibility.Collapsed : Visibility.Visible;
        _statusText.Text = string.IsNullOrWhiteSpace(_request.AlternativeText) || _request.IsInline
            ? "正在加载图片…"
            : $"正在加载图片：{_request.AlternativeText}";
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "MarkdownSecondaryForegroundBrush");
        SetContent(_placeholder);
        AutomationProperties.SetHelpText(this, "图片正在加载");
    }

    private void ShowFailureState(string friendlyMessage, Exception exception)
    {
        _progressBar.IsIndeterminate = false;
        _progressBar.Visibility = Visibility.Collapsed;
        _retryButton.Visibility = Visibility.Visible;
        _sourceText.Visibility = _request.IsInline ? Visibility.Collapsed : Visibility.Visible;
        _statusText.Text = string.IsNullOrWhiteSpace(_request.AlternativeText) || _request.IsInline
            ? $"图片加载失败：{friendlyMessage}"
            : $"{_request.AlternativeText}\n图片加载失败：{friendlyMessage}";
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "MarkdownErrorBrush");
        SetContent(_placeholder);
        AutomationProperties.SetHelpText(this, $"图片加载失败：{friendlyMessage}");
        _owner.ReportResourceDiagnostic(new MarkdownDiagnostic(
            MarkdownDiagnosticSeverity.Warning,
            $"图片加载失败：{friendlyMessage}（{exception.Message}）",
            Source: _request.RawSource));
    }

    private void SetContent(UIElement content)
    {
        if (Children.Count == 1 && ReferenceEquals(Children[0], content))
        {
            return;
        }

        Children.Clear();
        Children.Add(content);
    }

    private string GetSourceDisplayText()
    {
        return _request.Source.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase)
            ? "内嵌 data URI 图片"
            : _request.RawSource;
    }

    private static string GetFriendlyErrorMessage(Exception exception)
    {
        return exception switch
        {
            HttpRequestException { StatusCode: { } statusCode } =>
                $"服务器返回 HTTP {(int)statusCode}（{statusCode}）。",
            HttpRequestException => "网络连接失败，请检查网络、代理或图片地址后重试。",
            FileNotFoundException => "本地图片文件不存在。",
            UnauthorizedAccessException => "没有权限读取本地图片。",
            InvalidDataException invalidDataException => invalidDataException.Message,
            NotSupportedException notSupportedException => notSupportedException.Message,
            UriFormatException => "图片地址格式无效。",
            _ => string.IsNullOrWhiteSpace(exception.Message)
                ? "发生未知错误，请重试。"
                : exception.Message
        };
    }
}
