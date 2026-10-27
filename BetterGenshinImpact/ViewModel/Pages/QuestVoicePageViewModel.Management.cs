using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>把独立路由草稿编辑器挂到两个子页面共用的模型，不改变会话生命周期。</summary>
public partial class QuestVoicePageViewModel
{
    /// <summary>首次打开设置页才创建编辑器，切页后继续保留未保存草稿。</summary>
    private QuestVoiceManagementViewModel? _management;

    /// <summary>路由文件通过共享编辑器安全自动保存，其他配置沿用宿主机制。</summary>
    public QuestVoiceManagementViewModel Management => _management ??= new(this, _factory, _service, new RoutingCatalogStore());

    /// <summary>关闭和启动前排空已打开的编辑器；未进入设置页时不创建目录。</summary>
    public async System.Threading.Tasks.Task<bool> FlushSettingsAsync()
    {
        var routesSaved = _management is null || await _management.FlushAutoSaveAsync();
        return routesSaved;
    }
}
