using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BetterGenshinImpact.Service.AccountRecords;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>统一导入入口：选择文件、确认目标账号及字典补充，再局部合并库存。</summary>
public partial class AccountRecordsImportViewModel : ObservableObject
{
    /// <summary>库存持久化入口。</summary>
    private readonly InventoryStore _store;
    /// <summary>预览通过的文件快照，避免导入时文件已被替换。</summary>
    private JObject? _package;
    /// <summary>文件内补充字典的预览行。</summary>
    public ObservableCollection<string> Definitions { get; } = [];
    /// <summary>用户可编辑的目标 UID；默认沿用文件中的 UID。</summary>
    [ObservableProperty] private string _targetUid = "";
    /// <summary>当前文件摘要。</summary>
    [ObservableProperty] private string _summary = "请选择 AccountRecords JSON 文件。目前支持单个账号的当前库存。";
    /// <summary>导入反馈或读取错误。</summary>
    [ObservableProperty] private string _message = "";
    /// <summary>是否允许用文件中的分类补充本地字典空缺。</summary>
    [ObservableProperty] private bool _includeDefinitions;
    /// <summary>导入过程中锁定界面，避免目标账号和文件发生变化。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanInteract))] private bool _isBusy;
    /// <summary>文件已读取，允许用户确认导入。</summary>
    [ObservableProperty] private bool _hasPackage;
    /// <summary>操作进行期间禁用输入。</summary>
    public bool CanInteract => !IsBusy;
    /// <summary>最后成功导入的账号，供主页面关闭窗口后选中。</summary>
    public string? ImportedUid { get; private set; }

    /// <summary>由页面提供同一个库存服务。</summary>
    public AccountRecordsImportViewModel(InventoryStore store) => _store = store;

    /// <summary>读取并预览，失败时清除旧包以避免误导入。</summary>
    [RelayCommand]
    private async Task ChooseFile()
    {
        var dialog = new OpenFileDialog { Filter = "账号记录 JSON|*.json", Title = "选择账号记录", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        IsBusy = true;
        _package = null;
        HasPackage = false;
        IncludeDefinitions = false;
        Definitions.Clear();
        Message = "";
        try
        {
            _package = await InventoryStore.ReadExchangeAsync(dialog.FileName);
            var account = ((JObject)_package["accounts"]!).Properties().Single();
            TargetUid = account.Name;
            var count = ((JObject)account.Value["inventory"]!).Count;
            foreach (var item in ((JObject)_package["itemDefinitions"]!).Properties())
                Definitions.Add($"{item.Name}：{item.Value["category"]}");
            Summary = $"{Path.GetFileName(dialog.FileName)}\n文件 UID：{account.Name} · 库存 {count} 项 · 字典补充 {Definitions.Count} 项";
            if (_package["note"]?.Type == JTokenType.String) Summary += "\n" + (string?)_package["note"];
            HasPackage = true;
        }
        catch (Exception ex) { Summary = "文件未载入。"; Message = ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>用户确认后导入，保留错误供库存红灯处理。</summary>
    [RelayCommand]
    private async Task Import()
    {
        if (_package == null || IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _store.ImportInventoryAsync(_package, TargetUid.Trim(), IncludeDefinitions);
            ImportedUid = TargetUid.Trim();
            Message = $"导入完成：接受 {result.Accepted.Count} 项，异常 {result.Rejected.Count} 项。关闭窗口后可在目标 UID 的库存中查看，红灯异常需要手动处理。";
            HasPackage = false;
            _package = null;
        }
        catch (Exception ex) { Message = $"导入未完成：{ex.Message}\n已经成功保存的批次会保留，请查看库存后重试。"; }
        finally { IsBusy = false; }
    }
}
