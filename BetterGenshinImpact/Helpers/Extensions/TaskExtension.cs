using System;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Helpers.Extensions;

internal static class TaskExtension
{
    public static async void SafeForget(this Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {

        }
#if DEBUG
        catch (Exception)
        {
            if (System.Diagnostics.Debugger.IsAttached)
            {
                System.Diagnostics.Debugger.Break();
            }
        }
#else
        catch
        {
        }
#endif
    }
}
