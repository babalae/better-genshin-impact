using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;

namespace BetterGenshinImpact.Core.Script
{
    public class PackageDocumentLoader : DocumentLoader
    {
        private readonly string _scriptRootPath;

        public PackageDocumentLoader(string scriptRootPath)
        {
            _scriptRootPath = Path.GetFullPath(scriptRootPath);
        }

        public override async Task<Document> LoadDocumentAsync(DocumentSettings settings, DocumentInfo? sourceInfo, string specifier, DocumentCategory category, DocumentContextCallback contextCallback)
        {
            string? targetPath = ResolvePhysicalPath(settings, sourceInfo, specifier);

            if (targetPath == null || !File.Exists(targetPath))
            {
                return await Default.LoadDocumentAsync(settings, sourceInfo, specifier, category, contextCallback);
            }

            // 处理 JS 文件的重写
            if (Path.GetExtension(targetPath).ToLower() == ".js")
            {
                string content = await File.ReadAllTextAsync(targetPath);
                string processedCode = RewriteScriptCode(content, targetPath);
                return new StringDocument(new DocumentInfo(targetPath) { Category = ModuleCategory.Standard }, processedCode);
            }

            return await Default.LoadDocumentAsync(settings, sourceInfo, specifier, category, contextCallback);
        }

        /// <summary>
        /// js重写
        /// </summary>
        public string RewriteScriptCode(string code, string currentFilePath)
        {
            if (string.IsNullOrEmpty(code)) return code;

            string result = code.Replace("../../../packages", "packages");

            // 拦截资源导入
            var resourceRegex = new Regex(@"import\s+([\w\d_*$]+|[\s\S]*?)\s+from\s+(['""])([^'""\n]+)(['""])");
            result = resourceRegex.Replace(result, match =>
            {
                string importPart = match.Groups[1].Value.Trim();
                string quote = match.Groups[2].Value;
                string rawPath = match.Groups[3].Value;

                string path = rawPath.Replace("../../../packages", "packages");
                string? resourceFullPath = ResolvePathInternal(null, currentFilePath, path);
                
                if (resourceFullPath != null && File.Exists(resourceFullPath))
                {
                    string normalizedPath = Path.GetRelativePath(_scriptRootPath, resourceFullPath).Replace("\\", "/");
                    bool isJs = resourceFullPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase);
                    bool isImage = IsImageFile(resourceFullPath);

                    // 图片 -> Mat
                    if (isImage)
                    {
                        if (importPart.StartsWith("{")) return match.Value;
                        return $"const {importPart} = file.ReadImageMatSync({quote}{normalizedPath}{quote});";
                    }

                    // 非 JS 资源 -> 文本内容
                    if (!isJs)
                    {
                        if (importPart.StartsWith("{")) return match.Value;
                        return $"const {importPart} = file.ReadTextSync({quote}{normalizedPath}{quote});";
                    }
                }

                return match.Value;
            });

            return result;
        }

        private string? ResolvePhysicalPath(DocumentSettings settings, DocumentInfo? sourceInfo, string specifier)
        {
            return ResolvePathInternal(settings.SearchPath, sourceInfo?.Name, specifier);
        }

        private string? ResolvePathInternal(string? searchPath, string? referrer, string specifier)
        {
            if (specifier.StartsWith("packages/", StringComparison.OrdinalIgnoreCase))
            {
                return ProbeFile(Path.Combine(_scriptRootPath, specifier));
            }

            if (specifier.StartsWith("."))
            {
                if (!string.IsNullOrEmpty(referrer))
                {
                    string? dir = Path.GetDirectoryName(referrer);
                    if (dir != null)
                    {
                        return ProbeFile(Path.GetFullPath(Path.Combine(dir, specifier)));
                    }
                }
            }

            if (!string.IsNullOrEmpty(searchPath))
            {
                var paths = searchPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in paths)
                {
                    string combined = Path.Combine(p, specifier);
                    string? found = ProbeFile(combined);
                    if (found != null) return found;
                }
            }

            return ProbeFile(Path.Combine(_scriptRootPath, specifier));
        }

        private string? ProbeFile(string path)
        {
            try
            {
                if (File.Exists(path)) return path;
                if (File.Exists(path + ".js")) return path + ".js";
            }
            catch { }
            return null;
        }

        private static bool IsImageFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path).ToLower();
            string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".webp" };
            return imageExtensions.Contains(ext);
        }
    }
}
