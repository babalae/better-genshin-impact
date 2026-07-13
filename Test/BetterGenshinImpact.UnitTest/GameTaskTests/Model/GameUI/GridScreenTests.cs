using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.Model.GameUI
{
    [Collection("Init Collection")]
    public class GridScreenTests
    {
        private readonly PaddleFixture paddle;
        public GridScreenTests(PaddleFixture paddle)
        {
            this.paddle = paddle;
        }

        [Theory]
        [InlineData(@"AutoArtifactSalvage\ArtifactGrid.png", 4, 2)]
        [InlineData(@"GetGridIcons\FoodGrid.png", 32, 8)]
        [InlineData(@"GetGridIcons\WeaponGrid.png", 4, 2)]
        [InlineData(@"GetGridIcons\WeaponGrid3.png", 32, 8)]
        /// <summary>
        /// 测试获取各种界面中的物品图标，经过算法的后处理，结果应正确
        /// </summary>
        public void GetGridIcons_ShouldBeRight(string screenshot, int count, int columns)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\{screenshot}");

            //
            var rects = GridScreen.GridEnumerator.GetGridItems(mat, columns);
            var cells = GridScreen.GridEnumerator.PostProcess(mat, rects, (int)(0.025 * mat.Height));

            //
            Assert.Equal(count, cells.Count());
        }

        [Theory]
        [InlineData(@"AutoArtifactSalvage\ArtifactGrid.png", 4, 2)]
        [InlineData(@"GetGridIcons\FoodGrid.png", 32, 8)]
        [InlineData(@"GetGridIcons\WeaponGrid.png", 4, 2)]
        [InlineData(@"GetGridIcons\WeaponGrid3.png", 32, 8)]
        /// <summary>
        /// 测试获取各种界面中的物品图标，使用复杂的cv算法，经过算法的后处理，结果应正确
        /// </summary>
        public void GetGridIconsAlpha_ShouldBeRight(string screenshot, int count, int columns)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\{screenshot}");

            //
            var rects = GridScreen.GridEnumerator.GetGridItems(mat, columns, findContoursAlpha: true);
            var cells = GridScreen.GridEnumerator.PostProcess(mat, rects, (int)(0.025 * mat.Height));

            //
            Assert.Equal(count, cells.Count());
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
            bool result1 = GridScroller.IsScrolling(mat, scrolled, out Point2d shift, upperThreshold: 0.99);
            bool result2 = GridScroller.IsScrolling(mat, mat, out Point2d _);
            bool result3 = GridScroller.IsScrolling(mat, black, out Point2d _);

            //
            Assert.True(result1);
            Assert.True(shift.Y <= 10 && shift.Y > 9.9);
            Assert.False(result2);
            Assert.False(result3);
        }

        public static IEnumerable<object[]> GetGridItemIconTextTestData()
        {
            yield return new object[] { @"GetGridIcons\FoodGrid.png", 8, new[] { 1850, 1073, 167, 85, 69, 6, 1, 90,
            6, 5, 1, 10, 15, 3, 2, 1,
            2, 2, 2, 1, 3, 2, 2, 4,
            1, 1, 3, 2, 2, 1, 1, 1} };
        }

        [Theory]
        [MemberData(nameof(GetGridItemIconTextTestData))]
        /// <summary>
        /// 测试获取各种界面中的物品图标文字，全部转数字，结果应正确
        /// </summary>
        public void GetGridItemIconText_Number_ShouldBeRight(string screenshot, int columns, int[] numbers)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\{screenshot}");

            //
            var gridItems = GridScreen.GridEnumerator.GetGridItems(mat, columns, findContoursAlpha: true);
            var cells = GridCell.ClusterToCells(gridItems, 10).OrderBy(c => c.RowNum).ThenBy(c => c.ColNum);

            var result = new List<string>();
            foreach (var cell in cells)
            {
                using Mat gridItemMat = mat.SubMat(cell.Rect);
                string numStr = gridItemMat.GetGridItemIconText(paddle.Get());
                result.Add(numStr);
            }

            //
            for (int i = 0; i < numbers.Length - 1; i++)
            {
                Assert.True(int.TryParse(result[i], out int intResult), $"第{i + 1}个图标文字解析失败-->{result[i]}<--");
                Assert.Equal(numbers[i], intResult);
            }
        }
    }
}
