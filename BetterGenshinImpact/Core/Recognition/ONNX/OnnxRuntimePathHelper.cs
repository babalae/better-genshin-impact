using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

internal static class OnnxRuntimePathHelper
{
    public static void Prepare(HardwareAccelerationConfig config, ILogger logger)
    {
        if (config.AutoAppendCudaPath)
        {
            AppendCudaPaths(config, logger);
        }

        // AdditionalPath 非空时才处理，避免把空字符串解析为当前目录。
        if (!string.IsNullOrWhiteSpace(config.AdditionalPath))
        {
            AppendToProcessPath(config.AdditionalPath.Split(Path.PathSeparator), logger);
        }
    }

    public static void AppendPluginDirectory(string libraryPath, ILogger logger)
    {
        var directory = Path.GetDirectoryName(libraryPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            AppendToProcessPath([directory], logger);
        }
    }

    private static void AppendCudaPaths(HardwareAccelerationConfig config, ILogger logger)
    {
        string[] filePrefixes = ["cudnn", "nvrtc", "cudart", "cublas"];
        var paths = OnnxRuntimeDependencyChecker.GetCudaSearchDirectories(config)
            .Where(Directory.Exists)
            .Where(path => filePrefixes.Any(prefix =>
            {
                try
                {
                    return Directory.EnumerateFiles(path, $"{prefix}*.dll").Any();
                }
                catch
                {
                    return false;
                }
            }))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        AppendToProcessPath(paths, logger);
    }

    private static void AppendToProcessPath(IEnumerable<string> extraPaths, ILogger logger)
    {
        var validPaths = extraPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (validPaths.Length == 0)
        {
            return;
        }

        var processPaths = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process)
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .ToList() ?? new List<string>();
        processPaths.InsertRange(0, validPaths);
        var updatedPath = string.Join(Path.PathSeparator,
            processPaths.Distinct(StringComparer.OrdinalIgnoreCase));
        Environment.SetEnvironmentVariable("PATH", updatedPath, EnvironmentVariableTarget.Process);
        logger.LogDebug("[ONNX] 已附加原生依赖搜索路径：{Paths}", string.Join(Path.PathSeparator, validPaths));
    }
}
