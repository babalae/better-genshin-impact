using MicaSetup.Helper;

namespace MicaSetup.Services;

public class ExplorerService : IExplorerService
{
    public void Refresh()
    {
        ExplorerHelper.Refresh();
    }
}
