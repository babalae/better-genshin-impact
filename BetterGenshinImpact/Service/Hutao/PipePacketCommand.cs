namespace BetterGenshinImpact.Service.Hutao;

internal enum PipePacketCommand : byte
{
    None = 0,
    Exit = 1,

    RedirectActivation = 10,
    RequestElevationStatus = 11,
    ResponseElevationStatus = 12,

    BetterGenshinImpactToSnapHutaoRequest = 20,
    BetterGenshinImpactToSnapHutaoResponse = 21,
    SnapHutaoToBetterGenshinImpactRequest = 22,
    SnapHutaoToBetterGenshinImpactResponse = 23,
}
