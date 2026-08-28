using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public class BgiOnnxModelTests
{
    [Fact]
    public void BgiWorld_UsesDedicatedCacheNamespace()
    {
        Assert.Equal("BgiWorld", BgiOnnxModel.BgiWorld.Name);
        Assert.NotEqual(BgiOnnxModel.BgiTree.CachePath, BgiOnnxModel.BgiWorld.CachePath);
    }
}
