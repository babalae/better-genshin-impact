using MicaSetup.Helper;
using System;

namespace MicaSetup.Services;

public interface IDotNetVersionService
{
    public DotNetInstallInfo GetInfo(Version version, bool offline = false);

    public Version GetNetFrameworkVersion();

    public bool InstallNetFramework(Version version, InstallerProgressChangedEventHandler callback = null!);

    public Version GetNetCoreVersion();

    public bool InstallNetCore(Version version, InstallerProgressChangedEventHandler callback = null!);
}
