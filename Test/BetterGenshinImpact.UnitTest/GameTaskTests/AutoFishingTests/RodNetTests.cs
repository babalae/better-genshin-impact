using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.AutoFishing.RodNet;
using static TorchSharp.torch;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests
{
    [Collection("Init Collection")]
    public class RodNetTests
    {
        public RodNetTests(TorchFixture torch)
        {
            if (!torch.UseTorch)
                throw new NotSupportedException("torch加载失败，请检查BetterGenshinImpact项目编译环境的配置");
        }

        [Theory]
        [InlineData(517.6326F, 548.49023F, 255.25723F, 263.55743F, 256.57538F, 351.56964F, 274.65656F, 333.1523F, 5)]
        /// <summary>
        /// 测试计算给到后处理之前的浮点数输出，Torch推理的结果和直接用数学计算的结果，两者的数值应该在转换到单精度时相同
        /// </summary>
        public void ComputeScoresTest_ShouldBeTheSame(double rod_x1, double rod_x2, double rod_y1, double rod_y2, double fish_x1, double fish_x2, double fish_y1, double fish_y2, int fish_label)
        {
            //
            RodInput rodInput = new RodInput
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
            RodNet sut = new RodNet();

            //
            NetInput netInput = GeometryProcessing(rodInput) ?? throw new NullReferenceException();
            Tensor outputTensor = sut.ComputeScores_Torch(netInput);
            double[] pred = ComputeScores(netInput);

            //
            Assert.Equal((float)pred[0], (float)outputTensor.data<double>()[0]);    // 对比时降低精度，差不多就行
            Assert.Equal((float)pred[1], (float)outputTensor.data<double>()[1]);
            Assert.Equal((float)pred[2], (float)outputTensor.data<double>()[2]);
        }
    }
}
