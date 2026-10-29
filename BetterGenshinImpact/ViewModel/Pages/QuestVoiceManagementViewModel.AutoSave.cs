using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Routing;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Routing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>路由草稿的防抖与串行持久化；不替换正在编辑的对象或运行快照。</summary>
public partial class QuestVoiceManagementViewModel
{
    /// <summary>同一编辑器的保存及显式文件操作串行执行。</summary>
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    /// <summary>UI 线程防抖，不在按键事件同步写盘。</summary>
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    /// <summary>每次编辑都递增，保存期间的新输入不能被清除。</summary>
    private long _draftRevision;
    /// <summary>已保存资源，用于只对新增或改变的本机资源进行存在性校验。</summary>
    private Dictionary<string, RoutingVoice> _savedProfiles = new();
    /// <summary>异步保存状态独立于编辑可用性，保存时仍允许继续输入。</summary>
    [ObservableProperty] private string _saveStatus = "尚未加载";
    /// <summary>首次进入设置页面才加载，避免运行页面自动产生目录。</summary>
    public async Task EnsureLoadedAsync()
    {
        if (IsLoaded || IsWorking) return;
        if (!File.Exists(_factory.RoutingCatalogPath))
        {
            try { await _factory.EnsureResourcesAsync(default); }
            catch (Exception exception) { Report(exception); SaveStatus = "加载失败，配置保留"; return; }
        }
        await LoadCommand.ExecuteAsync(null);
        SaveStatus = IsLoaded ? "已加载，有效修改自动保存" : "加载失败，请检查目录";
    }

    /// <summary>只登记草稿，不通过后台线程访问可变 WPF 集合。</summary>
    private void InitializeAutoSave()
    {
        _saveTimer.Tick += async (_, _) => { _saveTimer.Stop(); await FlushAutoSaveAsync(); };
        _owner.Config.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(QuestVoiceConfig.RoutingCatalogPath)) return;
            _saveTimer.Stop();
            if (IsLoaded) SaveStatus = "配置路径已改变，旧草稿保留；请导出或重新加载";
        };
    }

    /// <summary>每次有效编辑重新安排保存，不依赖 HasChanges 首次改变的通知。</summary>
    private void MarkDraftChanged()
    {
        if (_loading) return;
        HasChanges = true;
        _draftRevision++;
        SaveStatus = "等待输入有效配置";
        _saveTimer.Stop();
        _saveTimer.Start();
        NotifyVoiceChoices();
    }

    /// <summary>退出和启动前可立即排空；失败保留草稿并返回 false，供关闭守卫阻止数据丢失。</summary>
    public async Task<bool> FlushAutoSaveAsync()
    {
        _saveTimer.Stop();
        await _saveGate.WaitAsync();
        try
        {
            if (!HasChanges) return true;
            if (!IsLoaded || _loadedPath is null) return false;
            if (!string.Equals(Path.GetFullPath(_loadedPath), Path.GetFullPath(_factory.RoutingCatalogPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("路由路径已改变，请导出旧草稿或重新加载；不会写入新目标。");
            var revision = _draftRevision;
            var catalog = BuildCatalog();
            _store.Validate(catalog);
            // 原本不可用的已存声线仍可被保留；仅阻止新输入的本机错误路径覆盖有效配置。
            foreach (var item in Profiles)
            {
                var voice = BuildProfile(item);
                var old = _savedProfiles.GetValueOrDefault(item.OriginalId);
                if (old is not null && old.GptWeightsPath == item.GptWeightsPath && old.SovitsWeightsPath == item.SovitsWeightsPath
                    && old.ReferenceAudioPath == item.ReferenceAudioPath && old.InstanceId == voice.InstanceId && old.ProviderId == voice.ProviderId) continue;
                if (voice.ProviderId != "gpt-sovits") continue;
                var reason = await RoutingVoiceAssets.UnavailableAsync(voice, default, _factory.RoutingServerSidePaths);
                if (reason is not null) throw new InvalidOperationException(reason);
            }
            // 资源校验期间仍可能输入；废弃旧快照，下一次防抖保存最新值。
            if (revision != _draftRevision) { _saveTimer.Start(); return false; }
            SaveStatus = "正在保存";
            // 状态通知和资源校验均可能让用户切换路径；提交前再次核对，不把旧草稿写到错误目标。
            if (!string.Equals(Path.GetFullPath(_loadedPath), Path.GetFullPath(_factory.RoutingCatalogPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("路由路径已改变，请导出旧草稿或重新加载。");
            var saved = await _store.SaveAsync(_loadedPath, catalog, _expectedSha256, serverSidePaths: _factory.RoutingServerSidePaths);
            _expectedSha256 = saved.CatalogSha256;
            if (revision == _draftRevision)
            {
                HasChanges = false;
                _savedProfiles = Profiles.ToDictionary(x => x.OriginalId, x => x.Build());
                SaveStatus = "已保存 · 下次启动配音生效";
                EditStatus = "有效配置已自动保存，旧文件备份已保留。";
                _owner.CommandError = "";
            }
            else { SaveStatus = "等待保存较新的输入"; _saveTimer.Start(); }
            return !HasChanges;
        }
        catch (Exception exception)
        {
            SaveStatus = exception.Message.Contains("其他操作修改", StringComparison.Ordinal) ? "文件冲突：请导出草稿或重新加载"
                : exception is IOException or UnauthorizedAccessException || exception.Message.Contains("保存失败", StringComparison.Ordinal)
                    ? "保存失败：草稿保留" : "等待输入有效配置";
            Report(exception);
            return false;
        }
        finally { _saveGate.Release(); }
    }
}
