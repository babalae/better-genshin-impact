using System;
using System.IO;
using System.Text;
using System.Text.Json;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Plugins.Hosting;

/// <summary>
/// <see cref="IPluginSettingsStore"/> 的实现：插件数据目录下的 settings.json。
/// </summary>
/// <param name="dataDirectory">取插件数据目录，首次调用时创建目录。</param>
/// <param name="logger">插件的日志。</param>
internal sealed class PluginSettingsStore(Func<string> dataDirectory, ILogger logger) : IPluginSettingsStore
{
    /// <summary>
    /// 设置文件名。
    /// </summary>
    private const string FileName = "settings.json";

    /// <summary>
    /// UTF-8 无 BOM。
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// 串行化同一插件的读写。
    /// </summary>
    private readonly object _lock = new();

    /// <inheritdoc />
    public T Load<T>() where T : class, new()
    {
        lock (_lock)
        {
            var path = Path.Combine(dataDirectory(), FileName);
            if (!File.Exists(path))
            {
                return new T();
            }

            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Utf8NoBom), ConfigService.JsonOptions) ?? new T();
            }
            catch (JsonException e)
            {
                // 损坏的文件保留一份，便于排查，然后按默认设置继续
                logger.LogWarning(e, "插件设置文件损坏，已备份为 {Backup} 并使用默认设置", path + ".bak");
                File.Copy(path, path + ".bak", overwrite: true);
                return new T();
            }
        }
    }

    /// <inheritdoc />
    public void Save<T>(T settings) where T : class
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(settings, ConfigService.JsonOptions);
        lock (_lock)
        {
            var directory = dataDirectory();
            var path = Path.Combine(directory, FileName);
            // 带上进程号和 GUID，避免多个实例互相踩到同一个临时文件
            var tempFile = Path.Combine(directory, $"{FileName}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(tempFile, json, Utf8NoBom);
                // 同卷 rename，替换是原子的
                File.Move(tempFile, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
    }
}
