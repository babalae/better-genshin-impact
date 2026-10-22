using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.Shell;

/// <summary>
/// Shell 任务参数
/// </summary>
public class ShellTaskParam : BaseTaskParam<ShellTask>
{
    /// <summary>
    /// 创建默认参数，默认值与 <see cref="ShellConfig"/> 的默认值一致
    /// </summary>
    public ShellTaskParam() : base(null, null)
    {
    }

    /// <summary>
    /// 是否禁用 Shell 任务，禁用时任务直接跳过
    /// </summary>
    public bool Disable { get; set; }

    /// <summary>
    /// 要执行的命令
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// 最长等待命令返回的时间，单位秒；小于等于 0 时只启动命令，不等待也不获取输出
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// 是否隐藏命令执行窗口
    /// </summary>
    public bool NoWindow { get; set; } = true;

    /// <summary>
    /// 是否向日志打印命令执行输出
    /// </summary>
    public bool Output { get; set; } = true;

    /// <summary>
    /// 按 Shell 执行配置创建参数
    /// </summary>
    /// <param name="command">要执行的命令</param>
    /// <param name="config">Shell 执行配置</param>
    /// <returns>任务参数</returns>
    public static ShellTaskParam BuildFromConfig(string command, ShellConfig config)
    {
        return new ShellTaskParam
        {
            Command = command,
            TimeoutSeconds = config.Timeout,
            NoWindow = config.NoWindow,
            Output = config.Output,
            Disable = config.Disable
        };
    }
}
