using BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;
using CsTrees.Display;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoComboTests;

/// <summary>
/// 从已知队伍角色名开始调用 LLM 构建连招行为树
/// 全程不触及主程序静态初始化（TaskControl.Logger / App host 等），日志直接打进测试输出
/// LLM 配置取自主项目 User/config.json 的 autoComboBuildConfig 节点（MainProjectConfigFixture 定位），
/// 未找到配置或 LLM 参数未配置时输出提示并跳过
/// 说明：xunit v2 的测试类不支持注入 IMessageSink（v3 特性），因此日志只能在测试结束后于该用例的输出中查看
/// </summary>
[Collection("Init Collection")]
public class AutoComboBuildTaskLlmTests
{
    /// <summary>测试队伍（标准角色名，写死即可）</summary>
    private static readonly string[] Team = ["安柏", "凯亚", "丽莎", "可莉"];

    private readonly ITestOutputHelper _output;

    public AutoComboBuildTaskLlmTests(ITestOutputHelper output, MainProjectConfigFixture fixture)
    {
        _output = output;
        Fixture = fixture;
    }

    private MainProjectConfigFixture Fixture { get; }

    [Fact]
    public async Task BuildTree_FromKnownTeam_PrintsTree()
    {
        var config = Fixture.AutoComboBuildConfig;
        if (config == null)
        {
            _output.WriteLine(Fixture.LoadError + "，跳过");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.PlanningLlmEndpoint) || string.IsNullOrWhiteSpace(config.ModelName))
        {
            _output.WriteLine($"主项目 config.json（{Fixture.ConfigPath}）中 autoComboBuildConfig 未配置 LLM 参数，请先在主程序【自动连招】设置里配置，跳过");
            return;
        }

        _output.WriteLine("使用配置：{0}（{1}）", config.PlanningLlmEndpoint, config.ModelName);
        _output.WriteLine("测试队伍：{0}", string.Join("、", Team));

        var logger = new TestOutputLogger(_output);
        var session = await AutoComboBuildTask.BuildComboTreeAsync(Team.ToList(), config, logger, CancellationToken.None);
        var root = session.Builder.Build();

        _output.WriteLine("生成的行为树：\n{0}", Display.AsciiTree(root));
        Assert.NotNull(root);
    }

    /// <summary>ITestOutputHelper 转 ILogger：建树过程中的日志全部打进测试输出</summary>
    private sealed class TestOutputLogger(ITestOutputHelper output) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
                if (exception != null)
                {
                    output.WriteLine(exception.ToString());
                }
            }
            catch (InvalidOperationException)
            {
                // 测试已结束输出通道关闭时忽略
            }
        }
    }
}
