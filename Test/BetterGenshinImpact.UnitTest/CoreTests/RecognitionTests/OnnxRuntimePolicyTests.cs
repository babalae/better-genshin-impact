using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public class OnnxRuntimePolicyTests
{
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

    [Fact]
    public void ParseAdditionalPaths_EmptyValue_ReturnsEmpty()
    {
        Assert.Empty(BgiOnnxFactory.ParseAdditionalPaths(null));
        Assert.Empty(BgiOnnxFactory.ParseAdditionalPaths("  "));
    }

    [Fact]
    public void ParseAdditionalPaths_MultipleValues_TrimsAndRemovesEmptyEntries()
    {
        var separator = Path.PathSeparator;
        var input = $" C:\\CUDA {separator}{separator} D:\\TensorRT ";

        var actual = BgiOnnxFactory.ParseAdditionalPaths(input);

        Assert.Equal([@"C:\CUDA", @"D:\TensorRT"], actual);
    }

    [Fact]
    public void EnumerateProviderFallbacks_RemovesFailedProviderOneAtATime()
    {
        var actual = BgiOnnxFactory.EnumerateProviderFallbacks(
                [ProviderType.TensorRt, ProviderType.Cuda, ProviderType.Cpu])
            .Select(item => item.ProviderTypes)
            .ToArray();

        Assert.Equal(
            [
                [ProviderType.TensorRt, ProviderType.Cuda, ProviderType.Cpu],
                [ProviderType.Cuda, ProviderType.Cpu],
                [ProviderType.Cpu]
            ],
            actual);
    }

    [Fact]
    public void RegisteredWorldAndTreeModels_UseDifferentCacheNamespaces()
    {
        Assert.NotEqual(BgiOnnxModel.BgiTree.Name, BgiOnnxModel.BgiWorld.Name);
        Assert.NotEqual(BgiOnnxModel.BgiTree.CachePath, BgiOnnxModel.BgiWorld.CachePath);
    }

}
