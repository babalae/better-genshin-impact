using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;

/// <summary>
/// 自动连招配置
/// </summary>
public partial class AutoComboBuildConfig : ObservableObject
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

    /// <summary>
    /// 注入给建树 LLM 的额外提示词（追加在系统指令末尾），留空则不注入
    /// </summary>
    [ObservableProperty]
    private string _extraPrompt = "";

    /// <summary>
    /// 主建树系统指令的正文，留空使用内置默认正文
    /// “当前队伍”“元素反应”“用户自定义要求”小节由程序固定拼接在正文之后，无需填写
    /// </summary>
    [ObservableProperty]
    private string _mainPrompt = "";

    /// <summary>
    /// 兜底建树系统指令的正文，留空使用内置默认正文
    /// “当前队伍”小节由程序固定拼接在正文之后，无需填写
    /// </summary>
    [ObservableProperty]
    private string _fallbackPrompt = "";

    /// <summary>
    /// 角色战术描述覆盖：只使用 Name/Description 字段，Tags 由内置档案决定（忽略）
    /// 未覆盖的角色使用内置描述，删除行即恢复内置描述
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AvatarProfile> _avatarDescriptionOverrides = [];
}
