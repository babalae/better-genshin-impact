using BetterGenshinImpact.GameTask.GetGridIcons;
using BetterGenshinImpact.GameTask.Model.GameUI;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.GetGridIconsTests
{
    [Collection("Init Collection")]
    public class GridIconsAccuracyTestTaskTests
    {
        private readonly InferenceSession session;
        private readonly Dictionary<string, float[]> prototypes;

        public GridIconsAccuracyTestTaskTests(GridIconModelFixture model)
        {
            this.session = model.modelLoader.Value.session;
            this.prototypes = model.modelLoader.Value.prototypes;
        }

        public static IEnumerable<object[]> InferTestData()
        {
            yield return new object[] { @"GetGridIcons\FoodGrid.png", 8, new[] { ("苹果", 0), ("日落果", 0), ("星蕈", 0), ("泡泡桔", 0), ("烛伞蘑菇", 0), ("宝石闪闪", 4), ("咚咚", 4), ("枫达", 2),
                ("雾凇秋分", 4), ("蒙德土豆饼", 3), ("爪爪土豆饼", 3), ("北地苹果焖肉", 3), ("四方和平", 3), ("盛世太平", 3), ("三彩团子", 3), ("夏祭游鱼", 3)} };
            // todo 爪爪土豆饼被吃掉了没进训练集。。。
        }

        [Theory]
        [MemberData(nameof(InferTestData))]
        /// <summary>
        /// 测试推理图标的标签，结果应正确
        /// </summary>
        public void Infer_ShouldBeRight(string screenshot, int columns, (string, int)[] nameAndStars)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\{screenshot}");

            var gridItems = GridScreen.GridEnumerator.GetGridItems(mat, columns, findContoursAlpha: true);
            var rows = GridScreen.GridEnumerator.ClusterRows(gridItems, 5);

            //
            var result = new List<(string, int)>();
            foreach (var row in rows)
            {
                foreach (Rect rect in row)
                {
                    Mat gridItemMat = mat.SubMat(rect);
                    var pred = GridIconsAccuracyTestTask.Infer(gridItemMat, this.session, this.prototypes);
                    result.Add(pred);
                }
            }

            //
            for (int i = 0; i < nameAndStars.Length - 1; i++)
            {
                Assert.Equal(nameAndStars[i].Item1, result[i].Item1);
                Assert.Equal(nameAndStars[i].Item2, result[i].Item2);
            }
        }
    }
}
