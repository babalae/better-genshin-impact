namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 多次识别出战角色结果上下文
/// 置信度规则：
/// 1. 通过颜色识别成功的，置信度最高，一次识别就返回
/// 2. 颜色无法识别
/// </summary>
public class AvatarActiveCheckContext
{
    public bool NeedRetry { get; set; } = false;
    
    public int ActiveIndex { get; set; } = -1;
}