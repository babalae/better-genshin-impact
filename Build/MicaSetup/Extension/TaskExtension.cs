using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace MicaSetup.Helper;

public static class TaskExtension
{
    [SuppressMessage("Style", "IDE0060:")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Forget(this Task self)
    {
    }

    [SuppressMessage("Style", "IDE0060:")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Forget(this ConfiguredTaskAwaitable self)
    {
    }
}
