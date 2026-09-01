using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 内置特殊相邻传送点清单 —— 硬编码静态只读列表，编译进 dll。
/// 这是"系统自带、开箱即用"的清单：程序一编译就有，永不丢失、永不被任何文件拷贝覆盖，
/// 也不存在"铺默认文件覆盖用户运行时文件"的风险（对比用户 JSON 走 User 目录纯数据）。
/// 运行时与用户 JSON 清单合并后统一判定（见 TpTask.GetSpecialAdjacentTpPointList）。
/// 详见 .kiro/specs/teleport-adjacent-point-misclick-zoom-whitelist-fix/design.md。
/// </summary>
public static class SpecialAdjacentTpPointBuiltins
{
    /// <summary>
    /// 内置清单：上半部分为手动维护条目，下半部分为 tp.json 自动分析生成（最近邻距离 &lt; 40）。
    /// 无专属 Zoom → 命中时用全局默认 2。
    /// </summary>
    public static readonly IReadOnlyList<SpecialAdjacentTpPoint> List = new List<SpecialAdjacentTpPoint>
    {
        new() { X = -796.32, Y = 1037.22 }, //雪山
        new() { X = 1184.09, Y = 622.63 },
        new() { X = 3383.84, Y = 2692.99 },//E045枫丹卡布狄斯堡
        new() { X = -2469.92, Y = 4300.78 },//莫尔泰区
        new() { X = 3887.81, Y = 1235.82 },//佩可莉镇
        new() { X = 2777.39, Y = 1525.53 },//赤望台
        new() { X = 9605.335, Y = -1852.241 }, 
        new() { X = 828.93, Y = -582.76}, 
        new() { X = 522.56, Y = 528.84}, //ZB006璃月层岩地下无名遗迹

        // ===== 以下为 tp.json 自动分析生成（最近邻距离 < 40，坐标系与上方手动条目一致）=====
        // 生成规则：遍历 tp.json 全部传送点，取最近邻欧氏距离 < 40 的点，坐标直接用真实点坐标。
        // 已自动跳过与上方手动条目重复的点（传送锚点(蒙德) -796,1039 = 雪山）。
        // 无专属 Zoom → 命中时用全局默认 2。
        new() { X = 117.9796, Y = 2651.545 }, //传送锚点(蒙德) 最近邻 14.0
        new() { X = 131.9534, Y = 2651.928 }, //深入风龙废墟(蒙德) 最近邻 14.0
        new() { X = -825.87, Y = 1039.45 }, //芬德尼尔之顶(蒙德) 最近邻 29.8
        new() { X = -4378.864, Y = -2501.427 }, //传送锚点(稻妻) 最近邻 30.5
        new() { X = -4404.72, Y = -2485.26 }, //梦想乐土之殁(稻妻) 最近邻 30.5
        new() { X = 9691.779, Y = -1624.362 }, //传送锚点(纳塔) 最近邻 30.8
        new() { X = 9709.071, Y = -1649.9 }, //曜石图腾柱·「烟谜主」(纳塔) 最近邻 30.8
        new() { X = 9724.226, Y = 5445.849 }, //传送锚点(挪德卡莱) 最近邻 35.7
        new() { X = 9715.974, Y = 5480.602 }, //「聚所·魔女的花园」(挪德卡莱) 最近邻 35.7
        new() { X = 9643.603, Y = -1857.9 }, //传送锚点(纳塔) 最近邻 38.7
        
    };
}
