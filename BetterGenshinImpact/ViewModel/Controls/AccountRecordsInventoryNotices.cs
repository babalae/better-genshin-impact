using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.AccountRecords;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>底部多卡片展示与独立操作，沿用物品选择、悬停和存储服务的校验规则。</summary>
public partial class AccountRecordsInventoryViewModel
{
    /// <summary>当前物品的全部未归档事项；每组提示独立固定操作目标。</summary>
    public ObservableCollection<AccountRecordsNoticeCard> NoticeCards { get; } = [];
    /// <summary>是否正在展示事件卡片。</summary>
    public bool HasNoticeCards => NoticeCards.Count > 0;
    /// <summary>无卡片时显示一般状态，有卡片时仍显示独立操作错误或导入摘要。</summary>
    public bool ShowStatusMessage => !HasNoticeCards || Status != AccountRecordsErrorMessages.ForDisplay(NoticeRow?.Event?.Message ?? NoticeRow?.Error?.Message ?? "");

    /// <summary>用当前快照替换卡片，同时刷新状态区的可见性。</summary>
    private void RefreshNoticeCards()
    {
        NoticeCards.Clear();
        if (NoticeRow != null && _noticeUid == Uid)
            foreach (var card in AccountRecordsNoticeCard.Build(_workspace, NoticeRow.Name)) NoticeCards.Add(card);
        OnPropertyChanged(nameof(HasNoticeCards));
        OnPropertyChanged(nameof(ShowStatusMessage));
        OnPropertyChanged(nameof(HasStatus));
    }

    /// <summary>拒绝来自旧账号、旧悬停目标或已被刷新替换的按钮请求。</summary>
    private bool IsCurrentCard(AccountRecordsNoticeCard? card) => card != null && card.Uid == Uid &&
        card.Uid == _noticeUid && NoticeRow?.Name == card.Name && NoticeCards.Contains(card);

    /// <summary>打开本张卡片对应的物品事件并定位到组内最新事件。</summary>
    [RelayCommand]
    private Task OpenNoticeCardEvents(AccountRecordsNoticeCard? card) => IsCurrentCard(card)
        ? OpenEventRecordsAsync(card!.Uid, card.Name, card.EventIds.FirstOrDefault()) : Task.CompletedTask;

    /// <summary>错误合并卡片使用当前库存异常的明确舍弃入口。</summary>
    [RelayCommand]
    private async Task ResolveNoticeCard(AccountRecordsNoticeCard? card)
    {
        if (!IsCurrentCard(card) || !card!.CanResolve) return;
        await RunManualAsync(() => _store.ResolveAsync(card.Uid, card.Name, card.ErrorId!, false, card.EventIds.FirstOrDefault()));
    }

    /// <summary>只归档展示时固定在本张卡片内的事件；待核实事项不能归档。</summary>
    [RelayCommand]
    private async Task ArchiveNoticeCard(AccountRecordsNoticeCard? card)
    {
        if (!IsCurrentCard(card) || !card!.CanArchive) return;
        if (card.State == "Completed")
        {
            await RunManualAsync(() => _store.ArchiveCompletedEventsAsync(card.Uid, card.Kind, card.EventIds));
            return;
        }
        await RunManualAsync(async () =>
        {
            foreach (var id in card.EventIds) await _store.UpdateEventAsync(card.Uid, id, false, true);
        });
    }

    /// <summary>批量归档仍使用服务端同类完成资格，不被卡片展开解除原有阻塞条件。</summary>
    [RelayCommand]
    private async Task ArchiveNoticeCardBatch(AccountRecordsNoticeCard? card)
    {
        if (!IsCurrentCard(card) || !card!.CanBatchArchive) return;
        await RunManualAsync(() => _store.ArchiveCompletedEventsAsync(card.Uid, card.Kind, card.BatchArchiveIds));
    }
}
