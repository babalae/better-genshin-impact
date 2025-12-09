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

			// BetterGI 主配置文件（由 BetterGI 本体写入），用于记录一条龙当前执行状态
			var oneDragonConfigPath = Path.Combine(betterGiDir, "User", "config.json");

			// 看门狗自己的文本配置（可选），用于控制资源日志与高内存阈值
			var watchdogConfigPath = Path.Combine(betterGiDir, "User", "watchdog.config.json");
			var watchdogConfig = LoadWatchdogConfig(watchdogConfigPath);

				// 日志文件路径（基础路径 + 本次看门狗启动时间戳后缀）：
				// 1. LogFileName 为空时，使用默认 "watchdog.log"；
				// 2. 相对路径（默认 watchdog.log）先映射到 BetterGI/User 目录；
				// 3. 然后在文件名后追加一次性的启动时间戳后缀 -yyyyMMdd-HHmmss，
				//    例如 watchdog-20250101-120000.log，每次启动生成一个新的日志文件。
				var logFileName = string.IsNullOrWhiteSpace(watchdogConfig.LogFileName)
					? "watchdog.log"
					: watchdogConfig.LogFileName!;
				var baseLogPath = Path.IsPathRooted(logFileName)
					? logFileName
					: Path.Combine(betterGiDir, "User", logFileName);
				var startupTimestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
				var baseDir = Path.GetDirectoryName(baseLogPath);
				var baseFileName = Path.GetFileNameWithoutExtension(baseLogPath);
				var baseExt = Path.GetExtension(baseLogPath);
				if (string.IsNullOrEmpty(baseFileName))
				{
					baseFileName = "watchdog";
				}
				var watchdogLogPath = Path.Combine(
					string.IsNullOrEmpty(baseDir) ? betterGiDir : baseDir,
					$"{baseFileName}-{startupTimestamp}{baseExt}");

			Console.WriteLine($"[Watchdog] BetterGI: {betterGiExePath}");
			Console.WriteLine($"[Watchdog] Config : {oneDragonConfigPath}");
			Console.WriteLine($"[Watchdog] WatchdogConfig: {watchdogConfigPath}");

			if (watchdogConfig.EnableLogging)
			{
				Console.WriteLine(
					$"[Watchdog] Resource logging enabled. LogFile='{watchdogLogPath}', MemoryThresholdMB={watchdogConfig.MemoryThresholdMB}");
			}

			while (true)
			{
				try
				{
					var genshinProcesses = Process.GetProcessesByName(YuanShenProcessName);
					var betterGiProcesses = Process.GetProcessesByName(BetterGIProcessName);

					bool genshinAlive = genshinProcesses.Length > 0;
					bool betterGiAlive = betterGiProcesses.Length > 0;

					// 1. 可选：记录当前内存占用，并在超过阈值时杀死并重启
					bool handledHighMemory = CheckMemoryAndMaybeRestart(
						genshinProcesses,
						betterGiProcesses,
						watchdogConfig,
						watchdogLogPath,
						betterGiExePath,
						oneDragonConfigPath);

					if (handledHighMemory)
					{
						// 如果已经因为高内存触发了一次重启，本轮就不再重复走崩溃检测逻辑
						goto Sleep;
					}

					// 2. 原有崩溃/退出检测逻辑
					if (!genshinAlive || !betterGiAlive)
					{
						var reason =
							$"Detected crash/exit. genshinAlive={genshinAlive}, betterGiAlive={betterGiAlive}";
						TryKillAndRestart(betterGiExePath, oneDragonConfigPath, reason);
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Watchdog] Error: {ex}");
				}

			Sleep:
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
					// 此时调用 p.Kill(true) 会抛出：
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
							Console.WriteLine(
								$"[Watchdog] Kill tree failed for {processName}: {ex.Message}, fallback to single kill.");
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

		/// <summary>
		/// 统一的“杀死游戏 + BetterGI 并重启”逻辑。
		/// 只有在一条龙配置仍然存在时才会生效，避免误杀用户手动打开的进程。
		/// </summary>
		private static void TryKillAndRestart(string betterGiExePath, string configPath, string reason)
		{
			// 只有在配置中存在一条龙恢复目标时，才认为这是需要处理的场景。
			// 如果 SelectedOneDragonFlowConfigName 为空（例如一条龙正常结束或用户手动停止后），
			// 则不再去杀 BetterGI / 游戏进程，避免在用户之后手动打开 BetterGI 时被误杀。
			var state = LoadOneDragonState(configPath);
			if (string.IsNullOrEmpty(state.ConfigName))
			{
				Console.WriteLine(
					"[Watchdog] " +
					reason +
					", but no SelectedOneDragonFlowConfigName in config.json, skip killing and restart.");
				return;
			}

			Console.WriteLine(
				$"[{DateTime.Now:HH:mm:ss}] {reason}, config='{state.ConfigName}', group='{state.GroupName}'");

			KillProcess(YuanShenProcessName);
			KillProcess(BetterGIProcessName);

			StartBetterGi(betterGiExePath, state.ConfigName, state.GroupName);
		}

		/// <summary>
		/// 记录 BetterGI / 原神 的内存占用，并在超过阈值时触发一次 Kill+Restart。
		/// </summary>
		/// <returns>true 表示本轮已经因为高内存触发了 Kill+Restart。</returns>
		private static bool CheckMemoryAndMaybeRestart(
			Process[] genshinProcesses,
			Process[] betterGiProcesses,
			WatchdogConfig config,
			string logPath,
			string betterGiExePath,
			string oneDragonConfigPath)
		{
			bool needLog = config.EnableLogging;
			long thresholdBytes = config.MemoryThresholdMB > 0
				? config.MemoryThresholdMB * 1024L * 1024L
				: 0L;

			// 如果既不开日志，也没有配置阈值，就直接跳过
			if (!needLog && thresholdBytes <= 0)
			{
				return false;
			}

			long genshinWs = GetMaxWorkingSet(genshinProcesses);
			long betterGiWs = GetMaxWorkingSet(betterGiProcesses);

			if (needLog)
			{
				string line =
					$"{DateTime.Now:yyyy-MM-dd HH:mm:ss} " +
					$"YuanShen_WS={BytesToMb(genshinWs)}MB " +
					$"BetterGI_WS={BytesToMb(betterGiWs)}MB";
				TryAppendLogLine(logPath, line);
			}

			if (thresholdBytes > 0)
			{
				bool overGenshin = genshinWs > thresholdBytes;
				bool overBetterGi = betterGiWs > thresholdBytes;

				if (overGenshin || overBetterGi)
				{
					string reason =
						$"Detected high memory usage. Threshold={config.MemoryThresholdMB}MB, " +
						$"YuanShen_WS={BytesToMb(genshinWs)}MB, " +
						$"BetterGI_WS={BytesToMb(betterGiWs)}MB, " +
						$"OverGenshin={overGenshin}, OverBetterGI={overBetterGi}";

					Console.WriteLine("[Watchdog] " + reason);
					TryAppendLogLine(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {reason}");

					TryKillAndRestart(betterGiExePath, oneDragonConfigPath, reason);
					return true;
				}
			}

			return false;
		}

		private static long GetMaxWorkingSet(Process[] processes)
		{
			long max = 0;
			foreach (var p in processes)
			{
				try
				{
					p.Refresh();
					if (!p.HasExited)
					{
						max = Math.Max(max, p.WorkingSet64);
					}
				}
				catch
				{
					// 忽略单个进程读取失败
				}
			}

			return max;
		}

		private static long BytesToMb(long bytes)
		{
			if (bytes <= 0)
			{
				return 0;
			}

			return bytes / (1024L * 1024L);
		}

		private static void TryAppendLogLine(string logPath, string line)
		{
			try
			{
				var dir = Path.GetDirectoryName(logPath);
				if (!string.IsNullOrEmpty(dir))
				{
					Directory.CreateDirectory(dir);
				}

				File.AppendAllText(logPath, line + Environment.NewLine);
			}
			catch (Exception ex)
			{
				// 写日志失败不应影响看门狗主流程
				Console.WriteLine($"[Watchdog] Failed to write log file '{logPath}': {ex.Message}");
			}
		}

		private static WatchdogConfig LoadWatchdogConfig(string path)
		{
			try
			{
				if (!File.Exists(path))
				{
					Console.WriteLine($"[Watchdog] Watchdog config not found: {path}, use default.");
					return new WatchdogConfig();
				}

				var json = File.ReadAllText(path);
				var options = new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true,
					ReadCommentHandling = JsonCommentHandling.Skip,
					AllowTrailingCommas = true
				};

				var cfg = JsonSerializer.Deserialize<WatchdogConfig>(json, options);
				if (cfg == null)
				{
					Console.WriteLine(
						$"[Watchdog] Watchdog config is empty or invalid, use default. Path={path}");
					return new WatchdogConfig();
				}

				return cfg;
			}
			catch (Exception ex)
			{
				Console.WriteLine(
					$"[Watchdog] Failed to load watchdog config '{path}', use default. Error={ex.Message}");
				return new WatchdogConfig();
			}
		}
	}
}

internal class WatchdogConfig
{
	/// <summary>
	/// 是否开启资源占用日志（主要记录 BetterGI / 原神的内存占用）。
	/// </summary>
	public bool EnableLogging { get; set; } = false;

	/// <summary>
	/// 内存占用阈值（MB）。当 BetterGI 或原神 WorkingSet 超过该值时，
	/// 将视为异常状态，触发一次 Kill+Restart（前提是一条龙配置仍存在）。
	/// 小于等于 0 表示不启用该功能。
	/// </summary>
	public long MemoryThresholdMB { get; set; } = 0;

	/// <summary>
	/// 日志文件名或完整路径。默认 "watchdog.log"，会写入 BetterGI/User 目录。
	/// 如果设置为绝对路径，则直接写入该路径。
	/// </summary>
	public string? LogFileName { get; set; } = "watchdog.log";
}
