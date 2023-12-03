using MicaSetup.Shell.NetFw;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MicaSetup.Helper;

public static class FirewallHelper
{
    public static void AllowApplication(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentNullException(nameof(path));
        }
        if (File.Exists(path) == false)
        {
            throw new FileNotFoundException(path);
        }

        string ruleName = Path.GetFileNameWithoutExtension(path);
        dynamic fwPolicy2 = null!;
        dynamic inboundRule = null!;

        try
        {
            fwPolicy2 = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));

            try
            {
                if (fwPolicy2.Rules.Item(ruleName) != null)
                {
                    return;
                }
            }
            catch
            {
            }

            inboundRule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule"));
            inboundRule.Enabled = true;
            inboundRule.Action = NET_FW_ACTION.NET_FW_ACTION_ALLOW;
            inboundRule.ApplicationName = path;
            inboundRule.Name = ruleName;
            inboundRule.Profiles = (int)NET_FW_PROFILE_TYPE2.NET_FW_PROFILE2_ALL;
            fwPolicy2.Rules.Add(inboundRule);
        }
        finally
        {
            if (fwPolicy2 != null)
            {
                _ = Marshal.FinalReleaseComObject(fwPolicy2);
            }
            if (inboundRule != null)
            {
                _ = Marshal.FinalReleaseComObject(inboundRule);
            }
        }
    }

    public static void RemoveApplication(string path)
    {
        string ruleName = Path.GetFileNameWithoutExtension(path);
        dynamic fwPolicy2 = null!;
        dynamic inboundRule = null!;

        try
        {
            fwPolicy2 = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));

            try
            {
                inboundRule = fwPolicy2.Rules.Item(ruleName);
                if (inboundRule != null)
                {
                    fwPolicy2.Rules.Remove(inboundRule.Name);
                }
            }
            catch
            {
            }
        }
        finally
        {
            if (fwPolicy2 != null)
            {
                _ = Marshal.FinalReleaseComObject(fwPolicy2);
            }
            if (inboundRule != null)
            {
                _ = Marshal.FinalReleaseComObject(inboundRule);
            }
        }
    }
}
