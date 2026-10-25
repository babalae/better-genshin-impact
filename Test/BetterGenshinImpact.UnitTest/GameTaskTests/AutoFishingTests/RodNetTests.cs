using BetterGenshinImpact.GameTask.AutoFishing;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    /// <summary>
    /// 验证当前纯 C# RodNet 推理实现。
    /// </summary>
    public class RodNetTests
    {
        [Theory]
        [InlineData(517.6326F, 548.49023F, 255.25723F, 263.55743F, 256.57538F, 351.56964F, 274.65656F, 333.1523F, 5)]
        /// <summary>
        /// 固定输入的分数和最终状态应保持稳定，避免模型参数或计算流程被意外改动。
        /// </summary>
        public void ComputeScores_KnownInput_ShouldMatchExpectedValues(double rod_x1, double rod_x2, double rod_y1, double rod_y2, double fish_x1, double fish_x2, double fish_y1, double fish_y2, int fish_label)
        {
            var rodInput = new RodInput
            {
                rod_x1 = rod_x1,
                rod_x2 = rod_x2,
                rod_y1 = rod_y1,
                rod_y2 = rod_y2,
                fish_x1 = fish_x1,
                fish_x2 = fish_x2,
                fish_y1 = fish_y1,
                fish_y2 = fish_y2,
                fish_label = fish_label
            };

            var scores = RodNet.ComputeScores(rodInput);
            var state = new RodNet().GetRodState(rodInput);

            Assert.Equal(-0.29814935209909826, scores[0], 12);
            Assert.Equal(0.000000337885823033982, scores[1], 12);
            Assert.Equal(0.9981490142132753, scores[2], 12);
            Assert.Equal(2, state);
        }
    }
}
