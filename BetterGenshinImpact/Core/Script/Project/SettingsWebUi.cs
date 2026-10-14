using System;
using System.IO;
using BetterGenshinImpact.Core.Script.Utils;

namespace BetterGenshinImpact.Core.Script.Project;

[Serializable]
public class SettingsWebUi
{
    public string Html { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;

    public (string HtmlPath, string ScriptPath) Resolve(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(Html))
        {
            throw new Exception("manifest.json: settings_web_ui.html is required.");
        }

        if (string.IsNullOrWhiteSpace(Script))
        {
            throw new Exception("manifest.json: settings_web_ui.script is required.");
        }

        var htmlPath = ResolveProjectFile(projectPath, Html, ".html");
        var scriptPath = ResolveProjectFile(projectPath, Script, ".js");
        return (htmlPath, scriptPath);
    }

    public static string ResolveProjectFile(string projectPath, string relativePath, string requiredExtension)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException($"路径必须位于脚本项目目录内: {relativePath}");
        }

        var fullPath = ScriptUtils.NormalizePath(projectPath, relativePath);
        if (!fullPath.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"路径扩展名必须是 {requiredExtension}: {relativePath}");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"文件不存在: {relativePath}", fullPath);
        }

        return fullPath;
    }
}
