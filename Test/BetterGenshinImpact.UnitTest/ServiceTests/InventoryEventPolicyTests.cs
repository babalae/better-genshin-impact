using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.ViewModel.Controls;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>验证同类完成事件的显示优先级、阻塞条件和筛选范围。</summary>
[Collection("AccountRecordsManualOperations")]
public sealed class InventoryEventPolicyTests
{
    /// <summary>不同类型、未处理状态及库存错误均不能混入核实完成的归档集合。</summary>
    [Fact]
    public void Candidates_ExcludeBlockedItemsAndOtherCompletionKinds()
    {
        var inventory = new InventoryDocument
        {
            Events = [Event("a", "甲"), Event("b", "乙"), Event("c", "丙"), Event("d", "丁"),
                Event("e", "戊"), Event("f", "己"), Event("g", "庚"),
                Event("z1", "丙", state: "Error"), Event("z2", "丁", state: "Pending"),
                Event("z3", "戊", state: "Warning"), Event("z4", "庚", kind: "Requirement")],
            Errors = new() { ["己"] = new() }
        };
        Assert.Equal(new[] { "a", "b" }, InventoryEventPolicy.GetCompletedArchiveCandidates(inventory, "Delta").Select(item => item.Id));
        Assert.Equal("z4", Assert.Single(InventoryEventPolicy.GetCompletedArchiveCandidates(inventory, "Requirement")).Id);
        inventory.Events.Single(item => item.Id == "z1").Archived = true;
        Assert.Contains(inventory.Events[2], InventoryEventPolicy.GetCompletedArchiveCandidates(inventory, "Delta"));
    }

    /// <summary>搜索隐藏阻塞事件不解除阻塞；选中类型、按钮计数和提交范围保持一致。</summary>
    [Fact]
    public async Task Dialog_ArchivesOnlyVisibleEligibleEventsOfSelectedKind()
    {
        var inventory = new InventoryDocument { Events = [Event("a", "甲"), Event("b", "乙"), Event("c", "丙"),
            Event("z", "丙", state: "Error"), Event("d", "丁", kind: "Requirement")] };
        string? requestedKind = null;
        string[] requestedIds = [];
        var vm = new AccountRecordsEventsViewModel(inventory, "a", (kind, ids) =>
        {
            requestedKind = kind;
            requestedIds = ids.ToArray();
            foreach (var item in inventory.Events.Where(item => ids.Contains(item.Id))) item.Archived = true;
            return Task.FromResult(inventory);
        });
        Assert.True(vm.HasCompletedEvents);
        vm.SearchText = "甲";
        Assert.False(vm.HasCompletedEvents);
        vm.SearchText = "完成";
        vm.Selected = inventory.Events[0];
        Assert.True(vm.HasCompletedEvents);
        vm.Selected = inventory.Events.Last();
        Assert.False(vm.HasCompletedEvents);
        vm.Selected = inventory.Events[0];
        await vm.ArchiveCompletedEventsCommand.ExecuteAsync(null);
        Assert.Equal("Delta", requestedKind);
        Assert.Equal(new[] { "a", "b" }, requestedIds.OrderBy(id => id));
        Assert.False(vm.HasCompletedEvents);
        Assert.False(inventory.Events[2].Archived);
        Assert.False(inventory.Events.Last().Archived);
    }

    /// <summary>创建显式类型和顺序的事件，不依赖实际系统时钟。</summary>
    private static InventoryEvent Event(string id, string name, string kind = "Delta", string state = "Completed") => new()
    {
        Id = id, Name = name, Kind = kind, State = state, Message = state == "Completed" ? "完成" : "待处理"
    };
}
