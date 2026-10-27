using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>ncnn 可用的 Vulkan 设备。</summary>
public sealed record NcnnDeviceInfo(int Index, string Name, int TypeCode)
{
    /// <summary>ncnn GpuInfo::type()：0 独显 / 1 核显 / 2 虚拟显卡 / 3 CPU。</summary>
    public string TypeName => TypeText(TypeCode);

    public string DisplayName => $"#{Index} {Name}（{TypeName}）";

    /// <summary>设备类型描述（ncnn GpuInfo::type()）：0 独显 / 1 核显 / 2 虚拟显卡 / 3 CPU；设备列表与设备详情两处记录共用。</summary>
    public static string TypeText(int typeCode) => typeCode switch
    {
        0 => "独显",
        1 => "核显",
        2 => "虚拟显卡",
        3 => "CPU",
        _ => "未知类型"
    };
}

/// <summary>一次设备查询的结果；旧版 shim 缺少导出时返回空列表，由界面降级为索引。</summary>
public sealed record NcnnDeviceQuery(IReadOnlyList<NcnnDeviceInfo> Devices, int DefaultIndex);
