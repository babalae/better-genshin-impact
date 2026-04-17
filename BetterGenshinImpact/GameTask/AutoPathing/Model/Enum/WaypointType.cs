using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

/// <summary>
/// Specifies the topological roles and behavioral boundaries of individual waypoints.
/// 定义各个路点的拓扑角色及行为边界，主导状态机的分发流程。
/// </summary>
/// <param name="code">The underlying structural code identifier. 底层结构代码标识符。</param>
/// <param name="msg">The localized diagnostic message. 本地化诊断用消息。</param>
public class WaypointType(string code, string msg)
{
    /// <summary>Intermediate continuous routing nodal point. 中继性连续路由节点位（途径点）。</summary>
    public static readonly WaypointType Path = new("path", "途径点");
    
    /// <summary>Terminal interaction convergence node. 终端交互聚合节点（目标点/终点）。</summary>
    public static readonly WaypointType Target = new("target", "目标点");
    
    /// <summary>Dimensional displacement discontinuous junction. 维度位移间断结合点（传送锚点）。</summary>
    public static readonly WaypointType Teleport = new("teleport", "传送点");
    
    /// <summary>Camera vector alignment pivot point. 摄影机向量对齐枢轴点（方位点）。</summary>
    public static readonly WaypointType Orientation = new("orientation", "方位点");

    /// <summary>
    /// Gets all registered topological node enumeration values.
    /// 获取所有注册的拓扑节点枚举值集合。
    /// </summary>
    public static IEnumerable<WaypointType> Values
    {
        get
        {
            yield return Path;
            yield return Target;
            yield return Teleport;
            yield return Orientation;
        }
    }

    /// <summary>
    /// The canonical identity marker of the waypoint behavior model.
    /// 路点行为模型的规范性身份标记代码。
    /// </summary>
    public string Code { get; private set; } = code;
    
    /// <summary>
    /// The localized label describing this node's role.
    /// 描述该节点角色的本地化标签。
    /// </summary>
    public string Msg { get; private set; } = msg;

    /// <summary>
    /// Safely resolves the localized diagnostic message from a topology code.
    /// 安全地从拓扑代码解析本地化诊断消息，避免大小写或空指针污染。
    /// </summary>
    /// <param name="code">The target topological execution code. 目标拓扑执行代码。</param>
    /// <returns>The specified localized string, falling back to raw code mapping. 指定的本地化字符串，容错回落为原始映射代码。</returns>
    public static string GetMsgByCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return string.Empty;

        foreach (var item in Values)
        {
            if (string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return item.Msg;
            }
        }
        return code;
    }
}
