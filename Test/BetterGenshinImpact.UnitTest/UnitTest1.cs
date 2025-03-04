namespace BetterGenshinImpact.UnitTest;

public class UnitTest1
{
    /// <summary>
    /// 测试从Assets目录获取图片，结果应为成功。
    /// Assets是BGI项目的一个submodule，须要单独获取。
    /// </summary>
    /// <param name="filename"></param>
    [Theory]
    [InlineData("UI_Icon_Intee_AlchemySim_Dealer.png")]
    public void TestGetDataFromAssets(string filename)
    {
        bool actual = File.Exists(@$"..\..\..\Assets\{filename}");

        Assert.True(actual);
    }
}