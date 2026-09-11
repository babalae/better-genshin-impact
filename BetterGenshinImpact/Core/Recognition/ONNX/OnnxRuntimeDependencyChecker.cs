using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using Microsoft.ML.OnnxRuntime;
using Microsoft.Win32;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

internal sealed record OnnxRuntimePluginCompatibility(bool IsCompatible, string Message);

/// <summary>
/// 只检查 Plugin EP 启动前能够确定的依赖。最终是否兼容仍以 ORT 注册插件的结果为准。
/// </summary>
internal static class OnnxRuntimeDependencyChecker
{
    public static OnnxRuntimePluginCompatibility Check(
        OnnxRuntimePluginDescriptor descriptor, HardwareAccelerationConfig config)
    {
        var currentOrtVersion = typeof(OrtEnv).Assembly.GetName().Version ?? new Version();
        if (Version.TryParse(descriptor.MinimumOrtVersion, out var minimumOrtVersion) &&
            currentOrtVersion < minimumOrtVersion)
        {
            return new OnnxRuntimePluginCompatibility(false,
                $"需要 ORT {minimumOrtVersion} 或更高版本，当前为 {currentOrtVersion}。");
        }

        if (descriptor.Provider != InferenceDeviceType.Cuda || descriptor.CudaMajor is not (12 or 13))
        {
            return new OnnxRuntimePluginCompatibility(true, "依赖检查通过");
        }

        var searchDirectories = GetCudaSearchDirectories(config, descriptor.CudaMajor.Value).ToArray();
        var missing = new List<string>();
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "nvcuda.dll")))
        {
            missing.Add("NVIDIA 驱动");
        }

        if (!ContainsFile(searchDirectories, $"cudart64_{descriptor.CudaMajor}*.dll") ||
            !ContainsFile(searchDirectories, $"cublas64_{descriptor.CudaMajor}*.dll"))
        {
            missing.Add($"CUDA {descriptor.CudaMajor}");
        }

        if (!ContainsFile(searchDirectories, "cudnn64_9.dll"))
        {
            missing.Add("cuDNN 9");
        }

        return missing.Count == 0
            ? new OnnxRuntimePluginCompatibility(true, "CUDA、cuDNN 与 NVIDIA 驱动依赖检查通过")
            : new OnnxRuntimePluginCompatibility(false,
                $"缺少或未发现：{string.Join("、", missing)}。请检查注册表、PATH、CUDA_PATH、CUDNN_PATH 或附加 PATH。");
    }

    internal static IReadOnlyList<string> GetCudaSearchDirectories(
        HardwareAccelerationConfig config, int? cudaMajor = null)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddPathList(candidates, config.AdditionalPath);
        foreach (var target in new[]
                 {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine
                 })
        {
            AddPathList(candidates, Environment.GetEnvironmentVariable("PATH", target));
            AddRoot(candidates, Environment.GetEnvironmentVariable("CUDA_PATH", target));
                AddRoot(candidates, Environment.GetEnvironmentVariable("CUDNN_PATH", target));
            foreach (var major in cudaMajor is null ? new[] { 12, 13 } : new[] { cudaMajor.Value })
            {
                for (var minor = 0; minor <= 9; minor++)
                {
                    AddRoot(candidates,
                        Environment.GetEnvironmentVariable($"CUDA_PATH_V{major}_{minor}", target));
                }
            }
        }

        try
        {
            using var cudaRoot = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\NVIDIA Corporation\GPU Computing Toolkit\CUDA");
            if (cudaRoot is not null)
            {
                AddRoot(candidates, cudaRoot.GetValue("InstallDir")?.ToString());
                foreach (var subKeyName in cudaRoot.GetSubKeyNames())
                {
                    using var versionKey = cudaRoot.OpenSubKey(subKeyName);
                    AddRoot(candidates, versionKey?.GetValue("InstallDir")?.ToString());
                }
            }
        }
        catch
        {
            // 注册表不可访问时仍继续检查 PATH 和常见安装目录。
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        AddImmediateChildren(candidates, Path.Combine(programFiles,
            "NVIDIA GPU Computing Toolkit", "CUDA"));
        AddImmediateChildren(candidates, Path.Combine(programFiles, "NVIDIA", "CUDNN"));

        return candidates.Where(Directory.Exists).ToArray();
    }

    private static void AddPathList(ISet<string> candidates, string? value)
    {
        foreach (var path in value?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
        {
            AddRoot(candidates, path);
        }
    }

    private static void AddRoot(ISet<string> candidates, string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        root = root.Trim().Trim('"');
        candidates.Add(root);
        candidates.Add(Path.Combine(root, "bin"));
        candidates.Add(Path.Combine(root, "lib"));
        candidates.Add(Path.Combine(root, "lib", "x64"));
        candidates.Add(Path.Combine(root, "x64"));
        AddImmediateChildren(candidates, Path.Combine(root, "bin"));
    }

    private static void AddImmediateChildren(ISet<string> candidates, string root)
    {
        try
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            candidates.Add(root);
            foreach (var child in Directory.EnumerateDirectories(root))
            {
                candidates.Add(child);
                candidates.Add(Path.Combine(child, "bin"));
                candidates.Add(Path.Combine(child, "lib"));
                candidates.Add(Path.Combine(child, "lib", "x64"));
                foreach (var grandChild in SafeEnumerateDirectories(Path.Combine(child, "bin")))
                {
                    candidates.Add(grandChild);
                }
            }
        }
        catch
        {
            // 某些系统级目录不可枚举时，继续使用其他候选路径。
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.Exists(root) ? Directory.EnumerateDirectories(root).ToArray() : [];
        }
        catch
        {
            return [];
        }
    }

    private static bool ContainsFile(IEnumerable<string> directories, string pattern)
    {
        foreach (var directory in directories)
        {
            try
            {
                if (Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Any())
                {
                    return true;
                }
            }
            catch
            {
                // 单个目录无权限不应中断完整依赖检查。
            }
        }

        return false;
    }
}
