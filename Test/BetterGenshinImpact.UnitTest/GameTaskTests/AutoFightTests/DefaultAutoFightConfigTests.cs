using BetterGenshinImpact.GameTask.AutoFight.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests
{
    public partial class DefaultAutoFightConfigTests
    {
        /// <summary>
        /// 测试默认战斗配置初始化，结果应成功
        /// </summary>
        [Fact]
        public void DefaultAutoFightConfig_Init_ShouldSuccess()
        {
            //
            // Do nothing

            //
            var sut = DefaultAutoFightConfig.CombatAvatars;

            //
            Assert.NotNull(sut);
        }
    }
}
