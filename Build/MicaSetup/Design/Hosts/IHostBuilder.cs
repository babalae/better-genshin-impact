using Microsoft.Extensions.DependencyInjection;

namespace MicaSetup.Design.Controls;

public interface IHostBuilder
{
    public IApp App { get; set; }
    public ServiceProvider ServiceProvider { get; set; }

    public IHostBuilder CreateApp();

    public void RunApp();
}
