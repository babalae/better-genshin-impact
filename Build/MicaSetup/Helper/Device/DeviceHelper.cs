using System.Linq;
using System.Management;

namespace MicaSetup.Helper;

public static class DeviceHelper
{
    public static string DeviceID
        => MD5CryptoHelper.ComputeHash($"{ProcessorSerialNumber},{BIOSSerialNumber},{BaseBoardSerialNumber}");

    public static string ProcessorSerialNumber
        => GetManagementProperty("Win32_Processor", "SerialNumber");

    public static string BIOSSerialNumber
        => GetManagementProperty("Win32_BIOS", "SerialNumber");

    public static string BaseBoardSerialNumber
        => GetManagementProperty("Win32_BaseBoard", "SerialNumber");

    private static string GetManagementProperty(string path, string name)
    {
        try
        {
            using ManagementClass managementClass = new(path);
            using ManagementObjectCollection mn = managementClass.GetInstances();
            PropertyDataCollection properties = managementClass.Properties;

            foreach (PropertyData property in properties)
            {
                if (property.Name == name)
                {
                    foreach (ManagementObject m in mn.Cast<ManagementObject>())
                    {
                        return m.Properties[property.Name].Value.ToString();
                    }
                }
            }
        }
        catch
        {
        }
        return string.Empty;
    }
}
