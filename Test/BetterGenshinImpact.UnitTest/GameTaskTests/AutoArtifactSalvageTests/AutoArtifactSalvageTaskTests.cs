using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;
using Microsoft.Extensions.Localization;
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
    public class AutoArtifactSalvageTaskTests
    {
        private readonly PaddleFixture paddle;
        private readonly IStringLocalizer<AutoArtifactSalvageTask> stringLocalizer;

        public AutoArtifactSalvageTaskTests(PaddleFixture paddle, LocalizationFixture localization)
        {
            this.paddle = paddle;
            this.stringLocalizer = localization.CreateStringLocalizer<AutoArtifactSalvageTask>();
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
            AutoArtifactSalvageTask sut = new AutoArtifactSalvageTask(new AutoArtifactSalvageTaskParam(5, null, null, null, null, cultureInfo, this.stringLocalizer), new FakeLogger());
            string result = PaddleResultDic.GetOrAdd(screenshot, screenshot_ =>
            {
                using Mat mat = new Mat(@$"..\..\..\Assets\AutoArtifactSalvage\{screenshot_}");
                sut.GetArtifactStat(mat, paddle.Get(), out string allText);
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

        public static IEnumerable<object[]> GetArtifactStatTestData
        {
            get
            {
                yield return new object[] { "ArtifactAffixes.png", new ArtifactStat("异种的期许", new ArtifactAffix(ArtifactAffixType.HP, 717f), [
                    new ArtifactAffix(ArtifactAffixType.ElementalMastery, 16f),
                    new ArtifactAffix(ArtifactAffixType.EnergyRecharge, 6.5f),
                    new ArtifactAffix(ArtifactAffixType.ATKPercent, 5.8f),
                    new ArtifactAffix(ArtifactAffixType.DEF, 23f)
                ], 0), new CultureInfo("zh-Hans") };
                yield return new object[] { "202508242224_GetArtifactStat.png", new ArtifactStat("裁判的时刻", new ArtifactAffix(ArtifactAffixType.DEFPercent, 8.7f), [
                    new ArtifactAffix(ArtifactAffixType.ATK, 16f),
                    new ArtifactAffix(ArtifactAffixType.CRITDMG, 7.8f),
                    new ArtifactAffix(ArtifactAffixType.DEF, 19),
                    new ArtifactAffix(ArtifactAffixType.CRITRate, 2.7f)
                ], 0), new CultureInfo("zh-Hans") };
                yield return new object[] { "202508252004_GetArtifactStat.png", new ArtifactStat("Deep Gallery's Bestowed Banquet", new ArtifactAffix(ArtifactAffixType.DEFPercent, 8.7f), [
                    new ArtifactAffix(ArtifactAffixType.HP, 239),
                    new ArtifactAffix(ArtifactAffixType.ATK, 18),
                    new ArtifactAffix(ArtifactAffixType.HPPercent, 4.1f)
                ], 0), new CultureInfo("en") };
                yield return new object[] { "20250827000123_GetArtifactStat.png", new ArtifactStat("Pacte distant des galeries profondes", new ArtifactAffix(ArtifactAffixType.ATK, 47), [
                    new ArtifactAffix(ArtifactAffixType.DEFPercent, 7.3f),
                    new ArtifactAffix(ArtifactAffixType.DEF, 16),
                    new ArtifactAffix(ArtifactAffixType.HPPercent, 5.3f)
                ], 0), new CultureInfo("fr") };
                yield return new object[] { "20250827000246_GetArtifactStat.png", new ArtifactStat("Reckoning of the Xenogenic", new ArtifactAffix(ArtifactAffixType.HP, 717f), [
                    new ArtifactAffix(ArtifactAffixType.ElementalMastery, 16),
                    new ArtifactAffix(ArtifactAffixType.EnergyRecharge, 6.5f),
                    new ArtifactAffix(ArtifactAffixType.ATKPercent, 5.8f),
                    new ArtifactAffix(ArtifactAffixType.DEF, 23)
                ], 0), new CultureInfo("en") };
                yield return new object[] { "20250827151043_GetArtifactStat.png", new ArtifactStat("Couronne de laurier", new ArtifactAffix(ArtifactAffixType.ATKPercent, 7.0f), [
                    new ArtifactAffix(ArtifactAffixType.CRITDMG, 5.4f),
                    new ArtifactAffix(ArtifactAffixType.DEFPercent, 7.3f),
                    new ArtifactAffix(ArtifactAffixType.DEF, 16),
                    new ArtifactAffix(ArtifactAffixType.HPPercent, 5.8f)
                ], 0), new CultureInfo("fr") }; // 一个百里挑一的识别失败的例子
                yield return new object[] { "20250827163340_GetArtifactStat.png", new ArtifactStat("Couronne de Watatsumi", new ArtifactAffix(ArtifactAffixType.DEFPercent, 8.7f), [
                    new ArtifactAffix(ArtifactAffixType.HP, 299),
                    new ArtifactAffix(ArtifactAffixType.EnergyRecharge, 4.5f),
                    new ArtifactAffix(ArtifactAffixType.CRITDMG, 6.2f),
                    new ArtifactAffix(ArtifactAffixType.ElementalMastery, 23)
                ], 0), new CultureInfo("fr") };
                yield return new object[] { "20250827204545_GetArtifactStat.png", new ArtifactStat("Agitation de la nuit dorée", new ArtifactAffix(ArtifactAffixType.GeoDMGBonus, 7.0f), [
                    new ArtifactAffix(ArtifactAffixType.HP, 269),
                    new ArtifactAffix(ArtifactAffixType.ElementalMastery, 23),
                    new ArtifactAffix(ArtifactAffixType.CRITRate, 3.1f)
                ], 0), new CultureInfo("fr") };
                yield return new object[] { "20250828084843_GetArtifactStat.png", new ArtifactStat("異種的期許",  new ArtifactAffix(ArtifactAffixType.HP, 717f), [
                    new ArtifactAffix(ArtifactAffixType.DEFPercent, 7.3f),
                    new ArtifactAffix(ArtifactAffixType.ElementalMastery, 23),
                    new ArtifactAffix(ArtifactAffixType.ATKPercent, 4.1f)
                ], 0), new CultureInfo("zh-Hant") };
                yield return new object[] { "20250828093344_GetArtifactStat.png", new ArtifactStat("黃金時代的先聲",new ArtifactAffix(ArtifactAffixType.DEFPercent, 8.7f), [
                    new ArtifactAffix(ArtifactAffixType.DEF, 19),
                    new ArtifactAffix(ArtifactAffixType.CRITDMG, 7.8f),
                    new ArtifactAffix(ArtifactAffixType.ATK, 18),
                    new ArtifactAffix(ArtifactAffixType.ElementalMastery, 23)
                ], 0), new CultureInfo("zh-Hant") };
            }
        }

        /// <summary>
        /// 测试获取分解圣遗物界面右侧圣遗物的各种结构化信息，结果应正确
        /// </summary>
        /// <param name="screenshot"></param>
        [Theory]
        [MemberData(nameof(GetArtifactStatTestData))]
        public void GetArtifactStat_AffixesShouldBeRight(string screenshot, ArtifactStat expectedArtifactStat, CultureInfo cultureInfo)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\AutoArtifactSalvage\{screenshot}");

            /*
            using var gray = mat.CvtColor(ColorConversionCodes.BGR2GRAY);
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(15, 15));
            using var bottomHat = gray.MorphologyEx(MorphTypes.TopHat, kernel);
            Cv2.ImShow("bottomHat", bottomHat);
            Mat threshold = new Mat();
            var otsu = Cv2.Threshold(bottomHat, threshold, 30, 255, ThresholdTypes.Binary);
            Cv2.ImShow($"thres = {otsu}", threshold);
            Cv2.WaitKey();
            */

            //
            AutoArtifactSalvageTask sut = new AutoArtifactSalvageTask(new AutoArtifactSalvageTaskParam(5, null, null, null, null, cultureInfo, this.stringLocalizer), new FakeLogger());
            ArtifactStat result = sut.GetArtifactStat(mat, paddle.Get(cultureInfo.Name), out string _);

            //
            Assert.Equal(expectedArtifactStat.Name, result.Name);
            Assert.Equal(expectedArtifactStat.MainAffix.Type, result.MainAffix.Type);
            Assert.Equal(expectedArtifactStat.MainAffix.Value, result.MainAffix.Value);
            foreach (ArtifactAffix expectedArtifactAffix in expectedArtifactStat.MinorAffixes)
            {
                Assert.Contains(result.MinorAffixes, a =>
                a.Type == expectedArtifactAffix.Type && a.Value == expectedArtifactAffix.Value);
            }
            Assert.True(result.Level == expectedArtifactStat.Level);
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
            using Mat mat = new Mat(@$"..\..\..\Assets\AutoArtifactSalvage\{screenshot}");
            CultureInfo cultureInfo = new CultureInfo("zh-Hans");

            //
            AutoArtifactSalvageTask sut = new AutoArtifactSalvageTask(new AutoArtifactSalvageTaskParam(5, null, null, null, null, cultureInfo, this.stringLocalizer), new FakeLogger());
            ArtifactStat artifact = sut.GetArtifactStat(mat, paddle.Get(), out string _);
            bool result = IsMatchJavaScript(artifact, js);

            //
            Assert.Equal(expected, result);
        }
    }
}
