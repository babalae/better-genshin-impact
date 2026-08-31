using System.Text;

namespace BetterGenshinImpact.I18nSync;

internal static class Program
{
    private const int SuccessExitCode = 0;
    private const int OutOfSyncExitCode = 1;
    private const int ErrorExitCode = 2;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return SuccessExitCode;
            }

            var projectDirectory = ResolveProjectDirectory(options.ProjectDirectory);
            var scanResult = I18nScanner.Scan(projectDirectory);
            var plans = I18nJsonSynchronizer.CreatePlans(projectDirectory, scanResult.Keys, options.AddOnly);

            Console.WriteLine($"已扫描 {scanResult.XamlFileCount} 个 XAML 文件，发现 {scanResult.Keys.Count} 个 i18n Key。");
            Console.WriteLine(options.AddOnly ? "模式：只补充，保留废弃 Key。" : "模式：补充并删除废弃 Key。" );

            foreach (var plan in plans)
            {
                var obsoleteAction = options.AddOnly ? "保留" : "删除";
                Console.WriteLine(
                    $"{Path.GetFileName(plan.FilePath)}：补充 {plan.MissingKeys.Count}，{obsoleteAction} {plan.ObsoleteKeys.Count}" +
                    (plan.RequiresWrite ? "。" : "，无需修改。"));
            }

            var changedPlans = plans.Where(plan => plan.RequiresWrite).ToArray();
            if (options.Check)
            {
                if (changedPlans.Length == 0)
                {
                    Console.WriteLine("多语言文件已同步。");
                    return SuccessExitCode;
                }

                Console.Error.WriteLine("多语言文件未同步，请运行同步命令后提交变更。");
                return OutOfSyncExitCode;
            }

            foreach (var plan in changedPlans)
            {
                I18nJsonSynchronizer.Write(plan);
            }

            Console.WriteLine(changedPlans.Length == 0
                ? "多语言文件无需修改。"
                : $"已更新 {changedPlans.Length} 个多语言文件。");
            return SuccessExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"i18n 同步失败：{exception.Message}");
            return ErrorExitCode;
        }
    }

    private static string ResolveProjectDirectory(string? configuredPath)
    {
        var path = configuredPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = File.Exists(Path.Combine(Environment.CurrentDirectory, "BetterGenshinImpact.csproj"))
                ? Environment.CurrentDirectory
                : Path.Combine(Environment.CurrentDirectory, "BetterGenshinImpact");
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(Path.Combine(fullPath, "BetterGenshinImpact.csproj")))
        {
            throw new DirectoryNotFoundException($"不是有效的 BetterGenshinImpact 项目目录：{fullPath}");
        }

        return fullPath;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
                          BetterGenshinImpact.I18nSync

                          用法：
                            dotnet run --project Build/BetterGenshinImpact.I18nSync -- [选项]

                          选项：
                            --project <目录>  BetterGenshinImpact 项目目录
                            --add-only       只补充缺少的 Key，不删除废弃 Key
                            --check          只检查，不写入文件；不同步时返回退出码 1
                            -h, --help       显示帮助

                          默认行为：补充缺少的 Key，并删除废弃 Key。
                          """);
    }

    private sealed record CliOptions(string? ProjectDirectory, bool AddOnly, bool Check, bool ShowHelp)
    {
        public static CliOptions Parse(IReadOnlyList<string> args)
        {
            string? projectDirectory = null;
            var addOnly = false;
            var check = false;
            var showHelp = false;

            for (var index = 0; index < args.Count; index++)
            {
                switch (args[index])
                {
                    case "--project":
                        if (++index >= args.Count)
                        {
                            throw new ArgumentException("--project 后必须提供项目目录。");
                        }

                        projectDirectory = args[index];
                        break;
                    case "--add-only":
                        addOnly = true;
                        break;
                    case "--check":
                        check = true;
                        break;
                    case "-h":
                    case "--help":
                        showHelp = true;
                        break;
                    default:
                        throw new ArgumentException($"未知参数：{args[index]}");
                }
            }

            return new CliOptions(projectDirectory, addOnly, check, showHelp);
        }
    }
}
