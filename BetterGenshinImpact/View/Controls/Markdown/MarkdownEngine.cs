using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.View.Controls.Markdown;

public interface IMarkdownEngine
{
    Task<MarkdownRenderPlan> CreatePlanAsync(
        MarkdownSource source,
        MarkdownOptions options,
        CancellationToken cancellationToken);

    MarkdownRenderResult Render(
        MarkdownRenderPlan plan,
        MarkdownView owner,
        IMarkdownImageLoader imageLoader,
        CancellationToken cancellationToken);
}

public sealed class MarkdownEngine : IMarkdownEngine
{
    public static MarkdownEngine Default { get; } = new();

    private readonly MarkdownPipeline _commonMarkPipeline;
    private readonly MarkdownPipeline _gfmPipeline;
    private readonly MarkdownPipeline _enhancedPipeline;

    public MarkdownEngine()
    {
        _commonMarkPipeline = new MarkdownPipelineBuilder().Build();

        _gfmPipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseTaskLists()
            .UseAutoLinks()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .UseCjkFriendlyEmphasis()
            .Build();

        _enhancedPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseEmojiAndSmiley()
            .UseYamlFrontMatter()
            .UseCjkFriendlyEmphasis()
            .UseAlertBlocks()
            .Build();
    }

    public Task<MarkdownRenderPlan> CreatePlanAsync(
        MarkdownSource source,
        MarkdownOptions options,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pipeline = options.Profile switch
            {
                MarkdownProfile.CommonMark => _commonMarkPipeline,
                MarkdownProfile.Gfm => _gfmPipeline,
                _ => _enhancedPipeline
            };

            var document = Markdig.Markdown.Parse(source.Text, pipeline);
            cancellationToken.ThrowIfCancellationRequested();
            return new MarkdownRenderPlan(document, source, options);
        }, cancellationToken);
    }

    public MarkdownRenderResult Render(
        MarkdownRenderPlan plan,
        MarkdownView owner,
        IMarkdownImageLoader imageLoader,
        CancellationToken cancellationToken)
    {
        owner.Dispatcher.VerifyAccess();
        return new MarkdownWpfRenderer(plan, owner, imageLoader, cancellationToken).Render();
    }
}
