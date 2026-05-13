using System;
using System.Linq;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Core.Script.Dependence;

/// <summary>
/// 战斗策略文件访问类
/// 提供JS脚本环境访问 User\AutoFight 目录下战斗策略文件的方法
/// </summary>
public class StrategyFile
{
    private readonly LimitedFile _strategyFile = new(Global.Absolute(@"User\AutoFight"));

    /// <summary>
    /// 判断 User\AutoFight 目录下的路径是否为文件夹
    /// </summary>
    /// <param name="subPath">相对于 User\AutoFight 的路径</param>
    /// <returns>是文件夹返回 true，否则返回 false</returns>
    public bool IsFolder(string subPath) => _strategyFile.IsFolder(subPath);

    /// <summary>
    /// 判断 User\AutoFight 目录下的路径是否为文件
    /// </summary>
    /// <param name="subPath">相对于 User\AutoFight 的路径</param>
    /// <returns>是文件返回 true，否则返回 false</returns>
    public bool IsFile(string subPath) => _strategyFile.IsFile(subPath);

    /// <summary>
    /// 判断 User\AutoFight 目录下的路径是否存在
    /// </summary>
    /// <param name="subPath">相对于 User\AutoFight 的路径</param>
    /// <returns>存在返回 true，否则返回 false</returns>
    public bool IsExists(string subPath) => _strategyFile.IsExists(subPath);

    /// <summary>
    /// 读取 User\AutoFight 目录下指定文件夹的内容（非递归方式）
    /// 目录不存在时返回空数组，不会自动创建目录
    /// </summary>
    /// <param name="subPath">相对于 User\AutoFight 的子目录路径，默认为根目录</param>
    /// <returns>文件夹内所有文件和文件夹的相对路径数组，出错时返回空数组</returns>
    public string[] ReadPathSync(string subPath = "./") => _strategyFile.ReadPathSync(subPath);
}
