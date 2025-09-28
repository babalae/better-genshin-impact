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
            yield return new object[] { @"GetGridIcons\FoodGrid.png", 8, true, new[] { ("苹果", 0), ("日落果", 0), ("星蕈", 0), ("泡泡桔", 0), ("烛伞蘑菇", 0), ("宝石闪闪", 4), ("咚咚", 4), ("枫达", 2),
                ("雾凇秋分", 4), ("蒙德土豆饼", 3), /*("爪爪土豆饼", 3), ("北地苹果焖肉", 3), ("四方和平", 3), ("盛世太平", 3), ("三彩团子", 3), ("夏祭游鱼", 3)*/} };
            // todo 爪爪土豆饼被吃掉了没进训练集。。。
            yield return new object[] { @"GetGridIcons\FoodGrid_Attack.png", 8, false, new[] { ("果果软糖", 3), ("方块戏法", 3), ("「缥雨一滴」", 3), ("繁弦急管", 3), ("繁弦急管", 3), ("「簇火赞歌」", 3), ("轻策家常菜", 3), ("冒险家蛋堡", 3), ("冒险家蛋堡", 3), ("测绘员蛋堡", 3), ("串串三味", 3), ("连心面", 3), ("摩拉急速来", 3), ("双果清露", 3), ("双果清露", 3), ("双果清露", 3), ("四喜圆满", 3), ("祝圣交响乐", 3), ("满足沙拉", 2), ("满足沙拉", 2), ("至高的智慧（生活）", 2), ("摇·滚·鸡！", 2), ("岩港三鲜", 2), ("鲜鱼炖萝卜", 2), ("炸萝卜丸子", 2), ("炸萝卜丸子", 2), ("杏仁豆腐", 2), ("杏仁豆腐", 2), ("「美梦」", 2), ("凉拌薄荷", 2), ("炸肉排三明治", 2), ("唯一的真相", 2) } };
            yield return new object[] { @"GetGridIcons\MaterialGrid_TreesAndBaits.png", 8, false, new[] { ("松木", 1), ("却砂木", 1), ("竹节", 1), ("垂香木", 1), ("杉木", 1), ("梦见木", 1), ("枫木", 1), ("孔雀木", 1), ("御伽木", 1), ("辉木", 1), ("业果木", 1), ("证悟木", 1), ("刺葵木", 1), ("柽木", 1), ("悬铃木", 1), ("白梣木", 1), ("香柏木", 1), ("白栗栎木", 1), ("灰灰楼林木", 1), ("燃爆木", 1), ("布匹", 0), ("红色染料", 0), ("黄色染料", 0), ("蓝色染料", 0), ("果酿饵", 2), ("赤糜饵", 2), ("蠕虫假饵", 2), ("飞蝇假饵", 2), ("甘露饵", 2), ("酸桔饵", 2), ("维护机关频闪诱饵", 2), ("澄晶果粒饵", 2) } };
            // string.Join(", ",result.Select(s=>$"(\"{s.Item1}\",  {s.Item2})"))
        }

        [Theory]
        [MemberData(nameof(InferTestData))]
        /// <summary>
        /// 测试推理图标的标签，结果应正确
        /// </summary>
        public void Infer_ShouldBeRight(string screenshot, int columns, bool findContoursAlpha, (string, int)[] nameAndStars)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\{screenshot}");

            var gridItems = GridScreen.GridEnumerator.GetGridItems(mat, columns, findContoursAlpha: findContoursAlpha);
            var rows = GridScreen.GridEnumerator.ClusterRows(gridItems, 10);

            //
            var result = new List<(string?, int)>();
            foreach (var row in rows)
            {
                foreach (Rect rect in row)
                {
                    Mat gridItemMat = mat.SubMat(rect);
                    using Mat icon = gridItemMat.GetGridIcon();
                    var pred = GridIconsAccuracyTestTask.Infer(icon, this.session, this.prototypes);
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
