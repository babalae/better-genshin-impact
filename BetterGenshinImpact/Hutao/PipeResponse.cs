namespace BetterGenshinImpact.Hutao;

internal class PipeResponse
{
    public required PipeResponseKind Kind { get; set; }
}

internal sealed class PipeResponse<T> : PipeResponse
{
    public T? Data { get; set; }
}