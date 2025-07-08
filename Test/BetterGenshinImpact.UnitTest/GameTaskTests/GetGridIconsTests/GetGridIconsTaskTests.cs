using GameTask.Model.GameUI;
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
        [InlineData(@"AutoArtifactSalvage\ArtifactGrid.png", 4, 2)]
        [InlineData(@"GetGridIcons\WeaponGrid3.png", 32, 8)]
        /// <summary>
        /// 测试获取各种Grid界面中的圣遗物，结果应正确
        /// </summary>
        public void GetGridIcons_ShouldBeRight(string screenshot, int count, int columns)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\{screenshot}");

            //
            var result = GridScreen.GridEnumerator.GetGridItems(mat, columns);

            //
            Assert.Equal(count, result.Count());
        }

        [Theory]
        [InlineData(@"GetGridIcons\FoodGrid.png", 32, 8)]
        [InlineData(@"GetGridIcons\WeaponGrid.png", 4, 2)]
        [InlineData(@"GetGridIcons\WeaponGrid3.png", 32, 8)]
        /// <summary>
        /// 测试获取各种Grid界面中的圣遗物，使用复杂的cv算法，结果应正确
        /// </summary>
        public void GetGridIconsAlpha_ShouldBeRight(string screenshot, int count, int columns)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\{screenshot}");

            //
            var result = GridScreen.GridEnumerator.GetGridItems(mat, columns, findContoursAlpha: true);

            //
            Assert.Equal(count, result.Count());
        }
    }
}
