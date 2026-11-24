using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BetterGenshinImpact.GameTask.I18n;
using BetterGenshinImpact.Service.I18n;

namespace BetterGenshinImpact.Core.Plugins.Hosting;

/// <summary>
/// 读取插件程序集上的 <see cref="UiLanguageAttribute"/>（界面语言）和 <see cref="GameLanguageAttribute"/>（游戏语言），校验后读入语言文件。
/// 在插件首次加载时调用，校验失败时抛异常，插件进入“加载失败”。两种语言的校验规则相同，游戏语言额外要求代码是 .NET 能识别的文化名。
/// </summary>
internal static partial class PluginLanguageReader
{
    /// <summary>
    /// 语言代码：2～3 个小写字母，后接若干段由连字符分隔的子标签，如 it、pt-BR、zh-Hant。
    /// </summary>
    [GeneratedRegex("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageCodeRegex();

    /// <summary>
    /// 读取插件声明的全部界面语言。
    /// </summary>
    /// <param name="assembly">插件入口程序集。</param>
    /// <param name="pluginDirectory">插件目录的绝对路径。</param>
    /// <returns>插件提供的界面语言，没有声明时为空。</returns>
    /// <exception cref="InvalidOperationException">声明不合法，或语言文件不存在、无法解析。</exception>
    public static IReadOnlyList<UiLanguagePack> ReadUiLanguages(Assembly assembly, string pluginDirectory)
    {
        var packs = new List<UiLanguagePack>();
        foreach (var info in assembly.GetCustomAttributes<UiLanguageAttribute>())
        {
            const string kind = "界面语言";
            Validate(kind, info.Code, info.DisplayName, packs.Select(p => p.Code));
            var filePath = ResolveFile(kind, info.Code, info.File, "I18n", pluginDirectory);
            var translations = ReadFile(kind, info.Code, filePath, I18nService.ReadTranslationFile);
            packs.Add(new UiLanguagePack(info.Code, info.DisplayName, translations));
        }

        return packs;
    }

    /// <summary>
    /// 读取插件声明的全部游戏语言。
    /// </summary>
    /// <param name="assembly">插件入口程序集。</param>
    /// <param name="pluginDirectory">插件目录的绝对路径。</param>
    /// <returns>插件提供的游戏语言，没有声明时为空。</returns>
    /// <exception cref="InvalidOperationException">声明不合法，或文本文件不存在、无法解析。</exception>
    public static IReadOnlyList<GameTextPack> ReadGameLanguages(Assembly assembly, string pluginDirectory)
    {
        var packs = new List<GameTextPack>();
        foreach (var info in assembly.GetCustomAttributes<GameLanguageAttribute>())
        {
            const string kind = "游戏语言";
            Validate(kind, info.Code, info.DisplayName, packs.Select(p => p.Code));

            // 任务会按游戏语言创建 CultureInfo 解析数字等，代码必须能被 .NET 识别
            try
            {
                _ = CultureInfo.GetCultureInfo(info.Code);
            }
            catch (CultureNotFoundException)
            {
                throw new InvalidOperationException($"游戏语言代码不是可识别的文化名：{info.Code}。");
            }

            var filePath = ResolveFile(kind, info.Code, info.File, "GameText", pluginDirectory);
            // 不认识的 Key 不报错：插件可能比主程序新，多出的条目只是用不到
            var table = ReadFile(kind, info.Code, filePath, GameTextService.ReadTableFile);
            packs.Add(new GameTextPack(info.Code, info.DisplayName, table));
        }

        return packs;
    }

    /// <summary>
    /// 校验代码格式、显示名称，以及同一插件内同类语言的代码不重复。
    /// </summary>
    /// <param name="kind">语言类别，用于错误信息。</param>
    /// <param name="code">语言代码。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="existingCodes">已读取的同类语言代码。</param>
    private static void Validate(string kind, string code, string displayName, IEnumerable<string> existingCodes)
    {
        if (!LanguageCodeRegex().IsMatch(code))
        {
            throw new InvalidOperationException($"{kind}代码不合法：{code}。");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException($"{kind} {code} 缺少显示名称。");
        }

        if (existingCodes.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{kind} {code} 重复声明。");
        }
    }

    /// <summary>
    /// 确定语言文件的绝对路径：必须是插件目录内的相对路径，且文件存在。
    /// </summary>
    /// <param name="kind">语言类别，用于错误信息。</param>
    /// <param name="code">语言代码。</param>
    /// <param name="file">特性中指定的相对路径，为空时为 &lt;默认目录&gt;/&lt;语言代码&gt;.json。</param>
    /// <param name="defaultDirectory">默认目录，相对插件目录。</param>
    /// <param name="pluginDirectory">插件目录的绝对路径。</param>
    private static string ResolveFile(string kind, string code, string? file, string defaultDirectory, string pluginDirectory)
    {
        var relativePath = string.IsNullOrWhiteSpace(file) ? Path.Combine(defaultDirectory, $"{code}.json") : file;
        if (Path.IsPathRooted(relativePath) || relativePath.Split('/', '\\').Contains(".."))
        {
            throw new InvalidOperationException($"{kind} {code} 的文件路径必须是插件目录内的相对路径：{relativePath}。");
        }

        var filePath = Path.GetFullPath(Path.Combine(pluginDirectory, relativePath));
        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"{kind} {code} 的文件不存在：{relativePath}。");
        }

        return filePath;
    }

    /// <summary>
    /// 读入语言文件，解析失败时转为带语言信息的异常。
    /// </summary>
    /// <param name="kind">语言类别，用于错误信息。</param>
    /// <param name="code">语言代码。</param>
    /// <param name="filePath">文件的绝对路径。</param>
    /// <param name="read">文件读取方法。</param>
    private static IReadOnlyDictionary<string, string> ReadFile(string kind, string code, string filePath,
        Func<string, IReadOnlyDictionary<string, string>> read)
    {
        try
        {
            return read(filePath);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"{kind} {code} 的文件无法解析：{Path.GetFileName(filePath)}，{e.Message}", e);
        }
    }
}
