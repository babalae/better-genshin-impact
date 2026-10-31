namespace BetterGenshinImpact.Core.Plugins;

/// <summary>
/// 插件设置的读写，文件为 User/PluginData/&lt;插件标识&gt;/settings.json，使用 System.Text.Json（与主配置相同的选项）。
/// 每个插件一个，线程安全。
/// </summary>
public interface IPluginSettingsStore
{
    /// <summary>
    /// 读取设置。文件不存在时返回新对象；文件损坏时备份为 settings.json.bak 并返回新对象。
    /// 每次调用都会读文件，频繁使用时请自行缓存。
    /// </summary>
    /// <typeparam name="T">设置类型，需要有无参构造函数。</typeparam>
    T Load<T>() where T : class, new();

    /// <summary>
    /// 立即写入设置，先写临时文件再替换，读取方只会看到完整的旧文件或新文件。
    /// </summary>
    /// <typeparam name="T">设置类型。</typeparam>
    /// <param name="settings">要保存的设置。</param>
    void Save<T>(T settings) where T : class;
}
