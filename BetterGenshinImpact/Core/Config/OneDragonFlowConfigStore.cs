using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
/// 一条龙配置文件（User\OneDragon\{配置名}.json）的读写。
/// 所有实例共用这个目录，写入使用"临时文件 + 原子替换"，其他实例读取时不会读到写了一半的文件
/// </summary>
public static class OneDragonFlowConfigStore
{
    /// <summary>
    /// 一条龙配置文件所在目录
    /// </summary>
    public static readonly string Folder = Global.Absolute(@"User\OneDragon");

    /// <summary>
    /// 不带 BOM 的 UTF-8，与原来 File.WriteAllText 的默认编码一致
    /// </summary>
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

    /// <summary>
    /// 串行化本进程内的写入，避免任务线程与界面线程同时替换同一个文件
    /// </summary>
    private static readonly object WriteLock = new();

    /// <summary>
    /// 一条龙任务把首领讨伐累计领奖次数写入文件后触发，参数为配置名与新的次数。
    /// 只在写入的进程内触发，可能来自任务线程
    /// </summary>
    public static event Action<string, int>? AutoBossCompletedRunCountSaved;

    /// <summary>
    /// 返回配置文件路径
    /// </summary>
    /// <param name="configName">配置名</param>
    /// <returns>配置文件的绝对路径</returns>
    public static string GetFilePath(string configName) => Path.Combine(Folder, $"{configName}.json");

    /// <summary>
    /// 按配置名读取一条龙配置
    /// </summary>
    /// <param name="configName">配置名</param>
    /// <returns>配置；文件不存在时返回 null</returns>
    /// <exception cref="JsonException">配置文件内容无效</exception>
    public static OneDragonFlowConfig? Load(string configName)
    {
        var filePath = GetFilePath(configName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        return JsonConvert.DeserializeObject<OneDragonFlowConfig>(File.ReadAllText(filePath));
    }

    /// <summary>
    /// 把配置原子写入 {配置名}.json
    /// </summary>
    /// <param name="config">要写入的配置</param>
    public static void Write(OneDragonFlowConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        lock (WriteLock)
        {
            WriteAtomically(GetFilePath(config.Name), json);
        }
    }

    /// <summary>
    /// 只更新文件中的首领讨伐累计领奖次数，其余字段保持文件中的最新值，
    /// 避免任务运行期间覆盖用户在界面上（或其他实例中）做的修改
    /// </summary>
    /// <param name="configName">配置名</param>
    /// <param name="count">新的累计领奖次数</param>
    public static void SaveAutoBossCompletedRunCount(string configName, int count)
    {
        lock (WriteLock)
        {
            var config = Load(configName);
            if (config is null)
            {
                return;
            }

            config.AutoBossCompletedRunCount = count;
            WriteAtomically(GetFilePath(configName), JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        AutoBossCompletedRunCountSaved?.Invoke(configName, count);
    }

    /// <summary>
    /// 读取文件中的首领讨伐累计领奖次数
    /// </summary>
    /// <param name="configName">配置名</param>
    /// <param name="count">读到的次数</param>
    /// <returns>文件存在且内容有效时返回 true</returns>
    public static bool TryReadAutoBossCompletedRunCount(string configName, out int count)
    {
        count = 0;
        try
        {
            var config = Load(configName);
            if (config is null)
            {
                return false;
            }

            count = config.AutoBossCompletedRunCount;
            return true;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 先写临时文件，再原子替换目标文件
    /// </summary>
    /// <param name="filePath">目标文件</param>
    /// <param name="json">文件内容</param>
    private static void WriteAtomically(string filePath, string json)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, json, Utf8WithoutBom);
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
