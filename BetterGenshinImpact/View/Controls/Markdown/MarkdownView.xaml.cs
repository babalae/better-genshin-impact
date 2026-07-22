using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace BetterGenshinImpact.View.Controls.Markdown;

public partial class MarkdownView : UserControl
{
    private static readonly IReadOnlyList<MarkdownDiagnostic> EmptyDiagnostics = Array.Empty<MarkdownDiagnostic>();
    private static readonly IReadOnlyList<MarkdownHeading> EmptyOutline = Array.Empty<MarkdownHeading>();

    private static readonly DependencyPropertyKey StatePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(State),
        typeof(MarkdownViewState),
        typeof(MarkdownView),
        new PropertyMetadata(MarkdownViewState.Empty));

    private static readonly DependencyPropertyKey IsLoadingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsLoading),
        typeof(bool),
        typeof(MarkdownView),
        new PropertyMetadata(false));

    private static readonly DependencyPropertyKey LastErrorPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(LastError),
        typeof(MarkdownRenderException),
        typeof(MarkdownView),
        new PropertyMetadata(null));

    private static readonly DependencyPropertyKey DiagnosticsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Diagnostics),
        typeof(IReadOnlyList<MarkdownDiagnostic>),
        typeof(MarkdownView),
        new PropertyMetadata(EmptyDiagnostics));

    private static readonly DependencyPropertyKey OutlinePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Outline),
        typeof(IReadOnlyList<MarkdownHeading>),
        typeof(MarkdownView),
        new PropertyMetadata(EmptyOutline));

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnInputPropertyChanged));

    public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
        nameof(FilePath),
        typeof(string),
        typeof(MarkdownView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnInputPropertyChanged));

    public static readonly DependencyProperty BasePathProperty = DependencyProperty.Register(
        nameof(BasePath),
        typeof(string),
        typeof(MarkdownView),
        new PropertyMetadata(null, OnInputPropertyChanged));

    public static readonly DependencyProperty SourceEncodingProperty = DependencyProperty.Register(
        nameof(SourceEncoding),
        typeof(Encoding),
        typeof(MarkdownView),
        new PropertyMetadata(new UTF8Encoding(false), OnInputPropertyChanged));

    public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(
        nameof(Profile),
        typeof(MarkdownProfile),
        typeof(MarkdownView),
        new PropertyMetadata(MarkdownProfile.Enhanced, OnInputPropertyChanged));

    public static readonly DependencyProperty HtmlPolicyProperty = DependencyProperty.Register(
        nameof(HtmlPolicy),
        typeof(MarkdownHtmlPolicy),
        typeof(MarkdownView),
        new PropertyMetadata(MarkdownHtmlPolicy.SafeSubset, OnInputPropertyChanged));

    public static readonly DependencyProperty EnableEmojiRenderingProperty = DependencyProperty.Register(
        nameof(EnableEmojiRendering),
        typeof(bool),
        typeof(MarkdownView),
        new PropertyMetadata(true, OnInputPropertyChanged));

    public static readonly DependencyProperty PresentationDelayProperty = DependencyProperty.Register(
        nameof(PresentationDelay),
        typeof(TimeSpan),
        typeof(MarkdownView),
        new PropertyMetadata(TimeSpan.Zero));

    public static readonly DependencyProperty WatchFileChangesProperty = DependencyProperty.Register(
        nameof(WatchFileChanges),
        typeof(bool),
        typeof(MarkdownView),
        new PropertyMetadata(false, OnWatchFileChangesPropertyChanged));

    public static readonly DependencyProperty LinkCommandProperty = DependencyProperty.Register(
        nameof(LinkCommand),
        typeof(ICommand),
        typeof(MarkdownView),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ImageCommandProperty = DependencyProperty.Register(
        nameof(ImageCommand),
        typeof(ICommand),
        typeof(MarkdownView),
        new PropertyMetadata(null));

    public static readonly DependencyProperty RenderFailedCommandProperty = DependencyProperty.Register(
        nameof(RenderFailedCommand),
        typeof(ICommand),
        typeof(MarkdownView),
        new PropertyMetadata(null));

    public static readonly DependencyProperty TaskChangedCommandProperty = DependencyProperty.Register(
        nameof(TaskChangedCommand),
        typeof(ICommand),
        typeof(MarkdownView),
        new PropertyMetadata(null));

    public static readonly DependencyProperty IsTaskListInteractiveProperty = DependencyProperty.Register(
        nameof(IsTaskListInteractive),
        typeof(bool),
        typeof(MarkdownView),
        new PropertyMetadata(false, OnInputPropertyChanged));

    public static readonly DependencyProperty LinkNavigationModeProperty = DependencyProperty.Register(
        nameof(LinkNavigationMode),
        typeof(MarkdownLinkNavigationMode),
        typeof(MarkdownView),
        new PropertyMetadata(MarkdownLinkNavigationMode.AnchorOnly));

    public static readonly DependencyProperty StateProperty = StatePropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsLoadingProperty = IsLoadingPropertyKey.DependencyProperty;
    public static readonly DependencyProperty LastErrorProperty = LastErrorPropertyKey.DependencyProperty;
    public static readonly DependencyProperty DiagnosticsProperty = DiagnosticsPropertyKey.DependencyProperty;
    public static readonly DependencyProperty OutlineProperty = OutlinePropertyKey.DependencyProperty;

    public static readonly RoutedEvent RenderCompletedEvent = EventManager.RegisterRoutedEvent(
        nameof(RenderCompleted),
        RoutingStrategy.Bubble,
        typeof(MarkdownRenderCompletedEventHandler),
        typeof(MarkdownView));

    public static readonly RoutedEvent RenderFailedEvent = EventManager.RegisterRoutedEvent(
        nameof(RenderFailed),
        RoutingStrategy.Bubble,
        typeof(MarkdownRenderFailedEventHandler),
        typeof(MarkdownView));

    public static readonly RoutedEvent LinkClickedEvent = EventManager.RegisterRoutedEvent(
        nameof(LinkClicked),
        RoutingStrategy.Bubble,
        typeof(MarkdownLinkClickedEventHandler),
        typeof(MarkdownView));

    private CancellationTokenSource? _renderCancellationTokenSource;
    private FileSystemWatcher? _fileWatcher;
    private IReadOnlyDictionary<string, FrameworkContentElement> _anchors =
        new Dictionary<string, FrameworkContentElement>(StringComparer.OrdinalIgnoreCase);
    private string _plainText = string.Empty;
    private long _renderVersion;
    private bool _renderPending;

    public MarkdownView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string? Markdown
    {
        get => (string?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string? FilePath
    {
        get => (string?)GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public string? BasePath
    {
        get => (string?)GetValue(BasePathProperty);
        set => SetValue(BasePathProperty, value);
    }

    public Encoding SourceEncoding
    {
        get => (Encoding)GetValue(SourceEncodingProperty);
        set => SetValue(SourceEncodingProperty, value);
    }

    public MarkdownProfile Profile
    {
        get => (MarkdownProfile)GetValue(ProfileProperty);
        set => SetValue(ProfileProperty, value);
    }

    public MarkdownHtmlPolicy HtmlPolicy
    {
        get => (MarkdownHtmlPolicy)GetValue(HtmlPolicyProperty);
        set => SetValue(HtmlPolicyProperty, value);
    }

    public bool EnableEmojiRendering
    {
        get => (bool)GetValue(EnableEmojiRenderingProperty);
        set => SetValue(EnableEmojiRenderingProperty, value);
    }

    public TimeSpan PresentationDelay
    {
        get => (TimeSpan)GetValue(PresentationDelayProperty);
        set => SetValue(PresentationDelayProperty, value);
    }

    public bool WatchFileChanges
    {
        get => (bool)GetValue(WatchFileChangesProperty);
        set => SetValue(WatchFileChangesProperty, value);
    }

    public ICommand? LinkCommand
    {
        get => (ICommand?)GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    public ICommand? ImageCommand
    {
        get => (ICommand?)GetValue(ImageCommandProperty);
        set => SetValue(ImageCommandProperty, value);
    }

    public ICommand? RenderFailedCommand
    {
        get => (ICommand?)GetValue(RenderFailedCommandProperty);
        set => SetValue(RenderFailedCommandProperty, value);
    }

    public ICommand? TaskChangedCommand
    {
        get => (ICommand?)GetValue(TaskChangedCommandProperty);
        set => SetValue(TaskChangedCommandProperty, value);
    }

    public bool IsTaskListInteractive
    {
        get => (bool)GetValue(IsTaskListInteractiveProperty);
        set => SetValue(IsTaskListInteractiveProperty, value);
    }

    public MarkdownLinkNavigationMode LinkNavigationMode
    {
        get => (MarkdownLinkNavigationMode)GetValue(LinkNavigationModeProperty);
        set => SetValue(LinkNavigationModeProperty, value);
    }

    public MarkdownViewState State => (MarkdownViewState)GetValue(StateProperty);

    public bool IsLoading => (bool)GetValue(IsLoadingProperty);

    public MarkdownRenderException? LastError => (MarkdownRenderException?)GetValue(LastErrorProperty);

    public IReadOnlyList<MarkdownDiagnostic> Diagnostics =>
        (IReadOnlyList<MarkdownDiagnostic>)GetValue(DiagnosticsProperty);

    public IReadOnlyList<MarkdownHeading> Outline =>
        (IReadOnlyList<MarkdownHeading>)GetValue(OutlineProperty);

    public IMarkdownEngine Engine { get; set; } = MarkdownEngine.Default;

    public IMarkdownImageLoader ImageLoader { get; set; } = MarkdownImageLoader.Default;

    public event MarkdownRenderCompletedEventHandler RenderCompleted
    {
        add => AddHandler(RenderCompletedEvent, value);
        remove => RemoveHandler(RenderCompletedEvent, value);
    }

    public event MarkdownRenderFailedEventHandler RenderFailed
    {
        add => AddHandler(RenderFailedEvent, value);
        remove => RemoveHandler(RenderFailedEvent, value);
    }

    public event MarkdownLinkClickedEventHandler LinkClicked
    {
        add => AddHandler(LinkClickedEvent, value);
        remove => RemoveHandler(LinkClickedEvent, value);
    }

    public Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        return StartRenderAsync(useDebounce: false, cancellationToken);
    }

    public bool NavigateToHeading(string anchor)
    {
        var normalizedAnchor = anchor.TrimStart('#');
        if (!_anchors.TryGetValue(normalizedAnchor, out var element))
        {
            return false;
        }

        element.BringIntoView();
        return true;
    }

    public string GetPlainText()
    {
        return _plainText;
    }

    internal void ActivateLink(MarkdownLinkRequest request)
    {
        RaiseEvent(new MarkdownLinkClickedEventArgs(LinkClickedEvent, this, request));

        if (request.Kind == MarkdownLinkKind.Anchor
            && LinkNavigationMode != MarkdownLinkNavigationMode.CommandOnly
            && NavigateToHeading(request.RawTarget))
        {
            return;
        }

        if (LinkCommand?.CanExecute(request) == true)
        {
            LinkCommand.Execute(request);
            return;
        }

        if (LinkNavigationMode != MarkdownLinkNavigationMode.SystemDefault || request.Target is null)
        {
            return;
        }

        if (request.Target.Scheme is not ("http" or "https" or "mailto" or "file"))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(request.Target.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ReportResourceDiagnostic(new MarkdownDiagnostic(
                MarkdownDiagnosticSeverity.Warning,
                $"无法打开链接：{ex.Message}",
                Source: request.RawTarget));
        }
    }

    internal void NotifyImageClicked(MarkdownImageRequest request)
    {
        if (ImageCommand?.CanExecute(request) == true)
        {
            ImageCommand.Execute(request);
        }
    }

    internal void ReportResourceDiagnostic(MarkdownDiagnostic diagnostic)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => ReportResourceDiagnostic(diagnostic));
            return;
        }

        var diagnostics = new List<MarkdownDiagnostic>(Diagnostics) { diagnostic };
        SetValue(DiagnosticsPropertyKey, diagnostics);
    }

    private static void OnInputPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MarkdownView)d).ScheduleRender();
    }

    private static void OnWatchFileChangesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MarkdownView)d;
        view.ConfigureFileWatcher();
        view.ScheduleRender();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_renderPending || State == MarkdownViewState.Empty)
        {
            _renderPending = false;
            ScheduleRender();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelCurrentRender();
        DisposeFileWatcher();
        _renderPending = true;
    }

    private void ScheduleRender()
    {
        if (!IsLoaded)
        {
            _renderPending = true;
            return;
        }

        _ = StartRenderAsync(useDebounce: true, CancellationToken.None);
    }

    private async Task StartRenderAsync(bool useDebounce, CancellationToken externalCancellationToken)
    {
        CancelCurrentRender();
        var startedAt = Stopwatch.GetTimestamp();
        var version = Interlocked.Increment(ref _renderVersion);
        _renderCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
        var cancellationToken = _renderCancellationTokenSource.Token;

        SetValue(StatePropertyKey, MarkdownViewState.Loading);
        SetValue(IsLoadingPropertyKey, true);
        SetValue(LastErrorPropertyKey, null);

        try
        {
            if (useDebounce)
            {
                await Task.Delay(100, cancellationToken);
            }

            var source = await LoadSourceAsync(cancellationToken);
            if (source is null)
            {
                if (version == _renderVersion)
                {
                    DocumentViewer.Document = CreateEmptyDocument();
                    _anchors = new Dictionary<string, FrameworkContentElement>(StringComparer.OrdinalIgnoreCase);
                    _plainText = string.Empty;
                    SetValue(DiagnosticsPropertyKey, EmptyDiagnostics);
                    SetValue(OutlinePropertyKey, EmptyOutline);
                    SetValue(StatePropertyKey, MarkdownViewState.Empty);
                }

                return;
            }

            var options = new MarkdownOptions(Profile, HtmlPolicy, EnableEmojiRendering);
            var plan = await Engine.CreatePlanAsync(source, options, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (useDebounce)
            {
                await WaitForPresentationDelayAsync(startedAt, cancellationToken);
            }

            var result = Engine.Render(plan, this, ImageLoader, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (version != _renderVersion)
            {
                return;
            }

            _plainText = new TextRange(result.Document.ContentStart, result.Document.ContentEnd).Text;
            DocumentViewer.Document = result.Document;
            _anchors = result.Anchors;
            SetValue(DiagnosticsPropertyKey, result.Diagnostics);
            SetValue(OutlinePropertyKey, result.Outline);
            SetValue(StatePropertyKey, MarkdownViewState.Ready);
            ConfigureFileWatcher();
            RaiseEvent(new MarkdownRenderCompletedEventArgs(RenderCompletedEvent, this, result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (version != _renderVersion)
            {
                return;
            }

            var renderException = ex as MarkdownRenderException
                                  ?? new MarkdownRenderException("无法渲染 Markdown 文档。", ex);
            SetValue(LastErrorPropertyKey, renderException);
            SetValue(StatePropertyKey, MarkdownViewState.Failed);
            SetValue(DiagnosticsPropertyKey, new[]
            {
                new MarkdownDiagnostic(MarkdownDiagnosticSeverity.Error, renderException.Message)
            });
            if (RenderFailedCommand?.CanExecute(renderException) == true)
            {
                RenderFailedCommand.Execute(renderException);
            }

            RaiseEvent(new MarkdownRenderFailedEventArgs(RenderFailedEvent, this, renderException));
        }
        finally
        {
            if (version == _renderVersion)
            {
                SetValue(IsLoadingPropertyKey, false);
            }
        }
    }

    private async Task WaitForPresentationDelayAsync(long startedAt, CancellationToken cancellationToken)
    {
        var presentationDelay = PresentationDelay;
        if (presentationDelay > TimeSpan.Zero)
        {
            var remaining = presentationDelay - Stopwatch.GetElapsedTime(startedAt);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
    }

    private async Task<MarkdownSource?> LoadSourceAsync(CancellationToken cancellationToken)
    {
        var hasMarkdown = !string.IsNullOrEmpty(Markdown);
        var hasFilePath = !string.IsNullOrWhiteSpace(FilePath);
        if (hasMarkdown && hasFilePath)
        {
            throw new MarkdownRenderException("Markdown 与 FilePath 不能同时设置。");
        }

        if (!hasMarkdown && !hasFilePath)
        {
            return null;
        }

        var encoding = SourceEncoding ?? new UTF8Encoding(false);
        if (hasMarkdown)
        {
            var basePath = ResolveBasePath(BasePath);
            return new MarkdownSource(Markdown!, basePath, null, encoding);
        }

        var filePath = ResolveFilePath(FilePath!, BasePath);
        if (!File.Exists(filePath))
        {
            throw new MarkdownRenderException($"Markdown 文件不存在：{filePath}");
        }

        var text = await File.ReadAllTextAsync(filePath, encoding, cancellationToken);
        return new MarkdownSource(text, Path.GetDirectoryName(filePath), filePath, encoding);
    }

    private void ConfigureFileWatcher()
    {
        DisposeFileWatcher();
        if (!WatchFileChanges || string.IsNullOrWhiteSpace(FilePath))
        {
            return;
        }

        try
        {
            var filePath = ResolveFilePath(FilePath, BasePath);
            var directory = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
            {
                return;
            }

            _fileWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _fileWatcher.Changed += OnWatchedFileChanged;
            _fileWatcher.Created += OnWatchedFileChanged;
            _fileWatcher.Renamed += OnWatchedFileChanged;
        }
        catch (Exception ex)
        {
            ReportResourceDiagnostic(new MarkdownDiagnostic(
                MarkdownDiagnosticSeverity.Warning,
                $"无法监听 Markdown 文件变化：{ex.Message}",
                Source: FilePath));
        }
    }

    private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, ScheduleRender);
    }

    private void CancelCurrentRender()
    {
        _renderCancellationTokenSource?.Cancel();
        _renderCancellationTokenSource?.Dispose();
        _renderCancellationTokenSource = null;
    }

    private void DisposeFileWatcher()
    {
        if (_fileWatcher is null)
        {
            return;
        }

        _fileWatcher.EnableRaisingEvents = false;
        _fileWatcher.Changed -= OnWatchedFileChanged;
        _fileWatcher.Created -= OnWatchedFileChanged;
        _fileWatcher.Renamed -= OnWatchedFileChanged;
        _fileWatcher.Dispose();
        _fileWatcher = null;
    }

    private static string ResolveFilePath(string filePath, string? basePath)
    {
        if (Path.IsPathRooted(filePath))
        {
            return Path.GetFullPath(filePath);
        }

        var resolvedBasePath = ResolveBasePath(basePath) ?? AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(resolvedBasePath, filePath));
    }

    private static string? ResolveBasePath(string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return null;
        }

        return Path.GetFullPath(Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(AppContext.BaseDirectory, basePath));
    }

    private static FlowDocument CreateEmptyDocument()
    {
        return new FlowDocument
        {
            PagePadding = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent
        };
    }
}
