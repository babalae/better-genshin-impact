using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>领取尘歌壶奖励的专属设置草稿，沿用原一条龙的进壶方式、购买日期和商品选择。</summary>
public partial class SereniteaPotSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <inheritdoc />
    public override string[] EditedParameterNames => ["teleport_type", "secret_treasure_objects"];

    /// <summary>洞天百宝支持的商品，顺序与原一条龙的商品勾选列表一致。</summary>
    private static readonly string[] Goods = ["布匹", "须臾树脂", "大英雄的经验", "流浪者的经验", "精锻用魔矿", "摩拉", "祝圣精华", "祝圣油膏"];

    /// <summary>载入时的进壶方式快照。</summary>
    private readonly string _originalTeleportType;
    /// <summary>载入时的购买日期；未配置购买时界面默认显示每天重复。</summary>
    private readonly string _originalPurchaseSchedule;
    /// <summary>载入时的购买商品，保存时保留尚未取消的商品顺序。</summary>
    private readonly string[] _originalPurchaseItems;

    /// <summary>进壶方式选项，保留导入配置中的当前值。</summary>
    public string[] TeleportTypeChoices { get; }
    /// <summary>购买日期选项，日期只影响购买商品，不影响领取好感和宝钱。</summary>
    public string[] PurchaseScheduleChoices { get; }
    /// <summary>可勾选的购买商品；导入配置中的额外商品也予以保留。</summary>
    public IReadOnlyList<SereniteaPotPurchaseItemViewModel> PurchaseItems { get; }

    /// <summary>进入尘歌壶的方式。</summary>
    [ObservableProperty] private string _teleportType;
    /// <summary>购买洞天百宝商品的日期。</summary>
    [ObservableProperty] private string _purchaseSchedule;

    /// <summary>从任务有效参数恢复进壶方式及购买选择，不改动参数来源。</summary>
    /// <param name="original">当前有效参数快照。</param>
    public SereniteaPotSettingsViewModel(JObject original) : base(original)
    {
        _originalTeleportType = ReadString(Original, "teleport_type");
        _teleportType = _originalTeleportType;
        TeleportTypeChoices = WithCurrentChoice(["地图传送", "尘歌壶道具"], _originalTeleportType);

        // 数组首项是购买日期，后续项是商品；空配置只提供界面默认值，不自动生成购买参数。
        var purchase = (Original["secret_treasure_objects"] as JArray)?.Values<string>().OfType<string>().ToArray() ?? [];
        _originalPurchaseSchedule = purchase.FirstOrDefault() ?? "每天重复";
        _originalPurchaseItems = purchase.Skip(1).ToArray();
        _purchaseSchedule = _originalPurchaseSchedule;
        PurchaseScheduleChoices = WithCurrentChoice(["每天重复", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日"], _originalPurchaseSchedule);
        PurchaseItems = Goods.Concat(_originalPurchaseItems).Distinct(StringComparer.Ordinal)
            .Select(name => new SereniteaPotPurchaseItemViewModel(name, _originalPurchaseItems.Contains(name, StringComparer.Ordinal))).ToArray();
    }

    /// <summary>仅提交真正修改的设置，并将日期及商品恢复为任务原有的字符串数组。</summary>
    /// <param name="changes">接收变化字段的对象。</param>
    public override void CollectChanges(JObject changes)
    {
        WriteString(changes, "teleport_type", _originalTeleportType, TeleportType);
        if (PurchaseSchedule == _originalPurchaseSchedule
            && PurchaseItems.All(item => item.IsSelected == _originalPurchaseItems.Contains(item.Name, StringComparer.Ordinal)))
        {
            return;
        }

        // 保留已配置商品的购买顺序，新勾选商品追加在后，避免编辑日期改变商品购买优先顺序。
        var selected = PurchaseItems.Where(item => item.IsSelected).Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var goods = _originalPurchaseItems.Where(selected.Contains)
            .Concat(PurchaseItems.Where(item => item.IsSelected && !_originalPurchaseItems.Contains(item.Name, StringComparer.Ordinal)).Select(item => item.Name));
        changes["secret_treasure_objects"] = new JArray(new[] { PurchaseSchedule }.Concat(goods));
    }
}
