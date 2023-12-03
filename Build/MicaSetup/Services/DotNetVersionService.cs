using MicaSetup.Helper;
using System;
using System.IO;

namespace MicaSetup.Services;

public class DotNetVersionService : IDotNetVersionService
{
    public DotNetInstallInfo GetInfo(Version version, bool offline = false)
    {
        if (version == new Version(1, 0))
        {
            throw new NotImplementedException();
        }
        else if (version == new Version(1, 1))
        {
            throw new NotImplementedException();
        }
        else if (version == new Version(2, 0))
        {
            throw new NotImplementedException();
        }
        else if (version == new Version(2, 1))
        {
            throw new NotImplementedException();
        }
        else if (version == new Version(2, 2))
        {
            throw new NotImplementedException();
        }
        else if (version == new Version(3, 0))
        {
            throw new NotImplementedException();
        }
        else if (version == new Version(3, 1))
        {
            throw new NotImplementedException();
        }
        else if (version >= new Version(5, 0))
        {
            throw new NotImplementedException();
        }
        return DotNetInstallerHelper.GetInfo(version, offline);
    }

    public Version GetNetFrameworkVersion()
    {
        Version? version = DotNetVersionHelper.GetNet4xVersion();
        if (version != null)
        {
            return version;
        }

        version = DotNetVersionHelper.GetNet3xVersion();
        if (version != null)
        {
            return version;
        }

        version = DotNetVersionHelper.GetNet2xVersion();
        if (version != null)
        {
            return version;
        }

        version = DotNetVersionHelper.GetNet1xVersion();
        if (version != null)
        {
            return version;
        }
        return new Version();
    }

    public bool InstallNetFramework(Version version, InstallerProgressChangedEventHandler callback = null!)
    {
        DotNetInstallInfo info = DotNetInstallerHelper.GetInfo(version);

        if (DotNetInstallerHelper.Download(info, callback))
        {
            bool ret = DotNetInstallerHelper.Install(info, callback);
            if (File.Exists(info.TempFilePath))
            {
                File.Delete(info.TempFilePath);
            }
            return ret;
        }
        return false;
    }

    public Version GetNetCoreVersion()
    {
        throw new NotImplementedException();
    }

    public bool InstallNetCore(Version version, InstallerProgressChangedEventHandler callback = null!)
    {
        throw new NotImplementedException();
    }
}
