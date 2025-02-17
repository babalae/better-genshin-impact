using System.Text.Json;

namespace BetterGenshinImpact.Hutao;

internal sealed class PipeRequest<T>
{
    public required PipeRequestKind Kind { get; set; }

    public T Data { get; set; }
}