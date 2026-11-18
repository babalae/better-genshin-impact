using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Service.I18n;

namespace BetterGenshinImpact.Core.Plugins.Hosting;

/// <summary>
/// 读取插件程序集上的 <see cref="UiLanguageAttribute"/>，校验后读入语言文件。
/// 在插件首次加载时调用，校验失败时抛异常，插件进入“加载失败”。
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
    /// <returns>插件提供的语言，没有声明时为空。</returns>
    /// <exception cref="InvalidOperationException">声明不合法，或语言文件不存在、无法解析。</exception>
    public static IReadOnlyList<UiLanguagePack> Read(Assembly assembly, string pluginDirectory)
    {
        var packs = new List<UiLanguagePack>();
        foreach (var info in assembly.GetCustomAttributes<UiLanguageAttribute>())
        {
            // 1. 校验代码、名称，同一插件内代码不重复
            if (!LanguageCodeRegex().IsMatch(info.Code))
            {
                throw new InvalidOperationException($"界面语言代码不合法：{info.Code}。");
            }

            if (string.IsNullOrWhiteSpace(info.DisplayName))
            {
                throw new InvalidOperationException($"界面语言 {info.Code} 缺少显示名称。");
            }

            if (packs.Any(p => string.Equals(p.Code, info.Code, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"界面语言 {info.Code} 重复声明。");
            }

            // 2. 语言文件必须在插件目录内
            var relativePath = string.IsNullOrWhiteSpace(info.File) ? Path.Combine("I18n", $"{info.Code}.json") : info.File;
            if (Path.IsPathRooted(relativePath) || relativePath.Split('/', '\\').Contains(".."))
            {
                throw new InvalidOperationException($"界面语言 {info.Code} 的文件路径必须是插件目录内的相对路径：{relativePath}。");
            }

            var filePath = Path.GetFullPath(Path.Combine(pluginDirectory, relativePath));
            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException($"界面语言 {info.Code} 的文件不存在：{relativePath}。");
            }

            // 3. 读入翻译
            IReadOnlyDictionary<string, string> translations;
            try
            {
                translations = I18nService.ReadTranslationFile(filePath);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"界面语言 {info.Code} 的文件无法解析：{relativePath}，{e.Message}", e);
            }

            packs.Add(new UiLanguagePack(info.Code, info.DisplayName, translations));
        }

        return packs;
    }
}
