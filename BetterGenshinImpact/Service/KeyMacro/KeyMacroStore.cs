using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.KeyMacro;
using BetterGenshinImpact.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace BetterGenshinImpact.Service.KeyMacro;

/// <summary>
/// 按键宏目录 User/BgiMacro 的读写。每个 txt 文件是一个宏，文件名即名称。
/// 界面修改后防抖写入；监听目录，外部修改、新增、删除防抖后载入。全部方法都在 UI 线程上调用
/// </summary>
public sealed class KeyMacroStore
{
    /// <summary>
    /// 名称最大长度
    /// </summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// 界面修改后写入文件的防抖时间
    /// </summary>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 监听到外部修改后重新载入的防抖时间
    /// </summary>
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 宏文件编码：UTF-8 无 BOM
    /// </summary>
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// Windows 保留的设备名，不能作为文件名
    /// </summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly ILogger<KeyMacroStore> _logger;

    /// <summary>
    /// 所属 UI 线程
    /// </summary>
    private readonly Dispatcher _dispatcher;

    /// <summary>
    /// 界面修改后尚未写入文件的宏
    /// </summary>
    private readonly HashSet<KeyMacroDefinition> _dirty = [];

    /// <summary>
    /// 每个文件最近一次读到或写入的内容。监听到的变化与它相同时说明是自己写入的，忽略
    /// </summary>
    private readonly Dictionary<string, string> _knownContents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 写入防抖计时器
    /// </summary>
    private readonly DispatcherTimer _saveTimer;

    /// <summary>
    /// 重新载入防抖计时器
    /// </summary>
    private readonly DispatcherTimer _reloadTimer;

    /// <summary>
    /// 目录监听
    /// </summary>
    private FileSystemWatcher? _watcher;

    /// <summary>
    /// 是否已载入
    /// </summary>
    private bool _loaded;

    /// <summary>
    /// 正在把文件内容写入宏定义，此时的属性变化不触发保存
    /// </summary>
    private bool _applyingFile;

    /// <summary>
    /// 宏目录
    /// </summary>
    public string DirectoryPath { get; } = Global.Absolute(@"User\BgiMacro");

    /// <summary>
    /// 全部宏，按名称排序
    /// </summary>
    public ObservableCollection<KeyMacroDefinition> Macros { get; } = [];

    /// <summary>
    /// 创建宏目录存储
    /// </summary>
    public KeyMacroStore(ILogger<KeyMacroStore> logger)
    {
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;
        _saveTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Flush();
        };
        _reloadTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = ReloadDelay };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            Reload();
        };
    }

    /// <summary>
    /// 首次调用时创建目录、载入全部宏并开始监听；之后调用无作用
    /// </summary>
    public void EnsureLoaded()
    {
        _dispatcher.VerifyAccess();
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "创建按键宏目录失败：{Path}", DirectoryPath);
            return;
        }

        Reload();
        StartWatcher();
    }

    /// <summary>
    /// 重新扫描目录，与内存中的宏同步。界面上尚未保存的修改优先，不被文件内容覆盖
    /// </summary>
    public void Reload()
    {
        _dispatcher.VerifyAccess();
        string[] files;
        try
        {
            files = Directory.Exists(DirectoryPath)
                ? Directory.GetFiles(DirectoryPath, "*.txt", SearchOption.TopDirectoryOnly)
                : [];
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "读取按键宏目录失败：{Path}", DirectoryPath);
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            seen.Add(file);
            string content;
            try
            {
                content = File.ReadAllText(file, Encoding.UTF8);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // 文件可能正被其他程序写入，保留现有内容，下次变化时再读
                _logger.LogDebug(e, "读取按键宏文件失败：{Path}", file);
                continue;
            }

            var existing = Find(file);
            if (existing == null)
            {
                var definition = new KeyMacroDefinition
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FilePath = file
                };
                Apply(definition, KeyMacroFileFormat.Parse(content));
                _knownContents[file] = content;
                Insert(definition);
                continue;
            }

            if (_dirty.Contains(existing))
            {
                continue;
            }

            if (_knownContents.TryGetValue(file, out var known) && known == content)
            {
                continue;
            }

            Apply(existing, KeyMacroFileFormat.Parse(content));
            _knownContents[file] = content;
        }

        // 文件已被删除或改名的宏从列表移除
        foreach (var removed in Macros.Where(m => !seen.Contains(m.FilePath)).ToList())
        {
            Remove(removed);
        }
    }

    /// <summary>
    /// 立即写入全部尚未保存的修改
    /// </summary>
    public void Flush()
    {
        foreach (var definition in _dirty.ToList())
        {
            Save(definition);
        }

        _dirty.Clear();
    }

    /// <summary>
    /// 新建宏并写入文件
    /// </summary>
    /// <param name="baseName">名称，重名时自动加序号</param>
    /// <param name="mode">触发方式</param>
    /// <param name="script">脚本</param>
    /// <returns>新建的宏</returns>
    /// <exception cref="IOException">写入文件失败</exception>
    public KeyMacroDefinition Create(string baseName, KeyMacroTriggerMode mode, string script)
    {
        return CreateCore(new KeyMacroFileContent(string.Empty, HotKeyTypeEnum.KeyboardMonitor, mode, true, script), baseName);
    }

    /// <summary>
    /// 复制宏。快捷键不复制，避免与原宏冲突
    /// </summary>
    /// <param name="source">要复制的宏</param>
    /// <returns>新建的副本</returns>
    public KeyMacroDefinition Duplicate(KeyMacroDefinition source)
    {
        return CreateCore(new KeyMacroFileContent(string.Empty, source.HotkeyType, source.TriggerMode, source.Enabled, source.Script),
            source.Name + " 副本");
    }

    /// <summary>
    /// 导入宏文件：复制到宏目录，重名时自动加序号。导入的快捷键清空，避免与本机已有快捷键冲突
    /// </summary>
    /// <param name="paths">txt 文件路径</param>
    /// <returns>成功导入的宏</returns>
    public IReadOnlyList<KeyMacroDefinition> Import(IEnumerable<string> paths)
    {
        var imported = new List<KeyMacroDefinition>();
        foreach (var path in paths)
        {
            try
            {
                var content = KeyMacroFileFormat.Parse(File.ReadAllText(path, Encoding.UTF8));
                imported.Add(CreateCore(content with { Hotkey = string.Empty }, Path.GetFileNameWithoutExtension(path)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(e, "导入按键宏失败：{Path}", path);
            }
        }

        return imported;
    }

    /// <summary>
    /// 重命名宏，同时重命名文件
    /// </summary>
    /// <param name="definition">要重命名的宏</param>
    /// <param name="newName">新名称</param>
    /// <param name="error">失败原因</param>
    /// <returns>是否成功</returns>
    public bool TryRename(KeyMacroDefinition definition, string newName, out string? error)
    {
        newName = newName.Trim();
        if (newName == definition.Name)
        {
            error = null;
            return true;
        }

        error = ValidateName(newName, definition);
        if (error != null)
        {
            return false;
        }

        // 先把待写入的修改写到原文件，再移动
        if (_dirty.Remove(definition))
        {
            Save(definition);
        }

        var newPath = Path.Combine(DirectoryPath, newName + ".txt");
        try
        {
            File.Move(definition.FilePath, newPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"重命名失败：{e.Message}";
            return false;
        }

        if (_knownContents.Remove(definition.FilePath, out var content))
        {
            _knownContents[newPath] = content;
        }

        definition.FilePath = newPath;
        definition.Name = newName;
        // 改名后按新名称重新排序。用 Move 而不是先移除再插入，订阅方不会误以为宏被删除
        var oldIndex = Macros.IndexOf(definition);
        var newIndex = 0;
        foreach (var other in Macros)
        {
            if (!ReferenceEquals(other, definition)
                && string.Compare(other.Name, newName, StringComparison.CurrentCultureIgnoreCase) <= 0)
            {
                newIndex++;
            }
        }

        if (oldIndex >= 0 && oldIndex != newIndex)
        {
            Macros.Move(oldIndex, newIndex);
        }

        return true;
    }

    /// <summary>
    /// 删除宏：文件移到回收站
    /// </summary>
    /// <param name="definition">要删除的宏</param>
    /// <exception cref="IOException">删除失败</exception>
    public void Delete(KeyMacroDefinition definition)
    {
        _dirty.Remove(definition);
        if (File.Exists(definition.FilePath))
        {
            // Microsoft.VisualBasic.FileIO 与 System.IO 有同名类型（SearchOption），这里写全名
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(definition.FilePath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }

        _knownContents.Remove(definition.FilePath);
        Remove(definition);
    }

    /// <summary>
    /// 检查名称是否可用
    /// </summary>
    /// <param name="name">名称</param>
    /// <param name="self">正在改名的宏，检查重名时排除它自己</param>
    /// <returns>不可用的原因，可用时返回 null</returns>
    public string? ValidateName(string name, KeyMacroDefinition? self)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "名称不能为空";
        }

        if (name.Length > MaxNameLength)
        {
            return $"名称不能超过 {MaxNameLength} 个字符";
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "名称不能包含 \\ / : * ? \" < > | 等字符";
        }

        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            return "名称不能以点或空格结尾";
        }

        if (ReservedNames.Contains(name))
        {
            return "名称不能使用系统保留名";
        }

        var exists = Macros.Any(m => !ReferenceEquals(m, self) && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                     || (!string.Equals(self?.Name, name, StringComparison.OrdinalIgnoreCase)
                         && File.Exists(Path.Combine(DirectoryPath, name + ".txt")));
        return exists ? "已有同名的宏" : null;
    }

    /// <summary>
    /// 按内容新建宏文件
    /// </summary>
    private KeyMacroDefinition CreateCore(KeyMacroFileContent content, string baseName)
    {
        EnsureLoaded();
        var name = GetUniqueName(baseName);
        var definition = new KeyMacroDefinition
        {
            Name = name,
            FilePath = Path.Combine(DirectoryPath, name + ".txt")
        };
        Apply(definition, content);
        var text = KeyMacroFileFormat.Format(definition);
        File.WriteAllText(definition.FilePath, text, Utf8NoBom);
        _knownContents[definition.FilePath] = text;
        Insert(definition);
        return definition;
    }

    /// <summary>
    /// 生成不重名的名称：清理非法字符，重名时加序号
    /// </summary>
    private string GetUniqueName(string baseName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(baseName.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
        if (cleaned.Length == 0 || ReservedNames.Contains(cleaned))
        {
            cleaned = "按键宏";
        }

        if (cleaned.Length > MaxNameLength - 4)
        {
            cleaned = cleaned[..(MaxNameLength - 4)];
        }

        var name = cleaned;
        for (var i = 2; ValidateName(name, null) != null; i++)
        {
            name = $"{cleaned} {i}";
        }

        return name;
    }

    /// <summary>
    /// 把文件内容写入宏定义，期间的属性变化不触发保存
    /// </summary>
    private void Apply(KeyMacroDefinition definition, KeyMacroFileContent content)
    {
        _applyingFile = true;
        try
        {
            definition.Hotkey = content.Hotkey;
            definition.HotkeyType = content.HotkeyType;
            definition.TriggerMode = content.TriggerMode;
            definition.Enabled = content.Enabled;
            definition.Script = content.Script;
        }
        finally
        {
            _applyingFile = false;
        }
    }

    /// <summary>
    /// 写入一个宏的文件。内容与上次写入相同时跳过
    /// </summary>
    private void Save(KeyMacroDefinition definition)
    {
        var text = KeyMacroFileFormat.Format(definition);
        if (_knownContents.TryGetValue(definition.FilePath, out var known) && known == text)
        {
            return;
        }

        try
        {
            File.WriteAllText(definition.FilePath, text, Utf8NoBom);
            _knownContents[definition.FilePath] = text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(e, "保存按键宏「{Name}」失败：{Message}", definition.Name, e.Message);
        }
    }

    /// <summary>
    /// 按文件路径查找已载入的宏
    /// </summary>
    private KeyMacroDefinition? Find(string path)
    {
        return Macros.FirstOrDefault(m => string.Equals(m.FilePath, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 按名称排序插入，并订阅修改
    /// </summary>
    private void Insert(KeyMacroDefinition definition)
    {
        definition.PropertyChanged += OnDefinitionPropertyChanged;
        Macros.Insert(GetSortedIndex(definition.Name), definition);
    }

    /// <summary>
    /// 从列表移除，并取消订阅
    /// </summary>
    private void Remove(KeyMacroDefinition definition)
    {
        definition.PropertyChanged -= OnDefinitionPropertyChanged;
        _dirty.Remove(definition);
        Macros.Remove(definition);
    }

    /// <summary>
    /// 名称在排序列表中的插入位置
    /// </summary>
    private int GetSortedIndex(string name)
    {
        var index = 0;
        while (index < Macros.Count && string.Compare(Macros[index].Name, name, StringComparison.CurrentCultureIgnoreCase) <= 0)
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// 界面修改宏定义后防抖写入。名称由 TryRename 处理，不在这里保存
    /// </summary>
    private void OnDefinitionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_applyingFile || sender is not KeyMacroDefinition definition || e.PropertyName == nameof(KeyMacroDefinition.Name))
        {
            return;
        }

        _dirty.Add(definition);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// 监听宏目录，变化后防抖重新载入
    /// </summary>
    private void StartWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(DirectoryPath, "*.txt")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            FileSystemEventHandler onChanged = (_, _) => _dispatcher.BeginInvoke(ScheduleReload);
            _watcher.Changed += onChanged;
            _watcher.Created += onChanged;
            _watcher.Deleted += onChanged;
            _watcher.Renamed += (_, _) => _dispatcher.BeginInvoke(ScheduleReload);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e)
        {
            // 监听失败只影响外部修改的自动载入，切换到按键宏页时仍会重新扫描
            _logger.LogWarning(e, "监听按键宏目录失败：{Path}", DirectoryPath);
        }
    }

    /// <summary>
    /// 重新开始载入防抖计时
    /// </summary>
    private void ScheduleReload()
    {
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }
}
