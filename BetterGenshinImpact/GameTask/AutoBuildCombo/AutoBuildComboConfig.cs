using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.GameTask.AutoBuildCombo;

/// <summary>
/// 自动连招配置
/// </summary>
public partial class AutoBuildComboConfig : ObservableObject
{
    /// <summary>
    /// 决策模型的 OpenAI 兼容端点
    /// </summary>
    [ObservableProperty]
    private string _planningLlmEndpoint = "";

    /// <summary>
    /// 向服务请求的模型名
    /// To learn more about the available models, see https://platform.openai.com/docs/models.
    /// </summary>
    [ObservableProperty]
    private string _modelName = "";

    /// <summary>
    /// llm服务密钥
    /// </summary>
    [ObservableProperty]
    private string _apiKey = "";
}
