namespace BetterGenshinImpact.Hutao;

// Snap Hutao reserves all variables that don't appear below
// Any command may add in furure should not send to Snap Hutao Pipe
internal enum PipePacketCommand : byte
{
    None = 0,
    BetterGenshinImpactToSnapHutaoRequest = 20,
    BetterGenshinImpactToSnapHutaoResponse = 21,
    SnapHutaoToBetterGenshinImpactRequest = 22,
    SnapHutaoToBetterGenshinImpactResponse = 23,
}