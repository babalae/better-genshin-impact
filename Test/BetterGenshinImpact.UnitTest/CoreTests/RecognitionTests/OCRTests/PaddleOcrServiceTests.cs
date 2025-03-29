using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using System.Drawing;
using OpenCvSharp.Extensions;

namespace BetterGenshinImpact.UnitTest.CoreTests.RecognitionTests.OCRTests
{
    [Collection("Paddle Collection")]
    public partial class PaddleOcrServiceTests
    {
        private readonly PaddleFixture paddle;
        public PaddleOcrServiceTests(PaddleFixture paddle)
        {
            this.paddle = paddle;
        }

        [Theory]
        [InlineData("zh-Hans", "挑战,达成", "[挑战,达成]")]
        [InlineData("zh-Hans", "凯瑟琳")]
        [InlineData("en", "Daily")]
        [InlineData("en", "Katheryne")]
        [InlineData("zh-Hant", "凱瑟琳")]
        [InlineData("zh-Hant", "委託", "委[託,話]")]
        [InlineData("zh-Hant", "挑戰,達成", "[挑戰,達成]")]
        [InlineData("zh-Hant", "自動,退出", "[自動,退出]")]
        [InlineData("zh-Hant", "跳過")]
        [InlineData("fr", "quotidien")]
        [InlineData("fr", "Expédition")]
        [InlineData("fr", "Pêcher", "P[ê|é]cher")]
        [InlineData("fr", "Synthèse", "Synth[è|é][se|tiser]")]
        [InlineData("fr", "Synthétiser", "Synth[è|é][se|tiser]")]
        [InlineData("fr", "Défi, terminé", "[Défi,terminé]")]
        [InlineData("fr", "Sortie")]
        [InlineData("fr", "Passer")]
        /// <summary>
        /// 测试识别各种文字，结果为成功
        /// </summary>
        public void PaddleOcrService_VariousLangWords_ShouldEqualOrMatch(string cultureInfoName, string word, string? pattern = null)
        {
            //
            using Bitmap bitmap = new Bitmap(word.Length * 50, 100);

            using Graphics g = Graphics.FromImage(bitmap);
            g.Clear(Color.White);
            using Font font = new Font("Arial", 12);
            using Brush brush = new SolidBrush(Color.Black);
            g.DrawString(word, font, brush, 0, 50);
            using Mat mat4 = bitmap.ToMat();
            using Mat mat = mat4.CvtColor(ColorConversionCodes.RGBA2RGB);

            //
            PaddleOcrService sut = paddle.Get(cultureInfoName);
            string actual = sut.Ocr(mat).Replace(" ", "");

            //
            if (pattern == null)
            {
                Assert.Equal(word, actual);
            }
            else
            {
                Assert.Matches(pattern, actual);
            }
        }
    }
}
