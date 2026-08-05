namespace BetterGenshinImpact.GameTask.AutoPathing.Strategy.Movement;

/// <summary>
/// 状态机或动作控制结果
/// Represents the result of handling a movement mode step
/// </summary>
public enum MoveModeResult
{
    /// <summary>
    /// 中断后续其他按键施放，直接执行下一轮主循环 `continue`
    /// Indication to short-circuit and run `continue` in the pathing loop
    /// </summary>
    Continue,
    
    /// <summary>
    /// 继续当前主循环的向下判断（如元素战技、道具使用） `fallthrough`
    /// Indication to keep processing actions downwards in the loop
    /// </summary>
    Pass,
    
    /// <summary>
    /// 即刻返回false退出当前方法
    /// Indication to immediately return false from the controller
    /// </summary>
    ReturnFalse
}
