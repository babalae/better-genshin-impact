using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;
using GameTask.Model.GameUI;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.AutoArtifactSalvage.AutoArtifactSalvageTask;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoArtifactSalvageTests
{
    [Collection("Init Collection")]
    public partial class AutoArtifactSalvageTaskTests
    {
        private readonly PaddleFixture paddle;
        public AutoArtifactSalvageTaskTests(PaddleFixture paddle)
        {
            this.paddle = paddle;
        }

        [Theory]
        [InlineData(@"ArtifactGrid.png", 7, 7, 126, 162, ArtifactStatus.Selected)]
        [InlineData(@"ArtifactGrid.png", 140, 2, 136, 168, ArtifactStatus.Selected)]
        [InlineData(@"ArtifactGrid.png", 7, 176, 126, 154, ArtifactStatus.Locked)]
        [InlineData(@"ArtifactGrid.png", 139, 172, 136, 165, ArtifactStatus.Locked)]
        /// <summary>
        /// 测试获取分解圣遗物Grid界面中圣遗物的状态，结果应正确
        /// </summary>
        public void GetArtifactStatus_VariousStatus_ShouldBeRight(string screenshot, int x, int y, int width, int height, ArtifactStatus status)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\AutoArtifactSalvage\{screenshot}");
            using Mat cropped = new Mat(mat, new Rect(x, y, width, height));

            //
            ArtifactStatus result = AutoArtifactSalvageTask.GetArtifactStatus(cropped);

            //
            Assert.Equal(status, result);
        }

        [Theory]
        [InlineData(@"ArtifactGrid.png")]
        /// <summary>
        /// 测试获取分解圣遗物Grid界面中的圣遗物，结果应正确
        /// </summary>
        public void GetArtifactGridItems_ShouldBeRight(string screenshot)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\AutoArtifactSalvage\{screenshot}");

            //
            var result = GridScreen.GridEnumerator.GetGridItems(mat);

            //
            Assert.Equal(4, result.Count());
        }


        [Fact]
        /// <summary>
        /// 测试获取分解圣遗物Grid界面中的圣遗物，以及其状态，结果应正确
        /// </summary>
        public void GetArtifactGridItems_AndStatus_ShouldBeRight()
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\AutoArtifactSalvage\ArtifactGrid.png");

            //
            var result = GridScreen.GridEnumerator.GetGridItems(mat);
            using Mat leftTopOne = new Mat(mat, result.Single(r => r.X + r.Width / 2 < mat.Width / 2 && r.Y + r.Height / 2 < mat.Height / 2));
            using Mat rightTopOne = new Mat(mat, result.Single(r => r.X + r.Width / 2 > mat.Width / 2 && r.Y + r.Height / 2 < mat.Height / 2));
            using Mat leftBottomOne = new Mat(mat, result.Single(r => r.X + r.Width / 2 < mat.Width / 2 && r.Y + r.Height / 2 > mat.Height / 2));
            using Mat rightBottomOne = new Mat(mat, result.Single(r => r.X + r.Width / 2 > mat.Width / 2 && r.Y + r.Height / 2 > mat.Height / 2));
            var leftTopOneStatus = AutoArtifactSalvageTask.GetArtifactStatus(leftTopOne);
            var rightTopOneStatus = AutoArtifactSalvageTask.GetArtifactStatus(rightTopOne);
            var leftBottomOneStatus = AutoArtifactSalvageTask.GetArtifactStatus(leftBottomOne);
            var rightBottomOneStatus = AutoArtifactSalvageTask.GetArtifactStatus(rightBottomOne);

            //
            Assert.Equal(4, result.Count());
            Assert.Equal(ArtifactStatus.Selected, leftTopOneStatus);
            Assert.Equal(ArtifactStatus.Selected, rightTopOneStatus);
            Assert.Equal(ArtifactStatus.Locked, leftBottomOneStatus);
            Assert.Equal(ArtifactStatus.Locked, rightBottomOneStatus);
        }

        private static ConcurrentDictionary<string, string> PaddleResultDic { get; } = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// 测试获取分解圣遗物界面右侧圣遗物的词缀等属性，结果应正确
        /// </summary>
        /// <param name="screenshot"></param>
        [Theory]
        [InlineData(@"ArtifactAffixes.png", "异种的期许")]
        [InlineData(@"ArtifactAffixes.png", "生之花")]
        [InlineData(@"ArtifactAffixes.png", "生命值")]
        [InlineData(@"ArtifactAffixes.png", @"(?=[\S\s]*元素精通\+)(?=[\S\s]*元素充能效率\+)")]
        [InlineData(@"ArtifactAffixes.png", @"(?=[\S\s]*元素精通\+)(?=[\S\s]*暴击率\+)", false)]
        [InlineData(@"ArtifactAffixes.png", @"(元素精通\+)|(暴击率\+)")]
        [InlineData(@"ArtifactAffixes.png", @"(暴击伤害\+)|(暴击率\+)", false)]
        [InlineData(@"ArtifactAffixes.png", @"攻击力\+[\d.]*%\n")]
        [InlineData(@"ArtifactAffixes.png", @"攻击力\+[\d]*\n", false)]
        [InlineData(@"ArtifactAffixes.png", @"防御力\+[\d.]*%\n", false)]
        [InlineData(@"ArtifactAffixes.png", @"防御力\+[\d]*\n")]
        public void GetArtifactAffixes_ShouldBeRight(string screenshot, string pattern, bool isMatch = true)
        {
            //

            //
            string result = PaddleResultDic.GetOrAdd(screenshot, screenshot_ =>
            {
                using Mat mat = new Mat(@$"..\..\..\Assets\AutoArtifactSalvage\{screenshot_}");
                return AutoArtifactSalvageTask.GetArtifactAffixes(mat, paddle.Get());
            });

            //
            if (isMatch)
            {
                Assert.Matches(pattern, result);
            }
            else
            {
                Assert.DoesNotMatch(pattern, result);
            }
        }
    }
}
