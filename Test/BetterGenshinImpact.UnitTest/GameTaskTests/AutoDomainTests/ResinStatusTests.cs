using BetterGenshinImpact.GameTask.AutoDomain.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomainTests
{
    [Collection("Init Collection")]
    public class ResinStatusTests
    {
        private readonly PaddleFixture paddle;

        public ResinStatusTests(PaddleFixture paddle)
        {
            this.paddle = paddle;
        }

        /// <summary>
        /// 测试顶栏原粹与浓缩树脂数量；须臾与脆弱库存不在该区域识别。
        /// </summary>
        [Theory]
        [InlineData(@"AutoDomain\SelectRevitalization.png", 21, 2)]
        [InlineData(@"AutoDomain\SelectRevitalizationOcrV4.png", 11, 1, "V4")]
        [InlineData(@"AutoDomain\SelectRevitalizationOcrV4_NSSZ1.png", 20, 1, "V4")]
        [InlineData(@"AutoDomain\SelectRevitalizationOcrV4_NSSZ1.png", 20, 1)]
        public void RecogniseFromRegion_ResinStatusShouldBeRight(string screenshot1080p, int originalResinCount, int condensedResinCount, string ocrVersion = "V5")
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\{screenshot1080p}");
            var imageRegion = new ImageRegion(mat, 0, 0, converter: new ScaleConverter(1d));
            FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);

            //
            var result = ResinStatus.RecogniseFromRegion(imageRegion, systemInfo, this.paddle.Get(version: ocrVersion));

            //
            Assert.Equal(originalResinCount, result.OriginalResinCount);
            Assert.Equal(condensedResinCount, result.CondensedResinCount);
        }
    }
}
