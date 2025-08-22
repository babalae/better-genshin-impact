using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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

        [Fact]
        /// <summary>
        /// 测试获取分解圣遗物Grid界面中的圣遗物，以及其状态，结果应正确
        /// </summary>
        public void GetArtifactGridItems_AndStatus_ShouldBeRight()
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\AutoArtifactSalvage\ArtifactGrid.png");

            //
            var result = GridScreen.GridEnumerator.GetGridItems(mat, 2);
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
        /// 测试获取分解圣遗物界面右侧圣遗物的词缀等属性，使用正则表达式，结果应正确
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
        public void GetArtifactStat_RegexPatternShouldBeRight(string screenshot, string pattern, bool isMatch = true)
        {
            //
            CultureInfo cultureInfo = new CultureInfo("zh-Hans");

            //
            string result = PaddleResultDic.GetOrAdd(screenshot, screenshot_ =>
            {
                using Mat mat = new Mat(@$"..\..\..\Assets\AutoArtifactSalvage\{screenshot_}");
                GetArtifactStat(mat, paddle.Get(), cultureInfo, out string allText);
                return allText;
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

        /// <summary>
        /// 测试获取分解圣遗物界面右侧圣遗物的各种结构化信息，结果应正确
        /// </summary>
        /// <param name="screenshot"></param>
        [Theory]
        [InlineData(@"ArtifactAffixes.png")]
        public void GetArtifactStat_AffixesShouldBeRight(string screenshot)
        {
            //
            CultureInfo cultureInfo = new CultureInfo("zh-Hans");

            //
            using Mat mat = new Mat(@$"..\..\..\Assets\AutoArtifactSalvage\{screenshot}");
            ArtifactStat artifact = GetArtifactStat(mat, paddle.Get(), cultureInfo, out string _);

            //
            Assert.Equal("异种的期许", artifact.Name);
            Assert.True(artifact.MainAffix.Type == ArtifactAffixType.HP);
            Assert.True(artifact.MainAffix.Value == 717f);
            Assert.Contains(artifact.MinorAffixes, a => a.Type == ArtifactAffixType.ElementalMastery && a.Value == 16f);
            Assert.Contains(artifact.MinorAffixes, a => a.Type == ArtifactAffixType.EnergyRecharge && a.Value == 6.5f);
            Assert.Contains(artifact.MinorAffixes, a => a.Type == ArtifactAffixType.ATKPercent && a.Value == 5.8f);
            Assert.Contains(artifact.MinorAffixes, a => a.Type == ArtifactAffixType.DEF && a.Value == 23f);
            Assert.True(artifact.Level == 0);
        }

        [Theory]
        [InlineData(@"ArtifactAffixes.png", @"(async function (artifact) {
                    var hasATK = Array.from(artifact.MinorAffixes).some(affix => affix.Type == 'ATK');
                    var hasDEF = Array.from(artifact.MinorAffixes).some(affix => affix.Type == 'DEF');
                    Output = hasATK && hasDEF;
                })(ArtifactStat);", false)]
        [InlineData(@"ArtifactAffixes.png", @"(async function (artifact) {
                    var level = artifact.Level;
                    var hasATKPercent = Array.from(artifact.MinorAffixes).some(affix => affix.Type == 'ATKPercent');
                    var hasDEF = Array.from(artifact.MinorAffixes).some(affix => affix.Type == 'DEF');
                    Output = level == 0 && hasATKPercent && hasDEF;
                })(ArtifactStat);", true)]
        public void IsMatchJavaScript_JSShouldBeRight(string screenshot, string js, bool expected)
        {
            //
            CultureInfo cultureInfo = new CultureInfo("zh-Hans");

            //
            using Mat mat = new Mat(@$"..\..\..\Assets\AutoArtifactSalvage\{screenshot}");
            ArtifactStat artifact = GetArtifactStat(mat, paddle.Get(), cultureInfo, out string _);
            bool result = IsMatchJavaScript(artifact, js);

            //
            Assert.Equal(expected, result);
        }
    }
}
