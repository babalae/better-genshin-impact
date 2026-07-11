using BetterGenshinImpact.Service.Model.OverlayMetric;

namespace BetterGenshinImpact.UnitTest.ServiceTests.OverlayMetricTests;

public class GameGpuUsageInstanceParserTests
{
    [Theory]
    [InlineData(
        "pid_3270_luid_0x00000000_0x0000ABCD_phys_0_eng_2_engtype_3D",
        3270,
        0x00000000u,
        0x0000ABCDu,
        0,
        2,
        "3D")]
    [InlineData(
        "pid_3270_luid_0x00000001_0x89ABCDEF_phys_1_eng_12_engtype_Compute_0",
        3270,
        0x00000001u,
        0x89ABCDEFu,
        1,
        12,
        "Compute_0")]
    [InlineData(
        "pid_3270_luid_0x00000000_0x0000ABCD_phys_0_eng_2_engtype_3D#1",
        3270,
        0x00000000u,
        0x0000ABCDu,
        0,
        2,
        "3D")]
    public void TryParse_ValidInstance_ReturnsAllFields(
        string instanceName,
        int expectedProcessId,
        uint expectedLuidHigh,
        uint expectedLuidLow,
        int expectedPhysicalAdapterIndex,
        int expectedEngineId,
        string expectedEngineType)
    {
        var parsed = GpuEngineInstanceParser.TryParse(instanceName, 42, out var sample);

        Assert.True(parsed);
        Assert.Equal(expectedProcessId, sample.ProcessId);
        Assert.Equal(new GpuAdapterKey(expectedLuidHigh, expectedLuidLow, expectedPhysicalAdapterIndex), sample.Adapter);
        Assert.Equal(expectedEngineId, sample.EngineId);
        Assert.Equal(expectedEngineType, sample.EngineType);
        Assert.Equal(42, sample.Utilization);
    }

    public static TheoryData<string> InvalidInstanceNames => new()
    {
        "",
        "GPU Engine",
        "pid_x_luid_0x00000000_0x0000ABCD_phys_0_eng_2_engtype_3D",
        "pid_3270_luid_0x00000000_phys_0_eng_2_engtype_3D",
        "pid_3270_luid_0xGGGGGGGG_0x0000ABCD_phys_0_eng_2_engtype_3D",
        "pid_3270_luid_0x00000000_0x0000ABCD_phys_x_eng_2_engtype_3D",
        "pid_3270_luid_0x00000000_0x0000ABCD_phys_0_eng_x_engtype_3D",
        "pid_3270_luid_0x00000000_0x0000ABCD_phys_0_eng_2",
        "foo_pid_3270_luid_0x00000000_0x0000ABCD_phys_0_eng_2_engtype_3D"
    };

    [Theory]
    [MemberData(nameof(InvalidInstanceNames))]
    public void TryParse_InvalidInstance_ReturnsFalse(string instanceName)
    {
        Assert.False(GpuEngineInstanceParser.TryParse(instanceName, 10, out _));
    }

    [Theory]
    [InlineData(PdhCounterStatus.ValidData, 10, true)]
    [InlineData(PdhCounterStatus.NewData, 10, true)]
    [InlineData(0xC0000BC6u, 10, false)]
    [InlineData(PdhCounterStatus.ValidData, double.NaN, false)]
    [InlineData(PdhCounterStatus.ValidData, double.PositiveInfinity, false)]
    [InlineData(PdhCounterStatus.ValidData, -1, false)]
    public void TryCreateSample_ValidatesPdhStatusAndValue(uint status, double value, bool expected)
    {
        var counterValue = new GpuEngineCounterValue(
            "pid_3270_luid_0x00000000_0x0000ABCD_phys_0_eng_2_engtype_3D",
            status,
            value);

        Assert.Equal(expected, GpuEngineInstanceParser.TryCreateSample(counterValue, out _));
    }
}

public class GameGpuUsageCalculatorTests
{
    private const int GameProcessId = 3270;
    private static readonly GpuAdapterKey AdapterA = new(0x00000000, 0x0000AAAA, 0);
    private static readonly GpuAdapterKey AdapterAPhys1 = new(0x00000000, 0x0000AAAA, 1);
    private static readonly GpuAdapterKey AdapterB = new(0x00000000, 0x0000BBBB, 0);

    [Fact]
    public void SelectAdapter_UsesExactGamePidAndIgnoresUnrelatedGpuLoad()
    {
        GpuEngineUsageSample[] samples =
        [
            S(GameProcessId, AdapterA, 0, "3D", 10),
            S(32700, AdapterB, 0, "3D", 99),
            S(9000, AdapterB, 1, "Copy", 100)
        ];

        Assert.Equal(AdapterA, GameGpuUsageCalculator.SelectAdapter(samples, GameProcessId));
    }

    [Fact]
    public void SelectAdapter_UsesPerAdapterPeakInsteadOfAddingDifferentEngines()
    {
        GpuEngineUsageSample[] samples =
        [
            S(GameProcessId, AdapterA, 0, "3D", 30),
            S(GameProcessId, AdapterA, 1, "Copy", 30),
            S(GameProcessId, AdapterB, 0, "3D", 40)
        ];

        Assert.Equal(AdapterB, GameGpuUsageCalculator.SelectAdapter(samples, GameProcessId));
    }

    [Fact]
    public void SelectAdapter_OnEqualUsagePrefersThreeDimensionalEngine()
    {
        GpuEngineUsageSample[] samples =
        [
            S(GameProcessId, AdapterA, 0, "Copy", 50),
            S(GameProcessId, AdapterB, 0, "3D", 50)
        ];

        Assert.Equal(AdapterB, GameGpuUsageCalculator.SelectAdapter(samples, GameProcessId));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.49, false)]
    [InlineData(0.5, true)]
    public void SelectAdapter_RequiresMinimumActivity(double usage, bool shouldSelect)
    {
        GpuEngineUsageSample[] samples = [S(GameProcessId, AdapterA, 0, "3D", usage)];

        Assert.Equal(shouldSelect, GameGpuUsageCalculator.SelectAdapter(samples, GameProcessId).HasValue);
    }

    [Fact]
    public void CalculateAdapterUsage_SumsSameEngineAndTakesMaximumAcrossEngines()
    {
        GpuEngineUsageSample[] samples =
        [
            S(GameProcessId, AdapterA, 0, "3D", 20),
            S(1001, AdapterA, 0, "3D", 25),
            S(GameProcessId, AdapterA, 1, "Copy", 30),
            S(1001, AdapterA, 1, "Copy", 35),
            S(1002, AdapterA, 2, "VideoDecode", 60),
            S(2001, AdapterB, 1, "Copy", 99),
            S(2002, AdapterAPhys1, 1, "Copy", 98)
        ];

        Assert.Equal(65, GameGpuUsageCalculator.CalculateAdapterUsage(samples, AdapterA));
    }

    [Fact]
    public void CalculateAdapterUsage_ClampsPhysicalEngineSumToOneHundred()
    {
        GpuEngineUsageSample[] samples =
        [
            S(GameProcessId, AdapterA, 0, "3D", 70),
            S(1001, AdapterA, 0, "3D", 55)
        ];

        Assert.Equal(100, GameGpuUsageCalculator.CalculateAdapterUsage(samples, AdapterA));
    }

    [Fact]
    public void CalculateAdapterUsage_DistinguishesZeroFromNoData()
    {
        GpuEngineUsageSample[] samples = [S(GameProcessId, AdapterA, 0, "3D", 0)];

        Assert.Equal(0, GameGpuUsageCalculator.CalculateAdapterUsage(samples, AdapterA));
        Assert.Null(GameGpuUsageCalculator.CalculateAdapterUsage(samples, AdapterB));
    }

    private static GpuEngineUsageSample S(int pid, GpuAdapterKey adapter, int engine, string engineType, double usage)
    {
        return new GpuEngineUsageSample(pid, adapter, engine, engineType, usage);
    }
}

public class GameGpuUsageTrackerTests
{
    private const int GameProcessId = 3270;
    private static readonly GpuAdapterKey AdapterA = new(0x00000000, 0x0000AAAA, 0);
    private static readonly GpuAdapterKey AdapterB = new(0x00000000, 0x0000BBBB, 0);

    [Fact]
    public void Update_InitialAllZeroDoesNotBind()
    {
        var tracker = new GameGpuUsageTracker();
        GpuEngineUsageSample[] samples =
        [
            S(GameProcessId, AdapterA, 0, "3D", 0),
            S(GameProcessId, AdapterB, 0, "Copy", 0),
            S(9000, AdapterB, 0, "3D", 99)
        ];

        Assert.Null(tracker.Update(GameProcessId, samples));
        Assert.Null(tracker.SelectedAdapter);
    }

    [Fact]
    public void Update_AfterBindingRetainsAdapterWhileGameIsIdle()
    {
        var tracker = new GameGpuUsageTracker();

        Assert.Equal(25, tracker.Update(GameProcessId,
        [
            S(GameProcessId, AdapterA, 0, "3D", 20),
            S(1001, AdapterA, 0, "3D", 5)
        ]));

        Assert.Equal(37, tracker.Update(GameProcessId,
        [
            S(GameProcessId, AdapterA, 0, "3D", 0),
            S(1001, AdapterA, 0, "3D", 37),
            S(GameProcessId, AdapterB, 0, "3D", 80)
        ]));
        Assert.Equal(AdapterA, tracker.SelectedAdapter);
    }

    [Fact]
    public void Update_PidChangeImmediatelyResetsAndReselects()
    {
        var tracker = new GameGpuUsageTracker();
        _ = tracker.Update(GameProcessId, [S(GameProcessId, AdapterA, 0, "3D", 90)]);

        var result = tracker.Update(3271,
        [
            S(GameProcessId, AdapterA, 0, "3D", 90),
            S(3271, AdapterB, 0, "3D", 10),
            S(1001, AdapterB, 0, "3D", 15)
        ]);

        Assert.Equal(25, result);
        Assert.Equal(AdapterB, tracker.SelectedAdapter);
    }

    [Fact]
    public void Update_SelectedAdapterMissingThreeTimesReselectsOnThirdSample()
    {
        var tracker = new GameGpuUsageTracker();
        _ = tracker.Update(GameProcessId, [S(GameProcessId, AdapterA, 0, "3D", 20)]);
        GpuEngineUsageSample[] movedSamples =
        [
            S(GameProcessId, AdapterB, 0, "3D", 40),
            S(9000, AdapterA, 0, "3D", 70),
            S(9001, AdapterB, 0, "3D", 5)
        ];

        Assert.Equal(70, tracker.Update(GameProcessId, movedSamples));
        Assert.Equal(70, tracker.Update(GameProcessId, movedSamples));
        Assert.Equal(45, tracker.Update(GameProcessId, movedSamples));
        Assert.Equal(AdapterB, tracker.SelectedAdapter);
    }

    [Fact]
    public void Update_SelectedAdapterReappearsResetsMissingStreak()
    {
        var tracker = new GameGpuUsageTracker();
        _ = tracker.Update(GameProcessId, [S(GameProcessId, AdapterA, 0, "3D", 20)]);
        GpuEngineUsageSample[] missing =
        [
            S(GameProcessId, AdapterB, 0, "3D", 40),
            S(9000, AdapterA, 0, "3D", 70)
        ];

        _ = tracker.Update(GameProcessId, missing);
        _ = tracker.Update(GameProcessId, [S(GameProcessId, AdapterA, 0, "3D", 0)]);
        _ = tracker.Update(GameProcessId, missing);
        _ = tracker.Update(GameProcessId, missing);

        Assert.Equal(AdapterA, tracker.SelectedAdapter);
        Assert.Equal(2, tracker.MissingSampleCount);
    }

    private static GpuEngineUsageSample S(int pid, GpuAdapterKey adapter, int engine, string engineType, double usage)
    {
        return new GpuEngineUsageSample(pid, adapter, engine, engineType, usage);
    }
}
