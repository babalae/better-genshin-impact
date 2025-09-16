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

        [Theory]
        [InlineData(@"AutoDomain\SelectRevitalization.png", 21, 0, 2, 1)]
        [InlineData(@"AutoDomain\SelectRevitalizationOcrV4.png", 11, 0, 1, 149, "V4")]
        /// <summary>
        /// 测试识别四种树脂数量，数量应正确
        /// </summary>
        public void RecogniseFromRegion_ResinStatusShouldBeRight(string screenshot1080p, int originalResinCount, int fragileResinCount, int condensedResinCount, int transientResinCount, string ocrVersion = "V5")
        {
            //
            Mat mat = new Mat(@$"..\..\..\Assets\{screenshot1080p}");
            var imageRegion = new ImageRegion(mat, 0, 0, converter: new ScaleConverter(1d));
            FakeSystemInfo systemInfo = new FakeSystemInfo(new Vanara.PInvoke.RECT(0, 0, mat.Width, mat.Height), 1);

            //
            var result = ResinStatus.RecogniseFromRegion(imageRegion, systemInfo, this.paddle.Get(version: ocrVersion));   // todo：System.Exception : 未找到原粹树脂图标

            //
            Assert.Equal(originalResinCount, result.OriginalResinCount);
            Assert.Equal(condensedResinCount, result.CondensedResinCount);
        }
    }
}
