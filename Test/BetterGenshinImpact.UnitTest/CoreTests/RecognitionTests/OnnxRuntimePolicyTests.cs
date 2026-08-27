using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public class OnnxRuntimePolicyTests
{
    /// <summary>
    /// 验证 <c>SelectGpuProviders_UsesAcceleratorThenCpuFallback</c> 所描述的行为。
    /// </summary>
    [Theory]
    [InlineData(true, true, true, ProviderType.TensorRt, ProviderType.Cuda, ProviderType.Cpu)]
    [InlineData(true, false, true, ProviderType.TensorRt, ProviderType.Cpu)]
    [InlineData(false, true, true, ProviderType.Cuda, ProviderType.Cpu)]
    [InlineData(false, false, true, ProviderType.Dml, ProviderType.Cpu)]
    [InlineData(false, false, false, ProviderType.Cpu)]
    public void SelectGpuProviders_UsesAcceleratorThenCpuFallback(
        bool hasTensorRt,
        bool hasCuda,
        bool hasDirectMl,
        params ProviderType[] expected)
    {
        var actual = BgiOnnxFactory.SelectGpuProviders(hasTensorRt, hasCuda, hasDirectMl);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 验证 <c>ParseAdditionalPaths_EmptyValue_ReturnsEmpty</c> 所描述的行为。
    /// </summary>
    [Fact]
    public void ParseAdditionalPaths_EmptyValue_ReturnsEmpty()
    {
        Assert.Empty(BgiOnnxFactory.ParseAdditionalPaths(null));
        Assert.Empty(BgiOnnxFactory.ParseAdditionalPaths("  "));
    }

    /// <summary>
    /// 验证 <c>ParseAdditionalPaths_MultipleValues_TrimsAndRemovesEmptyEntries</c> 所描述的行为。
    /// </summary>
    [Fact]
    public void ParseAdditionalPaths_MultipleValues_TrimsAndRemovesEmptyEntries()
    {
        var separator = Path.PathSeparator;
        var input = $" C:\\CUDA {separator}{separator} D:\\TensorRT ";

        var actual = BgiOnnxFactory.ParseAdditionalPaths(input);

        Assert.Equal([@"C:\CUDA", @"D:\TensorRT"], actual);
    }

    /// <summary>
    /// 验证 <c>EnumerateProviderFallbacks_RemovesFailedProviderOneAtATime</c> 所描述的行为。
    /// </summary>
    [Fact]
    public void EnumerateProviderFallbacks_RemovesFailedProviderOneAtATime()
    {
        var actual = BgiOnnxFactory.EnumerateProviderFallbacks(
                [ProviderType.TensorRt, ProviderType.Cuda, ProviderType.Cpu])
            .ToArray();

        Assert.Collection(
            actual,
            item =>
            {
                Assert.Equal([ProviderType.TensorRt, ProviderType.Cuda, ProviderType.Cpu], item.ProviderTypes);
                Assert.True(item.IsFirstAttempt);
            },
            item =>
            {
                Assert.Equal([ProviderType.Cuda, ProviderType.Cpu], item.ProviderTypes);
                Assert.False(item.IsFirstAttempt);
            },
            item =>
            {
                Assert.Equal([ProviderType.Cpu], item.ProviderTypes);
                Assert.False(item.IsFirstAttempt);
            });
    }

    /// <summary>
    /// 验证 <c>RegisteredWorldAndTreeModels_UseDifferentCacheNamespaces</c> 所描述的行为。
    /// </summary>
    [Fact]
    public void RegisteredWorldAndTreeModels_UseDifferentCacheNamespaces()
    {
        Assert.NotEqual(BgiOnnxModel.BgiTree.Name, BgiOnnxModel.BgiWorld.Name);
        Assert.NotEqual(BgiOnnxModel.BgiTree.CachePath, BgiOnnxModel.BgiWorld.CachePath);
    }

    /// <summary>
    /// 验证 <c>YoloPredictor_DisposeBeforeLazyCreation_IsIdempotentAndPreventsRun</c> 所描述的行为。
    /// </summary>
    [Fact]
    public void YoloPredictor_DisposeBeforeLazyCreation_IsIdempotentAndPreventsRun()
    {
        var factoryCalled = false;
        var predictor = new BgiYoloPredictor(
            BgiOnnxModel.BgiWorld,
            () =>
            {
                factoryCalled = true;
                throw new InvalidOperationException("factory should not run");
            });

        predictor.Dispose();
        predictor.Dispose();

        Assert.False(factoryCalled);
        Assert.Throws<ObjectDisposedException>(() => predictor.Run(_ => 0));
    }

}
