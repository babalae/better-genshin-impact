using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.AutoWood.Utils;

internal sealed class Login3rdParty
{
    public enum The3rdPartyType
    {
        None,
        Bilibili,
    }

    public bool IsAvailabled => Type != The3rdPartyType.None;
    public The3rdPartyType Type { get; private set; } = default;

    public void RefreshAvailabled()
    {
        Type = The3rdPartyType.None;

        try
        {
            if (Process.GetProcessesByName("YuanShen").FirstOrDefault() is Process p)
            {
                uint tid = User32.GetWindowThreadProcessId(p.MainWindowHandle, out uint pid);

                if (tid != 0)
                {
                    using Kernel32.SafeHPROCESS hProcess = Kernel32.OpenProcess(new ACCESS_MASK(Kernel32.ProcessAccess.PROCESS_QUERY_INFORMATION), false, pid);

                    if (!hProcess.IsInvalid)
                    {
                        StringBuilder devicePath = new(260);
                        uint size = (uint)devicePath.Capacity;

                        if (Kernel32.QueryFullProcessImageName(hProcess, 0, devicePath, ref size))
                        {
                            FileInfo fileInfo = new(devicePath.ToString());

                            if (fileInfo.Exists)
                            {
                                string? configIni = Path.Combine(fileInfo.DirectoryName!, "config.ini");
                                string[] lines = File.ReadAllLines(configIni);

                                foreach (string line in lines)
                                {
                                    string kv = line.Trim();
                                    if (kv.StartsWith("cps=") && kv.EndsWith("bilibili"))
                                    {
                                        Type = The3rdPartyType.Bilibili;
                                        break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"Error getting process image file name. Error code: {Marshal.GetLastWin32Error()}");
                        }
                    }
                }
            }
        }
        catch
        {
            ///
        }
    }

    public void Login()
    {
        // TODO
    }
}
