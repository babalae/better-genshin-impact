using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace BetterGI.Watchdog
{
    internal class Program
    {
        private const string BetterGIProcessName = "BetterGI";   // BetterGI 主程序进程名（不含 .exe）
        private const string YuanShenProcessName = "YuanShen";   // 原神国服进程名

        static void Main(string[] args)
        {
            // 可选参数：args[0] = BetterGI.exe 的完整路径
            var betterGiExePath = args.Length > 0
                ? args[0]
                : Path.Combine(AppContext.BaseDirectory, "BetterGI.exe");

            var betterGiDir = Path.GetDirectoryName(betterGiExePath)
                               ?? AppContext.BaseDirectory;

            var configPath = Path.Combine(betterGiDir, "User", "config.json");

            Console.WriteLine($"[Watchdog] BetterGI: {betterGiExePath}");
            Console.WriteLine($"[Watchdog] Config : {configPath}");

            while (true)
            {
                try
                {
                    bool genshinAlive = IsProcessAlive(YuanShenProcessName);
                    bool betterGiAlive = IsProcessAlive(BetterGIProcessName);

                    if (!genshinAlive || !betterGiAlive)
                    {
                        // 只有在配置中存在一条龙恢复目标时，才认为这是需要处理的崩溃场景。
                        // 如果 SelectedOneDragonFlowConfigName 为空（例如一条龙正常结束或用户手动停止后），
                        // 则不再去杀 BetterGI / 游戏进程，避免在用户之后手动打开 BetterGI 时被误杀。
                        var state = LoadOneDragonState(configPath);
                        if (string.IsNullOrEmpty(state.ConfigName))
                        {
                            Console.WriteLine("[Watchdog] No SelectedOneDragonFlowConfigName in config.json, skip killing and restart.");
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Detected crash/exit. genshinAlive={genshinAlive}, betterGiAlive={betterGiAlive}, config='{state.ConfigName}', group='{state.GroupName}'");

                            KillProcess(YuanShenProcessName);
                            KillProcess(BetterGIProcessName);

                            StartBetterGi(betterGiExePath, state.ConfigName, state.GroupName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Watchdog] Error: {ex}");
                }

                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }

        private static bool IsProcessAlive(string processName)
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }

        private static void KillProcess(string processName)
        {
            foreach (var p in Process.GetProcessesByName(processName))
            {
                try
                {
                    Console.WriteLine($"[Watchdog] Killing {processName} (PID={p.Id})");

                    // 注意：如果看门狗是由 BetterGI 启动的子进程，
                    // 那么 BetterGI 的进程树中会包含当前看门狗进程。
                    // 这时调用 p.Kill(true) 会抛出：
                    // "Cannot be used to terminate a process tree containing the calling process."
                    // 所以对 BetterGI 只杀单个进程，对游戏等其它进程仍然使用整棵树 Kill。
                    bool killTree = !string.Equals(processName, BetterGIProcessName, StringComparison.OrdinalIgnoreCase);

                    if (killTree)
                    {
                        try
                        {
                            p.Kill(true);
                        }
                        catch (InvalidOperationException ex)
                        {
                            // 保险起见，如果整棵树 Kill 失败（例如包含当前进程），回退为普通 Kill
                            Console.WriteLine($"[Watchdog] Kill tree failed for {processName}: {ex.Message}, fallback to single kill.");
                            if (!p.HasExited)
                            {
                                p.Kill();
                            }
                        }
                    }
                    else
                    {
                        p.Kill();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Watchdog] Failed to kill {processName}: {ex.Message}");
                }
            }
        }

        private static (string ConfigName, string GroupName) LoadOneDragonState(string configPath)
        {
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"[Watchdog] Config not found: {configPath}");
                return (string.Empty, string.Empty);
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = doc.RootElement;

            // 配置文件由 BetterGI 使用 System.Text.Json + CamelCase 命名策略写入，
            // 实际键名为 selectedOneDragonFlowConfigName / currentOneDragonScriptGroupName。
            // 这里做一个大小写不敏感兼容，既支持旧的 PascalCase，也支持现在的 camelCase。
            string configName = GetStringPropertyCaseInsensitive(root, "SelectedOneDragonFlowConfigName");
            string groupName = GetStringPropertyCaseInsensitive(root, "CurrentOneDragonScriptGroupName");

            Console.WriteLine($"[Watchdog] Loaded state: config='{configName}', group='{groupName}'");

            return (configName, groupName);
        }

        private static string GetStringPropertyCaseInsensitive(JsonElement root, string propertyName)
        {
            // 1. 先尝试精确匹配（PascalCase）
            if (root.TryGetProperty(propertyName, out var value))
            {
                return value.GetString() ?? string.Empty;
            }

            // 2. 再尝试 camelCase 形式（例如 SelectedOneDragonFlowConfigName -> selectedOneDragonFlowConfigName）
            if (!string.IsNullOrEmpty(propertyName))
            {
                var camel = char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
                if (root.TryGetProperty(camel, out value))
                {
                    return value.GetString() ?? string.Empty;
                }
            }

            // 3. 最后做一遍不区分大小写的遍历匹配，兼容意外命名
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return prop.Value.GetString() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static void StartBetterGi(string exePath, string configName, string groupName)
        {
            if (!File.Exists(exePath))
            {
                Console.WriteLine($"[Watchdog] BetterGI.exe not found: {exePath}");
                return;
            }

            // 构造命令行：startOneDragon <ConfigName> <GroupName?>
            string arguments = string.IsNullOrWhiteSpace(groupName)
                ? $"startOneDragon \"{configName}\""
                : $"startOneDragon \"{configName}\" \"{groupName}\"";

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
            };

            Console.WriteLine($"[Watchdog] Start BetterGI: \"{psi.FileName}\" {psi.Arguments}");
            Process.Start(psi);
        }
    }
}
