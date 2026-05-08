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
    public class PackageDocumentLoader : DefaultDocumentLoader
    {
        private readonly string _scriptRootPath;

        public PackageDocumentLoader(string scriptRootPath)
        {
            _scriptRootPath = Path.GetFullPath(scriptRootPath);
        }

        public override async Task<Document> LoadDocumentAsync(DocumentSettings settings, DocumentInfo? sourceInfo, string specifier, DocumentCategory category, DocumentContextCallback contextCallback)
        {
            string? targetPath = ResolvePhysicalPath(settings, sourceInfo, specifier);

            // ResolvePhysicalPath 可能因 sourceInfo 为空而失败，直接从脚本根目录兜底
            if (targetPath == null || !File.Exists(targetPath))
            {
                var stripped = Regex.Replace(specifier, @"^(?:\.\.?/)+", "");
                if (!Path.IsPathRooted(stripped))
                {
                    var fullPath = Path.GetFullPath(Path.Combine(_scriptRootPath, stripped));
                    if (fullPath.StartsWith(_scriptRootPath, StringComparison.OrdinalIgnoreCase)
                        && File.Exists(fullPath))
                    {
                        targetPath = fullPath;
                    }
                }
            }

            if (targetPath == null || !File.Exists(targetPath))
            {
                throw new FileNotFoundException($"无法解析模块导入路径: '{specifier}'", specifier);
            }

            // 处理 JS 文件的重写
            if (Path.GetExtension(targetPath).ToLower() == ".js")
            {
                var uri = new Uri(targetPath);

                // 检查缓存
                var cached = GetCachedDocument(uri);
                if (cached != null) return cached;

                string content = await File.ReadAllTextAsync(targetPath);
                string processedCode = RewriteScriptCode(content, targetPath);
                var documentInfo = new DocumentInfo(uri) { Category = ModuleCategory.Standard };
                return CacheDocument(new StringDocument(documentInfo, processedCode), false);
            }

            throw new FileNotFoundException($"不支持的模块导入类型: '{specifier}' (仅支持 .js 文件)", specifier);
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
                string path = match.Groups[3].Value.Replace("../../../packages", "packages");
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
            string? referrer = null;
            if (sourceInfo.HasValue)
            {
                var sourceUri = sourceInfo.Value.Uri;
                if (sourceUri != null && sourceUri.IsAbsoluteUri && sourceUri.IsFile)
                {
                    referrer = sourceUri.LocalPath;
                }
                else if (!string.IsNullOrEmpty(sourceInfo.Value.Name))
                {
                    if (Uri.TryCreate(sourceInfo.Value.Name, UriKind.Absolute, out var nameUri) && nameUri.IsFile)
                    {
                        referrer = nameUri.LocalPath;
                    }
                }
            }

            return ResolvePathInternal(settings.SearchPath, referrer, specifier);
        }

        private string? ResolvePathInternal(string? searchPath, string? referrer, string specifier)
        {
            if (specifier.StartsWith("packages/", StringComparison.OrdinalIgnoreCase))
            {
                return ProbeFile(Path.Combine(_scriptRootPath, specifier));
            }

            var packagesMatch = Regex.Match(specifier, @"^(?:\.\.\/)+(packages/.*)$");
            if (packagesMatch.Success)
            {
                return ProbeFile(Path.Combine(_scriptRootPath, packagesMatch.Groups[1].Value));
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
                var normalized = Path.GetFullPath(path);
                if (File.Exists(normalized)) return normalized;
                if (File.Exists(normalized + ".js")) return normalized + ".js";
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
