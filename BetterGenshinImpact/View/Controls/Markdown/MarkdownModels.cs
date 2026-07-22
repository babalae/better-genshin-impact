using Markdig.Syntax;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace BetterGenshinImpact.View.Controls.Markdown;

public enum MarkdownProfile
{
    CommonMark,
    Gfm,
    Enhanced
}

public enum MarkdownHtmlPolicy
{
    Disabled,
    SafeSubset
}

public enum MarkdownViewState
{
    Empty,
    Loading,
    Ready,
    Failed
}

public enum MarkdownDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public enum MarkdownLinkKind
{
    Anchor,
    Web,
    Email,
    File,
    Relative,
    Unknown
}

public enum MarkdownLinkNavigationMode
{
    AnchorOnly,
    CommandOnly,
    SystemDefault
}

public sealed record MarkdownHeading(int Level, string Text, string Anchor);

public sealed record MarkdownDiagnostic(
    MarkdownDiagnosticSeverity Severity,
    string Message,
    int? Line = null,
    string? Source = null);

public sealed record MarkdownLinkRequest(
    Uri? Target,
    string RawTarget,
    string DisplayText,
    string? Title,
    MarkdownLinkKind Kind);

public sealed record MarkdownImageRequest(
    Uri Source,
    string RawSource,
    string AlternativeText,
    string? Title,
    bool IsInline);

public sealed record MarkdownTaskChangedRequest(bool IsChecked);

public sealed record MarkdownSource(
    string Text,
    string? BasePath,
    string? FilePath,
    Encoding Encoding);

public sealed record MarkdownOptions(
    MarkdownProfile Profile,
    MarkdownHtmlPolicy HtmlPolicy,
    bool EnableEmojiRendering);

public sealed class MarkdownRenderException : Exception
{
    public MarkdownRenderException(string message)
        : base(message)
    {
    }

    public MarkdownRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class MarkdownRenderPlan
{
    internal MarkdownRenderPlan(MarkdownDocument document, MarkdownSource source, MarkdownOptions options)
    {
        Document = document;
        Source = source;
        Options = options;
    }

    internal MarkdownDocument Document { get; }

    internal MarkdownSource Source { get; }

    internal MarkdownOptions Options { get; }
}

public sealed class MarkdownRenderResult
{
    internal MarkdownRenderResult(
        FlowDocument document,
        IReadOnlyList<MarkdownHeading> outline,
        IReadOnlyList<MarkdownDiagnostic> diagnostics,
        IReadOnlyDictionary<string, FrameworkContentElement> anchors)
    {
        Document = document;
        Outline = outline;
        Diagnostics = diagnostics;
        Anchors = anchors;
    }

    public FlowDocument Document { get; }

    public IReadOnlyList<MarkdownHeading> Outline { get; }

    public IReadOnlyList<MarkdownDiagnostic> Diagnostics { get; }

    internal IReadOnlyDictionary<string, FrameworkContentElement> Anchors { get; }
}

public sealed record MarkdownImageResult(ImageSource ImageSource, int PixelWidth, int PixelHeight);

public sealed class MarkdownRenderCompletedEventArgs : RoutedEventArgs
{
    internal MarkdownRenderCompletedEventArgs(RoutedEvent routedEvent, object source, MarkdownRenderResult result)
        : base(routedEvent, source)
    {
        Result = result;
    }

    public MarkdownRenderResult Result { get; }
}

public sealed class MarkdownRenderFailedEventArgs : RoutedEventArgs
{
    internal MarkdownRenderFailedEventArgs(RoutedEvent routedEvent, object source, MarkdownRenderException exception)
        : base(routedEvent, source)
    {
        Exception = exception;
    }

    public MarkdownRenderException Exception { get; }
}

public sealed class MarkdownLinkClickedEventArgs : RoutedEventArgs
{
    internal MarkdownLinkClickedEventArgs(RoutedEvent routedEvent, object source, MarkdownLinkRequest request)
        : base(routedEvent, source)
    {
        Request = request;
    }

    public MarkdownLinkRequest Request { get; }
}

public delegate void MarkdownRenderCompletedEventHandler(object sender, MarkdownRenderCompletedEventArgs e);

public delegate void MarkdownRenderFailedEventHandler(object sender, MarkdownRenderFailedEventArgs e);

public delegate void MarkdownLinkClickedEventHandler(object sender, MarkdownLinkClickedEventArgs e);
