using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 盾奶位名单（自动识别"自动"模式专用）。
/// 数据保存在 User/GuardianAvatarList.txt：每行一个角色，标准中文名需与 combat_avatar.json 一致；
/// 需要长按 E 的角色在名字后加"长按"（如：钟离长按）。# 开头为注释行。
/// 与寻路走路开盾使用的 <see cref="PathingConditionConfig.AvatarConditions"/> 相互独立，互不影响。
/// </summary>
public static class GuardianAvatarListStore
{
    public const string FileRelativePath = @"User\GuardianAvatarList.txt";

    /// <summary>
    /// 代码内置默认名单（角色标准中文名 -> 是否长按E）。
    /// 当 User 名单文件不存在或为空时使用；顺序即识别优先级（越靠前越先被识别为盾奶位）。
    /// 角色名必须与 combat_avatar.json 中 name 完全一致。
    /// </summary>
    private static readonly (string Name, bool Hold)[] DefaultList =
    {
        // 盾位
        ("钟离", true), // 长按E开玉璋护盾
        ("尼可", false),
        ("伊涅芙", false),
        ("茜特菈莉", false),
        ("莱依拉", false),
        ("绮良良", false),
        ("蓝砚", false),
        ("迪奥娜", true), // 长按E护盾更厚
        // 奶位
        ("白术", false),
        ("七七", false),
        ("珊瑚宫心海", false),
        ("希格雯", false),
        ("芭芭拉", false),
        ("久岐忍", false),
    };

    /// <summary>
    /// 名单文件读写共用的进程内锁：避免战斗线程读取与设置窗口保存/还原并发时的共享冲突。
    /// 跨进程（多开、外部编辑器）场景由“临时文件原子替换 + 读取失败回退内置名单”兜底。
    /// </summary>
    private static readonly object FileLock = new();

    /// <summary>
    /// 读取当前生效的盾奶名单（User 文件优先；文件缺失/为空/全注释/读取失败时回退代码内置默认）。
    /// 文件中的非法角色名会被跳过并告警。
    /// </summary>
    public static List<(string Name, bool Hold)> Load()
    {
        var path = Global.Absolute(FileRelativePath);
        var result = new List<(string Name, bool Hold)>();

        lock (FileLock)
        {
            try
            {
                if (File.Exists(path))
                {
                    foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        var line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                        {
                            continue;
                        }

                        var hold = false;
                        var name = line;
                        if (name.EndsWith("长按", StringComparison.Ordinal))
                        {
                            hold = true;
                            name = name[..^"长按".Length].Trim();
                        }

                        if (string.IsNullOrEmpty(name) || !DefaultAutoFightConfig.CombatAvatarNames.Contains(name))
                        {
                            Console.WriteLine($"盾奶位名单忽略未知角色名：{rawLine.Trim()}");
                            continue;
                        }

                        result.Add((name, hold));
                    }
                }
            }
            catch (Exception e)
            {
                // 文件正被占用/正在写入/被外部编辑器锁定：回退内置默认，不让战斗读取中断
                Console.WriteLine($"盾奶位名单读取失败，使用内置默认名单：{e.Message}");
                result.Clear();
            }
        }

        // 空名单（文件不存在、读取失败或没有有效行）时回退到内置默认名单
        if (result.Count == 0)
        {
            result.AddRange(DefaultList);
        }

        return result;
    }

    /// <summary>
    /// 将名单写回 User 文件（含注释头，便于用户手工编辑）。
    /// 采用同目录临时文件 + 原子替换，避免写一半时被战斗读取到残缺内容。
    /// </summary>
    public static void Save(IEnumerable<(string Name, bool Hold)> list)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 自动战斗盾奶位名单（GuardianAvatar=\"自动\"时按此顺序匹配队伍中的盾奶角色）");
        sb.AppendLine("# 每行一个角色，使用游戏内标准中文名（须与 combat_avatar.json 一致）；");
        sb.AppendLine("# 需要长按 E 开盾/治疗的角色在名字后加“长按”，如：钟离长按");
        sb.AppendLine("# # 开头为注释行；文件为空或不存在时使用内置默认名单。");
        foreach (var (name, hold) in list)
        {
            sb.AppendLine(hold ? name + "长按" : name);
        }

        var path = Global.Absolute(FileRelativePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        lock (FileLock)
        {
            // 每次保存使用唯一临时文件名，避免多进程同时保存时互相覆盖同一个 .tmp 文件
            var tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmpPath, sb.ToString(), new UTF8Encoding(false));
            File.Move(tmpPath, path, true);
        }
    }

    /// <summary>
    /// 还原为代码内置默认名单（并写回文件）。
    /// </summary>
    public static void ResetToDefault()
    {
        Save(DefaultList);
    }

    /// <summary>
    /// 原样保存用户在编辑窗口中的文本（保留注释与排版）到 User 文件。
    /// 与 <see cref="Save" /> 相同：同目录临时文件 + 原子替换，避免战斗读取到半写内容。
    /// </summary>
    public static void SaveRawText(string content)
    {
        var path = Global.Absolute(FileRelativePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        lock (FileLock)
        {
            // 每次保存使用唯一临时文件名，避免多进程同时保存时互相覆盖同一个 .tmp 文件
            var tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmpPath, content, new UTF8Encoding(false));
            File.Move(tmpPath, path, true);
        }
    }
}
