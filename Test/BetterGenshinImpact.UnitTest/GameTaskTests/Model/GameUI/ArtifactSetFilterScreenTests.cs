using BetterGenshinImpact.GameTask.Model.GameUI;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.Model.GameUI
{
    public class ArtifactSetFilterScreenTests
    {
        [Theory]
        [InlineData(@"GameUI\ArtifactSetFilterBright.png", 20, 2)]
        [InlineData(@"GameUI\ArtifactSetFilterDark.png", 20, 2)]
        /// <summary>
        /// 测试获取圣遗物套装筛选界面中的项目，结果应正确
        /// </summary>
        public void GetGridItems_ShouldBeRight(string screenshot, int count, int columns)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\{screenshot}");

            //
            var result = ArtifactSetFilterScreen.GetGridItems(mat, columns);

            //
            Assert.Equal(count, result.Count());
        }
    }
}
