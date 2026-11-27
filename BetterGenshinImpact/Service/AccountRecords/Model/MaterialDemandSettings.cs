namespace BetterGenshinImpact.Service.AccountRecords.Model;

/// <summary>供自动任务与脚本读取的需求判断设置快照，不包含界面或存储选项。</summary>
/// <param name="UseDeltaForDemands">是否允许有效增量直接确认需求达标。</param>
/// <param name="UseFamiliesForDemands">是否计入同族材料合成。</param>
/// <param name="DemandCraftingReservePercent">合成收益预留百分比，范围 0～20；不支持收益加成的材料仍应按零处理。</param>
public sealed record MaterialDemandSettings(bool UseDeltaForDemands, bool UseFamiliesForDemands, int DemandCraftingReservePercent);
