using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests;

public class BgiOnnxModelTests
{
    [Fact]
    public void RegisteredModels_ShouldUseUniqueNames()
    {
        var names = BgiOnnxModel.GetAll().Select(model => model.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void BgiWorld_ShouldNotShareBgiTreeCacheName()
    {
        Assert.Equal("BgiTree", BgiOnnxModel.BgiTree.Name);
        Assert.Equal("BgiWorld", BgiOnnxModel.BgiWorld.Name);
        Assert.NotEqual(BgiOnnxModel.BgiTree.CacheRelativePath, BgiOnnxModel.BgiWorld.CacheRelativePath);
    }
}
