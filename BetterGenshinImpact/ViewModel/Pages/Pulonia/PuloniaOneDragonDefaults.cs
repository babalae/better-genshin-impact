using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>一条龙入口的默认清单；仅负责创建计划数据，不参与执行。</summary>
internal static class PuloniaOneDragonDefaults
{
    /// <summary>创建旧一条龙顺序的八个任务，不复制全局任务配置。</summary>
    internal static PuloniaTaskPlan CreatePlan(string name) => new()
    {
        Name = name, Purpose = PuloniaTaskPlanPurpose.OneDragon,
        RootTask = new PuloniaTask
        {
            Name = name,
            Children =
            [
                new() { Name = "领取邮件", TaskType = "builtin.claim_mail" },
                new() { Name = "合成树脂", TaskType = "builtin.craft_condensed_resin" },
                new() { Name = "自动秘境", TaskType = "builtin.auto_domain" },
                new() { Name = "自动首领讨伐", TaskType = "builtin.auto_boss" },
                new() { Name = "自动幽境危战", TaskType = "builtin.auto_stygian" },
                new() { Name = "自动地脉花", TaskType = "builtin.auto_ley_line" },
                new() { Name = "领取每日奖励", TaskType = "builtin.daily_rewards" },
                new() { Name = "领取尘歌壶奖励", TaskType = "builtin.serenitea_pot_rewards" }
            ]
        }
    };
}
