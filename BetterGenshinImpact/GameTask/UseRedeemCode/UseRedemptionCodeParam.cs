using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.UseRedeemCode;

/// <summary>
/// 自动使用兑换码任务的参数。
/// </summary>
public class UseRedemptionCodeParam
{
    /// <summary>
    /// 要依次使用的兑换码文本列表，空白项在运行时忽略。
    /// </summary>
    public List<string> Codes { get; set; } = [];
}
