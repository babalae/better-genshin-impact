namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild.FishRelief;

/// <summary>
/// 发鱼站服务地址约定
/// </summary>
internal static class FishReliefGateway
{
    /// <summary>
    /// 默认服务地址：发鱼站的唯一地址来源，切换环境时只改这一处
    /// 末尾带 /v1，OpenAI 兼容客户端会自行拼接 /chat/completions 与 /models
    /// </summary>
    public const string DefaultEndpoint = "http://8.133.180.35:5289/v1";
}
