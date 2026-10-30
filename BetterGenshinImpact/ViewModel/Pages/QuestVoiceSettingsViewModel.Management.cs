using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>把角色目录草稿编辑器挂到设置窗口模型，不改变会话生命周期。</summary>
public partial class QuestVoiceSettingsViewModel
{
    /// <summary>角色目录的存储边界，由 DI 提供，负责校验、冲突检测和备份。</summary>
    private readonly RoutingCatalogStore _store;
    /// <summary>首次打开设置窗口才创建编辑器，关闭窗口后继续保留未保存草稿。</summary>
    private QuestVoiceManagementViewModel? _management;

    /// <summary>路由文件通过共享编辑器安全自动保存，其他配置沿用宿主机制。</summary>
    public QuestVoiceManagementViewModel Management => _management ??= new(this, _factory, _service, _store);

    /// <summary>关闭窗口和退出前排空已打开的编辑器；未打开过设置窗口时不创建目录。</summary>
    public async Task<bool> FlushSettingsAsync()
    {
        var routesSaved = _management is null || await _management.FlushAutoSaveAsync();
        return routesSaved;
    }
}
