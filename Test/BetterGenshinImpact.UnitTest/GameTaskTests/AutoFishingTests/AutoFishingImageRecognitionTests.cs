using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    public class AutoFishingImageRecognitionTests
    {
        [Theory]
        [InlineData(@"20250306111752769_GetFishBoxArea_Succeeded.png", 2)]
        [InlineData(@"20250313-0442-39.4967763.mp4_20250313_132441.664.png", 2)]   // 网友反馈的案例，有偏色
        [InlineData(@"20250313-0442-39.4967763.mp4_20250313_132441.969.png", 2)]
        [InlineData(@"20250314112457916_Fishing_Running.png", 3)]   // 未熟练的鱼，条会变黄，两侧出现颜色接近的动态折线
        [InlineData(@"202503140802528967.png", 3)]
        [InlineData(@"20250314155120958_Fishing_Error.png", 3)]
        /// <summary>
        /// 测试获取钓鱼拉扯框，结果框数匹配
        /// </summary>
        public void GetFishBarRect_ShouldMatchRectCount(string screenshot1080p, int rectCount)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            using Mat cropped = new Mat(mat, new Rect(0, 0, 1920, 140));

            //
            List<Rect>? sut = AutoFishingImageRecognition.GetFishBarRect(cropped);

            //
            Assert.Equal(rectCount, sut?.Count);
        }

        /// <summary>
        /// 测试 IsValidFishBar 对非法输入返回 false
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData(new int[0])]
        [InlineData(new int[1] { 1 })]
        [InlineData(new int[3] { 1, 2, 3 })]
        public void IsValidFishBar_NullOrWrongCount_ShouldReturnFalse(int[] rectCounts)
        {
            //
            List<Rect>? rects = rectCounts?.Select((_, i) => new Rect(i * 10, 0, 100, 20)).ToList();

            //
            bool valid = AutoFishingImageRecognition.IsValidFishBar(rects, 1920);

            //
            Assert.False(valid);
        }

        /// <summary>
        /// 测试 IsValidFishBar 对高度差过大的两个矩形返回 false
        /// </summary>
        [Fact]
        public void IsValidFishBar_HeightDifferenceTooLarge_ShouldReturnFalse()
        {
            //
            List<Rect> rects = [new Rect(100, 0, 50, 20), new Rect(500, 0, 300, 40)]; // 高度差 20 > 10

            //
            bool valid = AutoFishingImageRecognition.IsValidFishBar(rects, 1920);

            //
            Assert.False(valid);
        }

        /// <summary>
        /// 测试标准布局（游标窄且靠左、进度条宽且靠右）返回 true
        /// </summary>
        [Fact]
        public void IsValidFishBar_NormalLayout_ShouldReturnTrue()
        {
            //
            // 游标：x=100, w=30, h=20；进度条：x=700, w=600, h=20
            // 校验：游标右缘 130 <= 中轴线 960；130 <= 700-300=400；130 <= 960-600=360；进度条在游标右侧
            List<Rect> rects = [new Rect(100, 0, 30, 20), new Rect(700, 0, 600, 20)];

            //
            bool valid = AutoFishingImageRecognition.IsValidFishBar(rects, 1920);

            //
            Assert.True(valid);
        }

        /// <summary>
        /// 测试游标在进度条右侧（左右顺序颠倒）返回 false
        /// </summary>
        [Fact]
        public void IsValidFishBar_CursorRightOfBar_ShouldReturnFalse()
        {
            //
            // 进度条 x=100，游标 x=700，游标反而在右侧
            List<Rect> rects = [new Rect(700, 0, 30, 20), new Rect(100, 0, 600, 20)];

            //
            bool valid = AutoFishingImageRecognition.IsValidFishBar(rects, 1920);

            //
            Assert.False(valid);
        }

        /// <summary>
        /// 测试游标越过屏幕左半侧（右缘超过中轴线）返回 false
        /// </summary>
        [Fact]
        public void IsValidFishBar_CursorCrossesCenterline_ShouldReturnFalse()
        {
            //
            // 游标 x=950, w=30 -> 右缘 980 > 960（中轴线）
            List<Rect> rects = [new Rect(950, 0, 30, 20), new Rect(1100, 0, 600, 20)];

            //
            bool valid = AutoFishingImageRecognition.IsValidFishBar(rects, 1920);

            //
            Assert.False(valid);
        }

        /// <summary>
        /// 测试游标距离进度条过远（超过进度条一半宽度）返回 false
        /// </summary>
        [Fact]
        public void IsValidFishBar_CursorTooFarFromBar_ShouldReturnFalse()
        {
            //
            // 进度条 x=1000, w=600 -> 左半宽边界 = 1000-300=700；游标右缘 800 > 700
            List<Rect> rects = [new Rect(770, 0, 30, 20), new Rect(1000, 0, 600, 20)];

            //
            bool valid = AutoFishingImageRecognition.IsValidFishBar(rects, 1920);

            //
            Assert.False(valid);
        }

        /// <summary>
        /// 测试游标超出"中轴线-进度条宽度"的左侧范围返回 false
        /// </summary>
        [Fact]
        public void IsValidFishBar_CursorBeyondCenterMinusBarWidth_ShouldReturnFalse()
        {
            //
            // 进度条 w=600 -> 边界 = 960-600=360；游标右缘 400 > 360
            List<Rect> rects = [new Rect(370, 0, 30, 20), new Rect(700, 0, 600, 20)];

            //
            bool valid = AutoFishingImageRecognition.IsValidFishBar(rects, 1920);

            //
            Assert.False(valid);
        }

        /// <summary>
        /// 集成测试：从真实截图检测出的矩形应能通过 IsValidFishBar 校验
        /// </summary>
        [Theory]
        [InlineData(@"20250306111752769_GetFishBoxArea_Succeeded.png")]
        public void IsValidFishBar_FromRealScreenshot_ShouldReturnTrue(string screenshot1080p)
        {
            //
            using Mat mat = new Mat(@$"..\..\..\Assets\AutoFishing\{screenshot1080p}");
            using Mat cropped = new Mat(mat, new Rect(0, 0, 1920, 140));

            //
            List<Rect>? rects = AutoFishingImageRecognition.GetFishBarRect(cropped);

            //
            Assert.NotNull(rects);
            bool valid = AutoFishingImageRecognition.IsValidFishBar(rects, 1920);
            Assert.True(valid);
        }
    }
}
