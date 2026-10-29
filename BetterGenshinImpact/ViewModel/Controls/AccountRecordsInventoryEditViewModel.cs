using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>数量编辑和补充字典维护，合法名称不允许就地重命名。</summary>
public partial class AccountRecordsInventoryEditViewModel : ObservableObject
{
    /// <summary>保存入口。</summary>
    private readonly InventoryStore _store;
    /// <summary>内置目录。</summary>
    private readonly InventoryCatalog _catalog;
    /// <summary>叠加后的目录。</summary>
    private readonly IReadOnlyDictionary<string, string> _definitions;
    /// <summary>编辑开始时固定 UID，账号切换不能改变写入目标。</summary>
    private readonly string _uid;
    /// <summary>待修订的原行。</summary>
    private readonly AccountRecordsInventoryRow? _original;
    /// <summary>用户明确反馈原上报错误，默认不选。</summary>
    [ObservableProperty] private bool _markScanIncorrect;
    /// <summary>仅有可追溯的核实记录时允许反馈。</summary>
    public bool CanMarkScanIncorrect => _original?.LastScanAttemptId != null;
    /// <summary>保存成功后请求视图关闭。</summary>
    public event Action? Saved;
    /// <summary>名称字段仅新增或未知名称可编辑。</summary>
    public bool CanEditName { get; }
    /// <summary>已有内置分类不可改写，缺失分类允许补充。</summary>
    public bool CanEditCategory => !_catalog.Items.TryGetValue(Name.Trim(), out var category) || string.IsNullOrEmpty(category);
    /// <summary>确认补充的选项仅在未知名称时出现。</summary>
    public bool NeedsDefinition => !_definitions.ContainsKey(Name.Trim());
    /// <summary>标准名称。</summary>
    [ObservableProperty] private string _name = "";
    /// <summary>可见分类。</summary>
    [ObservableProperty] private string _category = "";
    /// <summary>用户输入的数量，在保存前校验。</summary>
    [ObservableProperty] private string _quantityText = "";
    /// <summary>明确授权将未知名称加入单独用户目录。</summary>
    [ObservableProperty] private bool _addToDictionary;
    /// <summary>验证或存储错误。</summary>
    [ObservableProperty] private string _message = "";

    /// <summary>已有到期信息时展开编辑，不由补充字典控制。</summary>
    public bool ShowExpirationEditor { get; }
    /// <summary>本次人工确认的到期批次，可增删；未注明到期的数量仍由总数保留。</summary>
    public ObservableCollection<AccountRecordsExpirationEditRow> ExpirationRows { get; } = [];

    /// <summary>新增空批次，要求用户填写实际到期时间，不猜测时间。</summary>
    [RelayCommand]
    private void AddExpiration()
    {
        if (ExpirationRows.Count >= 100) { Message = "到期批次最多 100 条。"; return; }
        ExpirationRows.Add(new AccountRecordsExpirationEditRow());
    }

    /// <summary>移除本次确认中不再存在的批次。</summary>
    [RelayCommand]
    private void RemoveExpiration(AccountRecordsExpirationEditRow? row)
    {
        if (row != null) ExpirationRows.Remove(row);
    }

    /// <summary>从当前行初始化编辑数据。</summary>
    public AccountRecordsInventoryEditViewModel(InventoryStore store, InventoryCatalog catalog, string uid,
        AccountRecordsInventoryRow? row, IReadOnlyDictionary<string, string> definitions)
    {
        _store = store; _catalog = catalog; _uid = uid; _original = row; _definitions = definitions;
        CanEditName = row == null || !definitions.ContainsKey(row.Name);
        Name = row?.Name ?? "";
        var quantity = row?.Quantity ?? (row?.Error?.Code == "ReadFailed" ? null : row?.Error?.Incoming.Quantity);
        QuantityText = quantity?.ToString(CultureInfo.InvariantCulture) ?? "";
        // 优先使用当前展示的推算批次，仍要求用户按游戏实际情况核对。
        var batches = row?.Expirations ?? row?.Recent.FirstOrDefault()?.Expirations ??
            (row?.Event?.State == "Pending" ? row.Event.Expirations : null);
        ShowExpirationEditor = batches != null;
        foreach (var batch in batches ?? [])
            ExpirationRows.Add(new AccountRecordsExpirationEditRow
            {
                QuantityText = batch.Quantity.ToString(CultureInfo.InvariantCulture),
                ExpiresAtText = batch.Time.ToLocalTime().ToString("O", CultureInfo.InvariantCulture)
            });
        RefreshCategory();
    }

    /// <summary>名称变化后同步只读分类和补充选项。</summary>
    partial void OnNameChanged(string value)
    {
        RefreshCategory();
        OnPropertyChanged(nameof(CanEditCategory)); OnPropertyChanged(nameof(NeedsDefinition));
        AddToDictionary = false;
    }

    /// <summary>优先展示字典中的分类。</summary>
    private void RefreshCategory() => Category = _definitions.TryGetValue(Name.Trim(), out var category) ? category : "";

    /// <summary>校验后提交当前库存，同时结束本次修正的未知名称下的积压事项。</summary>
    [RelayCommand]
    private async Task Save()
    {
        Message = "";
        var name = Name.Trim();
        if (name.Length is 0 or > 128) { Message = "物品名称不能为空且不能超过 128 字符。"; return; }
        if (!long.TryParse(QuantityText, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity < 0)
        { Message = "当前实际库存数量须为不带分隔符的非负整数，且不超过 9223372036854775807。"; return; }
        if (NeedsDefinition && (!AddToDictionary || string.IsNullOrWhiteSpace(Category)))
        { Message = "物品名称未收录，且尚未填写分类或确认添加到补充字典。"; return; }
        List<InventoryExpiration>? batches = ShowExpirationEditor ? [] : null;
        foreach (var row in ExpirationRows)
        {
            if (!long.TryParse(row.QuantityText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
            { Message = "每个到期批次的数量必须为正整数。"; return; }
            batches!.Add(new InventoryExpiration { Quantity = count, ExpiresAt = row.ExpiresAtText.Trim() });
        }
        // 在任何字典写入前复用服务层校验，避免到期输入无效时分类已部分保存。
        var expirationError = InventoryStore.ValidateExpirations(batches, quantity);
        if (expirationError != null) { Message = expirationError; return; }
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(async () =>
            {
                if (CanEditCategory && !string.IsNullOrWhiteSpace(Category)) await _store.DefineAsync(name, Category);
                await _store.SetLocalQuantityAsync(_uid, name, quantity,
                    MarkScanIncorrect && CanMarkScanIncorrect ? _original!.LastScanAttemptId : null,
                    _original != null && _original.Name != name ? _original.Name : null, batches);
            });
            Saved?.Invoke();
        }
        catch (Exception ex) { Message = AccountRecordsErrorMessages.Describe(ex); }
    }
}
