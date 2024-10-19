using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Hutao;

// Layout:
// 0         1      2         3             4    Bytes
// ┌─────────┬──────┬─────────┬─────────────┐
// │ Version │ Type │ Command │ ContentType │
// ├─────────┴──────┴─────────┴─────────────┤ 4  Bytes
// │             ContentLength              │
// ├────────────────────────────────────────┤ 8  Bytes
// │                                        │
// │─────────────── Checksum ───────────────│
// │                                        │
// └────────────────────────────────────────┘ 16 Bytes
// Any content will be placed after the header.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct PipePacketHeader
{
    public byte Version;
    public PipePacketType Type;
    public PipePacketCommand Command;
    public PipePacketContentType ContentType;
    public int ContentLength;
    public ulong Checksum;
}