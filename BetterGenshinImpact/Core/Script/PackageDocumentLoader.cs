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

            // 检查是否在 packages 目录下
            string relPath = Path.GetRelativePath(_scriptRootPath, currentFilePath);
            relPath = relPath.Replace("\\", "/");
            
            bool isInPackages = relPath.StartsWith("packages", StringComparison.OrdinalIgnoreCase) || 
                                currentFilePath.IndexOf("packages", StringComparison.OrdinalIgnoreCase) != -1;

            if (!isInPackages)
            {
                return result;
            }

            // 资源导入转常量 (仅对 packages 下的代码生效)
            var resourceRegex = new Regex(@"import\s+([\w\d_$]+)\s+from\s+(['""])([^'""\n]+)(['""])");
            result = resourceRegex.Replace(result, match =>
            {
                string varName = match.Groups[1].Value;
                string quote = match.Groups[2].Value;
                string rawPath = match.Groups[3].Value;

                string path = rawPath.Replace("../../../packages", "packages");

                bool isJs = path.EndsWith(".js", StringComparison.OrdinalIgnoreCase);

                if (!isJs)
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
                        if (!path.Contains("/"))
                        {
                            return match.Value;
                        }
                        resourceFullPath = Path.GetFullPath(Path.Combine(_scriptRootPath, path));
                    }

                    // 计算相对于根目录的路径
                    string normalizedPath = Path.GetRelativePath(_scriptRootPath, resourceFullPath).Replace("\\", "/");
                    
                    // 再次检查：如果是 packages 下的资源，才重写为 packages/xxx 的形式
                    if (normalizedPath.StartsWith("packages/", StringComparison.OrdinalIgnoreCase))
                    {
                         // 重写为 const 变量定义
                        return $"const {varName} = {quote}{normalizedPath}{quote};";
                    }
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
                return ProbeFile(fullPath);
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
                        return ProbeFile(fullPath);
                    }
                }
            }

            return null;
        }

        private string? ProbeFile(string path)
        {
            if (File.Exists(path)) return path;
            if (File.Exists(path + ".js")) return path + ".js";
            return null;
        }
    }
}
