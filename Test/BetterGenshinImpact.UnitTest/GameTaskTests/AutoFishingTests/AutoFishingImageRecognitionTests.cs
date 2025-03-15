using BehaviourTree;
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
    }
}
