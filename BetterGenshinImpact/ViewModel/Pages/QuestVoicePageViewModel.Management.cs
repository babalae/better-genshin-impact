using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>把独立路由草稿编辑器挂到两个子页面共用的模型，不改变会话生命周期。</summary>
public partial class QuestVoicePageViewModel
{
    /// <summary>首次打开设置页才创建编辑器，切页后继续保留未保存草稿。</summary>
    private QuestVoiceManagementViewModel? _management;

    /// <summary>路由文件使用显式保存，其他统一配置仍沿用原自动保存机制。</summary>
    public QuestVoiceManagementViewModel Management => _management ??= new(this, _factory, _service, new RoutingCatalogStore());
}
