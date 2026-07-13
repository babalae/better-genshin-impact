using BetterGenshinImpact.GameTask.GetGridIcons;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.GetGridIconsTests
{
    public class GetGridIconsTaskTests
    {
        [Theory]
        [InlineData(@"AutoArtifactSalvage\ArtifactAffixes.png", 5)]
        /// <summary>
        /// 测试获取物品的黄色星星数，结果应正确
        /// </summary>
        public void GetStars_ShouldBeRight(string screenshot, int num)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\{screenshot}");

            //
            int actual = GetGridIconsTask.GetStars(mat);

            //
            Assert.Equal(num, actual);
        }
    }
}
