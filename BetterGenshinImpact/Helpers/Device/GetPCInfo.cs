using System;
using System.Collections.Generic;
using System.Management;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Helpers.Device;

static class GetPCInfo
{
    public static string GetJson()
    {
        var PCInfo = GetClass();
        return JsonConvert.SerializeObject(PCInfo);
    }


    public static PCInfo GetClass()
    {
        var PCInfo = new PCInfo()
        {
            CPU = GetCPUs(),
            主板 = Get主板s(),
            内存 = Get内存s(),
            打印机 = Get打印机s(),
            显卡 = Get显卡s(),
            磁盘 = Get磁盘s(),
            系统 = Get系统s(),
            网卡 = Get网卡s(),
            笔记本 = new 笔记本()
            {
                计算机类型 = Get计算机类型(),
                是否笔记本 = IsLaptop(),
                是否插入电源 = IsPluggedIn()
            }
        };
        return PCInfo;
    }

    public static CPU[] GetCPUs()
    {
        var cpus = new List<CPU>();
        try
        {
            var mc = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            var moc = mc.Get();
            foreach (ManagementObject mo in moc)
            {
                var cpu = new CPU
                {
                    CPU名称 = mo["Caption"].ToString(),
                    CPU序列号 = mo["ProcessorID"].ToString(),
                    设备ID = mo["DeviceID"].ToString(),
                    名称 = mo["Name"].ToString(),
                    状态 = mo["CpuStatus"].ToString(),
                    CPU级别 = mo["Level"].ToString(),
                    主机名 = mo["SystemName"].ToString(),
                    处理器类型 = mo["ProcessorType"].ToString()
                };

                cpus.Add(cpu);
            }

            moc = null;
            mc = null;
        }
        catch
        {
        }

        return cpus.ToArray();
    }


    public static 主板[] Get主板s()
    {
        var 主板s = new List<主板>();
        try
        {
            var mc = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
            var moc = mc.Get();
            foreach (ManagementObject mo in moc)
            {
                var 主板 = new 主板
                {
                    主板ID = mo["SerialNumber"].ToString(),
                    制造商 = mo["Manufacturer"].ToString(),
                    型号 = mo["Product"].ToString(),
                    版本 = mo["Version"].ToString()
                };

                主板s.Add(主板);
            }

            moc = null;
            mc = null;
        }
        catch
        {
        }

        return 主板s.ToArray();
    }


    public static 内存[] Get内存s()
    {
        var 内存s = new List<内存>();
        try
        {
            var mc = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
            var moc = mc.Get();
            foreach (ManagementObject mo in moc)
            {
                var 内存 = new 内存
                {
                    位置 = mo["DeviceLocator"].ToString(),
                    内存类型 = mo["Caption"].ToString(),
                    型号 = mo["PartNumber"].ToString(),
                    容量 = long.Parse(mo["Capacity"].ToString()) / 1073741824 + "GB",
                    序列号 = mo["SerialNumber"].ToString(),
                    插槽 = mo["Tag"].ToString(),
                    速度 = mo["Speed"].ToString()
                };

                内存s.Add(内存);
            }

            moc = null;
            mc = null;
        }
        catch (Exception)
        {
        }


        return 内存s.ToArray();
    }


    public static 网卡[] Get网卡s()
    {
        var 网卡s = new List<网卡>();
        try
        {
            var mc = new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapterConfiguration Where IPEnabled=TRUE");
            var moc = mc.Get();
            foreach (ManagementObject mo in moc)
            {
                var 网卡 = new 网卡
                {
                    IP地址 = ((String[])mo["IPAddress"])[0],
                    MAC地址 = mo["MACAddress"].ToString(),
                    名称 = mo["Caption"].ToString()
                };

                网卡s.Add(网卡);
            }

            moc = null;
            mc = null;
        }
        catch
        {
        }

        return 网卡s.ToArray();
    }


    public static 系统[] Get系统s()
    {
        var 系统s = new List<系统>();
        try
        {
            var mc = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            var moc = mc.Get();
            foreach (ManagementObject mo in moc)
            {
                var 系统 = new 系统
                {
                    启动设备 = mo["BootDevice"].ToString(),
                    安装时间 = mo["InstallDate"].ToString(),
                    序列号 = mo["SerialNumber"].ToString(),
                    系统位数 = mo["OSArchitecture"].ToString(),
                    系统名称 = mo["Caption"].ToString(),
                    系统版本 = mo["Version"].ToString(),
                    系统盘符 = mo["SystemDrive"].ToString(),
                    系统目录 = mo["SystemDirectory"].ToString(),
                    系统硬件位置 = mo["SystemDevice"].ToString(),
                    计算机名 = mo["CSName"].ToString()
                };

                系统s.Add(系统);
            }

            moc = null;
            mc = null;
        }
        catch
        {
        }

        return 系统s.ToArray();
    }

    public static 磁盘[] Get磁盘s()
    {
        var 磁盘s = new List<磁盘>();
        try
        {
            var mc = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            var moc = mc.Get();
            foreach (ManagementObject mo in moc)
            {
                var 磁盘 = new 磁盘
                {
                    型号 = mo["Model"].ToString(),
                    容量 = long.Parse(mo["Size"].ToString()) / 1073741824 + "GB",
                    序列号 = mo["SerialNumber"].ToString(),
                    接口类型 = mo["InterfaceType"].ToString(),
                    设备ID = mo["DeviceID"].ToString(),
                    设备名称 = mo["Caption"].ToString()
                };

                磁盘s.Add(磁盘);
            }

            moc = null;
            mc = null;
        }
        catch
        {
        }

        return 磁盘s.ToArray();
    }


    public static 打印机[] Get打印机s()
    {
        var 打印机s = new List<打印机>();
        try
        {
            var mc = new ManagementObjectSearcher("SELECT * FROM Win32_Printer");
            var moc = mc.Get();
            foreach (ManagementObject mo in moc)
            {
                var 打印机 = new 打印机
                {
                    使用接口 = mo["PortName"].ToString(),
                    设备ID = mo["DeviceID"].ToString(),
                    设备名称 = mo["Caption"].ToString(),
                    驱动名称 = mo["DriverName"].ToString()
                };

                打印机s.Add(打印机);
            }

            moc = null;
            mc = null;
        }
        catch
        {
        }

        return 打印机s.ToArray();
    }


    public static 显卡[] Get显卡s()
    {
        var 显卡s = new List<显卡>();
        try
        {
            var mc = new ManagementObjectSearcher("SELECT * FROM  Win32_VideoController");
            var moc = mc.Get();
            foreach (ManagementObject mo in moc)
            {
                var 显卡 = new 显卡
                {
                    分辨率 = mo["VideoModeDescription"].ToString(),
                    显存 = long.Parse(mo["AdapterRAM"].ToString()) / 1048576 + "MB",
                    设备ID = mo["PNPDeviceID"].ToString(),
                    设备名称 = mo["Caption"].ToString()
                };

                显卡s.Add(显卡);
            }

            moc = null;
            mc = null;
        }
        catch
        {
        }

        return 显卡s.ToArray();
    }


    static List<string> Get计算机类型()
    {
        List<string> types = new();
        // Use WMI to query the ChassisTypes
        var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_SystemEnclosure");
        foreach (ManagementObject queryObj in searcher.Get())
        {
            foreach (int type in (UInt16[])queryObj["ChassisTypes"])
            {
                types.Add(type.ToString());
            }
        }

        return types;
    }

    static bool IsLaptop()
    {
        // Use WMI to query the ChassisTypes
        var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_SystemEnclosure");
        foreach (ManagementObject queryObj in searcher.Get())
        {
            foreach (int type in (UInt16[])queryObj["ChassisTypes"])
            {
                // 8 and 9 represent portable and laptop, respectively
                if (type == 8 || type == 9 || type == 10 || type == 14 || type == 30)
                {
                    return true;
                }
            }
        }

        return false;
    }

    static bool IsPluggedIn()
    {
        // Use WMI to query the power status
        var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
        foreach (ManagementObject queryObj in searcher.Get())
        {
            // BatteryStatus 2 means the laptop is plugged in
            if ((UInt16)queryObj["BatteryStatus"] == 2)
            {
                return true;
            }
        }

        return false;
    }
}