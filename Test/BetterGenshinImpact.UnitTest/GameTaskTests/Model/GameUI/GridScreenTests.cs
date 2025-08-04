using BetterGenshinImpact.GameTask.Model.GameUI;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.Model.GameUI
{
    public class GridScreenTests
    {
        [Theory]
        [InlineData(@"AutoArtifactSalvage\ArtifactGrid.png", 4, 2)]
        [InlineData(@"GetGridIcons\WeaponGrid3.png", 32, 8)]
        /// <summary>
        /// 测试获取各种界面中的物品图标，结果应正确
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
        /// 测试获取各种界面中的物品图标，使用复杂的cv算法，结果应正确
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

        [Fact]
        /// <summary>
        /// 测试判断前后两张图是否属于滚动，结果应正确
        /// </summary>
        public void IsScrolling_ResultShouldBeRight()
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\AutoArtifactSalvage\ArtifactGrid.png", flags: ImreadModes.Grayscale);
            using Mat cropped = mat[new Rect(0, 0, mat.Width, mat.Height - 10)];
            using Mat black = new Mat(mat.Size(), mat.Type(), Scalar.Black);
            using Mat scrolled = black.Clone();    // 一个向下平移了10像素的图
            using Mat pos = scrolled[new Rect(0, 10, mat.Width, mat.Height - 10)];
            cropped.CopyTo(pos);

            //
            bool result1 = GridScreen.GridEnumerator.IsScrolling(mat, scrolled, out Point2d shift, upperThreshold: 0.99);
            bool result2 = GridScreen.GridEnumerator.IsScrolling(mat, mat, out Point2d _);
            bool result3 = GridScreen.GridEnumerator.IsScrolling(mat, black, out Point2d _);

            //
            Assert.True(result1);
            Assert.True(shift.Y <= 10 && shift.Y > 9.9);
            Assert.False(result2);
            Assert.False(result3);
        }
    }
}
