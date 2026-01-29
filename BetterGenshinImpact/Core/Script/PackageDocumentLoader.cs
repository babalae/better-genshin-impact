using System;
using System.IO;
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
            // 获取物理路径
            string? targetPath = ResolvePhysicalPath(sourceInfo, specifier);

            if (targetPath == null || !File.Exists(targetPath))
            {
                return await Default.LoadDocumentAsync(settings, sourceInfo, specifier, category, contextCallback);
            }

            // 处理 JS 代码
            bool isJs = targetPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase);

            if (isJs)
            {
                string content = await File.ReadAllTextAsync(targetPath);
                // 计算资源文件的归一化路径
                string processedCode = RewriteScriptCode(content, targetPath);
                return new StringDocument(new DocumentInfo(targetPath) { Category = ModuleCategory.Standard }, processedCode);
            }

            // 处理资源文件
            string repoRelPath = Path.GetRelativePath(_scriptRootPath, targetPath).Replace("\\", "/");
            return new StringDocument(new DocumentInfo(targetPath) { Category = ModuleCategory.Standard }, $"export default \"{repoRelPath}\";");
        }

        /// <summary>
        /// js重写
        /// </summary>
        /// <param name="code">源代码内容</param>
        /// <param name="currentFilePath">当前正在处理的文件物理路径</param>
        public string RewriteScriptCode(string code, string currentFilePath)
        {
            if (string.IsNullOrEmpty(code)) return code;

            // 全局替换 ../../../packages 为 packages
            string result = code.Replace("../../../packages", "packages");

            // 资源导入转常量
            var resourceRegex = new Regex(@"import\s+([\w\d_$]+)\s+from\s+(['""])([^'""\n]+)(['""])");
            result = resourceRegex.Replace(result, match =>
            {
                string varName = match.Groups[1].Value;
                string quote = match.Groups[2].Value;
                string rawPath = match.Groups[3].Value;

                // 三层路径替换
                string path = rawPath.Replace("../../../packages", "packages");

                bool isJs = path.EndsWith(".js", StringComparison.OrdinalIgnoreCase);

                if (path.Contains(".") && !isJs)
                {
                    // 计算该资源的绝对路径
                    string resourceFullPath;
                    if (path.StartsWith("packages/"))
                    {
                        resourceFullPath = Path.Combine(_scriptRootPath, path);
                    }
                    else if (path.StartsWith("."))
                    {
                        string? dir = Path.GetDirectoryName(currentFilePath);
                        resourceFullPath = Path.GetFullPath(Path.Combine(dir ?? _scriptRootPath, path));
                    }
                    else
                    {
                        resourceFullPath = Path.GetFullPath(Path.Combine(_scriptRootPath, path));
                    }

                    // 计算相对于根目录的路径
                    string normalizedPath = Path.GetRelativePath(_scriptRootPath, resourceFullPath).Replace("\\", "/");
                    
                    // 重写为 const
                    return $"const {varName} = {quote}{normalizedPath}{quote};";
                }

                return match.Value;
            });

            return result;
        }

        private string? ResolvePhysicalPath(DocumentInfo? sourceInfo, string specifier)
        {
            int pkgIndex = specifier.IndexOf("packages/", StringComparison.OrdinalIgnoreCase);
            if (pkgIndex >= 0)
            {
                string relPkg = specifier.Substring(pkgIndex);
                string fullPath = Path.GetFullPath(Path.Combine(_scriptRootPath, relPkg));
                if (!File.Exists(fullPath) && !fullPath.Contains(".") && File.Exists(fullPath + ".js")) return fullPath + ".js";
                return fullPath;
            }

            if (specifier.StartsWith("."))
            {
                string? referrer = sourceInfo?.Name;
                if (!string.IsNullOrEmpty(referrer))
                {
                    // 清理头尾
                    string cleanReferrer = referrer;

                    string? dir = Path.GetDirectoryName(cleanReferrer);
                    if (dir != null)
                    {
                        string fullPath = Path.GetFullPath(Path.Combine(dir, specifier));
                        if (!File.Exists(fullPath) && !fullPath.Contains(".") && File.Exists(fullPath + ".js")) return fullPath + ".js";
                        return fullPath;
                    }
                }
            }

            return null;
        }
    }
}
