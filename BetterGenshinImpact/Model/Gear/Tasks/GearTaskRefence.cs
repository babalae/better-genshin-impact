namespace BetterGenshinImpact.Model.Gear.Tasks;

/// <summary>
/// 针对GearTask的引用类
/// </summary>
public class GearTaskRefence
{
    /// <summary>
    /// 任务名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// GearTaskViewModel 的文件路径
    /// </summary>
    public string GearTaskFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 解析并转换为 BaseGearTask 对象
    /// </summary>
    /// <returns></returns>
    public BaseGearTask ToGearTask()
    {
        return BaseGearTask.ReadFileToBaseGearTasks(GearTaskFilePath);
    }
}