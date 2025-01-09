using System.Collections.Generic;

namespace BetterGenshinImpact.Helpers.Device;

public class PCInfo
{
    public CPU[] CPU { get; set; }
    public 主板[] 主板 { get; set; }
    public 内存[] 内存 { get; set; }
    public 网卡[] 网卡 { get; set; }
    public 系统[] 系统 { get; set; }
    public 磁盘[] 磁盘 { get; set; }
    public 打印机[] 打印机 { get; set; }
    public 显卡[] 显卡 { get; set; }

    public 笔记本 笔记本 { get; set; }
}

public class CPU
{
    public string CPU序列号 { get; set; }
    public string CPU名称 { get; set; }
    public string 设备ID { get; set; }
    public string 名称 { get; set; }
    public string 状态 { get; set; }
    public string CPU级别 { get; set; }
    public string 主机名 { get; set; }
    public string 处理器类型 { get; set; }
}

public class 主板
{
    public string 主板ID { get; set; }
    public string 制造商 { get; set; }
    public string 型号 { get; set; }
    public string 版本 { get; set; }
}

public class 内存
{
    public string 内存类型 { get; set; }
    public string 速度 { get; set; }
    public string 容量 { get; set; }
    public string 插槽 { get; set; }
    public string 位置 { get; set; }
    public string 型号 { get; set; }
    public string 序列号 { get; set; }
}

public class 网卡
{
    public string 名称 { get; set; }
    public string MAC地址 { get; set; }
    public string IP地址 { get; set; }
}

public class 系统
{
    public string 启动设备 { get; set; }
    public string 系统名称 { get; set; }
    public string 计算机名 { get; set; }
    public string 系统位数 { get; set; }
    public string 序列号 { get; set; }
    public string 系统硬件位置 { get; set; }
    public string 系统目录 { get; set; }
    public string 系统盘符 { get; set; }
    public string 系统版本 { get; set; }
    public string 安装时间 { get; set; }
}

public class 磁盘
{
    public string 设备ID { get; set; }
    public string 设备名称 { get; set; }
    public string 序列号 { get; set; }
    public string 接口类型 { get; set; }
    public string 型号 { get; set; }
    public string 容量 { get; set; }
}

public class 打印机
{
    public string 设备名称 { get; set; }
    public string 设备ID { get; set; }
    public string 驱动名称 { get; set; }
    public string 使用接口 { get; set; }
}

public class 显卡
{
    public string 设备名称 { get; set; }
    public string 显存 { get; set; }
    public string 设备ID { get; set; }
    public string 分辨率 { get; set; }
}

public class 笔记本
{
    /**
     * 其他 (1)
未知 (2)
桌面 (3)
低配置文件桌面 (4)
披萨盒 (5)
迷你塔 (6)
塔 (7)
便携式 (8)
笔记本电脑 (9)
Notebook (10)
手持 (11)
扩展坞 (12)
一起 (13)
子笔记本 (14)
节省空间 (15)
午餐盒 (16)
主系统底盘 (17)
扩展底盘 (18)
subchassis (19)
总线扩展底盘 (20)
外设底盘 (21)
存储机箱 (22)
机架式底盘 (23)
密封外壳 PC (24)
平板电脑 (30)
可转换 (31)
可拆离 (32)
https://learn.microsoft.com/zh-cn/windows/win32/cimwin32prov/win32-systemenclosure
     */
    public List<string> 计算机类型 { get; set; }

    public bool 是否笔记本 { get; set; }
    public bool 是否插入电源 { get; set; }

    public bool 系统级触控板是否启用 { get; set; } = TouchpadSoft.Instance.QueryTouchpadStatus() == 1;

    public bool 是否存在触控屏 { get; set; } = TouchpadManager.HasTouchInput();
    
    public bool 是否存在触控屏2 { get; set; } = TouchpadManager.HasTouchInput2();
}